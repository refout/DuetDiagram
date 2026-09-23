using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DuetDiagram.Core.Bus;

namespace DuetDiagram.Mcp.Server;

/// <summary>
/// 调用方随请求带上来的会话状态：它认为自己在编辑哪份文档、停在哪个版本。
/// </summary>
/// <remarks>
/// <para>
/// **它随每一条请求携带，不只是会话建立那一次。** 这条性质是实测出来的，也是这一层
/// 能做成无状态的原因：连接断了重连、服务端换一个实例、甚至服务端完全不保留会话上下文，
/// 同一个调用送出去的结果都一样。服务端因此不需要"记住"任何东西——
/// 它每次从请求里现读。
/// </para>
/// <para>
/// 三个字段都可能缺。缺一个不是错误：调用方可能只想读、还没打算改，那就不必声明版本。
/// 但**要改就必须声明**，否则命令总线会以"缺少版本声明"拒掉那条命令。
/// </para>
/// </remarks>
/// <param name="DocumentId">调用方认为自己在编辑的文档标识。</param>
/// <param name="Version">调用方看到的版本号。</param>
/// <param name="StructuralHash">调用方那边的结构哈希。</param>
public sealed record SessionState(string? DocumentId, int? Version, string? StructuralHash)
{
    /// <summary>扩展字段在能力声明里的键名。两端共用这一个名字。</summary>
    public const string CapabilityKey = "duetdiagram/session";

    /// <summary>客户端能力声明在请求元数据里的键名。</summary>
    private const string ClientCapabilitiesKey = "io.modelcontextprotocol/clientCapabilities";

    /// <summary>把这次声明的版本翻成一次版本检查。没声明版本时返回空。</summary>
    /// <remarks>
    /// 结构哈希一起带上：留空表示调用方不打算用"只回差异"那条捷径，而带上了它才有资格用。
    /// 版本缺失时不编一个零出来——编出来的零会被当成"我看的是第 0 版"，
    /// 而真相是"我没说"，两者的处置完全不同。
    /// </remarks>
    public VersionCheckRequest? ToVersionCheck() =>
        Version is { } version
            ? new VersionCheckRequest { ClientVersion = version, ClientStructuralHash = StructuralHash }
            : null;

    /// <summary>写进日志的一行说法。</summary>
    public string Describe()
    {
        var document = string.IsNullOrEmpty(DocumentId) ? "未声明文档" : DocumentId;
        var version = Version is { } value ? value.ToString(CultureInfo.InvariantCulture) : "未声明版本";

        return $"{document} 第 {version} 版";
    }

    /// <summary>把这次声明写成 JSON。</summary>
    public JsonObject ToJson()
    {
        var json = new JsonObject();

        if (DocumentId is not null)
        {
            json["documentId"] = DocumentId;
        }

        if (Version is { } version)
        {
            json["hasVersion"] = version;
        }

        if (StructuralHash is not null)
        {
            json["structuralHash"] = StructuralHash;
        }

        return json;
    }

    /// <summary>
    /// 从一条请求的参数里读出声明的会话状态。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **两条通道，调用自带的那条优先。** 会话建立时声明一次是常规做法，但一次会话里
    /// 可能改很多次，而每改一次版本号就往前一格——只认会话那一次声明的话，
    /// 第二次改就会被判成冲突，而调用方明明已经把新的版本号拿在手里了。
    /// </para>
    /// <para>
    /// 会话那条通道里还有两个位置都试：当前协议版本把能力声明放在请求元数据里，
    /// 早期版本放在参数顶层。只认一个位置的话，协议一动这条读取就静默失效，
    /// 而表现是"调用方明明声明了、服务端却说没收到"。
    /// </para>
    /// </remarks>
    public static SessionState? FromRequest(JsonNode? requestParams)
    {
        if (requestParams is not JsonObject parameters)
        {
            return null;
        }

        if (Read(parameters["_meta"]?[CapabilityKey]) is { } call)
        {
            return new SessionState(
                Text(call, "documentId"),
                Number(call, "hasVersion"),
                Text(call, "structuralHash"));
        }

        var capabilities = parameters["_meta"]?[ClientCapabilitiesKey] ?? parameters["capabilities"];

        return Read(capabilities?["extensions"]?[CapabilityKey]) is { } declared
            ? new SessionState(
                Text(declared, "documentId"),
                Number(declared, "hasVersion"),
                Text(declared, "structuralHash"))
            : null;
    }

    /// <summary>
    /// 把一个扩展位的值读成会话状态。
    /// </summary>
    /// <remarks>
    /// **四种形态都要认。** 声明上它是对象；经协议往返之后，它要么是装着对象的 JSON 节点，
    /// 要么是**装着一段文本的 JSON 值**——后者是对端把它当字符串处理的结果，实际线上就是这一种；
    /// 也可能因为别的实现而变成一段原生的文本。只认其中一种的话，会出现
    /// 「字段明明在、读出来却是空」这种很难查的现象——而它既不报错也不抛异常。
    /// </remarks>
    public static SessionState? FromExtensionValue(object? raw) =>
        Read(raw) is { } json
            ? new SessionState(Text(json, "documentId"), Number(json, "hasVersion"), Text(json, "structuralHash"))
            : null;

    /// <summary>
    /// 把一个扩展位的值读成一个 JSON 对象。
    /// </summary>
    /// <remarks>
    /// 「装着文本的 JSON 值」那一条要**排在**「JSON 节点」前面：前者本来就是后者的一种，
    /// 顺序反了的话它会被当成一个对象去读，而它的键一个都对不上，结果同样是读出来是空。
    /// </remarks>
    internal static JsonObject? Read(object? raw)
    {
        var json = raw switch
        {
            null => null,
            JsonValue value when value.GetValueKind() == JsonValueKind.String => Parse(value.GetValue<string>()),
            JsonNode node => node,
            JsonElement element => Parse(element.GetRawText()),
            string text => Parse(text),
            _ => Parse(raw.ToString()),
        };

        return json as JsonObject;
    }

    internal static string? Text(JsonObject json, string name) =>
        json[name] is { } node && node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;

    internal static int? Number(JsonObject json, string name) =>
        json[name] is { } node && node.GetValueKind() == JsonValueKind.Number ? node.GetValue<int>() : null;

    private static JsonNode? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            // 读不出来就当没声明。抛出去的话，一个格式不对的声明会把整条调用打断，
            // 而它本来只是"调用方少说了点什么"。
            return null;
        }
    }
}

/// <summary>
/// 服务端回给调用方的会话应答：我这边是哪份文档、停在第几版。
/// </summary>
/// <remarks>
/// <para>
/// 字段名与调用方那份**刻意不同**：调用方说的是"我看到的是第几版"，服务端说的是
/// "我这边是第几版"。两者同名的话，一份应答被当成声明读回来时不会被发现——
/// 而那种错误的表现是"服务端拿自己的版本去比自己的版本"，永远不冲突。
/// </para>
/// <para>
/// 它放在能力声明的扩展位里，客户端不挂任何拦截器就能读到。
/// </para>
/// </remarks>
/// <param name="DocumentId">服务端正在编辑的文档标识。</param>
/// <param name="Version">服务端这一刻的版本号。</param>
/// <param name="StructuralHash">服务端这一刻的结构哈希。</param>
public sealed record SessionAck(string DocumentId, int Version, string StructuralHash)
{
    public JsonObject ToJson() => new()
    {
        ["documentId"] = DocumentId,
        ["serverVersion"] = Version,
        ["serverStructuralHash"] = StructuralHash,
    };

    /// <summary>把扩展位里那份应答读回来。</summary>
    public static SessionAck? FromExtensionValue(object? raw) =>
        SessionState.Read(raw) is { } json && SessionState.Text(json, "documentId") is { } document
            ? new SessionAck(
                document,
                SessionState.Number(json, "serverVersion") ?? 0,
                SessionState.Text(json, "serverStructuralHash") ?? string.Empty)
            : null;
}
