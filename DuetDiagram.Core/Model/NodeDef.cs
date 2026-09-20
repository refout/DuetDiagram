namespace DuetDiagram.Core.Model;

/// <summary>
/// 节点定义。只存语义（画什么、叫什么、什么形状），不存坐标。
/// 坐标由布局引擎每次计算，人工拖动则记录在文档之外的 sidecar 里，不污染语义层。
/// </summary>
public sealed record NodeDef
{
    /// <summary>节点标识。同一文档内唯一，命令层用它定位节点。</summary>
    public required string Id { get; init; }

    /// <summary>显示文本。</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>形状。属于视觉信息，改变它只需要重绘，不需要重布局。</summary>
    public NodeShape Shape { get; init; } = NodeShape.Rect;

    /// <summary>
    /// 所属组合（分组、泳道、子流程）的标识。
    /// 它参与结构哈希：换父级会改变布局的嵌套关系，必须触发重布局。
    /// </summary>
    public string? Parent { get; init; }

    /// <summary>所属图层。只影响可见性与归属，不影响布局计算。</summary>
    public string? Layer { get; init; }

    /// <summary>调色板令牌名（例如 primary、danger）。属于视觉信息，已展开后的具体颜色不入 IR。</summary>
    public string? StyleToken { get; init; }

    /// <summary>给人看的补充说明。不参与渲染，也不参与布局。</summary>
    public string? Desc { get; init; }
}
