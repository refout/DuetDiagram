namespace DuetDiagram.Core.Model;

/// <summary>
/// 节点定义（IR 语义层）。
/// </summary>
/// <remarks>
/// 垂直切片范围：只含布局与命令必需字段。
/// 方案 §4.1 的 Ports / RichText / MathMode / Meta / Text / Style 在 Phase 4 之前不引入，
/// 差异登记在 docs/IR-Schema.md「本轮实现范围」。
/// </remarks>
public sealed record NodeDef
{
    public required string Id { get; init; }

    public string Label { get; init; } = string.Empty;

    public NodeShape Shape { get; init; } = NodeShape.Rect;

    /// <summary>所属组合（分组/泳道/子流程）。Phase 4 前仅作为字符串保留。</summary>
    public string? Parent { get; init; }

    public string? Layer { get; init; }

    /// <summary>调色板令牌名。视觉哈希的组成部分。</summary>
    public string? StyleToken { get; init; }

    /// <summary>给人看的说明，不参与渲染。</summary>
    public string? Desc { get; init; }
}
