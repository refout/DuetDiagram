using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using DuetDiagram.App.ViewModels;
using DuetDiagram.Render;
using ArrowStyle = DuetDiagram.Core.Model.ArrowStyle;
using CoreFontWeight = DuetDiagram.Core.Model.FontWeight;
using LineStyle = DuetDiagram.Core.Model.LineStyle;
using NodeShape = DuetDiagram.Core.Model.NodeShape;

namespace DuetDiagram.App.Controls;

/// <summary>
/// 把一份绘制列表画到屏幕上，并处理滚轮缩放与拖拽平移。
/// </summary>
/// <remarks>
/// <para>
/// **它只认绘制列表，不读 IR。** 读 IR 的话，渲染路径就绕过了绘制列表，
/// 于是快照测试覆盖到的东西与实际画出来的东西会分叉——两边都"通过"，
/// 而屏幕上看到的是另一回事。
/// </para>
/// <para>
/// **它自己也不判断该不该剔除。** 每一帧画哪几条由视图模型定下来，
/// 画布照着画。判据写在画布上的话，同一件事就有了第二个说法，
/// 而两处对不上时的表现是"有时画得少、有时画得多"，看不出规律。
/// </para>
/// <para>
/// 颜色、字体、笔刷按值缓存。千节点的图上每条指令都要一个笔刷，
/// 每帧新建两千个对象，光分配就够把帧率拖下去，而它们的取值其实只有几十种。
/// </para>
/// </remarks>
public sealed partial class DiagramCanvas : UserControl
{
    /// <summary>
    /// 滚轮每格改变多少倍。
    /// </summary>
    /// <remarks>
    /// 一格 15%：连续滚十格大约变成四倍。再大则一滚就过头，
    /// 用户得来回找；再小则要滚很多格才看得出变化。
    /// </remarks>
    private const double ZoomPerNotch = 1.15;

    private const double ArrowLength = 9;
    private const double ArrowWidth = 7;

    private static readonly Cursor PanCursor = new(StandardCursorType.SizeAll);

    private readonly Dictionary<string, IBrush> _brushes = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Family, CoreFontWeight Weight), Typeface> _typefaces = [];

    private CanvasViewModel? _subscribed;
    private bool _panning;
    private bool _spaceHeld;
    private Point _lastPointer;
    private int _drawnCommands;

    public DiagramCanvas() => InitializeComponent();

    /// <summary>
    /// 最近一帧执行掉的绘制指令条数。
    /// </summary>
    /// <remarks>
    /// 它是"列表真的被消费了"的凭据。只看屏幕的话，一份没画出来的列表
    /// 与一份画在视口之外的列表长得一模一样——都是空白。
    /// </remarks>
    public int DrawnCommands => _drawnCommands;

    /// <summary>当前的数据上下文，按画布要的类型取。类型不对时为空。</summary>
    public CanvasViewModel? Model => DataContext as CanvasViewModel;

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        base.Render(context);

        _drawnCommands = 0;

        if (Model is not { } model || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        // 这一帧画哪几条由它定：整份列表，或者是按视口剔过的一份子序列。
        // 画布自己不判断该不该剔除——那条判据只此一处，放在画布上就会与别处对不上。
        model.BeginFrame();

        var commands = model.FrameCommands;

        if (commands.Count == 0)
        {
            return;
        }

        var transform = model.Viewport.Transform;
        var viewport = new Rect(Bounds.Size);

        // 自绘不受 ClipToBounds 约束，超出画布的部分会盖到相邻面板上。
        using (context.PushClip(viewport))
        {
            context.FillRectangle(Brush(model.DrawList.Background), viewport);

            foreach (var command in commands)
            {
                Draw(context, transform, command);
                _drawnCommands++;
            }
        }
    }

    #region 指令分发

    private void Draw(DrawingContext context, ViewportTransform transform, DrawCommand command)
    {
        switch (command)
        {
            case DrawShape shape:
                DrawShape(context, transform, shape);
                break;
            case DrawPolyline polyline:
                DrawPolyline(context, transform, polyline);
                break;
            case DrawText text:
                DrawText(context, transform, text);
                break;
            default:
                // 认不出的指令说明绘制列表加了新的类型而这里没跟上。
                // 静默跳过的话，那种指令会永远画不出来，而测试仍然全绿。
                throw new NotSupportedException($"认不出的绘制指令：{command.GetType().Name}");
        }
    }

    #endregion

    #region 形状

    private void DrawShape(DrawingContext context, ViewportTransform transform, DrawShape shape)
    {
        var rect = ToRect(transform.ToScreen(shape.Rect));
        var fill = Brush(shape.Fill);
        var pen = Pen(shape.Stroke, shape.Weight, shape.Border);

        // 全不透明的形状占绝大多数，为它们推一次透明层等于白白多开一块离屏缓冲。
        if (shape.Opacity >= 1)
        {
            DrawShapeGeometry(context, fill, pen, shape, rect);
            return;
        }

        using (context.PushOpacity(shape.Opacity))
        {
            DrawShapeGeometry(context, fill, pen, shape, rect);
        }
    }

    private static void DrawShapeGeometry(
        DrawingContext context,
        IBrush fill,
        Pen pen,
        DrawShape shape,
        Rect rect)
    {
        switch (shape.Shape)
        {
            case NodeShape.Rect:
                context.DrawRectangle(fill, pen, rect);
                break;

            case NodeShape.Rounded:
                context.DrawRectangle(fill, pen, new RoundedRect(rect, shape.Radius));
                break;

            case NodeShape.Stadium:
                context.DrawRectangle(fill, pen, new RoundedRect(rect, Math.Min(rect.Width, rect.Height) / 2));
                break;

            case NodeShape.Circle:
                context.DrawEllipse(fill, pen, rect.Center, rect.Width / 2, rect.Height / 2);
                break;

            case NodeShape.Diamond:
                context.DrawGeometry(fill, pen, Polygon(
                    new Point(rect.Center.X, rect.Top),
                    new Point(rect.Right, rect.Center.Y),
                    new Point(rect.Center.X, rect.Bottom),
                    new Point(rect.Left, rect.Center.Y)));
                break;

            case NodeShape.Hexagon:
                context.DrawGeometry(fill, pen, Polygon(
                    new Point(rect.Left + (rect.Width * 0.25), rect.Top),
                    new Point(rect.Left + (rect.Width * 0.75), rect.Top),
                    new Point(rect.Right, rect.Center.Y),
                    new Point(rect.Left + (rect.Width * 0.75), rect.Bottom),
                    new Point(rect.Left + (rect.Width * 0.25), rect.Bottom),
                    new Point(rect.Left, rect.Center.Y)));
                break;

            case NodeShape.Parallelogram:
                context.DrawGeometry(fill, pen, Polygon(
                    new Point(rect.Left + (rect.Width * 0.25), rect.Top),
                    new Point(rect.Right, rect.Top),
                    new Point(rect.Right - (rect.Width * 0.25), rect.Bottom),
                    new Point(rect.Left, rect.Bottom)));
                break;

            case NodeShape.Cylinder:
                DrawCylinder(context, fill, pen, rect);
                break;

            default:
                throw new NotSupportedException($"认不出的节点形状：{shape.Shape}");
        }
    }

    /// <summary>
    /// 画圆柱。
    /// </summary>
    /// <remarks>
    /// 上下两条弧各占高度的一小部分，中间是柱身。弧高取高度的四分之一，
    /// 再高就成了一段管子，再低则看不出是圆柱。
    /// 用一段闭合路径而不是"两个椭圆加一个矩形"：三块图形各自的描边会在接缝处叠出一道深色线。
    /// </remarks>
    private static void DrawCylinder(DrawingContext context, IBrush fill, Pen pen, Rect rect)
    {
        var arc = Math.Min(rect.Height / 4, rect.Width / 2);
        var geometry = new StreamGeometry();

        using (var sink = geometry.Open())
        {
            sink.BeginFigure(new Point(rect.Left, rect.Top + arc), true);

            // 顶面的前半圈：从左肩拱到右肩。
            sink.ArcTo(
                new Point(rect.Right, rect.Top + arc),
                new Size(rect.Width / 2, arc),
                0,
                false,
                SweepDirection.Clockwise);

            sink.LineTo(new Point(rect.Right, rect.Bottom - arc));
            sink.ArcTo(
                new Point(rect.Left, rect.Bottom - arc),
                new Size(rect.Width / 2, arc),
                0,
                false,
                SweepDirection.Clockwise);

            sink.EndFigure(true);
        }

        context.DrawGeometry(fill, pen, geometry);
    }

    private static StreamGeometry Polygon(params Point[] points)
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

    #endregion

    #region 连线

    private void DrawPolyline(DrawingContext context, ViewportTransform transform, DrawPolyline polyline)
    {
        if (polyline.Points.Count < 2)
        {
            return;
        }

        var points = new Point[polyline.Points.Count];

        for (var index = 0; index < points.Length; index++)
        {
            var mapped = transform.ToScreen(polyline.Points[index]);
            points[index] = new Point(mapped.X, mapped.Y);
        }

        var brush = Brush(polyline.Color);
        var pen = Pen(polyline.Color, polyline.Weight, polyline.Line);
        var geometry = new StreamGeometry();

        using (var sink = geometry.Open())
        {
            sink.BeginFigure(points[0], false);

            for (var index = 1; index < points.Length; index++)
            {
                sink.LineTo(points[index]);
            }

            sink.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);

        DrawArrowHead(context, brush, points[^1], points[^2], polyline.Arrow, polyline.Weight);
    }

    /// <summary>
    /// 画终点箭头。
    /// </summary>
    /// <remarks>
    /// 方向取最后一段的走向。折线的最后一段总是正交的，所以箭头也总是水平或垂直，
    /// 与流程图里常见的画法一致。最后一段长度为零时什么都不画——
    /// 那时方向无从谈起，硬取一个默认方向会画出一个指向错误一侧的箭头。
    /// </remarks>
    private static void DrawArrowHead(
        DrawingContext context,
        IBrush brush,
        Point tip,
        Point previous,
        ArrowStyle arrow,
        double weight)
    {
        if (arrow == ArrowStyle.None)
        {
            return;
        }

        var dx = tip.X - previous.X;
        var dy = tip.Y - previous.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));

        if (length < 1e-6)
        {
            return;
        }

        var ux = dx / length;
        var uy = dy / length;
        var back = new Point(tip.X - (ux * ArrowLength), tip.Y - (uy * ArrowLength));

        // 垂直于走向的单位向量，用来把箭头的两个后角撑开。
        var nx = -uy * (ArrowWidth / 2);
        var ny = ux * (ArrowWidth / 2);

        switch (arrow)
        {
            case ArrowStyle.Arrow:
                context.DrawGeometry(brush, null, Polygon(
                    tip,
                    new Point(back.X + nx, back.Y + ny),
                    new Point(back.X - nx, back.Y - ny)));
                break;

            case ArrowStyle.OpenArrow:
                var pen = new Pen(brush, weight);
                context.DrawLine(pen, tip, new Point(back.X + nx, back.Y + ny));
                context.DrawLine(pen, tip, new Point(back.X - nx, back.Y - ny));
                break;

            case ArrowStyle.Circle:
                context.DrawEllipse(brush, null, tip, ArrowWidth / 2, ArrowWidth / 2);
                break;

            case ArrowStyle.Cross:
                // 一个叉号：沿走向与垂直于走向各画一道。
                var cross = new Pen(brush, weight);
                context.DrawLine(cross, new Point(tip.X - ux, tip.Y - uy), new Point(tip.X + ux, tip.Y + uy));
                context.DrawLine(cross, new Point(tip.X - nx, tip.Y - ny), new Point(tip.X + nx, tip.Y + ny));
                break;

            default:
                throw new NotSupportedException($"认不出的箭头样式：{arrow}");
        }
    }

    #endregion

    #region 文本

    /// <summary>
    /// 画一行文本。
    /// </summary>
    /// <remarks>
    /// 指令里的框已经是对齐算完之后的框，左边缘就是起始位置，
    /// 所以这里只做纵向居中——横向再对一次会把居中标签推偏半个字宽。
    /// 纵向居中不能省：框高是按行高算的，而字形的实际高度比行高小，
    /// 不居中则文字贴着框的上沿。
    /// </remarks>
    private void DrawText(DrawingContext context, ViewportTransform transform, DrawText text)
    {
        var origin = transform.ToScreen(text.Box.X, text.Box.Y);
        var box = transform.ToScreen(text.Box);

        var formatted = new FormattedText(
            text.Text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            TypefaceFor(text.FontFamily, text.Weight),
            text.FontSize * transform.Scale,
            Brush(text.Color));

        context.DrawText(formatted, new Point(origin.X, origin.Y + ((box.Height - formatted.Height) / 2)));
    }

    #endregion

    #region 缓存

    private IBrush Brush(string color)
    {
        if (_brushes.TryGetValue(color, out var cached))
        {
            return cached;
        }

        // 认不出的颜色退回黑色而不是抛异常：颜色来自文档，一份写错颜色的文档
        // 该表现成"颜色不对"，而不是整张图打不开。
        var brush = Color.TryParse(color, out var parsed)
            ? new ImmutableSolidColorBrush(parsed)
            : Brushes.Black;

        _brushes[color] = brush;

        return brush;
    }

    private Pen Pen(string color, double weight, LineStyle line)
    {
        var brush = Brush(color);

        return line switch
        {
            LineStyle.Dashed => new Pen(brush, weight, new DashStyle([6, 4], 0)),
            LineStyle.Dotted => new Pen(brush, weight, new DashStyle([1, 3], 0)),
            _ => new Pen(brush, weight),
        };
    }

    private Typeface TypefaceFor(string family, CoreFontWeight weight)
    {
        var key = (family, weight);

        if (_typefaces.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var typeface = new Typeface(
            new FontFamily(string.IsNullOrWhiteSpace(family) ? FontFamily.Default.Name : family),
            FontStyle.Normal,
            weight == CoreFontWeight.Bold ? FontWeight.Bold : FontWeight.Normal);

        _typefaces[key] = typeface;

        return typeface;
    }

    private static Rect ToRect(SpatialRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    #endregion

    #region 尺寸与状态

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);

        Model?.Resize(size.Width, size.Height);

        return size;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        Resubscribe();
    }

    private void Resubscribe()
    {
        var model = Model;

        if (ReferenceEquals(_subscribed, model))
        {
            return;
        }

        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnModelChanged;
        }

        _subscribed = model;

        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged += OnModelChanged;
        }

        InvalidateVisual();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    #endregion

    #region 输入

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (Model is not { } model || e.Delta.Y == 0)
        {
            return;
        }

        // 向上滚是放大。锚点取光标位置：以画布中心为锚点的话，
        // 用户想放大的那一处会随着缩放跑出视口，只能一边滚一边往回拖。
        var point = e.GetPosition(this);

        model.ZoomAt(Math.Pow(ZoomPerNotch, e.Delta.Y), point.X, point.Y);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (Model is null)
        {
            return;
        }

        // 键盘消息只发给有焦点的控件，所以点一下就把焦点收过来，空格键才会被这里收到。
        Focus();

        var point = e.GetCurrentPoint(this);
        var wantsPan = point.Properties.IsMiddleButtonPressed
            || (_spaceHeld && point.Properties.IsLeftButtonPressed);

        if (!wantsPan)
        {
            return;
        }

        _panning = true;
        _lastPointer = e.GetPosition(this);
        Cursor = PanCursor;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (Model is not { } model)
        {
            return;
        }

        var position = e.GetPosition(this);

        model.MovePointer(position.X, position.Y);

        if (!_panning)
        {
            return;
        }

        model.PanBy(position.X - _lastPointer.X, position.Y - _lastPointer.Y);
        _lastPointer = position;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_panning)
        {
            return;
        }

        EndPan();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        // 指针被别处抢走（例如窗口失去激活）时也要收尾，
        // 否则下一次按下会被当成"还在拖"，图会突然跳一段。
        EndPan();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        Model?.LeavePointer();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Space)
        {
            _spaceHeld = true;
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (e.Key == Key.Space)
        {
            _spaceHeld = false;
            e.Handled = true;
        }
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);

        // 焦点丢了就收不到抬起消息，空格会被永远记成"按着"，
        // 于是之后每一次左键按下都变成平移，用户以为选中坏了。
        _spaceHeld = false;
    }

    private void EndPan()
    {
        if (!_panning)
        {
            return;
        }

        _panning = false;
        Cursor = Cursor.Default;
    }

    #endregion

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
