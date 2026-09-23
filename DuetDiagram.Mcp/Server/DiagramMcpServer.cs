using System.Globalization;
using System.Text.Json.Nodes;
using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Diagnostics;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Core.Time;
using DuetDiagram.Llm.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DuetDiagram.Mcp.Server;

/// <summary>
/// 通过标准输入输出对话的服务端：把八个工具挂到协议上，作用在某一份文档上。
/// </summary>
/// <remarks>
/// <para>
/// 它由客户端作为子进程拉起来，标准输入输出就是传输，所以一个进程只服务一个客户端。
/// 挂上去的工具是**已经建好的实例**，不是按标注扫出来的静态方法——按标注那套要求
/// 参数由服务端容器解析，而这条通路上服务端不持有容器，非基础类型参数会被当成
/// 工具参数，调用直接失败。用实例还顺带保证了代理侧与模型侧是同一份定义。
/// </para>
/// <para>
/// 文档只在这里读一次。服务端不保存任何会话上下文：每条请求自带的声明现读现用，
/// 所以换个实例、重启一次，同一个调用送出去的结果都一样。
/// </para>
/// </remarks>
public sealed class DiagramMcpServer : IAsyncDisposable
{
    /// <summary>服务端自称的版本号。</summary>
    public const string Version = "0.1.0";

    private readonly DiagramCommandBus _bus;
    private readonly InProcessBroadcaster _broadcaster;

    /// <summary>本次请求声明的版本。随请求变，所以放在随异步流走的槽里。</summary>
    private readonly AsyncLocal<SessionState?> _declared = new();

    // 宿主那一步要拿着这个实例去挂过滤器，而日志出口又是从宿主身上取的，
    // 所以这两样要等宿主建好之后再填。拆成两处组装的话，工具表与过滤器会各建一份上下文。
    private IHost? _host;
    private ILogger? _logger;

    private DiagramMcpServer(DiagramCommandBus bus, InProcessBroadcaster broadcaster, DiagramDocument document)
    {
        _bus = bus;
        _broadcaster = broadcaster;
        Document = document;
    }

    /// <summary>宿主。调用方只用到"跑起来"与"停下来"。</summary>
    public IHost Host => _host ?? throw new InvalidOperationException("服务端还没建好。");

    /// <summary>服务端正在编辑的那份文档。</summary>
    public DiagramDocument Document { get; }

    /// <summary>
    /// 建一个服务端。
    /// </summary>
    /// <param name="documentPath">要编辑的文档文件。为空表示从一张空图开始。</param>
    /// <param name="diagnostics">应用日志出口。为空表示不记。</param>
    public static DiagramMcpServer Create(string? documentPath = null, IDiagnosticsSink? diagnostics = null)
    {
        var document = Load(documentPath);

        // 带版本检查的模式要求有一个真的广播器：这个模式意味着存在多个写入方，
        // 而变更必须真的能被送出去。空实现过不了这道检查，这正是那条约束的用意。
        var broadcaster = new InProcessBroadcaster();

        var bus = new DiagramCommandBus(DiagramCommandBusContext.Create(
            document,
            new SimpleSessionProvider(
                "mcp-agent",
                SessionIds.Mcp("stdio", Environment.ProcessId.ToString(CultureInfo.InvariantCulture))),
            broadcaster,
            DiagramCommandBusOptions.ForMcp(),
            SystemTimeProvider.Instance,
            diagnostics));

        // 会话应答里写的是**服务端这一刻的状态**。它在会话建立时读一次，
        // 而这条传输下一个进程只服务一个会话，所以那一读拿到的就是当前值；
        // 之后的当前版本从摘要里读，每次写入的产物里也带着新的版本号。
        var ack = new SessionAck(document.Id, document.Version, document.StructuralHash).ToJson();

        var server = new DiagramMcpServer(bus, broadcaster, document);

        server._host = BuildHost(server, bus, ack);
        server._logger = server._host.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("DuetDiagram.Mcp");

        return server;
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync().ConfigureAwait(false);
            _host.Dispose();
        }

        _bus.Dispose();

        await _broadcaster.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 记下这条请求，并把它自带的会话状态放进本次调用的槽里。
    /// </summary>
    /// <remarks>
    /// 每条请求都重写一次，**包括"这次没声明"那一种**：留着上一条的值的话，
    /// 一次没声明的调用会继承上一条请求的版本，而那条版本可能是另一个调用方报的。
    /// 那会变成一次"照着别人的版本改"的写入，两边都不报错。
    /// </remarks>
    internal void Observe(JsonRpcMessage message)
    {
        if (message is not JsonRpcRequest request)
        {
            return;
        }

        var state = SessionState.FromRequest(request.Params);

        _declared.Value = state;

        _logger?.LogInformation(
            "收到 {Method}：{Session}",
            request.Method,
            state?.Describe() ?? "调用方未声明会话状态");
    }

    /// <summary>本次调用要报给命令总线的版本声明。</summary>
    private VersionCheckRequest? DeclaredVersion() => _declared.Value?.ToVersionCheck();

    private static IHost BuildHost(DiagramMcpServer server, DiagramCommandBus bus, JsonObject ack)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

        StdioLogging.RouteToStandardError(builder.Logging);
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        var context = new DiagramToolContext
        {
            Bus = bus,
            ExpectedVersion = server.DeclaredVersion,
        };

        builder.Services
            .AddMcpServer(options => ConfigureOptions(options, ack))
            .WithTools(ToolRegistry.CreateDefault(context).ToMcpTools())
            .WithMessageFilters(filters => filters.AddIncomingFilter(next => async (messageContext, cancellationToken) =>
            {
                server.Observe(messageContext.JsonRpcMessage);
                await next(messageContext, cancellationToken).ConfigureAwait(false);
            }))
            .WithStdioServerTransport();

        return builder.Build();
    }

    private static void ConfigureOptions(McpServerOptions options, JsonObject ack)
    {
        options.ServerInfo = new Implementation { Name = "duetdiagram", Version = Version };
        options.ServerInstructions =
            "编辑当前打开的那份图。改之前先读一次摘要拿版本号，改的时候把它带上；" +
            "版本对不上会拿到冲突，那就重读一次再改。";

        // 自定义字段走能力声明的扩展位。这条通道两端都能按类型直接读到，
        // 不需要谁去拦截原始消息；协议给自定义字段留的那个元数据位在当前版本下没有入口。
        options.Capabilities ??= new ServerCapabilities();
        options.Capabilities.Extensions ??= new Dictionary<string, object>();
        options.Capabilities.Extensions[SessionState.CapabilityKey] = ack;
    }

    private static DiagramDocument Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new DiagramDocument("mcp-document");
        }

        var full = Path.GetFullPath(path);

        return DiagramSerializer.DeserializeFull(File.ReadAllText(full));
    }
}
