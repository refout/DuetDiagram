namespace DuetDiagram.Core.Model;

/// <summary>
/// 边定义。只记录拓扑（从哪到哪、走哪个端口）和外观，不记录路径点——
/// 折线由布局引擎算出来，人工调整的路径点存在 sidecar 里。
/// </summary>
/// <remarks>
/// <para>
/// 外观全部收在 <see cref="Style"/> 里，包括线型、箭头与样式令牌。
/// 这一点与节点不同：节点的样式令牌留在顶层，因为它是最常用的样式手段，
/// 而边没有这种主次之分，全部收在一处更整齐。
/// </para>
/// <para>
/// 这个不对称是方案本身的规定，不是疏漏。记在这里是为了避免后来者
/// 把节点也改成同样的形状，或者反过来把边的字段提回顶层——
/// 两种"统一"都会打乱已有的线格式。
/// </para>
/// </remarks>
public sealed record EdgeDef : IDefinition
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

    public EdgeStyle Style { get; init; } = new();

    /// <summary>线型的便捷读取。未声明时按实线处理，与渲染的缺省行为一致。</summary>
    public LineStyle Line => Style.Line ?? LineStyle.Solid;

    /// <summary>箭头的便捷读取。未声明时按有箭头处理。</summary>
    public ArrowStyle Arrow => Style.Arrow ?? ArrowStyle.Arrow;

    /// <summary>样式令牌的便捷读取。</summary>
    public string? StyleToken => Style.StyleToken;
}
