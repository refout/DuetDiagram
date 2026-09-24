using Avalonia;
using Avalonia.Media;
using DuetDiagram.Core.Shapes;

namespace DuetDiagram.App.Rendering;

/// <summary>
/// 把形状库给的几何画到 Avalonia 的绘制上下文上。
/// </summary>
/// <remarks>
/// <para>
/// 画布与形状面板的预览共用这一处。两处各画一份的话，预览里看到的形状与画布上
/// 画出来的迟早会对不上，而那种不一致只有把两个界面并排看才发现。
/// </para>
/// <para>
/// **它按几何的种类分发，不按形状枚举。** 形状库加一个形状，这里一行都不用改；
/// 加一种从没见过的几何种类才要回来补一个分支，而那是"绘制方学会了一种新图形"，
/// 本来就该在这里说清楚。
/// </para>
/// </remarks>
internal static class ShapeGeometryRenderer
{
    /// <summary>画一个形状。</summary>
    /// <param name="context">绘制上下文。</param>
    /// <param name="geometry">形状几何。</param>
    /// <param name="rect">外接矩形。单位框坐标按它换算成实际位置。</param>
    /// <param name="fill">填充笔刷。给空表示只描边。</param>
    /// <param name="pen">描边画笔。</param>
    /// <param name="styleRadius">样式里给的圆角半径。几何自带圆角规则时可能用不上。</param>
    public static void Draw(
        DrawingContext context,
        ShapeGeometry geometry,
        Rect rect,
        IBrush? fill,
        Pen pen,
        double styleRadius)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(pen);

        switch (geometry)
        {
            case RoundedRectOutline rounded:
                context.DrawRectangle(fill, pen, new RoundedRect(rect, CornerRadiusOf(rounded, styleRadius, rect)));
                break;

            case EllipseOutline:
                context.DrawEllipse(fill, pen, rect.Center, rect.Width / 2, rect.Height / 2);
                break;

            case PolygonOutline polygon:
                context.DrawGeometry(fill, pen, BuildPolygon([.. polygon.Points.Select(point => ToPoint(point, rect))]));
                break;

            case PathOutline path:
                context.DrawGeometry(fill, pen, Path(path, rect));
                break;

            default:
                // 认不出的几何说明形状库加了新的种类而这里没跟上。
                // 静默跳过的话，那种形状会永远画不出来，而测试仍然全绿。
                throw new NotSupportedException($"认不出的形状几何：{geometry.GetType().Name}");
        }
    }

    /// <summary>
    /// 圆角半径。
    /// </summary>
    /// <remarks>
    /// 半径从哪来由几何自己说：胶囊由短边算，圆角矩形用节点上填的值（没填时主题已给了兜底），
    /// 直角一律零。这里不判断是哪个形状。
    /// </remarks>
    private static double CornerRadiusOf(RoundedRectOutline geometry, double styleRadius, Rect rect) =>
        geometry.Corners switch
        {
            CornerRadiusMode.HalfMinSide => Math.Min(rect.Width, rect.Height) / 2,
            CornerRadiusMode.FromStyle => styleRadius,
            _ => 0,
        };

    /// <summary>把单位框坐标的一个点换算到实际矩形里。</summary>
    private static Point ToPoint(ShapePoint point, Rect rect) =>
        new(rect.Left + (point.X * rect.Width), rect.Top + (point.Y * rect.Height));

    /// <summary>
    /// 把一份路径几何建成绘制用的路径。
    /// </summary>
    /// <remarks>
    /// 弧的半径也按单位框缩放：定义里给的是比例，实际尺寸一变弧跟着变。
    /// </remarks>
    private static StreamGeometry Path(PathOutline path, Rect rect)
    {
        var geometry = new StreamGeometry();

        using (var sink = geometry.Open())
        {
            sink.BeginFigure(ToPoint(path.Start, rect), true);

            foreach (var segment in path.Segments)
            {
                switch (segment)
                {
                    case PathLine line:
                        sink.LineTo(ToPoint(line.To, rect));
                        break;

                    case PathArc arc:
                        sink.ArcTo(
                            ToPoint(arc.To, rect),
                            new Size(arc.RadiusX * rect.Width, arc.RadiusY * rect.Height),
                            0,
                            false,
                            arc.Clockwise ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
                        break;

                    default:
                        throw new NotSupportedException($"认不出的路径线段：{segment.GetType().Name}");
                }
            }

            sink.EndFigure(true);
        }

        return geometry;
    }

    /// <summary>把一串点连成一个闭合图形。多边形形状与箭头都用它。</summary>
    internal static StreamGeometry BuildPolygon(params Point[] points)
    {
        var geometry = new StreamGeometry();

        using (var sink = geometry.Open())
        {
            sink.BeginFigure(points[0], true);

            for (var index = 1; index < points.Length; index++)
            {
                sink.LineTo(points[index]);
            }

            sink.EndFigure(true);
        }

        return geometry;
    }
}
