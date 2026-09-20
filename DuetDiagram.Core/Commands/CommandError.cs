namespace DuetDiagram.Core.Commands;

/// <summary>
/// 结构化错误。
/// </summary>
/// <remarks>
/// 方案 §4.4 定义 <c>Payload</c> 为「可选对象」。本轮用 <c>string?</c>（承载 id / 字段名等），
/// 保证 AOT 源生成下无需多态注册；需要富载荷时改为注册的 <c>CommandErrorPayload</c> 联合类型。
/// AGENTS.md 约定 9：Payload 不携带异常类型名。
/// </remarks>
public sealed record CommandError
{
    public required string Code { get; init; }

    public string? Payload { get; init; }

    public static CommandError Of(string code, string? payload = null) => new() { Code = code, Payload = payload };
}
