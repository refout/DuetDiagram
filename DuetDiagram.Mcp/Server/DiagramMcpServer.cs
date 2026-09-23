using System.Text.Json.Nodes;
using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Diagnostics;
using DuetDiagram.Core.Model;
using DuetDiagram.Llm.Tools;
using DuetDiagram.Mcp.Skills;
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

    private readonly SessionCore _session;

    // 宿主那一步要拿着这个实例去挂过滤器，而日志出口又是从宿主身上取的，
    // 所以这两样要等宿主建好之后再填。拆成两处组装的话，工具表与过滤器会各建一份上下文。
    private IHost? _host;
    private ILogger? _logger;

    private DiagramMcpServer(SessionCore session)
    {
        _session = session;
    }

    /// <summary>宿主。调用方只用到"跑起来"与"停下来"。</summary>
    public IHost Host => _host ?? throw new InvalidOperationException("服务端还没建好。");

    /// <summary>服务端正在编辑的那份文档。</summary>
    public DiagramDocument Document => _session.Document;

    /// <summary>
    /// 建一个服务端。
    /// </summary>
    /// <param name="documentPath">要编辑的文档文件。为空表示从一张空图开始。</param>
    /// <param name="diagnostics">应用日志出口。为空表示不记。</param>
    public static DiagramMcpServer Create(string? documentPath = null, IDiagnosticsSink? diagnostics = null)
    {
        var session = SessionCore.Create("stdio", documentPath, diagnostics);
        var server = new DiagramMcpServer(session);

        server._host = BuildHost(server, session);
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

        await _session.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>记下这条请求，并把它自带的会话状态放进本次调用的槽里。</summary>
    internal void Observe(JsonRpcMessage message)
    {
        var state = _session.Observe(message);

        if (message is JsonRpcRequest request)
        {
            _logger?.LogInformation(
                "收到 {Method}：{Session}",
                request.Method,
                state?.Describe() ?? "调用方未声明会话状态");
        }
    }

    private static IHost BuildHost(DiagramMcpServer server, SessionCore session)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

        StdioLogging.RouteToStandardError(builder.Logging);
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        builder.Services
            .AddMcpServer(options => ConfigureOptions(options, session.Ack.ToJson()))
            .WithTools(ToolRegistry.CreateDefault(ToolContext(session)).ToMcpTools())
            .WithResources(SkillCatalog.Default.ToMcpResources())
            .WithMessageFilters(filters => filters.AddIncomingFilter(next => async (messageContext, cancellationToken) =>
            {
                server.Observe(messageContext.JsonRpcMessage);
                await next(messageContext, cancellationToken).ConfigureAwait(false);
            }))
            .WithStdioServerTransport();

        return builder.Build();
    }

    /// <summary>八个工具作用的那一份上下文。</summary>
    /// <remarks>
    /// 两样随请求变的东西都用委托取：版本声明与图层权限。工具表是按会话建一次、
    /// 之后一直复用的，存成值的话第二条请求会拿着第一条的值去判。
    /// </remarks>
    internal static DiagramToolContext ToolContext(SessionCore session) => new()
    {
        Bus = session.Bus,
        ExpectedVersion = session.DeclaredVersion,
        Permissions = () => session.Permissions,
    };

    private static void ConfigureOptions(McpServerOptions options, JsonObject ack)
    {
        options.ServerInfo = new Implementation { Name = "duetdiagram", Version = Version };
        options.ServerInstructions = Instructions;

        // 自定义字段走能力声明的扩展位。这条通道两端都能按类型直接读到，
        // 不需要谁去拦截原始消息；协议给自定义字段留的那个元数据位在当前版本下没有入口。
        options.Capabilities ??= new ServerCapabilities();
        options.Capabilities.Extensions ??= new Dictionary<string, object>();
        options.Capabilities.Extensions[SessionState.CapabilityKey] = ack;
    }

    /// <summary>给调用方看的那段话。两条传输说的是同一段。</summary>
    /// <remarks>
    /// 最后那一句是资源那一层的入口：不给它的话，调用方不会想到去看清单，
    /// 那两份 Skill 就等于不存在。而它只说了「按当前任务取」——把正文抄进来的话，
    /// 这一层就从「按需取」退化成了「一次性全给」，三层结构也就没有意义了。
    /// </remarks>
    internal const string Instructions =
        "编辑当前打开的那份图。改之前先读一次摘要拿版本号，改的时候把它带上；" +
        "版本对不上会拿到冲突，那就重读一次再改。" +
        "要按顺序改一份图、或者要写一整段 DSL 文本时，去看资源清单里那几份 Skill，" +
        "按当前任务取需要的那一份，不要全取。";
}
