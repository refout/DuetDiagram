namespace DuetDiagram.Core.Commands;

/// <summary>字段变更种类。</summary>
public enum ChangeKind
{
    Added,
    Modified,
    Removed,
    Moved,
    Resized,
}

/// <summary>
/// 单字段变更记录，用于版本 diff、审计与变更高亮。
/// </summary>
/// <remarks>
/// 值统一用 <c>string?</c> 承载（规范化 JSON 比较见 P1 判据 #7）。
/// 方案 §4.4 的 <c>PositionValue</c> / <c>SizeValue</c> 在引入坐标类命令（Phase 2）时补充。
/// </remarks>
public sealed record FieldChange
{
    public required string ElementId { get; init; }

    public required string Field { get; init; }

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }

    public ChangeKind Kind { get; init; }
}
