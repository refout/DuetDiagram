namespace DuetDiagram.Core.Commands;

/// <summary>
/// 命令的前置检查结果。
/// </summary>
/// <remarks>
/// 用一个记录而不是布尔值加输出参数，是为了让"没有错误"和"有哪些错误"用同一个对象表达。
/// <see cref="Valid"/> 是共享的无错实例，命令实现直接返回它即可，不必每次分配。
/// </remarks>
public sealed record ValidationResult
{
    public static ValidationResult Valid { get; } = new();

    public bool IsValid => Errors.Length == 0;

    public CommandError[] Errors { get; init; } = [];

    public static ValidationResult Invalid(params CommandError[] errors) => new() { Errors = errors };
}
