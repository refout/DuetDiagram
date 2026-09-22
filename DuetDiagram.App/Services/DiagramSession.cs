using DuetDiagram.App.Interaction;
using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Sidecar;
using DuetDiagram.Core.Workspace;
using DuetDiagram.Layout;
using DuetDiagram.Render;

namespace DuetDiagram.App.Services;

/// <summary>
/// 一份打开的文档，以及"改它"和"把它重新画出来"这两件事。
/// </summary>
/// <remarks>
/// <para>
/// **改文档只有一条路：构造一条命令交给总线。** 界面自己写文档的话，
/// "人和 LLM 能力对等"当场不成立——那条命令层会缺一个入口，
/// 而 LLM 那边没有面板可点。所以这里不提供任何直接的写入口，
/// 只有一个 <see cref="Apply"/>，它内部走的还是命令。
/// </para>
/// <para>
/// 重活（布局、绘制列表构建）由它一次做完并记下耗时。调用方拿到的永远是一份
/// 与当前文档一致的绘制列表——分成两步给的话，调用方迟早会在中间插进别的事情，
/// 于是画面与文档对不上。
/// </para>
/// <para>
/// 文本度量器由它持有并释放。度量器按字体家族缓存字体对象，而那些对象持有原生资源；
/// 让每个调用点各自 new 一个再丢掉，就是把"什么时候释放原生资源"交给垃圾回收决定。
/// </para>
/// <para>
/// 选中状态也在这里。它是"文档里哪几个元素被指着"这件事，属于文档这一侧；
/// 画布那边只是把选中框画出来。两处各存一份的话，换选中时总有一次会漏掉，
/// 而表现是"点了节点，属性面板换了但选中框还停在上一个节点上"。
/// </para>
/// </remarks>
public sealed class DiagramSession : IDisposable
{
    private readonly SkiaTextMeasurer _measurer = new();
    private readonly DiagramWorkspace _workspace;
    private readonly HighlightTracker _highlights;
    private readonly IDisposable _changes;

    /// <summary>
    /// 当前这份绘制列表对应的文档版本。
    /// </summary>
    /// <remarks>
    /// 用来判断一条广播回来的通知是不是已经被这一份算过了。自己发出去的命令也会广播回来，
    /// 而那时绘制列表已经重建过——不挡的话每个窗口每改一次就白算两遍布局，
    /// 而界面上表现为连闪两帧。它在界面线程上写、在投递线程上读，所以是 volatile。
    /// </remarks>
    private volatile int _sceneVersion;

    // 工作区可能是自己建的，也可能是多窗口共用的那一份。共用时不能释放——
    // 先关掉的那个窗口一放，剩下那个窗口的广播器就没了，
    // 而症状是"界面莫名不再刷新"，很难与别的毛病区分开。
    private readonly bool _ownsWorkspace;

    // 布局引擎可注入，默认用约束布局引擎。注入的用途只有一个：让"布局彻底失败"
    // 这条路径能被真正走到——拿一个会失败的引擎，比在真实引擎上构造一份它解不出的图可靠得多。
    private readonly ILayoutEngine? _engine;

    private LayoutFailedException? _layoutFailure;
    private bool _manualLayout;

    private readonly Dictionary<string, Anchor> _pinned = new(StringComparer.Ordinal);
    private readonly Stack<PinSnapshot> _pinUndo = new();
    private readonly Stack<PinSnapshot> _pinRedo = new();

    private readonly Dictionary<string, IReadOnlyList<Anchor>> _pinnedEdges = new(StringComparer.Ordinal);
    private readonly Stack<BendSnapshot> _bendUndo = new();
    private readonly Stack<BendSnapshot> _bendRedo = new();

    private DragSession? _drag;

    private ConnectSession? _connect;

    private IReadOnlyList<string> _selectedIds = [];

    public DiagramSession(
        DiagramDocument document,
        Theme? theme = null,
        string windowId = "main",
        ILayoutEngine? engine = null)
        : this(
            CreateWorkspace(document, windowId),
            theme,
            engine,
            readOnly: false,
            ownsWorkspace: true)
    {
        ArgumentNullException.ThrowIfNull(document);
    }

    /// <summary>
    /// 按界面那一套配置建一个工作区：操作者是人、会话标识按窗口给、不要求带版本号。
    /// </summary>
    /// <param name="document">初始文档。</param>
    /// <param name="windowId">窗口标识，进审计日志。</param>
    /// <remarks>
    /// 建工作区这件事收在这里一处。多窗口那条路要在会话之外先把工作区建好
    /// （它归引用计数管，不归会话管），要是那边自己再写一遍这几项配置，
    /// 两处的操作者或版本号要求迟早会分叉——而分叉的表现是"有些窗口的改动没进审计日志"。
    /// </remarks>
    public static DiagramWorkspace CreateWorkspace(DiagramDocument document, string windowId = "main")
    {
        ArgumentNullException.ThrowIfNull(document);

        return DiagramWorkspace.CreateOwned(
            document,
            new SimpleSessionProvider("human", SessionIds.Gui(windowId)),
            DiagramCommandBusOptions.ForGui());
    }

    /// <summary>
    /// 开一个共用某个工作区的窗口。
    /// </summary>
    /// <param name="workspace">这份文档的工作区，由调用方按引用计数持有。</param>
    /// <param name="theme">外观查表。</param>
    /// <param name="engine">布局引擎。</param>
    /// <param name="readOnly">
    /// 这一份是不是只读的。另一个进程拿着这份文档时为真——那时界面上所有写入口都要禁掉，
    /// 而不只是加一句提示：留一个能点的按钮就是一个能造成损坏的入口。
    /// </param>
    /// <remarks>
    /// 共用工作区的窗口共享同一份文档、同一条命令总线与同一个广播器，
    /// 因此版本号天然一致，也不存在版本冲突。窗口标识也一并共享——
    /// 同一个人开两个窗口看同一份文件，在审计日志里本来就是同一个人。
    /// </remarks>
    public static DiagramSession Shared(
        DiagramWorkspace workspace,
        Theme? theme = null,
        ILayoutEngine? engine = null,
        bool readOnly = false)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        return new DiagramSession(workspace, theme, engine, readOnly, ownsWorkspace: false);
    }

    private DiagramSession(
        DiagramWorkspace workspace,
        Theme? theme,
        ILayoutEngine? engine,
        bool readOnly,
        bool ownsWorkspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        _workspace = workspace;
        _ownsWorkspace = ownsWorkspace;
        _engine = engine;
        Theme = theme ?? Theme.Default;
        IsReadOnly = readOnly;
        ReadOnlyReason = readOnly ? "另一个进程正在编辑这份文档，这一份是只读的" : null;

        // 高亮听命令总线的广播，不自己比较文档。界面推断"哪个字段变了"的话，
        // LLM 改的东西不会亮——那条路径根本不经过界面。
        _highlights = new HighlightTracker(_workspace.CommandBus.Context.Broadcaster, Theme);
        _highlights.Changed += () => HighlightsChanged?.Invoke();

        // 第一份布局不走 Reload：那时还没有"上一次成功的结果"可以退守，
        // 算不出来就是算不出来，如实抛出比留一份空画面让人以为文档是空的要好。
        Scene = SampleDiagram.Build(Document, Theme, _measurer, null, _engine);
        _sceneVersion = Document.Version;

        // 别的窗口改的是同一份文档、同一条总线，所以通知能到这一份上来。
        // 只听广播而不自己比较文档：界面推断"文档变没变"的话，
        // 由模型或另一个进程改出来的变化不会被认出来，而那条路径根本不经过这个窗口。
        _changes = _workspace.Broadcaster.Subscribe(OnChange);
    }

    /// <summary>文档变了，绘制列表要换一份。</summary>
    public event Action? SceneChanged;

    /// <summary>
    /// 文档被这一份之外的什么东西改了，手上这份绘制列表已经过期。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 同一个进程里开着两个窗口看同一份文档时，其中一个窗口改的东西在另一个窗口那边
    /// 走的就是这条线。没有它的话，第二个窗口显示的还是改动之前那张图——
    /// 而两个窗口显示的是同一份文档，用户以为看到的是最新的。
    /// </para>
    /// <para>
    /// **在后台投递线程上触发，订阅方要自己回到界面线程。** 重算布局与绘制列表
    /// 要碰这份会话的状态，而那份状态是界面线程在读的。触发方不管这件事，
    /// 是因为投递线程上不该去猜宿主用的是哪条线程。
    /// </para>
    /// </remarks>
    public event Action? DocumentChanged;

    /// <summary>选中的元素变了。文档没动，所以绘制列表不用重算。</summary>
    public event Action? SelectionChanged;

    /// <summary>变更高亮的标记变了。在后台线程上触发，订阅方要自己回到界面线程。</summary>
    public event Action? HighlightsChanged;

    /// <summary>布局全部降级失败。画面保留上一次成功的结果，另弹提示。</summary>
    public event Action? LayoutFailed;

    /// <summary>
    /// 一次命令没有产生效果：被拒，或者合法但无事可做。
    /// </summary>
    /// <remarks>
    /// 成功且真的改了东西时不报。每一次按键都往状态栏写一句话的话，
    /// 那句话会一直在闪，而用户找不到是哪一次失败留下的。
    /// </remarks>
    public event Action<CommandResult>? CommandReported;

    public DiagramDocument Document => _workspace.Document;

    public DiagramCommandBus Bus => _workspace.CommandBus;

    /// <summary>这份文档的工作区。多个窗口共用同一份。</summary>
    public DiagramWorkspace Workspace => _workspace;

    /// <summary>
    /// 这一份是不是只读的。
    /// </summary>
    /// <remarks>
    /// 为真时所有写入口都会拒绝，返回 <see cref="ErrorCodes.DocumentReadOnly"/>。
    /// 界面另外把控件也禁掉，但那是为了让人一眼看出改不了——挡住写入的是这里，
    /// 因为界面上的入口不止一个，漏掉哪一个都看不出来。
    /// </remarks>
    public bool IsReadOnly { get; }

    /// <summary>只读的原因，一句话。可写时为空。</summary>
    public string? ReadOnlyReason { get; }

    /// <summary>外观查表。</summary>
    public Theme Theme { get; }

    /// <summary>最近一次算出来的绘制列表与两段重活的耗时。</summary>
    public SampleScene Scene { get; private set; }

    /// <summary>
    /// 手上这份绘制列表对应文档的哪个版本。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Document"/> 上的版本号比一比，就知道画面是不是最新的。
    /// 两者相等才是。别的窗口改了文档之后，这一份的重算要等界面线程空出来，
    /// 中间那一段就是这个数落后于文档版本的那一段。
    /// </remarks>
    public int SceneVersion => _sceneVersion;

    /// <summary>
    /// 人工固定的节点位置。来自 <c>user.json</c> 的 pinnedNodes，不进 IR。
    /// </summary>
    /// <remarks>
    /// 拖节点落定后写进来，重布局时作为不可移动的锚点交还给布局引擎。
    /// 它是这份文档的人工产物，重算不出来，所以单独留着而不是混进语义层。
    /// </remarks>
    public IReadOnlyDictionary<string, Anchor> PinnedNodes => _pinned;

    #region 选中

    /// <summary>当前选中的元素标识，按选中的先后次序。</summary>
    /// <remarks>
    /// 只会有节点。边与组合的属性编辑各自有别的依赖（端口、成员列表），
    /// 现在把它们收下来，面板只会显示一片空白，看起来像坏了。
    /// </remarks>
    public IReadOnlyList<string> SelectedIds => _selectedIds;

    /// <summary>选中的那些节点。标识在文档里找不到时会被跳过。</summary>
    public IReadOnlyList<NodeDef> SelectedNodes =>
        [.. _selectedIds.Select(Find).OfType<NodeDef>()];

    /// <summary>选中的那个节点。选中的不是节点、或者选了好几个时为空。</summary>
    public NodeDef? SelectedNode
    {
        get
        {
            var nodes = SelectedNodes;

            return nodes.Count == 1 ? nodes[0] : null;
        }
    }

    /// <summary>
    /// 选中一个元素。
    /// </summary>
    /// <param name="elementId">要选中的标识。传空清掉选中。</param>
    /// <param name="additive">
    /// 是不是在已有选中上增删。按住修饰键点画布时走这一档：点到已选中的会把它去掉，
    /// 点到别的会把它加进来，点空处不动——点空处在增选模式下清掉全部选中，
    /// 会让用户辛苦点出来的一批元素在一次落空里全没了。
    /// </param>
    public void Select(string? elementId, bool additive = false)
    {
        if (!additive)
        {
            SetSelection(elementId is null ? [] : [elementId]);
            return;
        }

        if (elementId is not null)
        {
            Toggle(elementId);
        }
    }

    /// <summary>在已有选中上加上或去掉一个元素。</summary>
    public void Toggle(string elementId)
    {
        ArgumentNullException.ThrowIfNull(elementId);

        var ids = _selectedIds.ToList();

        if (!ids.Remove(elementId))
        {
            ids.Add(elementId);
        }

        SetSelection(ids);
    }

    /// <summary>换一整批选中。</summary>
    public void SetSelection(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var accepted = new List<string>();

        foreach (var id in ids)
        {
            if (Find(id) is not null && !accepted.Contains(id, StringComparer.Ordinal))
            {
                accepted.Add(id);
            }
        }

        // 同一批选中重复上报是常态：画布上每次按下都会报一次。
        // 每次都通知一遍会让面板把每个字段重读一次，而它显示的东西根本没变。
        if (accepted.Count == _selectedIds.Count
            && accepted.SequenceEqual(_selectedIds, StringComparer.Ordinal))
        {
            return;
        }

        _selectedIds = accepted;
        SelectionChanged?.Invoke();
    }

    private NodeDef? Find(string? id) =>
        id is null ? null : Document.Nodes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.Ordinal));

    #endregion

    #region 只读

    /// <summary>
    /// 拒绝一次改动，并给出"这一份是只读的"。
    /// </summary>
    /// <remarks>
    /// 界面已经把写入口禁掉了，这里再挡一次不是多余：界面上的入口不止一处
    /// （字段、约束、拖拽、连线、折点），漏掉哪一处都看不出来，
    /// 而漏掉的那一处就是一条能造成损坏的路。挡在这里，入口再多也只有一道门。
    /// </remarks>
    private CommandResult Refuse() =>
        Report(CommandResult.Fail(CommandError.Of(ErrorCodes.DocumentReadOnly, ReadOnlyReason)));

    #endregion

    #region 改动

    /// <summary>
    /// 给选中的每个节点写同一个字段。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 每个节点各一条命令，不是一条命令改多个节点。命令的原子性是对单份文档定义的，
    /// 一条命令改多处的话，它在失败时要还原的东西就不止一处，而这条路径上
    /// 每一条命令都只改一个节点，撤销栈里也是一节点一条——用户按一次撤销退回一个节点，
    /// 与"我改了哪一个"这件事对得上。
    /// </para>
    /// <para>
    /// **先全部校验，一条都没问题才动手。** 边校验边执行的话，后面某一条被拒时
    /// 前面几条已经写进去了，文档停在"改了一半"的状态上——而多选编辑的意思是
    /// "给这几个元素赋同一个值"，改一半与它的意思正相反。
    /// </para>
    /// </remarks>
    public CommandResult Apply(string field, string? value)
    {
        if (IsReadOnly)
        {
            return Refuse();
        }

        var nodes = SelectedNodes;

        if (nodes.Count == 0)
        {
            return Report(CommandResult.Fail(CommandError.Of(ErrorCodes.NodeMissing, "没有选中的节点")));
        }

        var commands = new List<IDiagramCommand>(nodes.Count);

        foreach (var node in nodes)
        {
            var command = new SetNodeFieldCommand(node.Id, field, value);
            var validation = command.Validate(Document);

            if (!validation.IsValid)
            {
                return Report(CommandResult.Fail(validation.Errors));
            }

            commands.Add(command);
        }

        var changed = false;
        var last = CommandResult.NoOp();

        foreach (var command in commands)
        {
            last = Bus.Execute(command);

            if (!last.IsSuccess)
            {
                // 校验都过了还是失败，说明失败来自校验看不见的东西（版本冲突、内部错误）。
                // 这时前面几条已经写进去了，不在这里替用户撤销：那几条就摆在撤销栈上，
                // 按一次撤销退回一个节点；而自作主张地撤销会把用户之前的手工编辑
                // 一并撤掉，那比"改了一半"更难收拾。
                return Report(last);
            }

            changed |= last.IsEffectiveSuccess;
        }

        if (changed)
        {
            Reload();
        }

        return Report(last);
    }

    /// <summary>
    /// 按当前文档重算布局与绘制列表。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 每次都整条链路重跑，不做增量。增量要判断"这次改动影响哪些元素"，
    /// 而那正是两个哈希要回答的问题——在哈希之上再写一套判断，
    /// 两处迟早会对同一份改动给出不同答案。
    /// </para>
    /// <para>
    /// **布局失败时画面不动。** 保留上一次成功的结果，另发一条失败通知让界面去提示。
    /// 清空画面的话，用户看到一片空白，第一反应是"图丢了"，而不是"布局没算出来"——
    /// 前者会让人去翻备份，而真正该做的是去掉那条算不出来的约束。
    /// </para>
    /// </remarks>
    public void Reload() => Reload(null);

    private void Reload(TimeSpan? budget)
    {
        if (_manualLayout)
        {
            // 手动布局模式：不再问引擎，位置冻在最近一次成功的布局上。
            Scene = SampleDiagram.Rebuild(Document, Theme, _measurer, Scene);
            _sceneVersion = Document.Version;
            SceneChanged?.Invoke();
            return;
        }

        try
        {
            Scene = SampleDiagram.Build(Document, Theme, _measurer, _pinned, _engine, budget);
            _layoutFailure = null;
            _sceneVersion = Document.Version;
            SceneChanged?.Invoke();
        }
        catch (LayoutFailedException failure)
        {
            // 失败也记下版本：这一份文档已经被算过一遍了，再算一遍还是同一个结果。
            // 不记的话，后面每来一条通知都会重试一次必然失败的布局。
            _layoutFailure = failure;
            _sceneVersion = Document.Version;
            LayoutFailed?.Invoke();
        }
    }

    /// <summary>
    /// 一条变更通知到了。只有比手上这份绘制列表新的才要重算。
    /// </summary>
    /// <remarks>
    /// 在后台投递线程上跑，只读一个版本号就返回——真正重活的在订阅方那边。
    /// </remarks>
    private void OnChange(ChangeNotification notification)
    {
        if (notification.Version <= _sceneVersion)
        {
            return;
        }

        DocumentChanged?.Invoke();
    }

    /// <summary>把一次命令结果报给界面。成功且真的改了东西时不报。</summary>
    private CommandResult Report(CommandResult result)
    {
        if (!result.IsEffectiveSuccess)
        {
            CommandReported?.Invoke(result);
        }

        return result;
    }

    #endregion

    #region 布局约束

    /// <summary>
    /// 加一条布局约束。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **归属默认人工。** 走这条路的都是界面上点出来的，而归属是降级矩阵的输入：
    /// 布局解不出来时按它决定先丢谁，人工定的留到最后。界面加的东西要是被标成自动归属，
    /// 降级时第一批就被丢掉，而用户以为自己刚加的那条是硬性的。
    /// </para>
    /// <para>
    /// **加完重排一次。** 约束本身不进坐标，但它决定坐标——只重绘不重排的话，
    /// 用户看到的是"我加了一条约束，画面纹丝不动"，然后会再点一次，
    /// 而第二次点出来的是同一条（命令按内容判重），于是还是没反应。
    /// </para>
    /// </remarks>
    public CommandResult AddConstraint(LayoutConstraintSpec spec, ConstraintOwner owner = ConstraintOwner.Human)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (IsReadOnly)
        {
            return Refuse();
        }

        var result = Bus.Execute(new AddLayoutConstraintCommand(spec, owner));

        if (result.IsEffectiveSuccess)
        {
            Reload();
        }

        return Report(result);
    }

    /// <summary>
    /// 删一条布局约束。
    /// </summary>
    /// <remarks>
    /// 要删的那条不在了，命令报的是失败而不是无操作：那通常意味着调用方手上那份
    /// 约束列表已经过期，报成功会让它继续拿一份错的列表往下走。
    /// </remarks>
    public CommandResult RemoveConstraint(LayoutConstraintSpec spec, ConstraintOwner owner = ConstraintOwner.Human)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (IsReadOnly)
        {
            return Refuse();
        }

        var result = Bus.Execute(new RemoveLayoutConstraintCommand(spec, owner));

        if (result.IsEffectiveSuccess)
        {
            Reload();
        }

        return Report(result);
    }

    #endregion

    #region 布局失败

    /// <summary>最近一次布局失败。之后有一次布局成功就清空。</summary>
    public LayoutFailedException? LayoutFailure => _layoutFailure;

    /// <summary>
    /// 是否处于手动布局模式。
    /// </summary>
    /// <remarks>
    /// 进入这一模式之后位置不再自动重算，只有用户显式重排才会动。
    /// 它的用途只有一个：布局反复失败时，让画面停在最后一张能看的图上，
    /// 而不是每改一个字就去撞一次必然失败的布局。
    /// </remarks>
    public bool ManualLayoutActive => _manualLayout;

    /// <summary>
    /// 降级过程中被丢掉的约束，一句一条。
    /// </summary>
    /// <remarks>
    /// 布局全级失败时用户要做的第一件事是"去掉那条算不出来的约束"，
    /// 而要知道去掉哪一条，就得先看到这一轮丢了哪些。布局没失败时为空。
    /// </remarks>
    public IReadOnlyList<string> ConflictSummary =>
        _layoutFailure is null
            ? []
            : [.. _layoutFailure.Payload.Attempts
                .SelectMany(attempt => attempt.Conflicts)
                .Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// 重试一次，用更长的预算。
    /// </summary>
    /// <remarks>
    /// 用户主动点的重试，愿意多等：自动重排那一档的预算是照着"超过一帧就是卡顿"定的，
    /// 拿它重试等于把同一次必然超时的计算再跑一遍，而用户以为重试会有不同结果。
    /// </remarks>
    public void RetryLayout()
    {
        _manualLayout = false;
        Reload(LayoutBudgets.ManualRelayout);
    }

    /// <summary>进入手动布局模式，把位置冻在最近一次成功的布局上。</summary>
    public void EnterManualLayout()
    {
        _manualLayout = true;
        _layoutFailure = null;
        SceneChanged?.Invoke();
    }

    #endregion

    #region 拖拽与固定

    /// <summary>
    /// 按下：决定这一拖动的是哪些节点。
    /// </summary>
    /// <remarks>
    /// 点中的不是节点（空白或边）时返回空，交给普通选中处理——
    /// 那种情况没有"跟着光标走"的东西。点中节点则先定下移动集合，
    /// 再让画布开预览。移动集合在按下这一刻算一次就锁定，拖动中不再重算，
    /// 否则拖到一半选中变了，跟着动的节点也会变。
    /// </remarks>
    public DragPreview? BeginDrag(string? elementId, bool additive, DrawPoint startDoc)
    {
        if (elementId is null || Find(elementId) is not { } node)
        {
            Select(elementId, additive);
            return null;
        }

        // 只读时选中照做、拖动不做。选中不改文档，而"改不了"与"点不动"是两回事：
        // 连点都点不动的话，用户会以为这份文档根本没打开。
        if (IsReadOnly)
        {
            Select(node.Id, additive);
            return null;
        }

        // 非增选：拖谁就只选谁。增选且这个已经在选中里：维持整批，
        // 不要再去 Toggle 把它去掉——否则点一个已选中的节点反而把它踢出选中，
        // 整批跟着拖就断了。增选且不在选中里才把它加进来。
        if (!additive)
        {
            Select(node.Id, additive: false);
        }
        else if (!_selectedIds.Contains(node.Id, StringComparer.Ordinal))
        {
            Select(node.Id, additive: true);
        }

        var dragIds = SelectionSet.ResolveDragSet(_selectedIds, node.Id, additive);
        var edgeIds = SelectionSet.ConnectedEdges(Document, dragIds);

        _drag = new DragSession(startDoc, dragIds, edgeIds);

        return new DragPreview(dragIds, edgeIds);
    }

    /// <summary>移动：把指针位置变成相对起点的偏移交还给画布。</summary>
    public DrawPoint UpdateDrag(DrawPoint currentDoc)
    {
        if (_drag is null)
        {
            return new DrawPoint(0, 0);
        }

        _drag.Move(currentDoc);

        return _drag.Delta;
    }

    /// <summary>
    /// 松手：把这一拖作为一条操作落定。
    /// </summary>
    /// <param name="dropTargetId">
    /// 松手时指针底下的节点。落在空白处时为空。
    /// </param>
    /// <remarks>
    /// <para>
    /// **落在兄弟节点上落定的是次序，不是位置。** 落在空白处是"把我放这儿"，
    /// 记一条固定位置；落在一个兄弟节点上是"把我排到它旁边"，记一条层内次序。
    /// 后者不写固定位置——固定位置一旦钉住，这条次序就再也推不动它了。
    /// </para>
    /// <para>
    /// **只落定一次。** 路径预算里拖拽松手是 200 毫秒那一档，逐像素发命令不在预算内；
    /// 一次拖动交一条"移动了哪些节点、落到哪"，撤销栈里就是一步，
    /// 用户按一次撤销退回的是整次拖动，而不是一像素。
    /// </para>
    /// <para>
    /// **写入侧挡重叠。** 落点不能压在另一个已固定节点上——两个都不可动时布局无解，
    /// 事后兜底要么悄悄挪走用户放好的节点、要么给出一张坏图。挡在入口比挡在出口便宜得多，
    /// 而且用户当场就知道"这里放不下"，而不是事后在一张错图上找原因。被挡下时不写固定位置，
    /// 节点回到拖动前的位置。
    /// </para>
    /// </remarks>
    public DragCommit CommitDrag(string? dropTargetId = null)
    {
        if (_drag is null)
        {
            return DragCommit.Ignored;
        }

        if (IsReadOnly)
        {
            _drag = null;
            Refuse();

            return DragCommit.Rejected;
        }

        var drag = _drag;
        _drag = null;

        if (ConstraintGestures.OrderForDrop(Document, drag.NodeIds, dropTargetId) is { } order)
        {
            var ordered = AddConstraint(order);

            // 次序已经排成这样就报无操作，而这一拖并不是被挡下的——它只是没改变什么。
            // 当成拒绝的话，画布会把节点弹回拖动前的位置，而用户明明拖到了对的地方。
            return ordered.IsSuccess ? DragCommit.Ordered(order.Members.Count) : DragCommit.Rejected;
        }

        var placements = Scene.Layout;
        var proposals = new Dictionary<string, Anchor>(StringComparer.Ordinal);

        foreach (var id in drag.NodeIds)
        {
            var placed = placements.Find(id);

            if (placed is null)
            {
                continue;
            }

            proposals[id] = new Anchor(placed.X + drag.Delta.X, placed.Y + drag.Delta.Y);
        }

        if (WouldOverlapPinned(proposals, placements))
        {
            return DragCommit.Rejected;
        }

        var before = SnapshotPins();

        foreach (var (id, anchor) in proposals)
        {
            _pinned[id] = anchor;
        }

        _pinUndo.Push(before);
        _pinRedo.Clear();

        Reload();

        return DragCommit.Pinned(drag.NodeIds.Count);
    }

    /// <summary>
    /// 手势作废：不写固定位置，丢弃这一拖的瞬时状态。
    /// </summary>
    public void CancelDrag()
    {
        _drag = null;
    }

    /// <summary>能否撤销最近一次固定。</summary>
    public bool CanUndoPin => _pinUndo.Count > 0;

    /// <summary>能否重做最近一次被撤销的固定。</summary>
    public bool CanRedoPin => _pinRedo.Count > 0;

    /// <summary>
    /// 撤销最近一次固定。节点回到松手前的位置，整次拖动算一步。
    /// </summary>
    public void UndoPin()
    {
        if (!_pinUndo.TryPop(out var before))
        {
            return;
        }

        var after = SnapshotPins();

        RestorePins(before);
        _pinRedo.Push(after);
        Reload();
    }

    /// <summary>重做最近一次被撤销的固定。</summary>
    public void RedoPin()
    {
        if (!_pinRedo.TryPop(out var after))
        {
            return;
        }

        var before = SnapshotPins();

        RestorePins(after);
        _pinUndo.Push(before);
        Reload();
    }

    /// <summary>
    /// 落点是否与另一个已固定节点重叠。
    /// </summary>
    /// <remarks>
    /// 只比已固定的节点：自动布局出来的位置随时会变，拿它当判据会让一次正常的拖动
    /// 莫名其妙地被拒。固定位置是用户明确"放这儿"的，两个固定节点互相压住才是真矛盾。
    /// </remarks>
    private bool WouldOverlapPinned(
        IReadOnlyDictionary<string, Anchor> proposals,
        EngineLayoutResult layout)
    {
        foreach (var (id, anchor) in proposals)
        {
            var current = layout.Find(id);

            if (current is null)
            {
                continue;
            }

            var moved = new PlacedNode(id, anchor.X, anchor.Y, current.Width, current.Height);

            foreach (var other in _pinned.Keys)
            {
                if (string.Equals(other, id, StringComparison.Ordinal))
                {
                    continue;
                }

                var placed = layout.Find(other);

                if (placed is not null && moved.Overlaps(placed))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private PinSnapshot SnapshotPins() =>
        new(new Dictionary<string, Anchor>(_pinned, StringComparer.Ordinal));

    private void RestorePins(PinSnapshot snapshot)
    {
        _pinned.Clear();

        foreach (var (id, anchor) in snapshot.Pins)
        {
            _pinned[id] = anchor;
        }
    }

    #endregion

    #region 连线与边的编辑

    /// <summary>
    /// 人工固定的折线。来自 <c>user.json</c> 的 pinnedEdges，不进 IR——
    /// 折线是用户「改道」的产物，与文档结构无关，绕开命令总线后「折点增删只算一步」
    /// 由这份快照栈保证，与节点固定同构。
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Anchor>> PinnedEdges => _pinnedEdges;

    /// <summary>布局给出的某条边的折线（从起点到终点）。</summary>
    public RoutedEdge? FindEdgeRoute(string id) =>
        Scene.Layout.Edges.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));

    /// <summary>连线手势进行中，从起点端口到当前指针的预览线；没有手势时为空。</summary>
    public IReadOnlyList<DrawPoint>? ConnectPreview => _connect?.PreviewPoints;

    /// <summary>
    /// 从某个端点按下，开始一次连线手势。
    /// </summary>
    /// <remarks>
    /// 端口在按下这一刻就记下来，不等到松手再去猜用户从哪个端口出发——
    /// 自动端口按形状均分，端点压在边界哪一侧、偏多少一目了然，松手时再算反而可能选错边。
    /// </remarks>
    public bool BeginConnect(string sourceId, string? sourcePort, DrawPoint startDoc)
    {
        if (Find(sourceId) is null)
        {
            return false;
        }

        if (IsReadOnly)
        {
            Refuse();
            return false;
        }

        _connect = new ConnectSession(sourceId, sourcePort, startDoc);

        return true;
    }

    /// <summary>移动：记录当前指针位置（仅用于预览，不改动文档）。</summary>
    public void UpdateConnect(DrawPoint currentDoc) => _connect?.Move(currentDoc);

    /// <summary>
    /// 松手：把这一拖作为一条边落定。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **拖到空白取消，不弹窗。** 拖错的代价是再拖一次，弹窗打断的是整条操作链。
    /// </para>
    /// <para>
    /// **拖到自身被拒。** 一条边连到它自己既没有意义，布局也给不出走向，
    /// 这种落点当场挡下而不是写一条坏边。
    /// </para>
    /// </remarks>
    public ConnectOutcome CommitConnect(string? targetId, string? targetPort)
    {
        var gesture = _connect;
        _connect = null;

        if (gesture is null || targetId is null)
        {
            return ConnectOutcome.Cancelled;
        }

        if (IsReadOnly)
        {
            Refuse();
            return ConnectOutcome.Failed;
        }

        if (string.Equals(targetId, gesture.SourceId, StringComparison.Ordinal))
        {
            return ConnectOutcome.Rejected;
        }

        var edge = new EdgeDef
        {
            Id = NextEdgeId(),
            From = gesture.SourceId,
            FromPort = gesture.SourcePort,
            To = targetId,
            ToPort = targetPort,
        };

        var result = Bus.Execute(new ConnectEdgeCommand(edge));

        if (!result.IsSuccess)
        {
            Report(result);
            return ConnectOutcome.Failed;
        }

        Reload();

        return ConnectOutcome.Connected;
    }

    /// <summary>连线手势作废：不创建任何边。</summary>
    public void CancelConnect() => _connect = null;

    /// <summary>重连一条边的端点（含端口）。一次操作改四个端点字段。</summary>
    public CommandResult ReconnectEdge(string edgeId, string from, string? fromPort, string to, string? toPort)
    {
        if (IsReadOnly)
        {
            return Refuse();
        }

        var command = new ReconnectEdgeCommand(edgeId, from, fromPort, to, toPort);
        var result = Bus.Execute(command);

        if (result.IsEffectiveSuccess)
        {
            Reload();
        }

        return Report(result);
    }

    /// <summary>改一条边的一个字段（当前为标签、样式）。</summary>
    public CommandResult ApplyEdgeField(string edgeId, string field, string? value)
    {
        if (IsReadOnly)
        {
            return Refuse();
        }

        var command = new SetEdgeFieldCommand(edgeId, field, value);
        var result = Bus.Execute(command);

        if (result.IsEffectiveSuccess)
        {
            Reload();
        }

        return Report(result);
    }

    /// <summary>
    /// 把一条边的折线固定为给定点列。
    /// </summary>
    /// <remarks>
    /// **折点增删是一条操作。** 加一个折点、删一个折点都是写这一份点列，
    /// 拆成"删一段再加一段"的话，撤销要按两次，而用户眼里那一次拖拽是一下子的事。
    /// 点列空表示清掉固定折线、交回自动路由。
    /// </remarks>
    public void SetEdgeBends(string edgeId, IReadOnlyList<DrawPoint> points)
    {
        ArgumentNullException.ThrowIfNull(edgeId);
        ArgumentNullException.ThrowIfNull(points);

        if (IsReadOnly)
        {
            Refuse();
            return;
        }

        var before = SnapshotBends();

        if (points.Count == 0)
        {
            _pinnedEdges.Remove(edgeId);
        }
        else
        {
            _pinnedEdges[edgeId] = [.. points.Select(p => new Anchor(p.X, p.Y))];
        }

        _bendUndo.Push(before);
        _bendRedo.Clear();

        Reload();
    }

    /// <summary>清掉一条边的固定折线，交回自动路由。</summary>
    public void ClearEdgeBends(string edgeId)
    {
        ArgumentNullException.ThrowIfNull(edgeId);

        if (IsReadOnly)
        {
            Refuse();
            return;
        }

        if (!_pinnedEdges.ContainsKey(edgeId))
        {
            return;
        }

        var before = SnapshotBends();

        _pinnedEdges.Remove(edgeId);
        _bendUndo.Push(before);
        _bendRedo.Clear();

        Reload();
    }

    /// <summary>能否撤销最近一次折点编辑。</summary>
    public bool CanUndoBend => _bendUndo.Count > 0;

    /// <summary>能否重做最近一次被撤销的折点编辑。</summary>
    public bool CanRedoBend => _bendRedo.Count > 0;

    /// <summary>撤销最近一次折点编辑，整次增删算一步。</summary>
    public void UndoBend()
    {
        if (!_bendUndo.TryPop(out var before))
        {
            return;
        }

        var after = SnapshotBends();

        RestoreBends(before);
        _bendRedo.Push(after);
        Reload();
    }

    /// <summary>重做最近一次被撤销的折点编辑。</summary>
    public void RedoBend()
    {
        if (!_bendRedo.TryPop(out var after))
        {
            return;
        }

        var before = SnapshotBends();

        RestoreBends(after);
        _bendUndo.Push(before);
        Reload();
    }

    private string NextEdgeId()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var edge in Document.Edges)
        {
            used.Add(edge.Id);
        }

        for (var index = 1; ; index++)
        {
            var candidate = $"e{index}";

            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private BendSnapshot SnapshotBends() =>
        new(new Dictionary<string, IReadOnlyList<Anchor>>(
            _pinnedEdges.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Anchor>)[.. kv.Value], StringComparer.Ordinal),
            StringComparer.Ordinal));

    private void RestoreBends(BendSnapshot snapshot)
    {
        _pinnedEdges.Clear();

        foreach (var (id, anchors) in snapshot.Bends)
        {
            _pinnedEdges[id] = [.. anchors];
        }
    }

    #endregion

    #region 变更高亮

    /// <summary>有没有任何被标记的元素。</summary>
    public bool HasHighlights => _highlights.HasHighlights;

    /// <summary>当前是否还有脉冲在跑。它决定画布要不要继续出帧。</summary>
    public bool HasActivePulse => _highlights.HasActivePulse;

    /// <summary>脉冲当前相位；没有脉冲时为负。</summary>
    public double HighlightPhase => _highlights.PulsePhase;

    /// <summary>当前标记的一份快照，供画布算出这一帧要叠的指令。</summary>
    public IReadOnlyList<ElementHighlight> HighlightSnapshot => _highlights.Snapshot();

    /// <summary>等最近一批变更通知被处理完。只给测试用，真实界面按帧刷新。</summary>
    public bool WaitForHighlights(TimeSpan? timeout = null) =>
        _highlights.WaitForNotifications(timeout ?? TimeSpan.FromSeconds(2));

    #endregion

    /// <summary>固定位置的一次落定结果。</summary>
    public sealed record DragCommit
    {
        private DragCommit(CommitKind kind, int count)
        {
            Kind = kind;
            Count = count;
        }

        /// <summary>这一拖的结局。</summary>
        public CommitKind Kind { get; }

        /// <summary>
        /// 落定涉及的元素数。
        /// </summary>
        /// <remarks>
        /// 固定时是被固定的节点数，落成次序时是那条次序里的出边数。
        /// 两种结局各有各的单位，所以这个名字不带单位——写成"节点数"的话，
        /// 次序那一档的读数会被读成"排进去了几个节点"，而它数的是边。
        /// </remarks>
        public int Count { get; }

        /// <summary>没开始拖（按下的是空白或边）。</summary>
        public static DragCommit Ignored => new(CommitKind.Ignored, 0);

        /// <summary>落点与另一个固定节点重叠，被挡下，没写任何固定位置。</summary>
        public static DragCommit Rejected => new(CommitKind.Rejected, 0);

        /// <summary>固定成功。</summary>
        public static DragCommit Pinned(int count) => new(CommitKind.Pinned, count);

        /// <summary>落定成一条层内次序，节点数不计入固定。</summary>
        public static DragCommit Ordered(int count) => new(CommitKind.Ordered, count);
    }

    /// <summary>固定落定的几种结局。</summary>
    public enum CommitKind
    {
        /// <summary>根本没进入拖拽。</summary>
        Ignored,

        /// <summary>被重叠判据挡下。</summary>
        Rejected,

        /// <summary>成功固定。</summary>
        Pinned,

        /// <summary>落在一个兄弟节点上，落定的是一条层内次序。</summary>
        Ordered,
    }

    /// <summary>连线手势落定的几种结局。</summary>
    public enum ConnectOutcome
    {
        /// <summary>松手在空白或别处，没创建边。</summary>
        Cancelled,

        /// <summary>落点是连线起点自身，当场挡下。</summary>
        Rejected,

        /// <summary>命令被拒（端点不存在等）。</summary>
        Failed,

        /// <summary>边已创建。</summary>
        Connected,
    }

    /// <summary>固定位置的一份快照，用于撤销重做。</summary>
    private sealed record PinSnapshot(IReadOnlyDictionary<string, Anchor> Pins);

    /// <summary>固定折线的一份快照，用于撤销重做。</summary>
    private sealed record BendSnapshot(IReadOnlyDictionary<string, IReadOnlyList<Anchor>> Bends);

    public void Dispose()
    {
        // 先退订：总线一关就不再广播，但投递任务可能还在收尾，这时若还挂着回调，
        // 回调会打到一份正在释放的状态上。
        _changes.Dispose();

        // 高亮同理：它听的是同一个广播器，也要在总线关掉之前退掉。
        _highlights.Dispose();

        // 共用的工作区不归这一份释放：先关掉的那个窗口一放，剩下那个窗口的广播器
        // 就没了，而症状是"界面莫名不再刷新"。谁建的谁放，由引用计数决定。
        if (_ownsWorkspace)
        {
            // 先关命令总线再放度量器：释放之后不会再有新的变更进来，
            // 这时再去放掉绘制要用到的原生资源才不会有竞态。
            _workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _measurer.Dispose();
    }
}
