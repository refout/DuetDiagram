namespace DuetDiagram.Core.Model;

/// <summary>
/// 数学排版模式。
/// </summary>
/// <remarks>
/// 分成行内与独立成行两档，是因为两者的排版方式差别很大：
/// 行内的基线要与周围文字对齐，独立成行的要单独占一块并居中。
/// 合成一个布尔值会让渲染层无从判断该用哪种。
/// </remarks>
public enum MathMode
{
    None,
    Inline,
    Block,
}

/// <summary>
/// 节点定义。只存语义（画什么、叫什么、什么形状），不存坐标。
/// 坐标由布局引擎每次计算，人工拖动则记录在文档之外的 sidecar 里，不污染语义层。
/// </summary>
public sealed record NodeDef : IDefinition
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
    /// <remarks>
    /// 与组合的成员列表表达的是同一件事，约定以成员列表为准，本字段是冗余索引。
    /// 两者必须一致，校验见 <c>DiagramValidator</c>。
    /// </remarks>
    public string? Parent { get; init; }

    /// <summary>所属图层。只影响可见性与归属，不影响布局计算。</summary>
    public string? Layer { get; init; }

    /// <summary>
    /// 所属页面。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **它进结构哈希**：布局是按页算的，同一份文档在第二页上解出来的坐标与在第一页上
    /// 不是一回事。报成外观变更的话，翻页之后不重排。
    /// </para>
    /// <para>
    /// **空值指向缺省页**（次序最小的那一页），指向一个不存在的页面也按缺省页处理。
    /// 这条口径在 <see cref="PageMembership"/> 里只有一份——布局、渲染、导出与摘要
    /// 四处都调它，各判一次的话表现是"翻页之后有几个元素赖着不走"。
    /// </para>
    /// </remarks>
    public string? Page { get; init; }

    /// <summary>调色板令牌名（例如 primary、danger）。属于视觉信息，已展开后的具体颜色不入 IR。</summary>
    public string? StyleToken { get; init; }

    /// <summary>具体样式。用于令牌表达不了的场合，与令牌同时存在时以本记录为准。</summary>
    public NodeStyle? Style { get; init; }

    /// <summary>文本样式。字段全部可选，未声明的部分逐层继承。</summary>
    public TextStyle? Text { get; init; }

    /// <summary>
    /// 端口列表。为空表示由布局引擎按形状自动均分。
    /// </summary>
    /// <remarks>
    /// 端口计入结构哈希：它不改变节点坐标，但改变连线的出入点，
    /// 而走线属于布局求解的一部分。
    /// </remarks>
    public IReadOnlyList<PortDef> Ports { get; init; } = [];

    /// <summary>标签是否按富文本解析。为假时标签里的标记符号原样显示。</summary>
    public bool RichText { get; init; }

    /// <summary>数学排版模式。</summary>
    public MathMode MathMode { get; init; } = MathMode.None;

    /// <summary>给人看的补充说明。不参与渲染，也不参与布局。</summary>
    public string? Desc { get; init; }

    /// <summary>宿主自定义的附加数据。不参与渲染、布局与任何哈希。</summary>
    public IReadOnlyDictionary<string, string> Meta { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>取指定名称的端口。找不到返回空。</summary>
    public PortDef? FindPort(string? name) =>
        name is null ? null : Ports.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// 结构化相等。
    /// </summary>
    /// <remarks>
    /// 必须重写：<see cref="Ports"/> 与 <see cref="Meta"/> 是集合，
    /// 而记录自动生成的相等性对集合用引用比较，会把内容相同的两份定义判为不等。
    /// </remarks>
    public bool Equals(NodeDef? other) =>
        other is not null
        && string.Equals(Id, other.Id, StringComparison.Ordinal)
        && string.Equals(Label, other.Label, StringComparison.Ordinal)
        && Shape == other.Shape
        && string.Equals(Parent, other.Parent, StringComparison.Ordinal)
        && string.Equals(Layer, other.Layer, StringComparison.Ordinal)
        && string.Equals(Page, other.Page, StringComparison.Ordinal)
        && string.Equals(StyleToken, other.StyleToken, StringComparison.Ordinal)
        && Equals(Style, other.Style)
        && Equals(Text, other.Text)
        && CollectionEquality.List(Ports, other.Ports)
        && RichText == other.RichText
        && MathMode == other.MathMode
        && string.Equals(Desc, other.Desc, StringComparison.Ordinal)
        && CollectionEquality.Map(Meta, other.Meta);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Id, StringComparer.Ordinal);
        hash.Add(Label, StringComparer.Ordinal);
        hash.Add(Shape);
        hash.Add(Parent, StringComparer.Ordinal);
        hash.Add(Layer, StringComparer.Ordinal);
        hash.Add(Page, StringComparer.Ordinal);
        hash.Add(StyleToken, StringComparer.Ordinal);
        hash.Add(Style);
        hash.Add(Text);
        hash.Add(CollectionEquality.ListHash(Ports));
        hash.Add(RichText);
        hash.Add(MathMode);
        hash.Add(Desc, StringComparer.Ordinal);
        hash.Add(CollectionEquality.MapHash(Meta));

        return hash.ToHashCode();
    }
}
