using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Diagnostics;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Core.Time;

namespace DuetDiagram.Core.Tests;

/// <summary>固定测试台。时钟是手动的，因此所有断言可复现。</summary>
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

    /// <summary>规范化 JSON —— P1 判据 #7 的原子性比较基准。</summary>
    public string Snapshot() => DiagramSerializer.Normalize(Document);

    public CommandResult AddNode(string id, string label = "", NodeShape shape = NodeShape.Rect, ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new AddNodeCommand(new NodeDef { Id = id, Label = label, Shape = shape })
            .WithContext(ChangeContext.For(source, "tester")));

    public CommandResult Connect(string id, string from, string to, string label = "", ChangeSource source = ChangeSource.Human)
        => Bus.Execute(new ConnectEdgeCommand(new EdgeDef { Id = id, From = from, To = to, Label = label })
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

/// <summary>记录 DisposeAsync 次数的广播器，用于验证 Workspace 的所有权语义。</summary>
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
