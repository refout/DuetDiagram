using System.Globalization;

namespace DuetDiagram.Core.Shapes;

/// <summary>
/// 单位框里的一个点。
/// </summary>
/// <remarks>
/// <para>
/// 坐标取 0 到 1，与外接矩形相乘才是实际位置。用归一化坐标而不是绝对坐标，
/// 是因为一份几何要能画在任意大小的框里——同一个菱形画在 40×40 与 200×80 上，
/// 定义只有一份。
/// </para>
/// <para>
/// 与绘制列表里那个点类型分开：这个属于形状库，是"形状长什么样"的定义；
/// 那个属于一次绘制，是"这次画在哪儿"。两者的生命周期不同。
/// </para>
/// </remarks>
/// <param name="X">横向位置，0 是左边缘、1 是右边缘。</param>
/// <param name="Y">纵向位置，0 是上边缘、1 是下边缘。</param>
public readonly record struct ShapePoint(double X, double Y)
{
    /// <inheritdoc/>
    public override string ToString() => $"({Format(X)},{Format(Y)})";

    /// <summary>数字的规范写法。与绘制列表同一口径：不变文化、限定位数。</summary>
    internal static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// 圆角矩形的圆角从哪来。
/// </summary>
/// <remarks>
/// 圆角半径不是一个固定的几何量：同一个"圆角矩形"形状，节点上填了半径就用填的，
/// 没填就用主题兜底；而"胶囊"的圆角由短边算出来，填什么都不算数。
/// 这三种来源要分得清，否则形状库就没法把半径这件事说全。
/// </remarks>
public enum CornerRadiusMode
{
    /// <summary>直角。样式里填了半径也忽略。</summary>
    None,

    /// <summary>用样式里的半径；没填时用主题的兜底值。</summary>
    FromStyle,

    /// <summary>短边的一半。样式与主题都不参与。</summary>
    HalfMinSide,
}

/// <summary>
/// 一个形状的几何：与绘制方无关的一份描述。
/// </summary>
/// <remarks>
/// <para>
/// **它不含任何绘制库的类型。** 形状库在 Core 里，Core 不依赖 Avalonia；
/// 绘制方拿到这份描述之后自己转成它那套路径类型。这样同一份几何能被画布、
/// 导出器（SVG / PNG）各画一遍，而不必各写一份形状表。
/// </para>
/// <para>
/// 用抽象基类加派生密封记录，理由与绘制指令一致：读取方要按类型分发，
/// 而"类型字段加万能载荷"的写法里，任何一处忘了判类型都会静默走错分支。
/// </para>
/// <para>
/// 派生类型名一律带 <c>Outline</c> 后缀：绘制库里通常已经有叫
/// <c>EllipseGeometry</c> / <c>PathGeometry</c> 的类型，撞名会让每个用到两边的地方
/// 都要写别名。名字带后缀这件事在调用点上看得见，别名看不见。
/// </para>
/// </remarks>
public abstract record ShapeGeometry
{
    /// <summary>规范文本，供比较与错误信息使用。</summary>
    public abstract string Describe();
}

/// <summary>
/// 圆角矩形。直角、圆角与胶囊都是它，区别只在圆角从哪来。
/// </summary>
/// <param name="Corners">圆角从哪来。</param>
public sealed record RoundedRectOutline(CornerRadiusMode Corners) : ShapeGeometry
{
    /// <inheritdoc/>
    public override string Describe() => $"rounded-rect({Corners})";
}

/// <summary>椭圆。圆形是它的特例：外接矩形是正方形时就是圆。</summary>
public sealed record EllipseOutline : ShapeGeometry
{
    /// <inheritdoc/>
    public override string Describe() => "ellipse";
}

/// <summary>
/// 闭合多边形。顶点按顺序给，最后一点自动连回第一点。
/// </summary>
/// <param name="Points">顶点，单位框坐标。</param>
public sealed record PolygonOutline(IReadOnlyList<ShapePoint> Points) : ShapeGeometry
{
    /// <summary>
    /// 结构化相等。
    /// </summary>
    /// <remarks>
    /// 必须重写：记录自动生成的相等性对集合用引用比较，会把顶点完全相同的两个多边形判为不等。
    /// </remarks>
    public bool Equals(PolygonOutline? other) =>
        other is not null
        && Points.Count == other.Points.Count
        && Points.SequenceEqual(other.Points);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Points.Count);

        foreach (var point in Points)
        {
            hash.Add(point);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string Describe() => $"polygon[{string.Join(" ", Points)}]";
}

/// <summary>
/// 一条路径线段。
/// </summary>
/// <remarks>
/// 只有直线与椭圆弧两种。圆柱那种带弧的轮廓用它；再多的段类型等真有形状需要时再加。
/// </remarks>
public abstract record PathSegment;

/// <summary>一条直线，走到给定的点。</summary>
/// <param name="To">终点，单位框坐标。</param>
public sealed record PathLine(ShapePoint To) : PathSegment;

/// <summary>
/// 一段椭圆弧，走到给定的点。
/// </summary>
/// <param name="To">终点，单位框坐标。</param>
/// <param name="RadiusX">横向半径，单位框坐标。</param>
/// <param name="RadiusY">纵向半径，单位框坐标。</param>
/// <param name="Clockwise">顺时针还是逆时针。</param>
public sealed record PathArc(ShapePoint To, double RadiusX, double RadiusY, bool Clockwise) : PathSegment;

/// <summary>
/// 任意闭合路径。起点加一串线段。
/// </summary>
/// <param name="Start">起点，单位框坐标。</param>
/// <param name="Segments">从起点出发的线段。</param>
public sealed record PathOutline(ShapePoint Start, IReadOnlyList<PathSegment> Segments) : ShapeGeometry
{
    /// <summary>
    /// 结构化相等。
    /// </summary>
    /// <remarks>理由与 <see cref="PolygonOutline"/> 相同：集合要用值比较。</remarks>
    public bool Equals(PathOutline? other) =>
        other is not null
        && Start.Equals(other.Start)
        && Segments.Count == other.Segments.Count
        && Segments.SequenceEqual(other.Segments);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Start);
        hash.Add(Segments.Count);

        foreach (var segment in Segments)
        {
            hash.Add(segment);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string Describe() =>
        $"path[{Start} {string.Join(" ", Segments.Select(DescribeSegment))}]";

    private static string DescribeSegment(PathSegment segment) => segment switch
    {
        PathLine line => $"L{line.To}",
        PathArc arc => $"A{arc.To}/{(arc.Clockwise ? "cw" : "ccw")}",
        _ => throw new NotSupportedException($"认不出的路径线段：{segment.GetType().Name}"),
    };
}
