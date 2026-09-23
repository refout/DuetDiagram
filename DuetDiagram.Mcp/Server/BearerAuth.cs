using System.Globalization;
using System.Text.Json.Nodes;
using DuetDiagram.Core.Commands;
using Microsoft.AspNetCore.Http;

namespace DuetDiagram.Mcp.Server;

/// <summary>
/// 一份凭据能做什么。
/// </summary>
/// <remarks>
/// 三档而不是两档：中间那一档（能改，但不能做那些影响整份文档或别人会话的事）现在没有
/// 具体判据，所以它此刻与最高档同义。留着它是因为凭据表本身要能表达这个意图——
/// 等真的有了只给最高档留的入口，改的是判据，不是所有凭据的写法。
/// </remarks>
public enum AgentScope
{
    /// <summary>只能读：摘要、导出、校验。</summary>
    Read,

    /// <summary>能改这份图。</summary>
    Edit,

    /// <summary>不受限制。</summary>
    Full,
}

/// <summary>
/// 一份凭据：它叫什么、能做什么。
/// </summary>
/// <remarks>
/// <see cref="Name"/> 是审计里用的那个名字，不是凭据本身。日志里写凭据的话，
/// 任何能看到日志的人都能拿它去冒充——而日志是最容易被复制走的东西。
/// </remarks>
/// <param name="Name">这份凭据的名字，进审计。</param>
/// <param name="Scope">能做什么。</param>
/// <param name="Value">凭据本身。只在比对时用，绝不写进任何输出。</param>
public sealed record AgentToken(string Name, AgentScope Scope, string Value);

/// <summary>
/// 凭据表：从请求头里取出的凭据换成一份身份。
/// </summary>
/// <remarks>
/// <para>
/// 只有查找，没有"写入失败响应"这类动作：拒绝怎么写是传输那一层的事，
/// 混进来的话，同一个拒绝会在两个地方各写一遍，而两处迟早不一样。
/// </para>
/// <para>
/// 比对用序号相等，不用忽略大小写的比较：凭据是一串随机字节的文本形式，
/// 忽略大小写等于把可猜的空间砍掉一大块。
/// </para>
/// </remarks>
public sealed class BearerAuth
{
    /// <summary>凭据没通过时用的错误码。</summary>
    public const string UnauthorizedCode = ErrorCodes.McpUnauthorized;

    /// <summary>凭据通过了、但权限档不够时用的错误码。</summary>
    public const string ForbiddenCode = ErrorCodes.McpForbidden;

    /// <summary>凭据前面那个前缀名。</summary>
    public const string Scheme = "Bearer";

    private readonly Dictionary<string, AgentToken> _byValue;

    public BearerAuth(IEnumerable<AgentToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        _byValue = new Dictionary<string, AgentToken>(StringComparer.Ordinal);

        foreach (var token in tokens)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(token.Value);

            if (!_byValue.TryAdd(token.Value, token))
            {
                throw new ArgumentException($"凭据 {token.Name} 的值与另一份重复了。", nameof(tokens));
            }
        }
    }

    /// <summary>登记过的凭据，按登记次序。凭据本身不从这里露出去。</summary>
    public IReadOnlyList<string> Names => [.. _byValue.Values.Select(token => token.Name)];

    /// <summary>
    /// 从请求头里取出的凭据换成一份身份。
    /// </summary>
    /// <remarks>
    /// 头缺失、前缀名不对、凭据认不出，三种都返回空。分开报的话，
    /// 调用方能试出"这个凭据存在但前缀写错了"，而那是一次白送的探测机会。
    /// </remarks>
    public AgentToken? Authenticate(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return null;
        }

        var parts = authorization.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 2
            && string.Equals(parts[0], Scheme, StringComparison.OrdinalIgnoreCase)
            && _byValue.TryGetValue(parts[1], out var token)
                ? token
                : null;
    }
}

/// <summary>
/// 一条被传输层挡下来的请求。
/// </summary>
/// <remarks>
/// <para>
/// 四种拒绝各自一个状态码、各自一个错误码：合成一个「拒绝」的话，代理侧分不清
/// 「凭据不认得」「凭据权限不够」「你调得太快」「这份文件不许碰」——
/// 而这四件事的处置完全不同（换凭据、换权限更高的凭据、退避重试、改路径）。
/// </para>
/// <para>
/// 正文写成 JSON 而不是一句纯文本：调用方要能按码分支，而不是去匹配一句话。
/// </para>
/// </remarks>
public static class TransportRejection
{
    /// <summary>写一条拒绝，并把它的错误码与说明放进正文。</summary>
    /// <param name="context">这一条请求。</param>
    /// <param name="status">HTTP 状态码。</param>
    /// <param name="code">错误码。</param>
    /// <param name="message">给人看的一句话。</param>
    /// <param name="retryAfterSeconds">过多久可以再来。只在限流那一档给。</param>
    public static async Task WriteAsync(
        HttpContext context,
        int status,
        string code,
        string message,
        int? retryAfterSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";

        var body = new JsonObject
        {
            ["code"] = code,
            ["message"] = message,
        };

        if (retryAfterSeconds is { } seconds)
        {
            // 重试间隔两个地方都要写：响应头是给通用的 HTTP 客户端看的，
            // 正文里那一份是给只解析正文的调用方看的。少一个，总有一类调用方只能自己猜。
            context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            body["retryAfterSeconds"] = seconds;
        }

        await context.Response.WriteAsync(body.ToJsonString(), context.RequestAborted).ConfigureAwait(false);
    }
}
