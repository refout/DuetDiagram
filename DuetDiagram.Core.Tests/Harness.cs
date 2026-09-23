using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Diagnostics;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Core.Time;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 测试用的固定环境：一份文档、一条总线、一个可控时钟、一个可读的诊断出口。
/// </summary>
/// <remarks>
/// <para>
/// 时钟是手动推进的，所以"审计日志里的时间戳等于某个具体时刻"这类断言可以写成精确相等，
/// 而不是一个范围判断。凡是涉及时间或会话的测试都应该从这里拿环境，避免各自搭一套。
/// </para>
/// <para>
/// 广播器的归属由构造参数决定：不传就自己建一个并在释放时一并销毁，
/// 传入的则由调用方负责——这与被测代码里的所有权约定保持一致，
/// 否则测试本身会掩盖掉所有权写错的问题。
/// </para>
/// </remarks>
internal sealed class Harness : IDisposable
{
    private readonly bool _ownsBroadcaster;

    public Harness(DiagramCommandBusOptions? options = null, IChangeBroadcaster? broadcaster = null)
    {
        _ownsBroadcaster = broadcaster is null;

        Options = options ?? DiagramCommandBusOptions.ForGui();
        Broadcaster = broadcaster ?? new InProcessBroadcaster();
        Document = new DiagramDocument("test-doc", DiagramKind.Flowchart, Direction.LR);
        Session = new SimpleSessionProvider("tester", SessionIds.Gui("w1"));
        Diagnostics = new CollectingDiagnosticsSink();
        Clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero));

        Context = DiagramCommandBusContext.Create(
            Document,
            Session,
            Broadcaster,
            Options,
            Clock,
            Diagnostics);

        Bus = new DiagramCommandBus(Context);
    }

    public DiagramCommandBusOptions Options { get; }

    public IChangeBroadcaster Broadcaster { get; }

    public DiagramDocument Document { get; }

    public SimpleSessionProvider Session { get; }

    public CollectingDiagnosticsSink Diagnostics { get; }

    public ManualTimeProvider Clock { get; }

    public DiagramCommandBusContext Context { get; }

    public DiagramCommandBus Bus { get; }

    /// <summary>
    /// 当前的完整序列化结果，用来判断"文档有没有被改脏"。
    /// 写成快照比较而不是逐字段比较，是因为任何一处漏比都会让这类断言失效。
    /// </summary>
    public string Snapshot() => DiagramSerializer.Normalize(Document);

    /// <summary>按标识取一个节点。找不到时直接抛——用例里的标识都是自己写下的。</summary>
    public NodeDef Node(string id) => Document.Nodes.Single(n => n.Id == id);

    /// <summary>按标识取一个组合。</summary>
    public CompositeDef Composite(string id) => Document.Composites.Single(c => c.Id == id);

    /// <summary>按标识取一个图层。</summary>
    public LayerDef Layer(string id) => Document.Layers.Single(l => l.Id == id);

    /// <summary>按标识取一页。</summary>
    public PageDef Page(string id) => Document.Pages.Single(p => p.Id == id);

    /// <summary>按标识取一个标签。</summary>
    public TagDef Tag(string id) => Document.Tags.Single(t => t.Id == id);

    /// <summary>按标识取一个动作。</summary>
    public ActionDef Action(string id) => Document.Actions.Single(a => a.Id == id);

    public CommandResult AddNode(string id, string label = "", NodeShape shape = NodeShape.Rect, ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new AddNodeCommand(new NodeDef { Id = id, Label = label, Shape = shape })
            .WithContext(ChangeContext.For(source, "tester")));

    public CommandResult Connect(string id, string from, string to, string label = "", ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new ConnectEdgeCommand(new EdgeDef { Id = id, From = from, To = to, Label = label })
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>改一个节点的一个字段。</summary>
    public CommandResult SetField(
        string nodeId,
        string field,
        string? value,
        ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new SetNodeFieldCommand(nodeId, field, value)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>加一条布局约束。归属默认人工，与界面走的那条路一致。</summary>
    public CommandResult AddConstraint(
        LayoutConstraintSpec spec,
        ConstraintOwner owner = ConstraintOwner.Human,
        ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new AddLayoutConstraintCommand(spec, owner)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>删一条布局约束。</summary>
    public CommandResult RemoveConstraint(
        LayoutConstraintSpec spec,
        ConstraintOwner owner = ConstraintOwner.Human,
        ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new RemoveLayoutConstraintCommand(spec, owner)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>改布局主方向。</summary>
    public CommandResult SetDirection(Direction direction, ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new SetDirectionCommand(direction)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>改布局间距。传空表示那一项不动。</summary>
    public CommandResult SetSpacing(
        double? nodeSpacing = null,
        double? layerSpacing = null,
        ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new SetSpacingCommand(nodeSpacing, layerSpacing)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>设置或清除一条相对位置约束。关系传空表示清除。</summary>
    public CommandResult SetPlace(
        string nodeId,
        string relativeTo,
        PlaceRelation? relation,
        ConstraintOwner owner = ConstraintOwner.Human,
        ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new SetPlaceCommand(nodeId, relativeTo, relation, owner)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>定义一条调色板条目。</summary>
    public CommandResult DefinePalette(PaletteEntry entry, ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new DefinePaletteEntryCommand(entry)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>按名字定义一条调色板条目。只给要用的那几个成员，其余留空。</summary>
    public CommandResult DefinePalette(
        string name,
        string? fill = null,
        string? stroke = null,
        string? text = null,
        double? weight = null,
        ChangeSource source = ChangeSource.Human)
        => DefinePalette(
            new PaletteEntry { Name = name, Fill = fill, Stroke = stroke, Text = text, Weight = weight },
            source);

    /// <summary>改一条调色板条目的一个成员。</summary>
    public CommandResult UpdatePalette(
        string name,
        string field,
        string? value,
        ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new UpdatePaletteEntryCommand(name, field, value)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>删一条调色板条目。</summary>
    public CommandResult RemovePalette(string name, ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new RemovePaletteEntryCommand(name)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>给一个节点挂上样式令牌。走字段写入那条命令，与界面走的是同一条路。</summary>
    public CommandResult SetStyleToken(string nodeId, string token, ChangeSource source = ChangeSource.Human)
        => SetField(nodeId, FieldNames.StyleToken, token, source);

    /// <summary>新建一个组合。</summary>
    public CommandResult CreateComposite(CompositeDef composite, int? index = null, ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new CreateCompositeCommand(composite, index)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>建一个分组，成员一次给全。</summary>
    public CommandResult CreateGroup(
        string id,
        IReadOnlyList<string> members,
        string? parent = null,
        string label = "",
        ChangeSource source = ChangeSource.Human)
        => CreateComposite(
            new GroupDef { Id = id, Members = members, Parent = parent, Label = label },
            source: source);

    /// <summary>解散一个组合。</summary>
    public CommandResult DissolveComposite(string compositeId, ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new DissolveCompositeCommand(compositeId)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>把一个节点或组合搬进另一个组合。目标传空表示搬到顶层。</summary>
    public CommandResult MoveIntoComposite(
        string memberId,
        string? targetCompositeId,
        ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new MoveIntoCompositeCommand(memberId, targetCompositeId)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>新建一个图层。</summary>
    public CommandResult CreateLayer(string layerId, string name = "", ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new CreateLayerCommand(layerId, name)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>给图层改名。</summary>
    public CommandResult RenameLayer(string layerId, string name, ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new RenameLayerCommand(layerId, name)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>把图层挪到第几位。</summary>
    public CommandResult ReorderLayer(string layerId, int index, ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new ReorderLayerCommand(layerId, index)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>把一个图层藏起来或者放出来。</summary>
    public CommandResult SetLayerVisible(string layerId, bool visible, ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new SetLayerVisibleCommand(layerId, visible)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>锁上一个图层或者解锁。</summary>
    public CommandResult SetLayerLocked(string layerId, bool locked, ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new SetLayerLockedCommand(layerId, locked)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>把一个节点归到某个图层上。走字段写入那条命令，与界面走的是同一条路。</summary>
    public CommandResult PutOnLayer(string nodeId, string layerId, ChangeSource source = ChangeSource.Human)
        => SetField(nodeId, FieldNames.Layer, layerId, source);

    /// <summary>把一个节点归到某一页上。走字段写入那条命令。</summary>
    public CommandResult PutOnPage(string nodeId, string pageId, ChangeSource source = ChangeSource.Human)
        => SetField(nodeId, FieldNames.Page, pageId, source);

    /// <summary>把一批节点一次归到同一页上。</summary>
    /// <remarks>
    /// 归属没有批命令（页归属是按"在某一页上画东西"发生的，不是多选之后整批挪），
    /// 所以这里是逐条发。要一次进一条历史的话，那是另一条命令的事。
    /// </remarks>
    public CommandResult AssignPage(
        IEnumerable<string> nodeIds,
        string pageId,
        ChangeSource source = ChangeSource.Human)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);

        CommandResult last = CommandResult.NoOp();

        foreach (var id in nodeIds)
        {
            last = PutOnPage(id, pageId, source);
        }

        return last;
    }

    /// <summary>改一条边的一个字段。</summary>
    public CommandResult SetEdgeField(
        string edgeId,
        string field,
        string? value,
        ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new SetEdgeFieldCommand(edgeId, field, value)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>把一批节点一次归到同一个图层上。</summary>
    public CommandResult AssignLayer(IEnumerable<string> nodeIds, string layerId, ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new AssignLayerCommand(layerId, [.. nodeIds])
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>新建一页。</summary>
    public CommandResult CreatePage(string pageId, string name = "", ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new CreatePageCommand(pageId, name)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>删掉一页。</summary>
    public CommandResult DeletePage(string pageId, ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new DeletePageCommand(pageId)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>新建一个标签。</summary>
    public CommandResult AddTag(TagDef tag, ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new AddTagCommand(tag)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>按几个成员建一个标签，颜色留空。</summary>
    public CommandResult AddTag(
        string tagId,
        IReadOnlyList<string> members,
        string label = "",
        string? color = null,
        ChangeSource source = ChangeSource.Human)
        => AddTag(new TagDef { Id = tagId, Members = members, Label = label, Color = color }, source);

    /// <summary>删掉一个标签。</summary>
    public CommandResult RemoveTag(string tagId, ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new RemoveTagCommand(tagId)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>新建一个动作。</summary>
    public CommandResult AddAction(ActionDef action, ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new AddActionCommand(action)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>按事件与类型建一个动作。</summary>
    public CommandResult AddAction(
        string actionId,
        string @event,
        string kind,
        string? target = null,
        ChangeSource source = ChangeSource.Human)
        => AddAction(new ActionDef { Id = actionId, Event = @event, Kind = kind, Target = target }, source);

    /// <summary>删掉一个动作。</summary>
    public CommandResult RemoveAction(string actionId, ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new RemoveActionCommand(actionId)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>改文档类型。</summary>
    public CommandResult SetKind(DiagramKind kind, ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new SetKindCommand(kind)
            .WithContext(ChangeContext.For(source, "tester")));

    /// <summary>改画布设置。传空表示那一项不动。</summary>
    public CommandResult SetCanvas(
        GridStyle? grid = null,
        double? gridSize = null,
        Size? pageSize = null,
        CanvasOrientation? orientation = null,
        string? background = null,
        bool? infinite = null,
        ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new SetCanvasSettingsCommand(grid, gridSize, pageSize, orientation, background, infinite)
            .WithContext(ChangeContext.For(source, "tester")));

    public void Dispose()
    {
        Bus.Dispose();

        if (_ownsBroadcaster)
        {
            Broadcaster.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}

/// <summary>
/// 记录释放次数的广播器，用来验证工作区有没有在共享场景下错误地销毁别人的广播器。
/// </summary>
internal sealed class TrackingBroadcaster : IChangeBroadcaster
{
    public int DisposeCount { get; private set; }

    public void Enqueue(ChangeNotification notification)
    {
    }

    public IDisposable Subscribe(Action<ChangeNotification> handler) => new NoopSubscription();

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }

    private sealed class NoopSubscription : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
