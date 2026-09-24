using DuetDiagram.Core.Model;
using DuetDiagram.Core.Shapes;
using SkiaSharp;

namespace DuetDiagram.Render;

/// <summary>
/// 把一份绘制列表光栅化成位图。
/// </summary>
/// <remarks>
/// <para>
/// **它只消费绘制列表，不重新遍历文档**，与 SVG 导出同一条口径。重新遍历的话，
/// 导出与画布成了两条绘制路径，而两条迟早会对不上——对不上的表现是"导出的图与屏幕上不一样"，
/// 而两边各自都自洽。
/// </para>
/// <para>
/// **它是无头的。** 光栅化直接开一块离屏画布，不经过任何窗口平台。命令行、服务端与
/// 模型那条通路都没有窗口平台，依赖窗口的话，界面上一切正常而命令行导出必失败。
/// </para>
/// <para>
/// **确定性。** 同一份绘制列表与同一组选项导出两次逐像素相同：不带时间戳、
/// 不带随机标识、不依赖集合迭代顺序，抗锯齿也显式打开而不是听凭默认值。
/// 唯一与机器有关的是字体——字形由本机装的字体画出来，换一台机器字体不同则像素不同，
/// 这一条与"节点尺寸由字体量出来"是同一件事，不是导出引入的。
/// </para>
/// <para>
/// **认不出的指令抛异常，不静默跳过。** 跳过的话，加一种绘制指令之后导出会少画一样东西
/// 而没有任何报错。画布与 SVG 导出那两边同一条口径。
/// </para>
/// </remarks>
public static class BitmapExporter
{
    /// <summary>箭头与画布、SVG 导出用同一组尺寸，否则三处的箭头不一样大。</summary>
    private const double ArrowLength = 9;

    private const double ArrowWidth = 7;

    /// <summary>虚线：与 SVG 导出的 <c>stroke-dasharray</c> 同一组数，单位是文档单位。</summary>
    private const float DashOn = 6;

    private const float DashOff = 4;

    private const float DotOn = 1;

    private const float DotOff = 3;

    /// <summary>文档没点名家族时用的那个。与度量器的兜底同一个，否则量出来的与画出来的不是一套。</summary>
    private const string DefaultFamily = "Segoe UI";

    /// <summary>
    /// 导出。
    /// </summary>
    /// <remarks>
    /// 产物用的是 Core 那份记录，不是这里另立一个：工具层要把它原样交给调用方，
    /// 而工具层看不见渲染层。两边各一份的话，"丢失清单"就有了两种形状。
    /// </remarks>
    /// <param name="list">绘制列表。</param>
    /// <param name="options">选择。为空时用默认。</param>
    /// <exception cref="ArgumentException">按页面裁却没有给页面尺寸。</exception>
    public static BitmapExport Export(DrawList list, BitmapOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(list);

        var settings = options ?? new BitmapOptions();
        var (frameWidth, frameHeight) = Frame(list, settings);
        var pad = settings.Crop == BitmapCrop.Content ? Math.Max(0, settings.Padding) : 0;
        var width = frameWidth + (pad * 2);
        var height = frameHeight + (pad * 2);
        var scale = settings.Scale > 0 ? settings.Scale : throw new ArgumentException("缩放倍数要大于零。", nameof(options));
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));

        using var surface = SKSurface.Create(new SKImageInfo(pixelWidth, pixelHeight))
            ?? throw new InvalidOperationException("开不出一块离屏画布。");

        var canvas = surface.Canvas;

        // 底色总是显式铺一次。不铺的话，离屏画布的初始内容是未定义的，
        // 于是"两次导出逐像素相同"这件事取决于分配器给了什么。
        canvas.Clear(settings.Transparent ? SKColors.Transparent : Color(list.Background, SKColors.White));

        // 先缩放再平移：一个点先加留白、再整体乘倍数。反过来排的话留白也会被缩放，
        // 于是"留白十像素"在二倍图上变成二十像素。
        canvas.Scale((float)scale);
        canvas.Translate((float)pad, (float)pad);

        foreach (var command in list.Commands)
        {
            Draw(canvas, command);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG 编码没给出结果。");

        return new BitmapExport(data.ToArray(), pixelWidth, pixelHeight, Losses(list, settings, width, height));
    }

    #region 范围

    /// <summary>这一档导出要画多大，单位是文档单位。</summary>
    private static (double Width, double Height) Frame(DrawList list, BitmapOptions settings) =>
        settings.Crop switch
        {
            BitmapCrop.Page => Page(settings),
            _ => (list.Width, list.Height),
        };

    private static (double Width, double Height) Page(BitmapOptions settings) =>
        settings.PageSize.Width > 0 && settings.PageSize.Height > 0
            ? (settings.PageSize.Width, settings.PageSize.Height)
            : throw new ArgumentException("按页面裁要给出页面尺寸。", nameof(settings));

    /// <summary>
    /// 这份导出丢了什么。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 第一条是位图这种格式本身的代价：所有东西都变成像素，文字搜不到、图不能再编辑。
    /// 要可编辑的矢量就用 SVG 那一档。
    /// </para>
    /// <para>
    /// 第二条只在按页面裁、而且内容比页面大时出现。**这一条必须如实报出来**：
    /// 静默裁掉一块的话，用户拿到一张缺了角而"看起来正常"的图，
    /// 而那种缺失在缩略图上根本看不出来。
    /// </para>
    /// </remarks>
    private static DroppedFeature[] Losses(DrawList list, BitmapOptions settings, double width, double height)
    {
        var losses = new List<DroppedFeature>
        {
            new(
                "可编辑性",
                [],
                "位图里所有东西都是像素：文字搜不到、形状不能再改。要能接着编辑就用 SVG 那一档。"),
        };

        if (settings.Crop == BitmapCrop.Page && (list.Width > width || list.Height > height))
        {
            losses.Add(new(
                "超出页面的内容",
                [],
                $"按页面尺寸裁掉了超出纸张的部分：内容实际 {Numbers.Format(list.Width)}×{Numbers.Format(list.Height)}，"
                + $"页面 {Numbers.Format(width)}×{Numbers.Format(height)}。要完整内容就按内容外接框那一档导。"));
        }

        return [.. losses];
    }

    #endregion

    #region 指令分发

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

    #endregion

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
    /// 而那是"导出看起来坏了"里最容易被当成"程序坏了"的一种。
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

        var family = string.IsNullOrWhiteSpace(text.FontFamily) ? DefaultFamily : text.FontFamily;

        using var requested = FromFamily(family, text.Weight, text.Italic);
        using var primary = new SKFont(requested, (float)text.FontSize) { Hinting = SKFontHinting.None };

        var missing = FirstMissing(primary, text.Text);

        if (missing < 0)
        {
            PaintText(canvas, text, primary);
            return;
        }

        using var substitute = Substitute(family, missing, text.Weight, text.Italic);

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

    /// <summary>
    /// 这段文字里第一个这个字体画不出来的字。都在就返回零以下。
    /// </summary>
    /// <remarks>
    /// 逐字问一遍而不是只看第一个：混排的标签里第一个字常常是拉丁字母，
    /// 而画不出来的是后面那个中文字。
    /// </remarks>
    private static int FirstMissing(SKFont font, string text)
    {
        for (var index = 0; index < text.Length;)
        {
            var codePoint = char.ConvertToUtf32(text, index);

            if (font.GetGlyph(codePoint) == 0)
            {
                return codePoint;
            }

            index += char.IsSurrogatePair(text, index) ? 2 : 1;
        }

        return -1;
    }

    /// <summary>
    /// 换一个认得出这个字的字体。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 拿点名的家族当线索去问字体管理器：它认得这个字就把点名的家族还回来，
    /// 认不得就还一个认得的（例如西文家族遇上汉字，还的是本机的中文字体）。
    /// </para>
    /// <para>
    /// **整段换，不是逐字换。** 逐字换字体要真正的文字整形，而这里要的只是
    /// "不出现空白方块"——一个字体里通常拉丁与中日韩字形都有。代价是混排的标签
    /// 整段用回退字体，而画布那边是逐字回退，于是同一段里拉丁字用的字体略有不同。
    /// </para>
    /// </remarks>
    private static SKTypeface? Substitute(string family, int codePoint, FontWeight weight, bool italic)
    {
        var matched = SKFontManager.Default.MatchCharacter(family, codePoint);

        if (matched is null)
        {
            return null;
        }

        using (matched)
        {
            return FromFamily(matched.FamilyName, weight, italic);
        }
    }

    /// <summary>
    /// 按家族、字重与倾斜取一个字体。
    /// </summary>
    /// <remarks>
    /// 字重与倾斜要一起交给字体匹配，而不是拿到常规体再让绘制方自己变：
    /// 量出来的宽度必须与画出来的那一个字面一致，否则加粗的那一段会溢出。
    /// 家族不存在时退回系统默认字体，与度量器同一条口径。
    /// </remarks>
    private static SKTypeface FromFamily(string family, FontWeight weight, bool italic) =>
        SKTypeface.FromFamilyName(
            family,
            weight == FontWeight.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright)
            ?? SKTypeface.Default;

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
