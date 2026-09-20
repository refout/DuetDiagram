using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace DuetDiagram.Poc.McpTransport;

/// <summary>
/// 演示用的工具集合。刻意保持极简，只用来验证协议链路，不承载任何真实业务。
/// </summary>
/// <remarks>
/// 三个工具对应我们要验证的三种调用形态：无参读取、带参写入、返回结构化内容。
/// 真实产品里会有八个粗粒度工具，但那属于后续工作，与传输验证无关。
/// </remarks>
[McpServerToolType]
internal sealed class DiagramTools
{
    /// <summary>无参调用，用来验证最简调用路径。</summary>
    [McpServerTool(Name = "diagram_read")]
    [System.ComponentModel.Description("读取当前图的归一化摘要。")]
    public static string Read() => """{"nodes":["start","check"],"edges":["start->check"]}""";

    /// <summary>带参调用，用来验证参数能否从客户端正确传到服务端。</summary>
    [McpServerTool(Name = "diagram_edit")]
    [System.ComponentModel.Description("按动作修改图结构。")]
    public static string Edit(
        [System.ComponentModel.Description("要执行的动作名")] string action,
        [System.ComponentModel.Description("目标节点标识")] string nodeId)
        => $$"""{"action":"{{action}}","nodeId":"{{nodeId}}","ok":true}""";

    /// <summary>
    /// 把调用方传入的会话状态原样回显。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 状态是**显式参数**，不是注入的服务。这一点是被实测逼出来的：
    /// 无状态网络传输下服务端不持有服务容器，参数里的非基础类型不会被解析成服务，
    /// 而是被当成"必须由调用方提供"的工具参数，结果就是调用直接失败。
    /// 那种失败在服务端日志里表现为"缺少必需参数"，看起来像协议问题，实际是用法问题。
    /// </para>
    /// <para>
    /// 显式传参还有一个附带好处：调用是自包含的，服务端换个实例、重启、
    /// 甚至完全不保留会话上下文，调用结果都一样。这对多实例部署是必要条件。
    /// </para>
    /// </remarks>
    [McpServerTool(Name = "diagram_echo_session")]
    [System.ComponentModel.Description("回显调用方声明的会话状态。")]
    public static string EchoSession(
        [System.ComponentModel.Description("调用方当前所处的文档与版本，JSON 文本")] string sessionJson)
        => $$"""{"echo":{{sessionJson}}}""";
}

/// <summary>
/// 客户端在会话初始化时声明的状态。
/// </summary>
/// <remarks>
/// 内容对应产品设计里的 clientState：客户端告诉服务端"我这边看到的是哪份文档、停在哪个版本、
/// 结构哈希是什么"，服务端据此判断要不要回全量快照。
/// </remarks>
internal sealed record ClientState(string DocumentId, int HasVersion, string StructuralHash)
{
    public JsonObject ToJson() => new()
    {
        ["documentId"] = DocumentId,
        ["hasVersion"] = HasVersion,
        ["structuralHash"] = StructuralHash,
    };

    public static ClientState? FromJson(JsonNode? node)
    {
        if (node is not JsonObject json)
        {
            return null;
        }

        return new ClientState(
            json["documentId"]?.GetValue<string>() ?? string.Empty,
            json["hasVersion"]?.GetValue<int>() ?? 0,
            json["structuralHash"]?.GetValue<string>() ?? string.Empty);
    }
}

/// <summary>服务端对客户端状态的应答。</summary>
internal sealed record ClientStateAck(bool Accepted, int ServerVersion, string ServerStructuralHash, string? Reason)
{
    public JsonObject ToJson()
    {
        var json = new JsonObject
        {
            ["accepted"] = Accepted,
            ["serverVersion"] = ServerVersion,
            ["serverStructuralHash"] = ServerStructuralHash,
        };

        if (Reason is not null)
        {
            json["reason"] = Reason;
        }

        return json;
    }

    public static ClientStateAck? FromJson(JsonNode? node)
    {
        if (node is not JsonObject json)
        {
            return null;
        }

        return new ClientStateAck(
            json["accepted"]?.GetValue<bool>() ?? false,
            json["serverVersion"]?.GetValue<int>() ?? 0,
            json["serverStructuralHash"]?.GetValue<string>() ?? string.Empty,
            json["reason"]?.GetValue<string>());
    }
}

/// <summary>
/// 会话状态的读写位置。
/// </summary>
/// <remarks>
/// <para>
/// 客户端状态与服务端应答都放在**能力声明的扩展位**里，而不是协议给自定义字段留的元数据位。
/// 这个选择是被实测逼出来的：客户端选项里那个"附加到初始化请求元数据"的设置在当前协议版本下
/// 完全没有上线——因为当前协议版本用服务发现请求取代了初始化请求，那个设置挂在了不存在的请求上。
/// </para>
/// <para>
/// 走扩展位还有个更好的性质：它随**每一条请求**携带，而不是只在会话建立时协商一次。
/// 这意味着连接重建、服务端换实例、甚至服务端完全不保留上下文，调用语义都不变。
/// </para>
/// </remarks>
internal static class SessionStateReader
{
    /// <summary>扩展字段在能力声明里的键名。</summary>
    public const string CapabilityKey = "duetdiagram/session";

    /// <summary>客户端状态在旧版初始化请求元数据里的键名。仅用于说明这条通道已失效，不再使用。</summary>
    public const string LegacyClientStateKey = "duetdiagram/clientState";

    /// <summary>
    /// 应答在响应元数据里的键名。
    /// </summary>
    /// <remarks>
    /// 与能力扩展位承载同一份语义，只是放在两个位置上，用来对照两条下行通道的表现：
    /// 元数据位需要客户端能看原始消息才读得到，扩展位则由类型系统直接承载。
    /// </remarks>
    public const string AckKey = "duetdiagram/clientStateAck";

    /// <summary>
    /// 从能力声明里读回客户端状态。
    /// </summary>
    /// <remarks>
    /// 服务端能直接按类型读到这条通道，不需要挂过滤器去拦原始消息。
    /// </remarks>
    public static ClientState? ReadClientState(ModelContextProtocol.Protocol.ClientCapabilities? capabilities)
    {
        if (capabilities?.Extensions is null ||
            !capabilities.Extensions.TryGetValue(CapabilityKey, out var raw))
        {
            return null;
        }

        return ClientState.FromJson(ToJsonNode(raw));
    }

    /// <summary>
    /// 从服务端能力声明里读回应答。
    /// </summary>
    /// <remarks>
    /// 这条通道的验证意义在于：它证明**客户端不需要挂任何拦截器**就能拿到自定义字段。
    /// 走元数据位的话，客户端必须能看到原始消息才行，而标准客户端实现通常不暴露这个。
    /// </remarks>
    public static ClientStateAck? ReadAck(ModelContextProtocol.Protocol.ServerCapabilities? capabilities)
    {
        if (capabilities?.Extensions is null ||
            !capabilities.Extensions.TryGetValue(CapabilityKey, out var raw))
        {
            return null;
        }

        return ClientStateAck.FromJson(ToJsonNode(raw));
    }

    /// <summary>
    /// 把扩展位里的弱类型值还原成 JSON 节点。
    /// </summary>
    /// <remarks>
    /// 扩展位声明上是对象，但经序列化往返之后拿到的通常是 JSON 节点，
    /// 也可能因为对端把它当字符串处理而变成一段文本。两种都要认。
    /// </remarks>
    private static JsonNode? ToJsonNode(object? raw) => raw switch
    {
        null => null,
        JsonNode node => node,
        string text => TryParse(text),
        _ => TryParse(raw.ToString()),
    };

    private static JsonNode? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
