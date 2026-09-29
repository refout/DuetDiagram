using DuetDiagram.Core.Model;
using DuetDiagram.Core.Shapes;
using SkiaSharp;

namespace DuetDiagram.Render;

/// <summary>
/// 把一份绘制列表画到一块画布上。
/// </summary>
/// <remarks>
/// <para>
/// **这是全仓唯一一份"绘制列表怎么变成图元"的实现。** 位图导出与 PDF 导出都走它，
/// 差别只在最后那一步：一个把画布快照成 PNG，一个把画布交给 PDF 文档。
/// 各写一份的话，两处的箭头尺寸、虚线节距、圆角算法与字体回退迟早会分叉，
/// 而分叉的表现是"两种格式导出来的图不一样"，两边各自看都自洽。
/// </para>
/// <para>
/// **认不出的指令抛异常，不静默跳过。** 跳过的话，加一种绘制指令之后导出会少画一样东西
/// 而没有任何报错。画布那一边同一条口径。
/// </para>
/// <para>
/// **它不认识"格式"这件事。** 画布是位图还是 PDF 页，这里不关心，也不该关心——
/// 一旦它开始按格式分支，上面那条"只有一份实现"就名存实亡了。
/// </para>
/// </remarks>
internal static class CanvasPainter
{
    /// <summary>箭头与画布、SVG 导出用同一组尺寸，否则几处的箭头不一样大。</summary>
    private const double ArrowLength = 9;

    private const double ArrowWidth = 7;

    /// <summary>虚线：与 SVG 导出的 <c>stroke-dasharray</c> 同一组数，单位是文档单位。</summary>
    private const float DashOn = 6;

    private const float DashOff = 4;

    private const float DotOn = 1;

    private const float DotOff = 3;

    /// <summary>
    /// 铺底色。
    /// </summary>
    /// <remarks>
    /// 底色总是显式铺一次。不铺的话，离屏画布的初始内容是未定义的，
    /// 于是"两次导出逐像素相同"这件事取决于分配器给了什么。
    /// </remarks>
    /// <param name="canvas">画布。</param>
    /// <param name="background">绘制列表带的背景色。</param>
    /// <param name="transparent">底是不是透明的。透明时铺全透明。</param>
    internal static void Background(SKCanvas canvas, string background, bool transparent) =>
        canvas.Clear(transparent ? SKColors.Transparent : Color(background, SKColors.White));

    /// <summary>按层叠顺序画完所有指令。</summary>
    /// <param name="canvas">画布。</param>
    /// <param name="commands">绘制指令。</param>
    internal static void Commands(SKCanvas canvas, IReadOnlyList<DrawCommand> commands)
    {
        foreach (var command in commands)
        {
            Draw(canvas, command);
        }
    }

    private static void Draw(SKCanvas canvas, DrawCommand command)
    {
        switch (command)
        {
            case DrawShape shape:
                DrawShape(canvas, shape);
                break;

            case DrawPolyline polyline:
                DrawPolyline(canvas, polyline);
                break;

            case DrawText text:
                DrawText(canvas, text);
                break;

            default:
                throw new NotSupportedException($"认不出的绘制指令：{command.GetType().Name}");
        }
    }

    #region 形状

    private static void DrawShape(SKCanvas canvas, DrawShape shape)
    {
        var geometry = shape.Geometry ?? ShapeRegistry.Default.Find(shape.Shape).Geometry;
        var rect = new SKRect(
            (float)shape.Rect.X,
            (float)shape.Rect.Y,
            (float)shape.Rect.Right,
            (float)shape.Rect.Bottom);

        using var path = Path(geometry, rect, shape.Radius);

        if (shape.Opacity >= 1)
        {
            PaintShape(canvas, path, shape);
            return;
        }

        // 半透明形状要整块一起合成，不能只把填充色调淡：填充与描边重叠的那一圈
        // 会各调一次，于是边框比中间更深。画布那边用的是同一手法。
        using var layer = new SKPaint { Color = SKColors.White.WithAlpha(Alpha(shape.Opacity)) };

        canvas.SaveLayer(layer);
        PaintShape(canvas, path, shape);
        canvas.Restore();
    }

    private static void PaintShape(SKCanvas canvas, SKPath path, DrawShape shape)
    {
        using var fill = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = Color(shape.Fill, SKColors.Transparent),
            IsAntialias = true,
        };

        using var stroke = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = Color(shape.Stroke, SKColors.Transparent),
            StrokeWidth = (float)shape.Weight,
            IsAntialias = true,
            StrokeJoin = SKStrokeJoin.Miter,
            PathEffect = Dash(shape.Border),
        };

        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, stroke);
    }

    /// <summary>
    /// 把形状几何建成一条路径。
    /// </summary>
    /// <remarks>
    /// 几何由形状库给，画法按几何种类分发：加一个形状只要在形状库里加一条定义，
    /// 这里不必跟着改。自定义形状的几何在构建绘制列表时就算好了，带上来的那份直接用。
    /// </remarks>
    private static SKPath Path(ShapeGeometry geometry, SKRect rect, double styleRadius)
    {
        using var builder = new SKPathBuilder();

        switch (geometry)
        {
            case RoundedRectOutline corners:
                var radius = Radius(corners.Corners, styleRadius, rect);

                if (radius > 0)
                {
                    builder.AddRoundRect(rect, (float)radius, (float)radius);
                }
                else
                {
                    builder.AddRect(rect);
                }

                break;

            case EllipseOutline:
                builder.AddOval(rect);
                break;

            case PolygonOutline polygon:
                builder.MoveTo(Point(polygon.Points[0], rect));

                for (var index = 1; index < polygon.Points.Count; index++)
                {
                    builder.LineTo(Point(polygon.Points[index], rect));
                }

                builder.Close();
                break;

            case PathOutline outline:
                builder.MoveTo(Point(outline.Start, rect));

                foreach (var segment in outline.Segments)
                {
                    switch (segment)
                    {
                        case PathLine line:
                            builder.LineTo(Point(line.To, rect));
                            break;

                        case PathArc arc:
                            // 弧的两个标志位与画布、SVG 导出保持一致：半径按框缩放、不取大弧、
                            // 方向由几何自己说。三处不一致的话，圆柱这类形状在导出里会翻个面。
                            builder.ArcTo(
                                new SKPoint((float)(arc.RadiusX * rect.Width), (float)(arc.RadiusY * rect.Height)),
                                0,
                                SKPathArcSize.Small,
                                arc.Clockwise ? SKPathDirection.Clockwise : SKPathDirection.CounterClockwise,
                                Point(arc.To, rect));
                            break;

                        default:
                            throw new NotSupportedException($"认不出的路径线段：{segment.GetType().Name}");
                    }
                }

                builder.Close();
                break;

            default:
                throw new NotSupportedException($"认不出的形状几何：{geometry.GetType().Name}");
        }

        return builder.Detach();
    }

    /// <summary>圆角半径。三种来源分开算，与形状库、画布、SVG 导出同一口径。</summary>
    private static double Radius(CornerRadiusMode corners, double styleRadius, SKRect rect) => corners switch
    {
        CornerRadiusMode.None => 0,
        CornerRadiusMode.HalfMinSide => Math.Min(rect.Width, rect.Height) / 2,
        _ => styleRadius,
    };

    private static SKPoint Point(ShapePoint point, SKRect rect) =>
        new((float)(rect.Left + (point.X * rect.Width)), (float)(rect.Top + (point.Y * rect.Height)));

    #endregion

    #region 连线

    private static void DrawPolyline(SKCanvas canvas, DrawPolyline polyline)
    {
        if (polyline.Points.Count < 2)
        {
            return;
        }

        using var path = Polyline(polyline.Points);

        using var pen = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = Color(polyline.Color, SKColors.Black),
            StrokeWidth = (float)polyline.Weight,
            IsAntialias = true,
            PathEffect = Dash(polyline.Line),
        };

        canvas.DrawPath(path, pen);
        DrawArrow(canvas, polyline);
    }

    /// <summary>把一串点连成一条折线。</summary>
    private static SKPath Polyline(IReadOnlyList<DrawPoint> points)
    {
        using var builder = new SKPathBuilder();

        builder.MoveTo((float)points[0].X, (float)points[0].Y);

        for (var index = 1; index < points.Count; index++)
        {
            builder.LineTo((float)points[index].X, (float)points[index].Y);
        }

        return builder.Detach();
    }

    /// <summary>三个点围成一个闭合三角形。实心箭头用它。</summary>
    private static SKPath Triangle(DrawPoint a, DrawPoint b, DrawPoint c)
    {
        using var builder = new SKPathBuilder();

        builder.MoveTo((float)a.X, (float)a.Y);
        builder.LineTo((float)b.X, (float)b.Y);
        builder.LineTo((float)c.X, (float)c.Y);
        builder.Close();

        return builder.Detach();
    }

    /// <summary>终点箭头。方向取最后一段的走向，与画布、SVG 导出同一条口径。</summary>
    private static void DrawArrow(SKCanvas canvas, DrawPolyline polyline)
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
        var backX = tip.X - (ux * ArrowLength);
        var backY = tip.Y - (uy * ArrowLength);
        var nx = -uy * (ArrowWidth / 2);
        var ny = ux * (ArrowWidth / 2);
        var color = Color(polyline.Color, SKColors.Black);

        switch (polyline.Arrow)
        {
            case ArrowStyle.Arrow:
                using (var head = Triangle(
                    tip,
                    new DrawPoint(backX + nx, backY + ny),
                    new DrawPoint(backX - nx, backY - ny)))
                {
                    using var fill = new SKPaint
                    {
                        Style = SKPaintStyle.Fill,
                        Color = color,
                        IsAntialias = true,
                    };

                    canvas.DrawPath(head, fill);
                }

                break;

            case ArrowStyle.OpenArrow:
                using (var stroke = Stroke(color, polyline.Weight))
                {
                    canvas.DrawLine((float)tip.X, (float)tip.Y, (float)(backX + nx), (float)(backY + ny), stroke);
                    canvas.DrawLine((float)tip.X, (float)tip.Y, (float)(backX - nx), (float)(backY - ny), stroke);
                }

                break;

            case ArrowStyle.Circle:
                using (var fill = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = color,
                    IsAntialias = true,
                })
                {
                    canvas.DrawCircle((float)tip.X, (float)tip.Y, (float)(ArrowWidth / 2), fill);
                }

                break;

            case ArrowStyle.Cross:
                // 一个叉号：沿走向一道、垂直于走向一道。两条都过箭头尖。
                using (var stroke = Stroke(color, polyline.Weight))
                {
                    canvas.DrawLine((float)(tip.X - ux), (float)(tip.Y - uy), (float)(tip.X + ux), (float)(tip.Y + uy), stroke);
                    canvas.DrawLine((float)(tip.X - nx), (float)(tip.Y - ny), (float)(tip.X + nx), (float)(tip.Y + ny), stroke);
                }

                break;

            default:
                throw new NotSupportedException($"认不出的箭头样式：{polyline.Arrow}");
        }
    }

    #endregion

    #region 文本

    /// <summary>
    /// 画一段文本。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 指令里的框已经是对齐算完之后的框，左边缘就是起始位置，所以这里只做纵向居中——
    /// 横向再对一次会把居中标签推偏半个字宽。行高取与度量器同一条公式，
    /// 否则字会偏离框中线，而偏离量随字号变。
    /// </para>
    /// <para>
    /// **字体要挑一次。** 文档点名的家族未必认得出这段字里的每一个字——例如
    /// 一份写中文标签而字体是西文家族的文档。不挑的话，认不出的字画成空白方块，
    /// 而那是"导出看起来坏了"里最容易被当成"程序坏了"的一种。挑哪个字体由
    /// <see cref="FontFallback"/> 定，与度量那一步同一份规则。
    /// </para>
    /// <para>
    /// **整段换，不是逐字换。** 逐字换字体要真正的文字整形，而这里要的只是
    /// "不出现空白方块"——一个字体里通常拉丁与中日韩字形都有。代价是混排的标签
    /// 整段用回退字体，而画布那边是逐字回退，于是同一段里拉丁字用的字体略有不同。
    /// </para>
    /// <para>
    /// 下划线与删除线按需挂上。位置与粗细按字号算，不读字体的那一组度量：
    /// 那组数在不同字体上符号约定不一致，而装饰线偏一两个像素没有人在意。
    /// </para>
    /// </remarks>
    private static void DrawText(SKCanvas canvas, DrawText text)
    {
        if (text.Text.Length == 0)
        {
            return;
        }

        var family = string.IsNullOrWhiteSpace(text.FontFamily) ? FontFallback.DefaultFamily : text.FontFamily;

        using var requested = FontFallback.FromFamily(family, text.Weight, text.Italic);
        using var primary = new SKFont(requested, (float)text.FontSize) { Hinting = SKFontHinting.None };

        var missing = FontFallback.FirstMissing(primary, text.Text);

        if (missing < 0)
        {
            PaintText(canvas, text, primary);
            return;
        }

        using var substitute = FontFallback.Substitute(family, missing, text.Weight, text.Italic);

        if (substitute is null)
        {
            PaintText(canvas, text, primary);
            return;
        }

        using var font = new SKFont(substitute, (float)text.FontSize) { Hinting = SKFontHinting.None };

        PaintText(canvas, text, font);
    }

    /// <summary>把一段文字按框中线排好画出去。</summary>
    private static void PaintText(SKCanvas canvas, DrawText text, SKFont font)
    {
        using var paint = new SKPaint
        {
            Color = Color(text.Color, SKColors.Black),
            IsAntialias = true,
        };

        var metrics = font.Metrics;
        var lineHeight = metrics.Descent - metrics.Ascent + metrics.Leading;
        var baseline = (float)(text.Box.Y + ((text.Box.Height - lineHeight) / 2)) - metrics.Ascent;

        canvas.DrawText(text.Text, (float)text.Box.X, baseline, SKTextAlign.Left, font, paint);

        if (text.Underline || text.Strikethrough)
        {
            Decorate(canvas, text, paint, baseline);
        }
    }

    /// <summary>下划线与删除线各画一道横线，宽度取这一段的框宽。</summary>
    private static void Decorate(SKCanvas canvas, DrawText text, SKPaint paint, float baseline)
    {
        var size = (float)text.FontSize;
        var left = (float)text.Box.X;
        var right = (float)(text.Box.X + text.Box.Width);

        using var line = Stroke(paint.Color, Math.Max(1, size * 0.055));

        if (text.Underline)
        {
            var y = baseline + (size * 0.12f);

            canvas.DrawLine(left, y, right, y, line);
        }

        if (text.Strikethrough)
        {
            var y = baseline - (size * 0.28f);

            canvas.DrawLine(left, y, right, y, line);
        }
    }

    #endregion

    #region 公共

    private static SKPaint Stroke(SKColor color, double weight) => new()
    {
        Style = SKPaintStyle.Stroke,
        Color = color,
        StrokeWidth = (float)weight,
        IsAntialias = true,
    };

    private static SKPathEffect? Dash(LineStyle line) => line switch
    {
        LineStyle.Dashed => SKPathEffect.CreateDash([DashOn, DashOff], 0),
        LineStyle.Dotted => SKPathEffect.CreateDash([DotOn, DotOff], 0),
        _ => null,
    };

    /// <summary>
    /// 解析颜色。
    /// </summary>
    /// <remarks>
    /// 认不出的颜色退回给定值而不是抛异常：颜色来自文档，一份写错颜色的文档
    /// 该表现成"颜色不对"，而不是整张图导不出来。画布那一边同一条口径。
    /// </remarks>
    private static SKColor Color(string value, SKColor fallback) =>
        SKColor.TryParse(value, out var color) ? color : fallback;

    /// <summary>不透明度换算成 0 到 255 的通道值。</summary>
    private static byte Alpha(double opacity) =>
        (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255);

    #endregion
}
