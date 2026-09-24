using System.Globalization;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Shapes;

namespace DuetDiagram.Render;

/// <summary>
/// 绘制列表里的一个点。
/// </summary>
/// <remarks>
/// 与空间索引里的矩形同理：一条折线就有好几个点，千节点图上这个类型会被存下成千上万个，
/// 值类型省掉每一次分配与解引用。
/// <para>
/// 它也是绘制列表与布局层之间的**边界**。列表用自己的点类型而不是布局层的那个，
/// 是为了让这份数据在换掉布局引擎之后原样可用——折线的点序是布局的输出，
/// 但"点"这个概念不是它定义的。
/// </para>
/// </remarks>
public readonly record struct DrawPoint(double X, double Y)
{
    public override string ToString() => $"({Numbers.Format(X)}, {Numbers.Format(Y)})";
}

/// <summary>
/// 一条绘制指令。
/// </summary>
/// <remarks>
/// <para>
/// 抽象基类加派生密封记录，理由与命令的备忘录一致：读取方要按类型分发，
/// 而"类型字段加万能载荷"的写法里，任何一处忘了判类型都会静默走错分支。
/// </para>
/// <para>
/// 每条指令都带**元素标识**。它不参与绘制，但绘制列表要能回答"这一块是谁画的"——
/// 命中测试、变更高亮、快照差异定位都靠它。没有它，一份列表就只是一堆图形。
/// </para>
/// <para>
/// **颜色在这里是已经解析好的值**，不是令牌名。令牌的解析依赖调色板与主题，
/// 那一步在构建列表时就做完了；把令牌留在列表里，等于要求每一个绘制方
/// 各自再查一次表，而两处查表迟早会不一致。
/// </para>
/// </remarks>
/// <param name="ElementId">这条指令画的是哪个元素。</param>
public abstract record DrawCommand(string ElementId)
{
    /// <summary>
    /// 规范文本，供快照比较与差异定位使用。
    /// </summary>
    /// <remarks>
    /// 数字一律按不变文化格式化并限定小数位。同一份输入在换一台机器、
    /// 换一个区域设置之后必须给出同一份文本，否则快照会在别人机器上变红，
    /// 而那种红看起来像真的画错了。
    /// </remarks>
    public abstract string Describe();
}

/// <summary>画一个封闭形状。节点、组合与将来的其它容器都用它。</summary>
/// <param name="ElementId">元素标识。</param>
/// <param name="Shape">形状。</param>
/// <param name="Rect">外接矩形。</param>
/// <param name="Fill">填充色，已解析。</param>
/// <param name="Stroke">描边色，已解析。</param>
/// <param name="Weight">描边粗细。</param>
/// <param name="Border">边框线型。</param>
/// <param name="Radius">圆角半径。形状本身不带圆角时忽略。</param>
/// <param name="Opacity">不透明度，取值 0 到 1。</param>
/// <param name="Geometry">
/// 自定义形状的几何。为空表示按 <paramref name="Shape"/> 去形状表里查。
/// </param>
/// <remarks>
/// **自定义几何在构建列表时就算好，与颜色同一个道理。** 把路径文本留到绘制那一步再解析，
/// 等于要求每个绘制方各自再解析一次，而两处解析迟早不一致；解析本身也比查表贵，
/// 放进每一帧的绘制里是白花的时间。
/// </remarks>
public sealed record DrawShape(
    string ElementId,
    NodeShape Shape,
    SpatialRect Rect,
    string Fill,
    string Stroke,
    double Weight,
    LineStyle Border,
    double Radius,
    double Opacity,
    ShapeGeometry? Geometry = null) : DrawCommand(ElementId)
{
    /// <inheritdoc/>
    public override string Describe() =>
        $"shape {ElementId} {Shape} {Rect} fill={Fill} stroke={Stroke}"
        + $" weight={Numbers.Format(Weight)} border={Border} radius={Numbers.Format(Radius)}"
        + $" opacity={Numbers.Format(Opacity)}"
        + (Geometry is null ? string.Empty : $" geometry={Geometry.Describe()}");
}

/// <summary>
/// 画一行文本。
/// </summary>
/// <remarks>
/// <para>
/// **一行一条指令**，不是一段一条。分行由构建列表的那一步做完，
/// 绘制方拿到的框就是这一行的框，不必再自己做换行——换行依赖文本度量，
/// 而度量是注入进来的，绘制方没有它。
/// </para>
/// <para>
/// 指令里不带对齐方式：<see cref="Box"/> 已经是对齐算完之后的位置。
/// 留着对齐字段等于给了两处可以互相矛盾的真相。
/// </para>
/// </remarks>
/// <param name="ElementId">元素标识。同一个元素的多行共用它。</param>
/// <param name="Text">这一行的文本。</param>
/// <param name="Box">这一行的框。左边缘是起始位置，垂直方向按框中居中排。</param>
/// <param name="Color">文字颜色，已解析。</param>
/// <param name="FontFamily">字体名。</param>
/// <param name="FontSize">字号。</param>
/// <param name="Weight">字重。</param>
public sealed record DrawText(
    string ElementId,
    string Text,
    SpatialRect Box,
    string Color,
    string FontFamily,
    double FontSize,
    FontWeight Weight) : DrawCommand(ElementId)
{
    /// <inheritdoc/>
    public override string Describe() =>
        $"text {ElementId} {Box} \"{Text}\" color={Color}"
        + $" font={FontFamily}/{Numbers.Format(FontSize)}/{Weight}";
}

/// <summary>画一条折线。连线的走线用它。</summary>
/// <param name="ElementId">元素标识。</param>
/// <param name="Points">点序，从起点到终点。</param>
/// <param name="Color">线色，已解析。</param>
/// <param name="Weight">线宽。</param>
/// <param name="Line">线型。</param>
/// <param name="Arrow">终点箭头。</param>
public sealed record DrawPolyline(
    string ElementId,
    IReadOnlyList<DrawPoint> Points,
    string Color,
    double Weight,
    LineStyle Line,
    ArrowStyle Arrow) : DrawCommand(ElementId)
{
    /// <summary>
    /// 结构化相等。
    /// </summary>
    /// <remarks>
    /// 必须重写：<see cref="Points"/> 是集合，记录自动生成的相等性对它用引用比较，
    /// 会把两条点序完全相同的折线判为不等，于是快照永远对不上。
    /// </remarks>
    public bool Equals(DrawPolyline? other) =>
        other is not null
        && string.Equals(ElementId, other.ElementId, StringComparison.Ordinal)
        && string.Equals(Color, other.Color, StringComparison.Ordinal)
        && Weight.Equals(other.Weight)
        && Line == other.Line
        && Arrow == other.Arrow
        && Points.Count == other.Points.Count
        && Points.SequenceEqual(other.Points);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(ElementId, StringComparer.Ordinal);
        hash.Add(Color, StringComparer.Ordinal);
        hash.Add(Weight);
        hash.Add(Line);
        hash.Add(Arrow);
        hash.Add(Points.Count);

        foreach (var point in Points)
        {
            hash.Add(point);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string Describe() =>
        $"polyline {ElementId} color={Color} weight={Numbers.Format(Weight)}"
        + $" line={Line} arrow={Arrow} points=[{string.Join(" ", Points)}]";
}

/// <summary>
/// 数字的规范写法。
/// </summary>
/// <remarks>
/// 三处都用它，是为了让"同一个数在两台机器上写出来一样"这件事只在一处定义。
/// 小数位限定到三位：坐标是测量与布局算出来的，再多的位数只会把浮点噪声写进快照，
/// 让本该一致的两次构建产生不同的文本。
/// </remarks>
internal static class Numbers
{
    public static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
