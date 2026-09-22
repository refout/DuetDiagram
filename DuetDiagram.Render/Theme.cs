using DuetDiagram.Core.Model;

namespace DuetDiagram.Render;

/// <summary>
/// 节点或容器解析之后的外观。
/// </summary>
/// <remarks>
/// 颜色都是具体值。解析的先后次序是：元素上写死的字段、调色板令牌、主题兜底。
/// 这个次序与数据模型里的说法一致——令牌表达语义、具体字段是有意为之的例外，
/// 所以例外优先。反过来排的话，用户在一处写的颜色会被另一处的令牌悄悄盖掉。
/// </remarks>
public sealed record NodeAppearance(
    string Fill,
    string Stroke,
    string Text,
    double Weight,
    double Radius,
    double Opacity,
    LineStyle Border);

/// <summary>边解析之后的外观。</summary>
public sealed record EdgeAppearance(string Color, double Weight, LineStyle Line, ArrowStyle Arrow);

/// <summary>文本解析之后的样式。</summary>
public sealed record TextAppearance(
    string Color,
    string FontFamily,
    double FontSize,
    FontWeight Weight,
    TextAlign Align,
    VerticalAlign Vertical,
    double LineHeight);

/// <summary>
/// 外观查表。
/// </summary>
/// <remarks>
/// <para>
/// **它是显式参数，不是全局状态。** 同一份文档配两套主题要能给出两份绘制列表——
/// 导出浅色版与深色版就是同一个文档跑两遍。做成静态默认值之后，
/// 这件事只能靠"改全局再跑一遍"来做，而那时快照测试会互相干扰。
/// </para>
/// <para>
/// 这里只有兜底值加一张调色板。更丰富的主题（字体族、圆角、间距的整体替换）
/// 等主题那一项开工时再加，提前塞一批字段只会让现在这份没人用的配置成为负担。
/// </para>
/// </remarks>
public sealed record Theme
{
    /// <summary>画布背景。</summary>
    public string Background { get; init; } = "#ffffff";

    /// <summary>节点兜底填充色。</summary>
    public string NodeFill { get; init; } = "#f2f5f9";

    /// <summary>节点兜底描边色。</summary>
    public string NodeStroke { get; init; } = "#39424f";

    /// <summary>节点兜底文字色。</summary>
    public string NodeText { get; init; } = "#161d26";

    /// <summary>连线兜底色。</summary>
    public string EdgeStroke { get; init; } = "#5b6675";

    /// <summary>连线标签兜底文字色。</summary>
    public string EdgeText { get; init; } = "#39424f";

    /// <summary>组合兜底填充色。</summary>
    public string CompositeFill { get; init; } = "#e9eef5";

    /// <summary>组合兜底描边色。</summary>
    public string CompositeStroke { get; init; } = "#93a0b0";

    /// <summary>兜底描边粗细。</summary>
    public double StrokeWeight { get; init; } = 1.5;

    /// <summary>圆角形状的兜底圆角半径。</summary>
    public double CornerRadius { get; init; } = 8;

    /// <summary>兜底字体名。</summary>
    public string FontFamily { get; init; } = "Inter";

    /// <summary>兜底字号。</summary>
    public double FontSize { get; init; } = 14;

    /// <summary>行高倍数。按字号乘它得到一行的高度。</summary>
    public double LineHeight { get; init; } = 1.35;

    /// <summary>节点标签与框之间的横向留白。</summary>
    public double NodePaddingX { get; init; } = 16;

    /// <summary>节点标签与框之间的纵向留白。</summary>
    public double NodePaddingY { get; init; } = 10;

    /// <summary>
    /// 节点框的最小宽度。
    /// </summary>
    /// <remarks>
    /// 没有它，一个字的标签会得到一个很窄的框，看上去像画错了。
    /// 上下限只加在测量这一步，不影响布局——布局拿到的是已经定下来的尺寸。
    /// </remarks>
    public double MinNodeWidth { get; init; } = 60;

    /// <summary>节点框的最小高度。</summary>
    public double MinNodeHeight { get; init; } = 36;

    /// <summary>组合框与成员之间的留白。</summary>
    public double CompositePadding { get; init; } = 18;

    /// <summary>组合标题占的高度。</summary>
    public double CompositeHeader { get; init; } = 24;

    /// <summary>
    /// 视口能缩到的最小倍数。
    /// </summary>
    /// <remarks>
    /// 再往下缩，节点只剩几个像素，整张图看上去像一块灰斑，用户会以为文档是空的。
    /// 放在主题里而不是写死在滚轮处理里，是因为它是一条产品行为——
    /// 散在事件处理里就没人找得到，改的人只能靠搜魔数。
    /// </remarks>
    public double MinZoom { get; init; } = 0.1;

    /// <summary>
    /// 视口能放到的最大倍数。
    /// </summary>
    /// <remarks>
    /// 上界拦的是"放大到只剩一个色块"：再大也看不出更多信息，
    /// 而放大倍数越高，同样的平移误差被放得越大。
    /// </remarks>
    public double MaxZoom { get; init; } = 8;

    /// <summary>调色板。令牌名到具体外观的映射。</summary>
    public Palette Palette { get; init; } = new();

    /// <summary>缺省主题。</summary>
    public static Theme Default { get; } = new();

    /// <summary>换一份调色板，其余不变。</summary>
    public Theme WithPalette(Palette? palette) => this with { Palette = palette ?? new Palette() };

    /// <summary>解析节点的外观。</summary>
    public NodeAppearance Node(NodeDef node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var token = Palette.Find(node.StyleToken);
        var style = node.Style;

        return new NodeAppearance(
            ResolveFill(style?.Fill, token, NodeFill),
            ResolveStroke(style?.Stroke, token, NodeStroke),
            ResolveText(style?.Text, token, NodeText),
            style?.Weight ?? token?.Weight ?? StrokeWeight,
            style?.Radius ?? RadiusFor(node.Shape),
            style?.Opacity ?? 1,
            style?.Border ?? LineStyle.Solid);
    }

    /// <summary>
    /// 解析组合的外观。组合没有形状字段，一律画成圆角框。
    /// </summary>
    /// <remarks>
    /// 标题文字沿用节点的文字色：两者都是浅底深字，分开配两份颜色目前没有意义，
    /// 等主题那一项真的要做深色模式时再分。
    /// </remarks>
    public NodeAppearance Composite(CompositeDef composite)
    {
        ArgumentNullException.ThrowIfNull(composite);

        var style = composite.Style;

        return new NodeAppearance(
            ResolveFill(style?.Fill, null, CompositeFill),
            ResolveStroke(style?.Stroke, null, CompositeStroke),
            ResolveText(style?.Text, null, NodeText),
            style?.Weight ?? StrokeWeight,
            style?.Radius ?? CornerRadius,
            style?.Opacity ?? 1,
            style?.Border ?? LineStyle.Solid);
    }

    /// <summary>
    /// 解析边的外观。
    /// </summary>
    /// <remarks>
    /// 线色取调色板条目的描边色而不是填充色：一条线没有"里面"，
    /// 给它配填充色的令牌是给面用的，拿它的填充色当线色会得到意外的结果。
    /// </remarks>
    public EdgeAppearance Edge(EdgeDef edge)
    {
        ArgumentNullException.ThrowIfNull(edge);

        var token = Palette.Find(edge.StyleToken);

        return new EdgeAppearance(
            ResolveStroke(edge.Style.Color, token, EdgeStroke),
            edge.Style.Weight ?? token?.Weight ?? StrokeWeight,
            edge.Line,
            edge.Arrow);
    }

    /// <summary>
    /// 解析文本样式。
    /// </summary>
    /// <param name="style">元素上声明的文本样式。为空表示全部取兜底。</param>
    /// <param name="fallbackColor">颜色兜底。来自所在元素解析出来的文字色。</param>
    public TextAppearance Text(TextStyle? style, string fallbackColor)
    {
        var family = style?.FontFamily;

        return new TextAppearance(
            ResolveText(style?.FontColor, null, fallbackColor),
            string.IsNullOrWhiteSpace(family) ? FontFamily : family,
            style?.FontSize ?? FontSize,
            style?.FontWeight ?? FontWeight.Normal,
            style?.Align ?? TextAlign.Center,
            style?.VerticalAlign ?? VerticalAlign.Middle,
            style?.LineHeight ?? LineHeight);
    }

    /// <summary>
    /// 形状自带的圆角。
    /// </summary>
    /// <remarks>
    /// 圆角只对这两种形状有意义。其余形状忽略半径而不是报错——
    /// 半径是样式里的一个通用字段，用户给菱形也填了它并不算错，只是画的时候用不上。
    /// </remarks>
    private double RadiusFor(NodeShape shape) =>
        shape is NodeShape.Rounded or NodeShape.Stadium ? CornerRadius : 0;

    /// <summary>
    /// 解析填充色。
    /// </summary>
    /// <remarks>
    /// 三处颜色各自一个方法而不是合成一个带角色参数的：角色只影响"从条目的哪个字段取"，
    /// 合成之后每个调用点都要多写一个枚举值，读起来比三个短方法更绕。
    /// </remarks>
    private string ResolveFill(string? value, PaletteEntry? token, string fallback) =>
        value is null
            ? token?.Fill ?? fallback
            : Palette.Find(value) is { } named ? named.Fill ?? value : value;

    /// <inheritdoc cref="ResolveFill"/>
    private string ResolveStroke(string? value, PaletteEntry? token, string fallback) =>
        value is null
            ? token?.Stroke ?? fallback
            : Palette.Find(value) is { } named ? named.Stroke ?? value : value;

    /// <summary>
    /// 解析文字色。
    /// </summary>
    /// <remarks>
    /// 显式取值在调色板里查不到时**按颜色字面量处理**，而不是取兜底。
    /// 令牌名拼错的表现因此是"颜色不对"，而不是"整块变成默认色"——
    /// 后者会让人以为渲染坏了，而其实只是拼错了一个词。
    /// </remarks>
    private string ResolveText(string? value, PaletteEntry? token, string fallback) =>
        value is null
            ? token?.Text ?? fallback
            : Palette.Find(value) is { } named ? named.Text ?? value : value;
}
