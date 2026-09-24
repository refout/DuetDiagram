using System.Globalization;
using System.Text;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Shapes;

namespace DuetDiagram.Render;

/// <summary>
/// 把一份绘制列表写成 SVG。
/// </summary>
/// <remarks>
/// <para>
/// **它只消费绘制列表，不重新遍历文档。** 重新遍历的话，导出与画布成了两条绘制路径，
/// 而两条迟早会对不上——对不上的表现是"导出的图与屏幕上不一样"，而两边各自都自洽。
/// 同一份列表画布画一遍、导出器写一遍，两边对不上的唯一可能就只剩"某一边漏了"
/// 一种，而那种漏有逐类用例守着。
/// </para>
/// <para>
/// **确定性。** 同一份列表导出两次逐字节相同：不带时间戳、不带随机标识、
/// 不依赖集合迭代顺序。带上了的话 SVG 没法进版本控制，
/// 而"导出的图有没有变"这个问题就永远答不了。
/// </para>
/// <para>
/// **认不出的指令抛异常，不静默跳过。** 跳过的话，加一种绘制指令之后导出会少画一样东西，
/// 而没有任何报错——这类静默丢失在本仓已经踩过一次。画布那一边同一条口径。
/// </para>
/// </remarks>
public static class SvgExporter
{
    /// <summary>箭头与画布用同一组尺寸，否则导出的箭头与屏幕上的大小不一样。</summary>
    private const double ArrowLength = 9;

    private const double ArrowWidth = 7;

    /// <summary>
    /// 导出。
    /// </summary>
    /// <remarks>
    /// 产物用的是 Core 那份记录，不是这里另立一个：工具层要把它原样交给调用方，
    /// 而工具层看不见渲染层。两边各一份的话，"丢失清单"就有了两种形状。
    /// </remarks>
    /// <param name="list">绘制列表。</param>
    /// <param name="options">选择。为空时用默认。</param>
    public static SvgExport Export(DrawList list, SvgOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(list);

        var settings = options ?? new SvgOptions();
        var pad = settings.Padding;
        var width = list.Width + (pad * 2);
        var height = list.Height + (pad * 2);
        var svg = new StringBuilder();

        svg.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\"")
            .Append(" width=\"").Append(Number(width)).Append('"')
            .Append(" height=\"").Append(Number(height)).Append('"')
            .Append(" viewBox=\"0 0 ").Append(Number(width)).Append(' ').Append(Number(height)).Append("\">\n");

        if (settings.IncludeBackground)
        {
            svg.Append("  <rect x=\"0\" y=\"0\" width=\"").Append(Number(width))
                .Append("\" height=\"").Append(Number(height))
                .Append("\" fill=\"").Append(Attribute(list.Background)).Append("\"/>\n");
        }

        foreach (var command in list.Commands)
        {
            switch (command)
            {
                case DrawShape shape:
                    AppendShape(svg, shape, pad);
                    break;

                case DrawPolyline polyline:
                    AppendPolyline(svg, polyline, pad);
                    break;

                case DrawText text:
                    AppendText(svg, text, pad);
                    break;

                default:
                    throw new NotSupportedException($"认不出的绘制指令：{command.GetType().Name}");
            }
        }

        svg.Append("</svg>\n");

        return new SvgExport(svg.ToString(), Losses());
    }

    #region 形状

    private static void AppendShape(StringBuilder svg, DrawShape shape, double pad)
    {
        var geometry = shape.Geometry ?? ShapeRegistry.Default.Find(shape.Shape).Geometry;
        var rect = new SpatialRect(shape.Rect.X + pad, shape.Rect.Y + pad, shape.Rect.Width, shape.Rect.Height);
        var fill = Attribute(shape.Fill);
        var stroke = Attribute(shape.Stroke);

        var common = new StringBuilder()
            .Append(" fill=\"").Append(fill).Append('"')
            .Append(" stroke=\"").Append(stroke).Append('"')
            .Append(" stroke-width=\"").Append(Number(shape.Weight)).Append('"')
            .Append(Dash(shape.Border))
            .ToString();

        var opacity = shape.Opacity >= 1
            ? string.Empty
            : " opacity=\"" + Number(shape.Opacity) + "\"";

        switch (geometry)
        {
            case RoundedRectOutline corners:
                var radius = Radius(corners.Corners, shape, rect);
                svg.Append("  <rect").Append(Rect(rect))
                    .Append(radius > 0 ? " rx=\"" + Number(radius) + "\" ry=\"" + Number(radius) + "\"" : string.Empty)
                    .Append(common).Append(opacity).Append("/>\n");
                break;

            case EllipseOutline:
                svg.Append("  <ellipse cx=\"").Append(Number(rect.X + (rect.Width / 2)))
                    .Append("\" cy=\"").Append(Number(rect.Y + (rect.Height / 2)))
                    .Append("\" rx=\"").Append(Number(rect.Width / 2))
                    .Append("\" ry=\"").Append(Number(rect.Height / 2))
                    .Append('"').Append(common).Append(opacity).Append("/>\n");
                break;

            case PolygonOutline polygon:
                svg.Append("  <polygon points=\"")
                    .Append(string.Join(' ', polygon.Points.Select(point => $"{Number(rect.X + (point.X * rect.Width))},{Number(rect.Y + (point.Y * rect.Height))}")))
                    .Append('"').Append(common).Append(opacity).Append("/>\n");
                break;

            case PathOutline path:
                svg.Append("  <path d=\"").Append(Path(path, rect))
                    .Append('"').Append(common).Append(opacity).Append("/>\n");
                break;

            default:
                throw new NotSupportedException($"认不出的形状几何：{geometry.GetType().Name}");
        }
    }

    /// <summary>圆角半径。三种来源分开算，与形状库那一边同一口径。</summary>
    private static double Radius(CornerRadiusMode corners, DrawShape shape, SpatialRect rect) => corners switch
    {
        CornerRadiusMode.None => 0,
        CornerRadiusMode.HalfMinSide => Math.Min(rect.Width, rect.Height) / 2,
        _ => shape.Radius,
    };

    /// <summary>一条路径：起点、若干直线段与椭圆弧。</summary>
    private static string Path(PathOutline outline, SpatialRect rect)
    {
        var path = new StringBuilder();

        path.Append('M').Append(Point(outline.Start, rect));

        foreach (var segment in outline.Segments)
        {
            switch (segment)
            {
                case PathLine line:
                    path.Append('L').Append(Point(line.To, rect));
                    break;

                case PathArc arc:
                    // 弧的两个标志位与画布那一边保持一致：半径按框缩放、不取大弧、
                    // 方向由几何自己说。三处不一致的话，圆柱这类形状在导出里会翻个面。
                    path.Append('A')
                        .Append(Number(arc.RadiusX * rect.Width)).Append(' ')
                        .Append(Number(arc.RadiusY * rect.Height)).Append(" 0 0 ")
                        .Append(arc.Clockwise ? '1' : '0').Append(' ')
                        .Append(Point(arc.To, rect));
                    break;

                default:
                    throw new NotSupportedException($"认不出的路径线段：{segment.GetType().Name}");
            }
        }

        return path.Append('Z').ToString();
    }

    private static string Point(ShapePoint point, SpatialRect rect) =>
        $"{Number(rect.X + (point.X * rect.Width))} {Number(rect.Y + (point.Y * rect.Height))}";

    #endregion

    #region 连线

    private static void AppendPolyline(StringBuilder svg, DrawPolyline polyline, double pad)
    {
        if (polyline.Points.Count < 2)
        {
            return;
        }

        var points = polyline.Points
            .Select(point => $"{Number(point.X + pad)},{Number(point.Y + pad)}")
            .ToArray();

        svg.Append("  <polyline points=\"").Append(string.Join(' ', points))
            .Append("\" fill=\"none\" stroke=\"").Append(Attribute(polyline.Color))
            .Append("\" stroke-width=\"").Append(Number(polyline.Weight)).Append('"')
            .Append(Dash(polyline.Line))
            .Append("/>\n");

        AppendArrow(svg, polyline, pad);
    }

    /// <summary>终点箭头。方向取最后一段的走向，与画布同一条口径。</summary>
    private static void AppendArrow(StringBuilder svg, DrawPolyline polyline, double pad)
    {
        if (polyline.Arrow == ArrowStyle.None)
        {
            return;
        }

        var tip = polyline.Points[^1];
        var previous = polyline.Points[^2];
        var dx = tip.X - previous.X;
        var dy = tip.Y - previous.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));

        // 最后一段长度为零时什么都不画：方向无从谈起，硬取一个默认方向会画出一个
        // 指向错误一侧的箭头。
        if (length < 1e-6)
        {
            return;
        }

        var ux = dx / length;
        var uy = dy / length;
        var backX = tip.X - (ux * ArrowLength) + pad;
        var backY = tip.Y - (uy * ArrowLength) + pad;
        var nx = -uy * (ArrowWidth / 2);
        var ny = ux * (ArrowWidth / 2);
        var color = Attribute(polyline.Color);
        var tipX = Number(tip.X + pad);
        var tipY = Number(tip.Y + pad);

        switch (polyline.Arrow)
        {
            case ArrowStyle.Arrow:
                svg.Append("  <polygon points=\"")
                    .Append(tipX).Append(',').Append(tipY).Append(' ')
                    .Append(Number(backX + nx)).Append(',').Append(Number(backY + ny)).Append(' ')
                    .Append(Number(backX - nx)).Append(',').Append(Number(backY - ny))
                    .Append("\" fill=\"").Append(color).Append("\"/>\n");
                break;

            case ArrowStyle.OpenArrow:
                svg.Append("  <path d=\"M").Append(Number(backX + nx)).Append(' ').Append(Number(backY + ny))
                    .Append("L").Append(tipX).Append(' ').Append(tipY)
                    .Append("L").Append(Number(backX - nx)).Append(' ').Append(Number(backY - ny))
                    .Append("\" fill=\"none\" stroke=\"").Append(color)
                    .Append("\" stroke-width=\"").Append(Number(polyline.Weight)).Append("\"/>\n");
                break;

            case ArrowStyle.Circle:
                svg.Append("  <circle cx=\"").Append(tipX).Append("\" cy=\"").Append(tipY)
                    .Append("\" r=\"").Append(Number(ArrowWidth / 2))
                    .Append("\" fill=\"").Append(color).Append("\"/>\n");
                break;

            case ArrowStyle.Cross:
                // 一个叉号：沿走向一道、垂直于走向一道。两条都过箭头尖。
                svg.Append("  <path d=\"M").Append(Number(tip.X - ux + pad)).Append(' ').Append(Number(tip.Y - uy + pad))
                    .Append("L").Append(Number(tip.X + ux + pad)).Append(' ').Append(Number(tip.Y + uy + pad))
                    .Append("M").Append(Number(tip.X - nx + pad)).Append(' ').Append(Number(tip.Y - ny + pad))
                    .Append("L").Append(Number(tip.X + nx + pad)).Append(' ').Append(Number(tip.Y + ny + pad))
                    .Append("\" fill=\"none\" stroke=\"").Append(color)
                    .Append("\" stroke-width=\"").Append(Number(polyline.Weight)).Append("\"/>\n");
                break;

            default:
                throw new NotSupportedException($"认不出的箭头样式：{polyline.Arrow}");
        }
    }

    #endregion

    #region 文本

    /// <summary>
    /// 一段文字。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **留 <c>&lt;text&gt;</c>，不转路径。** 转路径的话视觉更稳（不依赖对方的字体），
    /// 但文字搜不到、选不中，也就没法在 draw.io 里改——而这一条导出存在的理由正是
    /// "导给同事用 draw.io 打开再改"。代价是对方要有同一份字体，
    /// 缺了会按对方的回退字体排、行宽跟着变，这一条记在丢失清单里。
    /// </para>
    /// <para>
    /// 垂直位置按框中心对齐，与画布"把字在框里居中排"同一条口径。
    /// </para>
    /// </remarks>
    private static void AppendText(StringBuilder svg, DrawText text, double pad)
    {
        if (text.Text.Length == 0)
        {
            return;
        }

        svg.Append("  <text x=\"").Append(Number(text.Box.X + pad))
            .Append("\" y=\"").Append(Number(text.Box.Y + pad + (text.Box.Height / 2)))
            .Append("\" dominant-baseline=\"middle\"")
            .Append(" fill=\"").Append(Attribute(text.Color)).Append('"')
            .Append(" font-family=\"").Append(Attribute(text.FontFamily)).Append('"')
            .Append(" font-size=\"").Append(Number(text.FontSize)).Append('"');

        if (text.Weight == FontWeight.Bold)
        {
            svg.Append(" font-weight=\"bold\"");
        }

        if (text.Italic)
        {
            svg.Append(" font-style=\"italic\"");
        }

        var decoration = Decoration(text);

        if (decoration.Length > 0)
        {
            svg.Append(" text-decoration=\"").Append(decoration).Append('"');
        }

        svg.Append('>').Append(Content(text.Text)).Append("</text>\n");
    }

    private static string Decoration(DrawText text) => (text.Underline, text.Strikethrough) switch
    {
        (true, true) => "underline line-through",
        (true, false) => "underline",
        (false, true) => "line-through",
        _ => string.Empty,
    };

    #endregion

    #region 公共

    private static string Dash(LineStyle line) => line switch
    {
        LineStyle.Dashed => " stroke-dasharray=\"6 4\"",
        LineStyle.Dotted => " stroke-dasharray=\"1 3\"",
        _ => string.Empty,
    };

    private static string Rect(SpatialRect rect) =>
        $" x=\"{Number(rect.X)}\" y=\"{Number(rect.Y)}\" width=\"{Number(rect.Width)}\" height=\"{Number(rect.Height)}\"";

    /// <summary>
    /// 这份导出丢了什么。
    /// </summary>
    /// <remarks>
    /// 只有一条：文字留成 <c>&lt;text&gt;</c> 而不是转成路径。其余每一类绘制指令都有
    /// 对应的 SVG 元素，形状的几何、线型、箭头与不透明度都照原样写出去。
    /// 空清单不等于无损——它只等于"没有东西落进已知的丢失清单"。
    /// </remarks>
    private static DroppedFeature[] Losses() =>
    [
        new(
            "文字",
            [],
            "文字导出成 <text> 而不是路径：对方要有同一份字体，缺了会按对方的回退字体排、"
            + "行宽跟着变。换来的是文字可搜索、可选中、能在 draw.io 里改。"),
    ];

    /// <summary>数字的规范写法。与绘制列表同一口径：不变文化、限定位数。</summary>
    private static string Number(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>属性值：转义 XML 里不能原样出现的字符。</summary>
    private static string Attribute(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    /// <summary>文本内容：除了属性那几个，还得管换行与制表。</summary>
    private static string Content(string value) =>
        Attribute(value).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "&#10;", StringComparison.Ordinal)
            .Replace("\t", "&#9;", StringComparison.Ordinal);

    #endregion
}
