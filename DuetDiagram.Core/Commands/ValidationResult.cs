namespace DuetDiagram.Core.Commands;

/// <summary>命令前置校验结果。</summary>
public sealed record ValidationResult
{
    public static ValidationResult Valid { get; } = new();

    public bool IsValid => Errors.Length == 0;

    public CommandError[] Errors { get; init; } = [];

    public static ValidationResult Invalid(params CommandError[] errors) => new() { Errors = errors };
}
