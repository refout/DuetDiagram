using System.ComponentModel;
using System.Globalization;
using DuetDiagram.Render;

namespace DuetDiagram.App.ViewModels;

/// <summary>
/// 画布的状态：画什么、看哪儿。
/// </summary>
/// <remarks>
/// <para>
/// **它只认绘制列表，不认文档。** 文档到绘制列表那一段在渲染层，
/// 画布这一侧再读一次 IR 的话，渲染路径就绕过了绘制列表——
/// 快照测试覆盖到的东西与实际画出来的东西会分叉，而两边都"通过"。
/// </para>
/// <para>
/// 视口在这里而不是在控件里：状态栏要显示坐标与缩放，而状态栏与画布是两个控件。
/// 状态放在其中一个控件里，另一个就只能靠回调去要，那样的耦合迟早会绕成一团。
/// </para>
/// </remarks>
public sealed class CanvasViewModel : INotifyPropertyChanged
{
    private readonly CullingPolicy _culling;
    private readonly ModeSwitch _switch;

    private DrawList _drawList = DrawList.Empty;
    private CullingIndex? _index;
    private IReadOnlyList<DrawCommand> _frameCommands = [];
    private Viewport _viewport;
    private DrawPoint _pointer;
    private bool _pointerInside;
    private IReadOnlyList<string> _selectedIds = [];
    private SpatialRect? _selectionBounds;

    private readonly HashSet<string> _draggedElements = new(StringComparer.Ordinal);
    private DrawPoint _dragDelta;
    private bool _dragging;

    // 连线与边编辑时叠在正式列表之上的一层临时图形（端口把手、连线预览、折点把手）。
    // 它只在手势进行中存在；松手后由宿主把结果写进文档，这层清空。
    private IReadOnlyList<DrawCommand> _overlay = [];

    // 变更高亮层。它比手势层活得久——标记一直留着，直到下一次变更或撤销把它改掉。
    // 与手势层分开两栏，是因为两者的生命周期与清空时机都不一样：手势一松手就清，
    // 高亮要等下一次变更。合成一栏的话，一次连线松手会把别人的高亮一起抹掉。
    private IReadOnlyList<DrawCommand> _highlight = [];

    public CanvasViewModel(Theme? theme = null, CullingPolicy? culling = null, DiagnosticsViewModel? diagnostics = null)
    {
        Theme = theme ?? Theme.Default;
        _culling = culling ?? CullingPolicy.Default;
        _switch = new ModeSwitch(_culling);

        Mode = new RenderModeViewModel();
        Diagnostics = diagnostics ?? new DiagnosticsViewModel();
        _viewport = Viewport.For(Theme);
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 用户在画布上点了一下，要求选中某个元素。
    /// </summary>
    /// <remarks>
    /// 参数是命中的元素标识（点空处为空）与"是不是在已有选中上增删"。
    /// 画布只管报出"点到了谁"，选不选、选几个由文档那一侧定——
    /// 两边各判一次的话，判据迟早会分叉，而表现是"选中框亮了但面板没换"。
    /// </remarks>
    public event Action<string?, bool>? SelectionRequested;

    /// <summary>当前选中的元素标识。</summary>
    public IReadOnlyList<string> SelectedIds => _selectedIds;

    /// <summary>选中元素的外接框，文档坐标。没有选中或列表里找不到它时为空。</summary>
    /// <remarks>
    /// 拖拽进行中，选中框跟着临时偏移走，让用户看到"这一拖会落到哪"。
    /// 偏移只加在读出来的这一刻，底层那一份（<see cref="_selectionBounds"/>）不动——
    /// 否则松手后框会停在偏移后的位置，而节点已经被固定到偏移后的位置，两者本该重合，
    /// 偏一下的话框就永远比节点慢半个拖动距离。
    /// </remarks>
    public SpatialRect? SelectionBounds => _selectionBounds is { } rect && _dragging
        ? new SpatialRect(rect.X + _dragDelta.X, rect.Y + _dragDelta.Y, rect.Width, rect.Height)
        : _selectionBounds;

    /// <summary>外观查表。缩放上下界也从它取。</summary>
    public Theme Theme { get; }

    /// <summary>这一帧走哪一档、剔掉了多少。</summary>
    public RenderModeViewModel Mode { get; }

    /// <summary>性能诊断面板的状态与采样窗口。</summary>
    public DiagnosticsViewModel Diagnostics { get; }

    /// <summary>
    /// 这一帧要画的指令。
    /// </summary>
    /// <remarks>
    /// 它是 <see cref="DrawList"/> 的一份子序列，也可能是整份。由 <see cref="BeginFrame"/>
    /// 在每帧开头定下来——顺序不变，只是少几条。
    /// </remarks>
    public IReadOnlyList<DrawCommand> FrameCommands => _frameCommands;

    /// <summary>要画的东西。</summary>
    public DrawList DrawList
    {
        get => _drawList;
        private set => Set(ref _drawList, value, nameof(DrawList), nameof(IsEmpty));
    }

    /// <summary>看哪儿。</summary>
    public Viewport Viewport
    {
        get => _viewport;
        private set => Set(ref _viewport, value, nameof(Viewport), nameof(ZoomText));
    }

    /// <summary>没有可画的东西。界面据此显示一句提示而不是一片空白。</summary>
    public bool IsEmpty => _drawList.Commands.Count == 0;

    /// <summary>光标在文档里的位置。光标不在画布上时给出一个占位符。</summary>
    public string PointerText =>
        _pointerInside
            ? string.Format(CultureInfo.InvariantCulture, "x {0:0.#}   y {1:0.#}", _pointer.X, _pointer.Y)
            : "—";

    /// <summary>当前缩放倍数。</summary>
    /// <remarks>
    /// 用百分比而不是倍数：用户判断"能不能看清"看的是百分比，
    /// 而 0.35 这种写法要先在心里乘一百。
    /// </remarks>
    public string ZoomText =>
        string.Format(CultureInfo.InvariantCulture, "缩放 {0:0}%", _viewport.Scale * 100);

    /// <summary>
    /// 换一份绘制列表，并把视口适配到内容上。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 每次换列表都重新适配：保留上一次的视角的话，换一份内容差得远的文档之后
    /// 用户看到的是一片空白，只能自己摸索着找回来。
    /// </para>
    /// <para>
    /// 索引与模式一起从头来过。旧索引指向的是上一份列表，留着它对新的这份毫无用处，
    /// 而"模式已经是虚拟化、索引却是空的"这种状态会让画布退回整份遍历，
    /// 看起来像剔除失效了。
    /// </para>
    /// <para>
    /// 帧窗口也一起清掉：上一份文档的帧时对新文档没有意义，
    /// 混在一起算出来的分位数既不是这一份也不是那一份。
    /// </para>
    /// </remarks>
    public void Load(DrawList drawList) => Replace(drawList, fit: true);

    /// <summary>
    /// 换一份绘制列表，但保留当前视角。
    /// </summary>
    /// <remarks>
    /// 改了一个字段之后走这条。走 <see cref="Load"/> 的话视口会重新适配一次，
    /// 用户改一个字，图就跳回默认视角——而那一次改动与视角毫无关系。
    /// </remarks>
    public void Refresh(DrawList drawList) => Replace(drawList, fit: false);

    /// <summary>
    /// 换一批选中，并把选中框重算一次。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 外接框在这里一次算好并缓存。每帧在渲染路径里现算的话，
    /// 千节点的图上每帧都要遍历一遍整份列表，而选中几乎不变。
    /// </para>
    /// <para>
    /// 标识列表也留一份，为的是换绘制列表时能重算选中框。它是一份缓存，
    /// 不是"谁被选中了"的第二个说法——那个说法在文档那一侧，
    /// 这里只由宿主按那一边的结果调进来。
    /// </para>
    /// </remarks>
    public void SetSelection(IReadOnlyList<string> elementIds)
    {
        ArgumentNullException.ThrowIfNull(elementIds);

        if (elementIds.Count == _selectedIds.Count
            && elementIds.SequenceEqual(_selectedIds, StringComparer.Ordinal))
        {
            return;
        }

        _selectedIds = [.. elementIds];
        _selectionBounds = BoundsOfSelection();

        Raise(nameof(SelectedIds));
        Raise(nameof(SelectionBounds));
    }

    /// <summary>
    /// 屏幕上这个点下面是哪个元素，并把它报出去。
    /// </summary>
    /// <remarks>
    /// 命中的判定在 <see cref="Pick"/> 里，这里只负责把结果报给宿主。
    /// 报出去而不是自己记下来：选中是文档那一侧的状态，这里记一份会多出一个说法。
    /// </remarks>
    public void RequestSelection(double screenX, double screenY, bool additive) =>
        SelectionRequested?.Invoke(Pick(screenX, screenY), additive);

    /// <summary>
    /// 屏幕上这个点下面是哪个元素。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **按整份列表找，不按这一帧画出来的那份。** 剔除只是少画，不影响"点得到"——
    /// 按画出来的那份找的话，被剔掉的东西永远选不中，而用户在缩放到边界时
    /// 会遇到"明明看见了却点不中"。
    /// </para>
    /// <para>
    /// 容差按屏幕像素给，换算成文档单位再传下去。缩放倍数大时同样的屏幕距离
    /// 对应更小的文档距离，不换算的话放大之后连线会变得极难点中。
    /// </para>
    /// </remarks>
    public string? Pick(double screenX, double screenY, double tolerancePixels = 4)
    {
        var document = _viewport.Transform.ToDocument(screenX, screenY);
        var scale = _viewport.Scale <= 0 ? 1 : _viewport.Scale;

        return HitTester.Hit(_drawList.Commands, document, tolerancePixels / scale);
    }

    /// <summary>
    /// 一帧开始时调用：定下这一帧要画什么，并把下一帧要用的东西预备好。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 顺序不能颠倒。先让上一次的判定生效，再按新模式取指令，最后判定下一次该走哪一档——
    /// 反过来会让"判定"与"生效"挤在同一帧，而那一帧正是要避免多做事的那一帧。
    /// </para>
    /// <para>
    /// 需要为新模式准备的东西**付在这一帧**：大图上是那份空间索引，建它要遍历整份列表。
    /// 把它挪到生效那一帧，表现就是转一下视图卡一下。
    /// </para>
    /// </remarks>
    public void BeginFrame()
    {
        _switch.Commit();

        var baseCommands = _switch.Current == RenderMode.Virtualized && _index is not null
            ? _index.Visible(_culling.VisibleArea(_viewport.VisibleDocumentRect))
            : _drawList.Commands;

        // 拖拽进行中只把选中节点（及其相连边）的绘制指令整体挪一个偏移，布局不动、
        // 命令不发。这与"拖动中不调布局"是同一件事的两面：屏幕上的临时位置由画布改，
        // 文档那一边的固定位置留到松手那一刻才由宿主一次性写下去。
        var baseList = _dragging ? Shift(baseCommands) : baseCommands;

        // 叠加层永远画在正式列表之上。它只在连线/边编辑手势进行中非空，
        // 所以绝大多数帧这里就是 baseList 原样，没有额外分配。
        // 变更高亮是另一层，画在正式列表之上、手势层之下——手势层是用户当下正在做的事，
        // 该压在高亮之上；反过来高亮的角标会挡住端口把手。
        var highlighted = _highlight.Count == 0 ? baseList : [.. baseList, .. _highlight];
        _frameCommands = _overlay.Count == 0 ? highlighted : [.. highlighted, .. _overlay];

        if (_switch.Request(_drawList.ElementCount))
        {
            _index = _switch.Target == RenderMode.Virtualized ? new CullingIndex(_drawList) : null;
        }

        Mode.Update(_switch.Current, _switch.IsSwitching, _index?.CullRate ?? 0);
    }

    /// <summary>
    /// 开始一次拖拽预览。
    /// </summary>
    /// <param name="nodeIds">被拖动的节点。</param>
    /// <param name="edgeIds">要跟着重画的相连边。</param>
    /// <remarks>
    /// 偏移从零开始，等第一帧 <see cref="MoveDrag"/> 来。相连边一并记进来，
    /// 这样它们也跟着节点走——否则节点挪开了、线还连着旧位置，看起来像断了一截。
    /// </remarks>
    public void BeginDrag(IReadOnlyList<string> nodeIds, IReadOnlyList<string> edgeIds)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);
        ArgumentNullException.ThrowIfNull(edgeIds);

        _draggedElements.Clear();

        foreach (var id in nodeIds)
        {
            _draggedElements.Add(id);
        }

        foreach (var id in edgeIds)
        {
            _draggedElements.Add(id);
        }

        _dragDelta = new DrawPoint(0, 0);
        _dragging = _draggedElements.Count > 0;
    }

    /// <summary>更新拖拽预览的偏移，文档坐标。</summary>
    public void UpdateDrag(DrawPoint delta)
    {
        if (!_dragging)
        {
            return;
        }

        _dragDelta = delta;
    }

    /// <summary>结束拖拽预览，回到原始绘制列表。</summary>
    public void EndDrag()
    {
        _dragging = false;
        _draggedElements.Clear();
        _dragDelta = new DrawPoint(0, 0);
    }

    /// <summary>叠一层临时图形（端口把手、连线预览、折点把手）。</summary>
    public void SetOverlay(IReadOnlyList<DrawCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        _overlay = commands;
    }

    /// <summary>清掉叠加层，回到只画正式列表。</summary>
    public void ClearOverlay() => _overlay = [];

    /// <summary>
    /// 换一层变更高亮。传空列表等价于清掉。
    /// </summary>
    /// <remarks>
    /// 与手势叠加层分开：手势一松手就清，高亮要留到下一次变更。两者合成一栏的话，
    /// 一次连线松手会把别人的高亮一起抹掉。
    /// </remarks>
    public void SetHighlights(IReadOnlyList<DrawCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        _highlight = commands;
    }

    /// <summary>清掉变更高亮层。</summary>
    public void ClearHighlights() => _highlight = [];

    /// <summary>
    /// 把跟着拖的元素整体偏移。
    /// </summary>
    /// <remarks>
    /// 只拷被拖动的那些指令。千节点的图上一次拖动只波及几个节点加几条边，
    /// 整份列表拷一遍是浪费，而那一笔在每帧都发生。形状与文本按外接矩形平移，
    /// 折线逐点平移——后者的远端点也会跟着动，但那只在预览里看得见，
    /// 松手后布局会按真实端口重算，所以不是最终的线。
    /// </remarks>
    private IReadOnlyList<DrawCommand> Shift(IReadOnlyList<DrawCommand> commands)
    {
        if (_dragDelta.X == 0 && _dragDelta.Y == 0)
        {
            return commands;
        }

        var shifted = new DrawCommand[commands.Count];

        for (var index = 0; index < commands.Count; index++)
        {
            var command = commands[index];

            shifted[index] = _draggedElements.Contains(command.ElementId)
                ? Offset(command, _dragDelta)
                : command;
        }

        return shifted;
    }

    private static DrawCommand Offset(DrawCommand command, DrawPoint delta) => command switch
    {
        DrawShape shape => shape with
        {
            Rect = shape.Rect with { X = shape.Rect.X + delta.X, Y = shape.Rect.Y + delta.Y },
        },
        DrawText text => text with
        {
            Box = text.Box with { X = text.Box.X + delta.X, Y = text.Box.Y + delta.Y },
        },
        DrawPolyline polyline => polyline with
        {
            Points = [.. polyline.Points.Select(point => new DrawPoint(point.X + delta.X, point.Y + delta.Y))],
        },
        _ => command,
    };

    /// <summary>
    /// 窗口尺寸变了。
    /// </summary>
    /// <remarks>
    /// 第一次拿到尺寸时补一次适配。绘制列表通常在窗口排布之前就装进来了，
    /// 那时没有尺寸可适配，适配那一步只能空转；不补的话，内容会停在左上角，
    /// 看上去像视口算错了，而其实是它根本没适配过。
    /// 之后每次改尺寸都不再适配——用户已经摆好的视角不该因为拖了一下边框就变。
    /// </remarks>
    public void Resize(double width, double height)
    {
        var measured = _viewport.Width > 0 && _viewport.Height > 0;
        var resized = _viewport.Resize(width, height);

        Viewport = measured ? resized : resized.FitTo(ContentBounds);
    }

    /// <summary>以光标为锚点缩放。</summary>
    public void ZoomAt(double factor, double anchorX, double anchorY) =>
        Viewport = _viewport.ZoomAt(factor, anchorX, anchorY);

    /// <summary>平移。参数是屏幕上的位移。</summary>
    public void PanBy(double dx, double dy) => Viewport = _viewport.PanBy(dx, dy);

    /// <summary>光标移到了画布上的某个位置。参数是屏幕坐标。</summary>
    public void MovePointer(double screenX, double screenY)
    {
        var document = _viewport.Transform.ToDocument(screenX, screenY);

        // 同一个位置重复上报是常态：指针事件比像素密得多。
        // 每次都通知一遍会让状态栏每帧重排一次，而它显示的东西根本没变。
        if (_pointerInside && _pointer.Equals(document))
        {
            return;
        }

        _pointerInside = true;
        _pointer = document;
        Raise(nameof(PointerText));
    }

    /// <summary>光标离开了画布。</summary>
    public void LeavePointer()
    {
        if (!_pointerInside)
        {
            return;
        }

        _pointerInside = false;
        Raise(nameof(PointerText));
    }

    private void Set<T>(ref T field, T value, params string[] names)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;

        foreach (var name in names)
        {
            Raise(name);
        }
    }

    private void Replace(DrawList drawList, bool fit)
    {
        ArgumentNullException.ThrowIfNull(drawList);

        _index = null;
        _switch.Reset(RenderMode.Immediate);

        Diagnostics.Reload();

        DrawList = drawList;

        if (fit)
        {
            Viewport = _viewport.FitTo(ContentBounds);
        }

        // 选中跟着列表走：同一个标识在新列表里可能已经没有对应的指令了
        // （元素被删掉，或者这一份列表压根是别的文档）。外接框不重算的话，
        // 选中框会停在一个已经不存在的位置上。
        _selectionBounds = BoundsOfSelection();
        Raise(nameof(SelectionBounds));
    }

    /// <summary>
    /// 选中元素的外接框，取全部选中元素的并集。
    /// </summary>
    private SpatialRect? BoundsOfSelection()
    {
        SpatialRect? bounds = null;

        foreach (var id in _selectedIds)
        {
            if (BoundsOf(id) is not { } box)
            {
                continue;
            }

            bounds = bounds is { } current ? current.Union(box) : box;
        }

        return bounds;
    }

    /// <summary>
    /// 一个元素在文档坐标下的外接框。
    /// </summary>
    /// <remarks>
    /// 取它全部指令的并集。一个节点对应一条形状加若干行文本，只看第一条的话，
    /// 标签比形状宽时选中框会把标签切掉一半。
    /// 折线取点集的包围盒——它没有现成的矩形。
    /// </remarks>
    private SpatialRect? BoundsOf(string elementId)
    {
        SpatialRect? bounds = null;

        foreach (var command in _drawList.Commands)
        {
            if (!string.Equals(command.ElementId, elementId, StringComparison.Ordinal))
            {
                continue;
            }

            var box = Box(command);

            if (box is null)
            {
                continue;
            }

            bounds = bounds is { } current ? current.Union(box.Value) : box;
        }

        return bounds;
    }

    private static SpatialRect? Box(DrawCommand command) => command switch
    {
        DrawShape shape => shape.Rect,
        DrawText text => text.Box,
        DrawPolyline polyline => PolylineBounds(polyline.Points),
        _ => null,
    };

    private static SpatialRect? PolylineBounds(IReadOnlyList<DrawPoint> points)
    {
        if (points.Count == 0)
        {
            return null;
        }

        var left = points[0].X;
        var top = points[0].Y;
        var right = left;
        var bottom = top;

        foreach (var point in points)
        {
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }

        return new SpatialRect(left, top, right - left, bottom - top);
    }

    /// <summary>内容的范围。绘制列表的宽高就是它。</summary>
    private SpatialRect ContentBounds => new(0, 0, _drawList.Width, _drawList.Height);

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
