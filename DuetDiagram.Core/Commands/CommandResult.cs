using DuetDiagram.Core.Logging;

namespace DuetDiagram.Core.Commands;

/// <summary>
/// 命令执行结果。8 个字段（方案 §4.4）。
/// </summary>
public sealed record CommandResult
{
    public bool IsSuccess { get; init; }

    public bool IsNoOp { get; init; }

    public string? Message { get; init; }

    public CommandError[] Errors { get; init; } = [];

    public string[] AffectedIds { get; init; } = [];

    public FieldChange[] FieldChanges { get; init; } = [];

    public bool StructuralChanged { get; init; }

    public bool VisualChanged { get; init; }

    /// <summary>成功且非空操作。只有它为真才应触发重布局 / 重绘 / 广播。</summary>
    public bool IsEffectiveSuccess => IsSuccess && !IsNoOp;

    /// <summary>错误码可重试。</summary>
    public bool IsRetryable => Errors.Any(e =>
        string.Equals(e.Code, ErrorCodes.VersionConflict, StringComparison.Ordinal) ||
        string.Equals(e.Code, ErrorCodes.McpRateLimited, StringComparison.Ordinal));

    public static CommandResult Ok(
        string[]? affected = null,
        FieldChange[]? changes = null,
        bool structural = false,
        bool visual = false,
        string? message = null) => new()
        {
            IsSuccess = true,
            AffectedIds = affected ?? [],
            FieldChanges = changes ?? [],
            StructuralChanged = structural,
            VisualChanged = visual,
            Message = message,
        };

    public static CommandResult NoOp(string? message = null) => new()
    {
        IsSuccess = true,
        IsNoOp = true,
        Message = message,
    };

    public static CommandResult Fail(params CommandError[] errors) => new() { Errors = errors };

    public static CommandResult FailWith(string message, params CommandError[] errors) => new()
    {
        Message = message,
        Errors = errors,
    };

    /// <summary>
    /// 按 <see cref="DiffResult"/> 类型分发冲突结果（方案 §4.4）。
    /// </summary>
    public static CommandResult Conflict(DiffResult diff) => diff switch
    {
        InvalidDiff => Fail(CommandError.Of(ErrorCodes.InvalidExpectedVersion)),
        _ => Fail(CommandError.Of(ErrorCodes.VersionConflict)),
    };
}
