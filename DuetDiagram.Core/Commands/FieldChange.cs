namespace DuetDiagram.Core.Commands;

/// <summary>单个字段的变更种类，用于差异展示与变更高亮。</summary>
public enum ChangeKind
{
    Added,
    Modified,
    Removed,
    Moved,
    Resized,
}

/// <summary>
/// 一次字段级变更。
/// </summary>
/// <remarks>
/// 新旧值统一用字符串承载。这样做的好处是"规范化序列化后比较"这条判断能直接工作：
/// 只要两个变更的字符串表示相同，就认为它们等价，不需要为每种值类型写比较逻辑。
/// 代价是坐标、尺寸这类结构化的值会被拍平成文本，等引入坐标类命令时再补专门的表示。
/// </remarks>
public sealed record FieldChange
{
    /// <summary>发生变更的元素标识。</summary>
    public required string ElementId { get; init; }

    /// <summary>字段名。用固定的短名（node、edge 等）而不是属性名，避免内部改名污染外部协议。</summary>
    public required string Field { get; init; }

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }

    public ChangeKind Kind { get; init; }
}
