using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DuetDiagram.Poc.McpTransport;

/// <summary>
/// 一次会话里服务端经过的原始消息记录。
/// </summary>
/// <remarks>
/// <para>
/// 抓包口径是服务端过滤器看到的原始消息，也就是真正落到传输上的内容。
/// 用它判断某个字段有没有上线，比读文档或者猜序列化行为可靠得多。
/// </para>
/// <para>
/// 记录可以落到文件里。父子进程那条路径上服务端在另一个进程里，
/// 父进程拿不到它的内存，只能靠文件把观察结果带回来。
/// </para>
/// </remarks>
internal sealed class MessageLog
{
    private readonly string? _path;

    public MessageLog(string? path = null) => _path = path;

    public string? FilePath => _path;

    public List<string> Incoming { get; } = [];

    public List<string> Outgoing { get; } = [];

    public JsonNode? FirstRequestParams { get; private set; }

    public JsonNode? FirstResponseResult { get; private set; }

    public void RecordIncoming(JsonRpcMessage message)
    {
        var description = Describe(message);
        Incoming.Add(description);
        Persist("in", description);

        // 记第一条请求的参数。会话建立那一次请求在不同协议版本下名字不一样——
        // 早期版本叫 initialize，当前协议版本叫 server/discover——
        // 所以按"第一条"取而不是按方法名匹配，免得协议一改断言就静默失效。
        if (message is JsonRpcRequest { Params: not null } request && FirstRequestParams is null)
        {
            FirstRequestParams = request.Params.DeepClone();
            Persist("request-params", FirstRequestParams.ToJsonString());
        }
    }

    public void RecordOutgoing(JsonRpcMessage message)
    {
        var description = Describe(message);
        Outgoing.Add(description);
        Persist("out", description);

        // 会话里第一条请求必然是初始化，所以第一条响应就是初始化的响应。
        if (message is JsonRpcResponse { Result: not null } response && FirstResponseResult is null)
        {
            FirstResponseResult = response.Result.DeepClone();
            Persist("response-result", FirstResponseResult.ToJsonString());
        }
    }

    /// <summary>把子进程写下的记录读回来。</summary>
    public static MessageLog LoadFrom(string path)
    {
        var log = new MessageLog();

        if (!File.Exists(path))
        {
            return log;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            var separator = line.IndexOf('\t', StringComparison.Ordinal);

            if (separator < 0)
            {
                continue;
            }

            var kind = line[..separator];
            var payload = line[(separator + 1)..];

            switch (kind)
            {
                case "in":
                    log.Incoming.Add(payload);
                    break;
                case "out":
                    log.Outgoing.Add(payload);
                    break;
                case "request-params":
                    log.FirstRequestParams = JsonNode.Parse(payload);
                    break;
                case "response-result":
                    log.FirstResponseResult = JsonNode.Parse(payload);
                    break;
                default:
                    break;
            }
        }

        return log;
    }

    private void Persist(string kind, string payload)
    {
        if (_path is null)
        {
            return;
        }

        // 每次追加一行。记录量很小，不值得为它维护一个长期打开的文件句柄。
        File.AppendAllText(_path, $"{kind}\t{payload}{Environment.NewLine}");
    }

    private static string Describe(JsonRpcMessage message) => message switch
    {
        JsonRpcRequest request => $"请求 {request.Method}",
        JsonRpcResponse => "响应",
        JsonRpcNotification notification => $"通知 {notification.Method}",
        _ => message.GetType().Name,
    };
}

/// <summary>
/// 服务端组装。
/// </summary>
/// <remarks>
/// 三条传输路径共用同一份组装逻辑，只有传输那一步不同。这样验证出来的结论才具有可比性：
/// 如果每条路径各写一套组装代码，行为差异到底来自传输还是来自配置就说不清了。
/// </remarks>
internal static class ServerHost
{
    /// <summary>服务端自称的版本号，用来在应答里给出可对照的值。</summary>
    public const int ServerVersion = 42;

    public const string ServerStructuralHash = "server-hash-abc";

    public static IHost Build(Action<IMcpServerBuilder> configureTransport, MessageLog log)
    {
        var builder = Host.CreateApplicationBuilder();

        // stdio 传输下这一步是必须的：控制台日志默认写标准输出，而标准输出被协议占用，
        // 一条日志就会让对端解析失败。全部改走标准错误。
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddSingleton(log);

        var mcp = builder.Services.AddMcpServer(ConfigureServerOptions)
            .WithTools<DiagramTools>()
            .WithObservability(log);

        configureTransport(mcp);

        return builder.Build();
    }

    /// <summary>
    /// 挂上消息观察与应答注入。
    /// </summary>
    /// <remarks>
    /// 三条传输共用同一份过滤器配置。分开写的话，某条路径漏挂过滤器会表现为"这条路径的字段传不过去"，
    /// 而那其实是配置遗漏而不是协议限制——两种结论的处置方式完全不同。
    /// </remarks>
    public static IMcpServerBuilder WithObservability(this IMcpServerBuilder mcp, MessageLog log) =>
        mcp.WithMessageFilters(filters =>
        {
            filters.AddIncomingFilter(next => async (context, cancellationToken) =>
            {
                log.RecordIncoming(context.JsonRpcMessage);
                await next(context, cancellationToken).ConfigureAwait(false);
            });

            filters.AddOutgoingFilter(next => async (context, cancellationToken) =>
            {
                InjectSessionAck(context.JsonRpcMessage, log);
                log.RecordOutgoing(context.JsonRpcMessage);
                await next(context, cancellationToken).ConfigureAwait(false);
            });
        });

    public static void ConfigureServerOptions(McpServerOptions options)
    {
        options.ServerInfo = new Implementation { Name = "duetdiagram-poc", Version = "0.1.0" };
        options.ServerInstructions = "传输验证用的最小服务端。";

        // 应答同时放在能力扩展位里。走这条通道不需要过滤器，两端都能直接按类型读到，
        // 是"自定义字段能不能传"这个问题的最省事答案。
        var ack = new ClientStateAck(true, ServerVersion, ServerStructuralHash, Reason: null).ToJson().ToJsonString();

        options.Capabilities ??= new ServerCapabilities();
        options.Capabilities.Extensions ??= new Dictionary<string, object>();
        options.Capabilities.Extensions[SessionStateReader.CapabilityKey] = ack;
    }

    /// <summary>
    /// 把应答注入到初始化响应的元数据位里。
    /// </summary>
    /// <remarks>
    /// 初始化请求里的客户端状态是会话开始之后才知道的，而服务端能力是在会话开始之前配置好的，
    /// 所以需要在响应发出的那一刻再去写。这里用出站过滤器完成。
    /// </remarks>
    private static void InjectSessionAck(JsonRpcMessage message, MessageLog log)
    {
        if (message is not JsonRpcResponse { Result: JsonObject result })
        {
            return;
        }

        // 只改初始化的响应：其它响应不该被塞进会话字段。
        if (log.FirstResponseResult is not null)
        {
            return;
        }

        var ack = new ClientStateAck(true, ServerVersion, ServerStructuralHash, Reason: null).ToJson();

        if (result["_meta"] is not JsonObject meta)
        {
            meta = [];
            result["_meta"] = meta;
        }

        meta[SessionStateReader.AckKey] = ack;
    }
}
