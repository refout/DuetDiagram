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
