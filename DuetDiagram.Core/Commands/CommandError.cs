namespace DuetDiagram.Core.Commands;

/// <summary>
/// 结构化错误：一个错误码加一段可选说明。
/// </summary>
/// <remarks>
/// <see cref="Payload"/> 只承载标识、字段名这类短字符串。
/// 有一条硬规矩：**绝不把异常类型名写进来**。
/// 原因是审计日志会长期保留并可能被外部读到，异常类型名会暴露内部实现细节，
/// 攻击者能据此推断代码结构。类型名只写进应用日志（诊断出口），审计日志里永远是干净的。
/// </remarks>
public sealed record CommandError
{
    public required string Code { get; init; }

    public string? Payload { get; init; }

    public static CommandError Of(string code, string? payload = null) => new() { Code = code, Payload = payload };
}
