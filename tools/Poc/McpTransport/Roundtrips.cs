using System.IO.Pipelines;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DuetDiagram.Poc.McpTransport;

/// <summary>一次回环验证的结果。</summary>
internal sealed record RoundtripReport(string Transport, List<(string Name, bool Passed, string Detail)> Checks)
{
    public bool AllPassed => Checks.All(c => c.Passed);
}

/// <summary>回环过程中从客户端侧观察到的事实。</summary>
internal sealed record ClientObservations(
    string ServerInfo,
    int ToolCount,
    string ToolNames,
    string EchoText,
    string CapabilityAckDetail,
    bool CapabilityAckAccepted);

/// <summary>
/// 回环验证。
/// </summary>
/// <remarks>
/// 两条传输做同一组断言，这样可以分辨"某个字段传不过去"到底是协议层面的限制，
/// 还是某条传输自己特有的问题。
/// </remarks>
internal static class Roundtrips
{
    /// <summary>客户端声明的状态。用可辨认的值，避免与默认值混淆。</summary>
    internal static readonly ClientState State = new("doc-7f3a", HasVersion: 12, StructuralHash: "hash-9c02");

    /// <summary>
    /// 内存流对回环。
    /// </summary>
    /// <remarks>
    /// 不经过进程边界，可以把注意力集中在协议内容上：自定义字段有没有上线、能不能读回来，
    /// 与传输是进程内管道还是父子进程无关。
    /// </remarks>
    public static async Task<RoundtripReport> WireAsync(CancellationToken cancellationToken)
    {
        var toServer = new Pipe();
        var toClient = new Pipe();

        var log = new MessageLog();

        // 客户端往 toServer 写、服务端从 toServer 读；服务端往 toClient 写、客户端从 toClient 读。
        await using var rawServerInput = toServer.Reader.AsStream();
        await using var serverOutput = toClient.Writer.AsStream();
        await using var clientOutput = toServer.Writer.AsStream();
        await using var clientInput = toClient.Reader.AsStream();

        // 服务端读入侧套一层抓包，用来取初始化的原始内容——
        // 那一次交互不经过消息过滤器，只能在这一层看。
        var recorder = new RecordingReadStream(rawServerInput);

        var host = ServerHost.Build(
            mcp => mcp.WithStreamServerTransport(recorder, serverOutput),
            log);

        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var client = await McpClient.CreateAsync(
                new StreamClientTransport(clientOutput, clientInput),
                ClientOptions(),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var observations = await ExerciseAsync(client, cancellationToken).ConfigureAwait(false);

            return new RoundtripReport("内存流对", BuildChecks(observations, log, recorder.CapturedText()));
        }
        finally
        {
            await host.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 父子进程回环。
    /// </summary>
    /// <remarks>
    /// 这是外部代理真正使用的形态：服务端作为子进程，通过标准输入输出与父进程对话。
    /// 这条路径额外验证两件事——进程能不能拉起来，以及标准输出有没有被日志污染
    /// （被污染的话握手会直接失败，症状是解析错误而不是超时）。
    /// </remarks>
    public static async Task<RoundtripReport> StdioAsync(string executablePath, string logPath, CancellationToken cancellationToken)
    {
        // 上一次运行的残留会让"服务端到底看到过什么"这个判断失真，先清掉。
        if (File.Exists(logPath))
        {
            File.Delete(logPath);
        }

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "duetdiagram-poc-client",
            Command = executablePath,
            Arguments = [Program.ServerSwitch, Program.LogTargetSwitch, logPath],
        });

        await using var client = await McpClient.CreateAsync(transport, ClientOptions(), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var observations = await ExerciseAsync(client, cancellationToken).ConfigureAwait(false);

        // 客户端释放时会结束子进程，之后文件才写得完整。
        await client.DisposeAsync().ConfigureAwait(false);

        var log = MessageLog.LoadFrom(logPath);

        // 该路径抓不到包：服务端在另一个进程里，父进程看不到它的读入流。
        // 上行的结论以内存流对那条路径的抓包为准。
        return new RoundtripReport("标准输入输出（子进程）", BuildChecks(observations, log, capturedWire: null));
    }

    internal static McpClientOptions ClientOptions() => new()
    {
        ClientInfo = new Implementation { Name = "duetdiagram-poc-client", Version = "0.1.0" },

        // 通道一：能力声明的扩展位。弱类型对象，两端都能按类型直接读到。
        Capabilities = new ClientCapabilities
        {
            Extensions = new Dictionary<string, object>
            {
                [SessionStateReader.CapabilityKey] = State.ToJson().ToJsonString(),
            },
        },

        // 通道二：初始化请求的元数据位。协议给自定义字段留的标准位置。
        InitializeMeta = new JsonObject
        {
            [SessionStateReader.LegacyClientStateKey] = State.ToJson(),
        },
    };

    /// <summary>两条传输共用的动作序列。</summary>
    internal static async Task<ClientObservations> ExerciseAsync(McpClient client, CancellationToken cancellationToken)
    {
        var serverInfo = client.ServerInfo is null
            ? "未拿到服务端信息"
            : $"{client.ServerInfo.Name} {client.ServerInfo.Version}";

        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var echo = await client.CallToolAsync(
            "diagram_echo_session",
            new Dictionary<string, object?> { ["sessionJson"] = State.ToJson().ToJsonString() },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var echoText = echo.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? string.Empty;

        var ack = SessionStateReader.ReadAck(client.ServerCapabilities);

        return new ClientObservations(
            serverInfo,
            tools.Count,
            string.Join(", ", tools.Select(t => t.Name)),
            echoText,
            ack is null ? "客户端未从服务端能力里读到应答" : $"accepted={ack.Accepted} v{ack.ServerVersion}",
            ack?.Accepted ?? false);
    }

    /// <summary>两条传输共用的断言集合。</summary>
    /// <param name="capturedWire">抓到的原始上行内容。为空表示这条路径抓不到包。</param>
    internal static List<(string Name, bool Passed, string Detail)> BuildChecks(
        ClientObservations observations,
        MessageLog log,
        string? capturedWire)
    {
        var ackInResponse = ReadAckFromResult(log.FirstResponseResult);

        // 上行会话状态从服务端实际收到的第一条请求里读。
        // 位置在能力声明的扩展位里，不在网关注的那几个标准字段上。
        var upstreamState = SessionStateReader.ReadClientState(ReadClientCapabilities(log.FirstRequestParams));

        return
        [
            ("一·握手与会话建立", observations.ServerInfo != "未拿到服务端信息", observations.ServerInfo),
            ("二·工具发现", observations.ToolCount >= 3, $"发现 {observations.ToolCount} 个：{observations.ToolNames}"),
            (
                "三·工具调用",
                !observations.EchoText.StartsWith("An error", StringComparison.Ordinal) && observations.EchoText.Length > 0,
                $"服务端回显 {observations.EchoText}"),
            (
                "四·上行·客户端状态到达服务端",
                upstreamState is not null && upstreamState.DocumentId == State.DocumentId,
                upstreamState is null
                    ? $"第一条请求={string.Join(" / ", log.Incoming)}；参数={Truncate(log.FirstRequestParams)}"
                    : $"服务端从请求里读到 {upstreamState.DocumentId} v{upstreamState.HasVersion}"),
            (
                "五·上行·显式传参回显",
                observations.EchoText.Contains(State.DocumentId, StringComparison.Ordinal),
                observations.EchoText.Contains(State.DocumentId, StringComparison.Ordinal)
                    ? "工具收到并回显了调用方传入的状态"
                    : $"未回显（{observations.EchoText}）"),
            (
                "六·下行·响应元数据",
                ackInResponse is not null && ackInResponse.Accepted,
                ackInResponse is null ? "响应里没有应答字段" : $"accepted={ackInResponse.Accepted} v{ackInResponse.ServerVersion}"),
            ("七·下行·能力扩展位", observations.CapabilityAckAccepted, observations.CapabilityAckDetail),
            (
                "八·消息往返",
                log.Incoming.Count > 0 && log.Outgoing.Count > 0,
                $"服务端收到 {log.Incoming.Count} 条、发出 {log.Outgoing.Count} 条；依次为 {string.Join(" / ", log.Incoming)}"),
            (
                "九·会话建立方式",
                log.Incoming.Count > 0,
                capturedWire is null
                    ? $"服务端收到的第一条请求是 {log.Incoming.FirstOrDefault()}（该路径抓不到原始内容）"
                    : $"第一条请求 {log.Incoming.FirstOrDefault()}；原始内容中出现的自定义字段 {DescribeCustomFields(capturedWire)}"),
        ];
    }

    /// <summary>
    /// 在原始上行内容里找出我们自定义的字段出现在哪些位置。
    /// </summary>
    /// <remarks>
    /// 这一段是为了留下"自定义字段到底挂在哪儿"的证据。只看能不能读回来，
    /// 会漏掉"它其实被挂在了另一个位置"这种偏差——那种偏差将来换客户端实现就会失效。
    /// </remarks>
    private static string DescribeCustomFields(string capturedWire)
    {
        var markers = new List<string>();

        if (capturedWire.Contains("clientCapabilities", StringComparison.Ordinal))
        {
            markers.Add("能力声明");
        }

        if (capturedWire.Contains(SessionStateReader.LegacyClientStateKey, StringComparison.Ordinal))
        {
            markers.Add("初始化请求元数据（客户端选项声明的字段）");
        }

        if (capturedWire.Contains(SessionStateReader.CapabilityKey, StringComparison.Ordinal))
        {
            markers.Add($"能力扩展位 {SessionStateReader.CapabilityKey}");
        }

        return markers.Count == 0 ? "未发现" : string.Join("、", markers);
    }

    /// <summary>
    /// 从第一条请求的参数里取出客户端的能力声明。
    /// </summary>
    /// <remarks>
    /// 当前协议版本把能力声明放在请求元数据里，早期版本放在参数顶层。
    /// 两个位置都试，免得协议一变就取不到。
    /// </remarks>
    private static ClientCapabilities? ReadClientCapabilities(JsonNode? requestParams)
    {
        if (requestParams is not JsonObject parameters)
        {
            return null;
        }

        var node = parameters["capabilities"] ?? parameters["_meta"]?["io.modelcontextprotocol/clientCapabilities"];

        return node is null
            ? null
            : System.Text.Json.JsonSerializer.Deserialize<ClientCapabilities>(node.ToJsonString());
    }

    private static string TruncateText(string text)
    {
        var flat = text.ReplaceLineEndings(" ").Trim();

        return flat.Length <= 200 ? flat : flat[..200] + "…";
    }

    private static ClientState? ReadClientStateFromRequest(JsonNode? requestParams)
    {
        if (requestParams is not JsonObject parameters)
        {
            return null;
        }

        return ClientState.FromJson(parameters["_meta"]?[SessionStateReader.LegacyClientStateKey]);
    }

    private static ClientStateAck? ReadAckFromResult(JsonNode? result)
    {
        if (result is not JsonObject json)
        {
            return null;
        }

        return ClientStateAck.FromJson(json["_meta"]?[SessionStateReader.AckKey]);
    }

    /// <summary>把节点压成一行短文本，便于在检查结果里直接看出实际内容。</summary>
    private static string Truncate(JsonNode? node)
    {
        if (node is null)
        {
            return "<空>";
        }

        var text = node.ToJsonString();

        return text.Length <= 240 ? text : text[..240] + "…";
    }
}
