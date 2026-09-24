using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using DuetDiagram.App.Interaction;
using DuetDiagram.App.Services;
using DuetDiagram.App.ViewModels;
using DuetDiagram.Layout;
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
/// <para>
/// **计时也在这里**，因为"一帧"的边界就是 <see cref="Render"/>。
/// 剔除那一段与光栅化那一段分开记：出问题时第一件要判断的是该改哪儿，
/// 而一个总数回答不了它。诊断关着时只多做一次判断。
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

    /// <summary>文档那一侧。拖节点需要它来定选中、写固定位置；普通选中也走它。</summary>
    public DiagramSession? Session { get; set; }

    /// <summary>
    /// 这个画布属于哪个窗口。
    /// </summary>
    /// <remarks>
    /// 右键菜单要用它：条目的启用判据与执行都要看会话、画布状态与状态栏，
    /// 而那三样收在 <see cref="MenuContext"/> 上（见工具栏那边的同一处接线）。
    /// 由画布自己去找窗口的话，"这个画布属于谁"就有了两个来源。
    /// </remarks>
    public MainWindow? Host { get; set; }

    private DragController? _dragger;
    private bool _nodeDragging;

    // 一次框选。按在空白处才开始，拖动中只更新叠加层上的那个选框。
    private MarqueeSession? _marquee;
    private SpatialRect? _marqueeArea;

    /// <summary>右键菜单。每次右键现建一份：条目随选中的内容变。</summary>
    private ContextMenu? _contextMenu;

    // 脉冲动画的帧驱动。只有真的有脉冲在跑时才转——空闲时也在转的话，
    // 省电模式下会被系统降频，而那个降频会被误读成性能退化。
    private DispatcherTimer? _pulseTimer;

    // 连线 / 重连 / 加折点的手势状态。它们与节点拖拽互斥：一次按下只进一条手势。
    private bool _connecting;
    private bool _reconnecting;
    private string? _reconnectEdge;
    private bool _reconnectStart;
    private bool _bending;
    private string? _bendEdge;
    private int _bendSegment;
    private DrawPoint _bendStart;

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

        // 诊断关着的时候这里只多做一次判断，取时钟与记账全部跳过。
        // 这一行每帧都会走到，所以它自己不能有开销——诊断工具自己成了开销就没法用了。
        var diagnostics = model.Diagnostics;
        var measuring = diagnostics.Enabled;
        var frameStart = measuring ? Stopwatch.GetTimestamp() : 0L;

        // 这一帧画哪几条由它定：整份列表，或者是按视口剔过的一份子序列。
        // 画布自己不判断该不该剔除——那条判据只此一处，放在画布上就会与别处对不上。
        FeedHighlights(model);
        model.BeginFrame();

        var commands = model.FrameCommands;

        if (commands.Count == 0)
        {
            return;
        }

        var cull = measuring ? Elapsed(frameStart) : 0;
        var rasterStart = measuring ? Stopwatch.GetTimestamp() : 0L;

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

            DrawSelection(context, transform, model.SelectionBounds);
        }

        if (!measuring)
        {
            return;
        }

        diagnostics.Record(new DiagnosticsFrame(
            Elapsed(frameStart),
            cull,
            Elapsed(rasterStart),
            _drawnCommands,
            model.DrawList.Commands.Count - commands.Count,
            model.Mode.Mode));
    }

    private static double Elapsed(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    #region 选中框

    /// <summary>
    /// 选中框的笔。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 虚线，不填色。填一层半透明底会把元素自己的填充色改掉，而用户正看着那个颜色
    /// 判断这次改对了没有——选中框把要判断的东西盖住了，就没法判断了。
    /// </para>
    /// <para>
    /// 在屏幕坐标下画，线宽与虚线的疏密因此不随缩放变。跟着缩放变的话，
    /// 缩到两成时框线会细到看不见，放到四倍时那一段虚线会长得像个实框。
    /// </para>
    /// </remarks>
    private static readonly Pen SelectionPen =
        new(Avalonia.Media.Brush.Parse("#1f6feb"), 1, new DashStyle([4, 3], 0));

    private static void DrawSelection(
        DrawingContext context,
        ViewportTransform transform,
        SpatialRect? selection)
    {
        if (selection is not { } rect)
        {
            return;
        }

        // 往外撑一像素：贴着元素外沿画的话，框会压在元素自己的描边上，
        // 那一圈描边看起来就变粗了。
        context.DrawRectangle(null, SelectionPen, ToRect(transform.ToScreen(rect)).Inflate(1));
    }

    #endregion

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

    #region 变更高亮

    /// <summary>
    /// 把当前的标记算成这一帧要叠的指令交给视图模型，并按脉冲是否在跑决定要不要继续出帧。
    /// </summary>
    /// <remarks>
    /// 画布只认"有一份标记"与"相位是多少"，标记从哪来、怎么攒的不归它管。
    /// 高亮是叠加层，不进绘制列表的几何——混进去之后动画的时间戳会污染快照测试，
    /// 而那些快照本该是"同一份输入永远同一份输出"。
    /// </remarks>
    private void FeedHighlights(CanvasViewModel model)
    {
        // 没有会话（例如帧率基准直接把画布挂起来）时不碰高亮层：
        // 那种情况下高亮由宿主自己设好，画布清掉它等于把要量的东西量没了。
        if (Session is not { } session)
        {
            StopPulseTimer();
            return;
        }

        if (!session.HasHighlights)
        {
            model.ClearHighlights();
            StopPulseTimer();
            return;
        }

        model.SetHighlights(HighlightOverlay.Commands(
            session.HighlightSnapshot,
            model.DrawList,
            model.Theme,
            session.HighlightPhase));

        // 有脉冲在跑就继续出帧，跑完了就停。空闲时也转的话，省电模式下会被系统降频，
        // 而那个降频会被误读成性能退化。
        if (session.HasActivePulse)
        {
            StartPulseTimer();
        }
        else
        {
            StopPulseTimer();
        }
    }

    private void StartPulseTimer()
    {
        if (_pulseTimer is null)
        {
            // 约三十帧每秒。脉冲是渐亮渐暗的慢动作，再密的帧也看不出差别，
            // 而每一帧都要重算一遍高亮指令并重绘。
            _pulseTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(33),
                DispatcherPriority.Render,
                (_, _) => InvalidateVisual());
        }

        if (!_pulseTimer.IsEnabled)
        {
            _pulseTimer.Start();
        }
    }

    private void StopPulseTimer()
    {
        if (_pulseTimer is { IsEnabled: true })
        {
            _pulseTimer.Stop();
        }
    }

    #endregion

    #region 尺寸与状态

    protected override Avalonia.Size ArrangeOverride(Avalonia.Size finalSize)
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

        if (Model is not { } model)
        {
            return;
        }

        // 键盘消息只发给有焦点的控件，所以点一下就把焦点收过来，空格键才会被这里收到。
        // 它必须在选中之前：焦点一移开，属性面板上正在编辑的那个框就失焦提交了，
        // 而那次提交要落在点下去之前选中的那个元素上。
        Focus();

        var point = e.GetCurrentPoint(this);

        // 右键：先把菜单弹出来，不动选中。右键"选中并弹菜单"是另一种做法，
        // 但那样一次误触就换掉了用户好不容易攒起来的选中集合。
        if (point.Properties.IsRightButtonPressed)
        {
            ShowContextMenu(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        var wantsPan = point.Properties.IsMiddleButtonPressed
            || (_spaceHeld && point.Properties.IsLeftButtonPressed);

        if (!wantsPan)
        {
            if (point.Properties.IsLeftButtonPressed)
            {
                var position = e.GetPosition(this);
                var additive = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                    || e.KeyModifiers.HasFlag(KeyModifiers.Shift);

                // 先判连线类的把手：端口（新连线起点）、边的端点（重连）、边的中间（加折点）。
                // 这几样优先级高于节点拖拽——按在把手上用户想的是编辑这条边，不是挪节点。
                if (Session is not null && TryBeginEdgeGesture(model, position.X, position.Y))
                {
                    e.Pointer.Capture(this);
                    e.Handled = true;
                    return;
                }

                // 点中节点就进入拖拽：按下那一刻定下"动的是谁"，之后的移动只更新偏移，
                // 松手才写一次固定位置。点中的不是节点（空白或边）走普通选中。
                if (Session is not null)
                {
                    var startDoc = model.Viewport.Transform.ToDocument(position.X, position.Y);
                    _dragger = new DragController(Session, model);

                    if (_dragger.Press(model.Pick(position.X, position.Y), additive, startDoc))
                    {
                        _nodeDragging = true;
                        e.Pointer.Capture(this);
                        e.Handled = true;
                        return;
                    }

                    _dragger = null;
                }

                // 按在空白处：开始一次框选，**先不改选中**。
                // 松手时若指针几乎没动，那一下仍然算点选（清空选中），见 OnPointerReleased。
                // 按下就清掉的话，用户想框选却先把选中清空了，而在框选失败时那个清空已经发生。
                if (model.Pick(position.X, position.Y) is null)
                {
                    var anchor = model.Viewport.Transform.ToDocument(position.X, position.Y);

                    _marquee = new MarqueeSession(anchor, additive, model.Viewport.Scale);

                    e.Pointer.Capture(this);
                    e.Handled = true;
                    return;
                }

                model.RequestSelection(position.X, position.Y, additive);
                e.Handled = true;
            }

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

        // 框选进行中：只更新那个选框，不改文档、不改选中。
        if (_marquee is not null)
        {
            if (_marquee.Move(model.Viewport.Transform.ToDocument(position.X, position.Y)) is { } area)
            {
                _marqueeArea = area;
                model.SetOverlay(EdgeAdorner.Marquee(area));
                InvalidateVisual();
            }

            e.Handled = true;
            return;
        }

        // 拖拽进行中：移动只更新预览偏移，不碰文档、不碰布局。每一帧由画布把
        // 选中节点整体挪一下，松手才由宿主一次性落定。
        if (_nodeDragging && _dragger is not null)
        {
            _dragger.Move(model.Viewport.Transform.ToDocument(position.X, position.Y));
            e.Handled = true;
            return;
        }

        // 连线 / 重连手势：移动只更新预览线，不碰文档。
        if ((_connecting || _reconnecting) && Session is not null)
        {
            var doc = model.Viewport.Transform.ToDocument(position.X, position.Y);
            Session.UpdateConnect(doc);

            if (Session.ConnectPreview is { } points)
            {
                model.SetOverlay(EdgeAdorner.ConnectPreview(points));
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // 加折点手势：移动把预览的折点跟到光标。
        if (_bending && Session is not null)
        {
            var doc = model.Viewport.Transform.ToDocument(position.X, position.Y);
            model.SetOverlay(BendPreview(Session, _bendEdge!, _bendSegment, doc));
            InvalidateVisual();
            e.Handled = true;
            return;
        }

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

        // 框选松手：到这里才算一次选中。指针几乎没动的那一下算点选，
        // 走的还是原来那条"清空选中"的路。
        if (_marquee is not null)
        {
            var position = e.GetPosition(this);
            var marquee = _marquee;
            var area = _marqueeArea;

            _marquee = null;
            _marqueeArea = null;
            Model?.ClearOverlay();

            if (marquee.IsMarquee && area is { } box && Model is { } model && Session is not null)
            {
                var ids = marquee.Ids(model.DrawList, box);

                // 框选只收节点。组合的框与它的成员共用一片区域，框到成员必然同时
                // 碰到组合的框，而把组合收进来等于把它全部成员都拖走——比用户框的多。
                // 组合用点框的方式选中（点在框的空白处）。
                var compositeIds = Session.Document.Composites
                    .Select(composite => composite.Id)
                    .ToHashSet(StringComparer.Ordinal);

                if (compositeIds.Count > 0)
                {
                    ids = [.. ids.Where(id => !compositeIds.Contains(id))];
                }

                // 增选模式下并进已有选中，否则换掉它。并的时候保持原来的前后次序，
                // 新的追加在后面——选中次序有语义（属性面板按它取第一个元素）。
                Session.SetSelection(marquee.Additive
                    ? [.. Session.SelectedIds.Union(ids, StringComparer.Ordinal)]
                    : ids);
            }
            else
            {
                Model?.RequestSelection(position.X, position.Y, marquee.Additive);
            }

            InvalidateVisual();
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_nodeDragging)
        {
            // 松手时指针底下的那个元素决定这一拖落定成什么：压在一个兄弟节点上是一条层内次序，
            // 落在空白处是一个绝对位置。拾取按布局坐标来，不按预览坐标——
            // 预览只是画布把指令整体挪了一下，文档里的位置自始至终没动。
            var drop = e.GetPosition(this);

            _dragger?.Release(Model?.Pick(drop.X, drop.Y));
            _nodeDragging = false;
            _dragger = null;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        // 连线 / 重连 / 加折点：松手把这一手势落定，再清掉叠加层。
        if (_connecting || _reconnecting || _bending)
        {
            CommitEdgeGesture(e);
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

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
        if (_nodeDragging)
        {
            _dragger?.Cancel();
            _nodeDragging = false;
            _dragger = null;
        }

        // 框选也一样：被打断的那一次不落定成选中，选框也收掉。
        if (_marquee is not null)
        {
            _marquee = null;
            _marqueeArea = null;
            Model?.ClearOverlay();
            InvalidateVisual();
        }

        // 连线类手势被打断同样作废：不创建边、不改端点、不加折点。
        if (_connecting || _reconnecting || _bending)
        {
            CancelEdgeGesture();
        }

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

        // 拖拽中丢了焦点，这一拖作废：不写固定位置，节点回到原处。
        // 否则一次意外的失焦会把节点挪到用户没打算去的地方。
        if (_nodeDragging)
        {
            _dragger?.Cancel();
            _nodeDragging = false;
            _dragger = null;
        }

        // 连线类手势同样作废：焦点丢了就收不到抬起消息，留着会卡在"还在连"的状态。
        if (_connecting || _reconnecting || _bending)
        {
            CancelEdgeGesture();
        }
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

    #region 右键菜单

    /// <summary>
    /// 在这一点上弹出右键菜单。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **条目从注册表来，只按"落在元素上还是空白处"分两套。** 菜单里摆哪几条由
    /// <see cref="ContextMenuBuilder"/> 决定，这一层只把控件搭出来。
    /// </para>
    /// <para>
    /// **点不动的条目也摆出来，并把理由写在标题上** —— 与工具栏同一条口径：
    /// 灰掉而不说为什么，用户会以为程序坏了；藏起来的话，用户会以为这里没有这个功能。
    /// </para>
    /// </remarks>
    private void ShowContextMenu(Point position)
    {
        if (Host is not { } window || Model is not { } model)
        {
            return;
        }

        var target = model.Pick(position.X, position.Y);
        var context = new MenuContext(window, target);
        var menu = new ContextMenu { PlacementTarget = this };

        foreach (var entry in ContextMenuBuilder.Build(context, target is not null))
        {
            var reason = entry.Refusal(context);
            var item = new MenuItem
            {
                Header = reason is null ? entry.Label : $"{entry.Label}（{reason}）",
                IsEnabled = reason is null,
            };

            AutomationProperties.SetAutomationId(item, $"context.{entry.Id}");

            var chosen = entry;

            // 命令跑完之后不必在这里刷新菜单栏与工具栏：这一档里的每一条都会改选中
            // （建组合把选中换到新组合上、删除与两个选择动作更不用说），
            // 而选中一变，主窗口那条路就会刷一遍。
            item.Click += (_, _) => chosen.Run(context);

            menu.Items.Add(item);
        }

        _contextMenu?.Close();
        _contextMenu = menu;
        ContextMenu = menu;

        menu.Open(this);
    }

    #endregion

    #region 连线与边编辑手势

    /// <summary>
    /// 按下那一刻决定这一手势是连线、重连还是加折点。
    /// </summary>
    /// <remarks>
    /// 优先级：端口把手（新连线）&gt; 边端点（重连）&gt; 边中间（加折点）&gt; 节点拖拽。
    /// 按在把手上用户想的是编辑这条边，不是挪节点，所以把手先于节点判定。
    /// </remarks>
    private bool TryBeginEdgeGesture(CanvasViewModel model, double screenX, double screenY)
    {
        var session = Session!;

        // 只读时不进入连线、重连与折点手势。会话那边也会拒，但手势先于命令：
        // 放它进来会在画布上留一条跟着光标走的预览线，用户松手才发现什么也没发生——
        // 那比当场不动更难理解，也更像"软件卡了"。
        if (session.IsReadOnly)
        {
            return false;
        }

        var layout = session.Scene.Layout;
        var doc = model.Viewport.Transform.ToDocument(screenX, screenY);
        var scale = model.Viewport.Scale <= 0 ? 1 : model.Viewport.Scale;
        var tolerance = 6 / scale;

        // 端口把手：开始一条新连线。端口位置来自布局算出的锚点，按下那一刻记死。
        var port = EdgeHandleHitTest.HitPort(layout, session.Document, doc, tolerance);

        if (port is { } p)
        {
            if (session.BeginConnect(p.NodeId, p.PortName, EndpointAnchor(session, p.NodeId, p.PortName)))
            {
                _connecting = true;
                model.SetOverlay(EdgeAdorner.ConnectPreview(session.ConnectPreview!));
                InvalidateVisual();
                return true;
            }
        }

        var edge = EdgeHandleHitTest.HitEdgeHandle(layout, doc, tolerance);

        if (edge.Kind == EdgeHandleHitTest.EdgeHandleKind.None)
        {
            return false;
        }

        // 边中间：拖一下加一个折点。预览线把光标插到对应那段之间。
        if (edge.Kind == EdgeHandleHitTest.EdgeHandleKind.Midpoint)
        {
            _bending = true;
            _bendEdge = edge.EdgeId;
            _bendSegment = edge.Segment;
            _bendStart = doc;
            model.SetOverlay(BendPreview(session, edge.EdgeId, edge.Segment, doc));
            InvalidateVisual();
            return true;
        }

        // 边端点：把另一端当"固定端点"开始一次重连预览。
        var fixedEnd = edge.Kind == EdgeHandleHitTest.EdgeHandleKind.Start
            ? OtherEnd(session, edge.EdgeId, start: false)
            : OtherEnd(session, edge.EdgeId, start: true);

        if (fixedEnd is null)
        {
            return false;
        }

        _reconnecting = true;
        _reconnectEdge = edge.EdgeId;
        _reconnectStart = edge.Kind == EdgeHandleHitTest.EdgeHandleKind.Start;
        session.BeginConnect(fixedEnd.Value.NodeId, fixedEnd.Value.PortName, fixedEnd.Value.Anchor);
        model.SetOverlay(EdgeAdorner.ConnectPreview(session.ConnectPreview!));
        InvalidateVisual();
        return true;
    }

    /// <summary>松手把连线类手势落定：连线创建边、重连改端点、加折点写固定折线。</summary>
    private void CommitEdgeGesture(PointerReleasedEventArgs e)
    {
        var session = Session!;
        var model = Model!;
        var position = e.GetPosition(this);
        var doc = model.Viewport.Transform.ToDocument(position.X, position.Y);
        var scale = model.Viewport.Scale <= 0 ? 1 : model.Viewport.Scale;
        var tolerance = 6 / scale;
        var layout = session.Scene.Layout;

        if (_connecting)
        {
            var targetId = model.Pick(position.X, position.Y);
            var targetPort = EdgeHandleHitTest.HitPort(layout, session.Document, doc, tolerance)?.PortName;
            session.CommitConnect(targetId, targetPort);
        }
        else if (_reconnecting)
        {
            var targetId = model.Pick(position.X, position.Y);
            var targetPort = EdgeHandleHitTest.HitPort(layout, session.Document, doc, tolerance)?.PortName;
            var edge = session.Document.Edges.FirstOrDefault(x => x.Id == _reconnectEdge);

            if (edge is not null && targetId is not null)
            {
                // 固定的是被按住那一端对应的另一端，移动的才是落点这一端。
                if (_reconnectStart)
                {
                    session.ReconnectEdge(edge.Id, targetId, targetPort, edge.To, edge.ToPort);
                }
                else
                {
                    session.ReconnectEdge(edge.Id, edge.From, edge.FromPort, targetId, targetPort);
                }
            }
            else
            {
                session.CancelConnect();
            }
        }
        else if (_bending)
        {
            // 几乎没动就是一次点击，不加折点，避免误触把一条直边顶出一个弯。
            var moved = Math.Abs(doc.X - _bendStart.X) > tolerance || Math.Abs(doc.Y - _bendStart.Y) > tolerance;

            if (moved)
            {
                session.SetEdgeBends(_bendEdge!, [doc]);
            }
        }

        CancelEdgeGesture();
    }

    /// <summary>手势作废：清状态、清叠加层、清会话里的连线预览。</summary>
    private void CancelEdgeGesture()
    {
        _connecting = false;
        _reconnecting = false;
        _reconnectEdge = null;
        _bending = false;
        _bendEdge = null;

        Session?.CancelConnect();
        Model?.ClearOverlay();
        InvalidateVisual();
    }

    /// <summary>一条边被抓住那一端的另一端：节点、端口名与锚点。</summary>
    private static (string NodeId, string? PortName, DrawPoint Anchor)? OtherEnd(
        DiagramSession session,
        string edgeId,
        bool start)
    {
        var edge = session.Document.Edges.FirstOrDefault(e => e.Id == edgeId);

        if (edge is null)
        {
            return null;
        }

        var id = start ? edge.From : edge.To;
        var port = start ? edge.FromPort : edge.ToPort;

        return (id, port, EndpointAnchor(session, id, port));
    }

    /// <summary>一个端点（节点 + 端口名）在文档坐标下的锚点，用于预览线的起点。</summary>
    private static DrawPoint EndpointAnchor(DiagramSession session, string nodeId, string? portName)
    {
        var placed = session.Scene.Layout.Find(nodeId);

        if (placed is null)
        {
            return new DrawPoint(0, 0);
        }

        if (portName is null)
        {
            return new DrawPoint(placed.Right, placed.Y + (placed.Height / 2));
        }

        var node = session.Document.Nodes.FirstOrDefault(n => n.Id == nodeId);
        var port = node?.Ports.FirstOrDefault(p => string.Equals(p.Name, portName, StringComparison.Ordinal));

        var layoutPort = port is not null
            ? new LayoutPort(port.Name, port.Side, port.Offset)
            : new LayoutPort(portName, DuetDiagram.Core.Model.PortSide.Right, 0.5);

        var anchor = placed.PortAnchor(layoutPort);

        return new DrawPoint(anchor.X, anchor.Y);
    }

    /// <summary>加折点的预览：把光标插到边折线的对应那段之间。</summary>
    private static IReadOnlyList<DrawCommand> BendPreview(
        DiagramSession session,
        string edgeId,
        int segment,
        DrawPoint doc)
    {
        var route = session.FindEdgeRoute(edgeId);

        if (route is null || route.Points.Length == 0)
        {
            return [];
        }

        var points = new List<DrawPoint>(route.Points.Length + 1);

        for (var index = 0; index < route.Points.Length; index++)
        {
            if (index == segment + 1)
            {
                points.Add(doc);
            }

            points.Add(new DrawPoint(route.Points[index].X, route.Points[index].Y));
        }

        if (segment + 1 >= route.Points.Length)
        {
            points.Add(doc);
        }

        return EdgeAdorner.ConnectPreview(points);
    }

    #endregion

    #endregion

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
