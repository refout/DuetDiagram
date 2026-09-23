using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DuetDiagram.App.Controls;
using DuetDiagram.App.Services;
using DuetDiagram.App.ViewModels;
using DuetDiagram.Core.Commands;
using DuetDiagram.Layout;
using DuetDiagram.Render;

namespace DuetDiagram.App;

/// <summary>
/// 主窗口。左边画布铺满，右边属性面板，底下一行状态栏，画布右上角浮着性能诊断面板。
/// </summary>
/// <remarks>
/// <para>
/// 它把画布、属性面板与文档串起来：画布报出"点到了谁"，文档那一侧决定选中哪几个，
/// 再把结果推回画布与面板。两边都不自己判断该不该选中——各判一次的话，
/// 判据迟早会分叉，而表现是"选中框亮了但属性面板没换"。
/// </para>
/// <para>
/// **它不决定失败怎么呈现。** 失败怎么呈现由错误码查表给出，这里只负责把那一份呈现
/// 摆到状态栏与两条提示上。就地判断的话，同一个错误码在两个入口会长出两种样子，
/// 而用户以为遇到的是两个问题。
/// </para>
/// <para>
/// 菜单栏与工具栏的条目由 <see cref="MenuRegistry"/> 给出，两处读同一份。
/// 这个窗口负责在改动、换选中之后让它们刷新启用状态，以及把快捷键也接到同一批条目上——
/// 快捷键另跑一遍的话，菜单上写的组合键与实际生效的那个迟早会对不上。
/// 图层面板还没有，它依赖图层那一层的可见性与锁定。
/// </para>
/// </remarks>
public sealed partial class MainWindow : Window
{
    // 这个窗口对工作区的占用。窗口关掉时还回去；最后一个还回去的那个负责释放工作区。
    private readonly WorkspaceLease _lease;

    private bool _loaded;
    private bool _closed;

    /// <summary>用默认的布局引擎，开一份示例文档。</summary>
    public MainWindow()
        : this(DocumentLaunch.Sample())
    {
    }

    /// <summary>
    /// 用指定的布局引擎，开一份示例文档。
    /// </summary>
    /// <remarks>
    /// 引擎可注入的用途只有一个：让"布局彻底失败"这条路径能被真正走到。
    /// 拿一个会失败的引擎，比在真实引擎上构造一份它解不出的图可靠得多——
    /// 后者会随引擎的改进而失效，而那条路径本身不能没人走。
    /// </remarks>
    public MainWindow(ILayoutEngine? engine)
        : this(DocumentLaunch.Sample(engine))
    {
    }

    /// <summary>
    /// 按给定的说明开一个窗口。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **工作区是按标识取的，不是各建一份。** 同一个进程里再开一个窗口看同一份文档时，
    /// 取到的是已经有的那一份，于是两个窗口共用文档、总线与广播器：版本号天然一致，
    /// 一边改了另一边会被通知到，也不存在版本冲突——冲突是两个副本之间的事。
    /// </para>
    /// <para>
    /// **能不能写由跨进程所有权定，不是窗口自己决定的。** 另一个进程正拿着这份文件时，
    /// 这一个退成只读。界面另外把所有写入口禁掉，但挡住写入的是会话那一层：
    /// 界面上的入口不止一处，漏掉哪一处都看不出来。
    /// </para>
    /// </remarks>
    public MainWindow(DocumentLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);

        InitializeComponent();

        Launch = launch;

        // 先取工作区再建会话：能不能写是取租约那一刻才知道的，
        // 而它必须赶在会话建好之前定下来——会话建好之后再改，界面上已经挂上去的那些
        // 控件不会跟着变。
        _lease = WorkspaceRegistry.Shared.Acquire(launch.Key, launch.Create);
        Session = DiagramSession.Shared(_lease.Workspace, engine: launch.Engine, readOnly: _lease.ReadOnly);

        Model = new CanvasViewModel();
        Properties = new PropertyPanelViewModel(Session);
        Layers = new LayerPanelViewModel(Session);
        Pages = new PageTabsViewModel(Session);
        Status = new StatusBarViewModel(Model);
        Status.SetReadOnly(Session.ReadOnlyReason);

        Model.SelectionRequested += OnSelectionRequested;
        Session.SceneChanged += OnSceneChanged;
        Session.SelectionChanged += OnSelectionChanged;
        Session.LayoutFailed += OnLayoutFailed;
        Session.CommandReported += OnCommandReported;

        // 高亮的标记在后台投递线程上到达，画布只能在这条线程上被作废。
        // 不接这一下的话，一次变更之后画布只在"文档重载"那一帧刷新过，
        // 而通知通常比那一帧晚到，高亮就会一直不出现。
        Session.HighlightsChanged += OnHighlightsChanged;

        // 别的窗口改了同一份文档：这一份的绘制列表也过期了，要重算一遍。
        Session.DocumentChanged += OnDocumentChanged;

        // 画布要能拖节点，得知道文档那一侧在哪。会话与画布由主窗口串起来，
        // 不在各自内部互相引用——否则属性面板那条链也得各自再找一遍会话。
        Canvas.Session = Session;
        Canvas.Host = this;
        DiffView.Session = Session;
        StatusBarView.Status = Status;

        // 菜单栏与工具栏读同一份条目表。它们只在建窗口时搭一次，
        // 之后换选中、换文档都只刷新启用状态——重建的话，连续点选时整条工具栏会闪。
        MenuBarView.Attach(this);
        ToolBarView.Attach(this);

        // 提示上的按钮只把选择交回来，办不办由这里定：提示自己会去调布局的话，
        // 那件事就有了两个入口，两条路径迟早会对同一次失败给出不同的处置。
        LayoutFailureView.RetryRequested += OnRetryLayout;
        LayoutFailureView.ManualLayoutRequested += OnManualLayout;
        LayoutFailureView.SimplifyRequested += OnShowConflicts;

        PropertiesView.DataContext = Properties;
        LayersView.DataContext = Layers;
        PageTabsView.DataContext = Pages;
        DataContext = Model;

        // 第一份绘制列表走 Load：它把视口适配到内容上。之后每一次改动走 Refresh，
        // 用户摆好的视角不该因为改了一个字就跳回默认。
        ShowScene();
        _loaded = true;
    }

    /// <summary>这个窗口看的是哪份文档。</summary>
    public DocumentLaunch Launch { get; }

    /// <summary>文档、选中状态与绘制列表。</summary>
    public DiagramSession Session { get; }

    /// <summary>文档文件的路径。这份文档没有文件时为空。</summary>
    public string? DocumentPath => _lease.Path;

    /// <summary>画布、状态栏与诊断面板共用的状态。</summary>
    public CanvasViewModel Model { get; }

    /// <summary>属性面板的状态。</summary>
    public PropertyPanelViewModel Properties { get; }

    /// <summary>图层面板的状态。</summary>
    public LayerPanelViewModel Layers { get; }

    /// <summary>标签栏的状态。</summary>
    public PageTabsViewModel Pages { get; }

    /// <summary>状态栏的状态：光标、缩放与最近一次失败的提示。</summary>
    public StatusBarViewModel Status { get; }

    /// <summary>
    /// 人工产物读不出来时，把恢复提示摆出来。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 打开文档那条路还没做，所以现在没有调用点。放在这里而不是让提示自己去找会话，
    /// 是因为"要不要弹、弹什么"是宿主的事：提示自己去读文件的话，
    /// 同一份坏文件会在两个地方被解析一遍，而两处的判据迟早会不一样。
    /// </para>
    /// <para>
    /// 触发它的时机是"读 user.json 得到不可用的结果"。那条路上没有第二条出路——
    /// 那份内容重算不出来，所以提示上也没有"取消"。
    /// </para>
    /// </remarks>
    public void ShowSidecarRecovery(string detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        SidecarRecoveryView.SetDetail(detail);
        SidecarRecoveryView.IsVisible = true;
    }

    /// <summary>
    /// 快捷键：再开一个窗口、写回文件，以及菜单上写着的那几个组合键。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 键盘消息只发给有焦点的控件，而画布在指针按下时会把焦点收过去。
    /// 所以这里不能假设焦点在窗口上——好在按键事件是从焦点控件往上冒泡的，
    /// 画布不认这几个键，它们就冒到这儿了。
    /// </para>
    /// <para>
    /// **带条目的那几个键与菜单走同一条路**（见 <see cref="Invoke"/>）。
    /// 两处各跑一遍的话，菜单上显示的组合键与实际生效的那个迟早会对不上，
    /// 而那种对不上只有在用户按了没反应时才会被发现。
    /// </para>
    /// </remarks>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled)
        {
            return;
        }

        // 开窗口与写文件不办命令层的事，没有对应条目可走，所以留在这一层。
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.N)
            {
                OpenAnotherWindow();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.S)
            {
                Save();
                e.Handled = true;
                return;
            }
        }

        if (Shortcut(e) is not { } entryId)
        {
            return;
        }

        Invoke(entryId);
        e.Handled = true;
    }

    /// <summary>
    /// 这一下按键对应哪一条条目。
    /// </summary>
    /// <remarks>
    /// 写成一张表而不是一串 if：表里每一条都要在注册表里有一个同名的条目，
    /// 而"表里有、条目里没有"这种错会在这里被立刻发现（<see cref="Invoke"/> 会抛）。
    /// </remarks>
    private static string? Shortcut(KeyEventArgs e)
    {
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        return (e.Key, control, shift) switch
        {
            (Key.Z, true, false) => "edit.undo",
            (Key.Y, true, false) => "edit.redo",
            (Key.A, true, false) => "edit.select-all",
            (Key.Delete, false, false) => "edit.delete",
            (Key.F5, false, false) => "layout.relayout",
            (Key.P, true, true) => "view.diagnostics",
            _ => null,
        };
    }

    /// <summary>
    /// 再开一个窗口看同一份文档，返回新开的那个。
    /// </summary>
    /// <remarks>
    /// 返回它而不是就地丢掉：新窗口不是这个窗口的子窗口，谁都不拿着它，
    /// 而调用方往往还想再摆弄一下（例如把焦点移过去）。丢掉的话，
    /// 要拿到它就只能去翻应用级的窗口列表，而那个列表在测试里不一定存在。
    /// </remarks>
    public MainWindow OpenAnotherWindow()
    {
        var next = new MainWindow(Launch.Again());

        next.Show();

        return next;
    }

    /// <summary>
    /// 把文档写回它的文件。
    /// </summary>
    /// <remarks>
    /// 只读时不报错也不提示：状态栏上那句"这份文档是只读的"从打开起就一直在，
    /// 再叠一条只会让用户以为刚刚发生了一件新事。文档没有文件时给一句灰显说明——
    /// 那种情况下按了没反应，用户会以为快捷键坏了。
    /// </remarks>
    public void Save()
    {
        if (Session.IsReadOnly)
        {
            return;
        }

        if (_lease.Path is not { } path)
        {
            Status.Show(new ErrorPresentation(ErrorPresentationKind.StatusBarMuted, "这份文档没有文件，存不了"));

            return;
        }

        try
        {
            DocumentFile.Write(path, Session.Document);
            Status.Show(new ErrorPresentation(
                ErrorPresentationKind.StatusBarMuted,
                $"已写回 {System.IO.Path.GetFileName(path)}"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status.Show(new ErrorPresentation(
                ErrorPresentationKind.StatusBar,
                $"存不进去：{exception.Message}"));
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // 先立这个标志：排队等界面线程的那次重算可能还没跑，它要先看到窗口已经关了。
        _closed = true;

        // 度量器持有原生资源，交给垃圾回收决定什么时候放掉的话，
        // 关闭窗口之后还会有一批字体对象活着。
        Session.Dispose();

        // 租约最后还。还早了的话，这个窗口还在收尾而工作区已经被释放，
        // 剩下那个窗口会突然收不到任何通知。
        _lease.Dispose();
    }

    /// <summary>
    /// 画布报出点了谁。选不选中由文档那一侧定。
    /// </summary>
    private void OnSelectionRequested(string? elementId, bool additive) =>
        Session.Select(elementId, additive);

    private void OnSelectionChanged()
    {
        Model.SetSelection(Session.SelectedIds);
        RefreshMenu();
    }

    /// <summary>
    /// 高亮标记变了，让画布重画一帧。
    /// </summary>
    /// <remarks>
    /// 通知在后台投递线程上到达，而画布只能在这条线程上被作废，所以排一次队再作废。
    /// </remarks>
    private void OnHighlightsChanged() =>
        Dispatcher.UIThread.Post(Canvas.InvalidateVisual);

    /// <summary>
    /// 别的窗口改了这份文档，重算这一份的绘制列表。
    /// </summary>
    /// <remarks>
    /// 通知在后台投递线程上到达，而重算要碰会话的状态、还要更新画布与面板，
    /// 那些都只能在界面线程上做，所以排一次队再算。
    /// </remarks>
    private void OnDocumentChanged() =>
        Dispatcher.UIThread.Post(() =>
        {
            // 通知到达之后、这一帧轮到这里之前，窗口可能已经被关掉了。
            // 那时会话已经释放，重算会碰到已经放掉的度量器。
            if (!_closed)
            {
                Session.Reload();
            }
        });

    private void OnSceneChanged()
    {
        ShowScene();

        // 结构变了之后绘制列表换了一份，选中框要按新的这份重算。
        // 不推的话，被删掉的元素留下的那个框会停在原地。
        Model.SetSelection(Session.SelectedIds);

        // 排出来了就把上一次的失败提示收掉。不收的话，用户按了重试、
        // 图已经正常了，那条红字还挂着，看起来像失败还在。
        SyncLayoutFailure();

        // 一次改动之后撤销栈、选中、只读门都可能变了，菜单栏与工具栏跟着刷一遍。
        RefreshMenu();
    }

    /// <summary>
    /// 一次命令没写进去，在状态栏给一句话。
    /// </summary>
    /// <remarks>
    /// 只显示第一条。多选编辑失败时会攒下好几条同类的错误，全塞进一行的话
    /// 哪一条都读不清；而它们的原因通常是同一个。
    /// </remarks>
    private void OnCommandReported(CommandResult result)
    {
        var presentations = ErrorPresenter.Present(result);

        if (presentations.Count > 0)
        {
            Status.Show(presentations[0]);
        }

        // 一次失败之后那些"能不能点"的判据可能也变了（例如删除失败是因为选中没了）。
        RefreshMenu();
    }

    /// <summary>
    /// 按标识执行一条菜单或工具栏上的条目。
    /// </summary>
    /// <remarks>
    /// 快捷键与点击走同一条路：两边各跑一遍的话，显示出来的组合键与实际生效的那个
    /// 迟早会对不上——而那种对不上只有在用户按了没反应时才会被发现。
    /// </remarks>
    public void Invoke(string entryId)
    {
        ArgumentNullException.ThrowIfNull(entryId);

        if (MenuRegistry.Default.Find(entryId) is not { } entry)
        {
            throw new ArgumentException($"没有这条条目：{entryId}", nameof(entryId));
        }

        var context = new MenuContext(this);

        if (entry.Refusal(context) is { } reason)
        {
            Status.Show(new ErrorPresentation(ErrorPresentationKind.StatusBarMuted, reason));

            return;
        }

        entry.Run(context);
        RefreshMenu();
    }

    private void RefreshMenu()
    {
        MenuBarView.Refresh();
        ToolBarView.Refresh();
    }

    private void OnLayoutFailed() => SyncLayoutFailure();

    /// <summary>
    /// 重试：换更长的预算再排一次。
    /// </summary>
    /// <remarks>
    /// 这里不自己收提示：重试成功会发一次"绘制列表换了"，仍失败会再发一次"布局失败"，
    /// 两条都会走到同步那一步。在这里再收一次的话，两条路径对同一次重试的结局会各判一次。
    /// </remarks>
    private void OnRetryLayout() => Session.RetryLayout();

    /// <summary>手动布局：位置不再自动重排，画面停在最后一张能看的图上。</summary>
    private void OnManualLayout()
    {
        Session.EnterManualLayout();

        // 这句话是灰色的：它不是失败，是"接下来不会自动重排了"。
        // 报成红的会让用户以为又出了一次错。
        Status.Show(new ErrorPresentation(
            ErrorPresentationKind.StatusBarMuted,
            "手动布局：位置不再自动重排，改完元素要显式重排"));
    }

    /// <summary>
    /// 简化图：把这一轮降级丢掉的约束摆出来。
    /// </summary>
    /// <remarks>
    /// 去掉哪一条是用户的判断，这里只负责把候选摆出来。自动替用户去掉一条的话，
    /// 去掉的可能是他真正在乎的那条，而重排之后图变了却看不出是为什么。
    /// </remarks>
    private void OnShowConflicts()
    {
        var conflicts = Session.ConflictSummary;

        LayoutFailureView.SetDetail(conflicts.Count == 0
            ? "这一轮没有丢掉任何约束：失败与约束无关。换个办法——重试，或者转手动布局。"
            : "这一轮丢掉的约束，去掉其中一条再试："
                + Environment.NewLine
                + string.Join(Environment.NewLine, conflicts));
    }

    /// <summary>把"布局失败"这件事同步到提示与状态栏上。</summary>
    /// <remarks>
    /// 失败已经过去时连状态栏一起收掉。只收提示的话，图已经排出来了、红字还挂在下面，
    /// 看起来像失败还在——而用户会去查一个已经不存在的问题。
    /// </remarks>
    private void SyncLayoutFailure()
    {
        var failure = Session.LayoutFailure;

        if (failure is null)
        {
            LayoutFailureView.IsVisible = false;
            Status.Clear();
            return;
        }

        LayoutFailureView.SetDetail(Detail(failure));
        LayoutFailureView.IsVisible = true;
        Status.Show(ErrorPresenter.Present(failure));
    }

    /// <summary>提示上那句补充说明：试了几级、这一轮丢了什么。</summary>
    private static string Detail(LayoutFailedException failure)
    {
        var attempts = failure.Payload.Attempts.Count;
        var conflicts = failure.Payload.Attempts
            .SelectMany(attempt => attempt.Conflicts)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var head = attempts == 0
            ? "一级都没来得及试：总预算不够。"
            : $"试过 {attempts} 级，都不成。";

        return conflicts.Count == 0
            ? $"{head}画面停在上一次成功的结果上。"
            : $"{head}画面停在上一次成功的结果上。按「简化图」看这一轮丢了哪些约束。";
    }

    private void ShowScene()
    {
        var scene = Session.Scene;

        Model.Diagnostics.RecordLayout(
            scene.LayoutMilliseconds,
            scene.Result.AppliedLevel,
            scene.Result.Attempts.Count);
        Model.Diagnostics.RecordStage(DiagnosticsStage.DrawList, scene.DrawListMilliseconds);

        if (_loaded)
        {
            Model.Refresh(scene.DrawList);
            return;
        }

        Model.Load(scene.DrawList);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        // 手写 InitializeComponent 会盖掉生成器原本对 x:Name 字段的赋值，
        // 不在这里补一句的话，这些字段永远是 null，而界面上明明摆着它们。
        // 画布要拿到会话、属性面板要拿到数据上下文，都得先在这里把字段接上。
        Canvas = this.FindControl<DiagramCanvas>(nameof(Canvas))
            ?? throw new InvalidOperationException("主窗口的界面标记里没有名为 Canvas 的画布");
        PropertiesView = this.FindControl<PropertyPanel>(nameof(PropertiesView))
            ?? throw new InvalidOperationException("主窗口的界面标记里没有名为 PropertiesView 的容器");
        LayersView = this.FindControl<LayerPanel>(nameof(LayersView))
            ?? throw new InvalidOperationException("主窗口的界面标记里没有名为 LayersView 的面板");
        PageTabsView = this.FindControl<PageTabs>(nameof(PageTabsView))
            ?? throw new InvalidOperationException("主窗口的界面标记里没有名为 PageTabsView 的标签栏");
        DiffView = this.FindControl<DiffSidebar>(nameof(DiffView))
            ?? throw new InvalidOperationException("主窗口的界面标记里没有名为 DiffView 的边栏");
        StatusBarView = this.FindControl<StatusBar>(nameof(StatusBarView))
            ?? throw new InvalidOperationException("主窗口的界面标记里没有名为 StatusBarView 的状态栏");
        MenuBarView = this.FindControl<DiagramMenuBar>(nameof(MenuBarView))
            ?? throw new InvalidOperationException("主窗口的界面标记里没有名为 MenuBarView 的菜单栏");
        ToolBarView = this.FindControl<DiagramToolBar>(nameof(ToolBarView))
            ?? throw new InvalidOperationException("主窗口的界面标记里没有名为 ToolBarView 的工具栏");
        LayoutFailureView = this.FindControl<LayoutFailureDialog>(nameof(LayoutFailureView))
            ?? throw new InvalidOperationException("主窗口的界面标记里没有名为 LayoutFailureView 的提示");
        SidecarRecoveryView = this.FindControl<SidecarRecoveryDialog>(nameof(SidecarRecoveryView))
            ?? throw new InvalidOperationException("主窗口的界面标记里没有名为 SidecarRecoveryView 的提示");
    }
}
