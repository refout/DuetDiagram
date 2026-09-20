namespace DuetDiagram.Core.Model;

/// <summary>
/// 边定义。只记录拓扑（从哪到哪、走哪个端口）和外观，不记录路径点——
/// 折线由布局引擎算出来，人工调整的路径点存在 sidecar 里。
/// </summary>
public sealed record EdgeDef
{
    /// <summary>边标识。同一文档内唯一。</summary>
    public required string Id { get; init; }

    /// <summary>起点节点标识。必须存在于 <see cref="DiagramDocument.Nodes"/> 中。</summary>
    public required string From { get; init; }

    /// <summary>终点节点标识。必须存在于 <see cref="DiagramDocument.Nodes"/> 中。</summary>
    public required string To { get; init; }

    /// <summary>起点端口名。为空表示由布局引擎自动选边。</summary>
    public string? FromPort { get; init; }

    /// <summary>终点端口名。为空表示由布局引擎自动选边。</summary>
    public string? ToPort { get; init; }

    /// <summary>边上的文字，例如"是""否"。</summary>
    public string Label { get; init; } = string.Empty;

    public LineStyle Line { get; init; } = LineStyle.Solid;

    public ArrowStyle Arrow { get; init; } = ArrowStyle.Arrow;

    /// <summary>调色板令牌名，与 <see cref="NodeDef.StyleToken"/> 共用同一套令牌。</summary>
    public string? StyleToken { get; init; }
}
