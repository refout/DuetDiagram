using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>P1 判据 #28 / #31：Workspace 与 Context、Broadcaster 的所有权一致。</summary>
public sealed class WorkspaceTests
{
    [Fact]
    [Trait("Category", "Workspace")]
    public async Task Owned_workspace_disposes_the_broadcaster()
    {
        var broadcaster = new TrackingBroadcaster();
        var workspace = new DiagramWorkspace(Context(broadcaster), ownsBroadcaster: true);

        await workspace.DisposeAsync();

        broadcaster.DisposeCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public async Task Shared_workspace_does_not_dispose_the_broadcaster()
    {
        var broadcaster = new TrackingBroadcaster();
        var workspace = new DiagramWorkspace(Context(broadcaster), ownsBroadcaster: false);

        await workspace.DisposeAsync();

        broadcaster.DisposeCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public void Broadcaster_property_comes_from_the_context()
    {
        var broadcaster = new TrackingBroadcaster();
        var context = Context(broadcaster);

        var workspace = new DiagramWorkspace(context);

        workspace.Broadcaster.Should().BeSameAs(broadcaster);
        workspace.Broadcaster.Should().BeSameAs(context.Broadcaster);
        workspace.Document.Should().BeSameAs(context.Document);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public async Task Two_workspaces_can_share_one_broadcaster()
    {
        var broadcaster = new InProcessBroadcaster();
        var left = new DiagramWorkspace(Context(broadcaster));
        var right = new DiagramWorkspace(Context(broadcaster));

        using var seen = new ManualResetEventSlim(false);
        ChangeNotification? observed = null;

        using var subscription = broadcaster.Subscribe(notification =>
        {
            observed = notification;
            seen.Set();
        });

        left.CommandBus.Execute(new AddNodeCommand(new NodeDef { Id = "a" })
            .WithContext(ChangeContext.For(ChangeSource.Human)));

        seen.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).Should().BeTrue();
        observed.Should().NotBeNull();

        var notification = observed!;
        notification.Source.Should().Be(ChangeSource.Human);
        notification.Version.Should().Be(1);
        notification.DocumentId.Should().Be(left.Document.Id);

        // 两边都释放后，共享的 broadcaster 仍然可用（所有权在宿主手里）。
        await left.DisposeAsync();
        await right.DisposeAsync();

        broadcaster.Enqueue(new ChangeNotification
        {
            DocumentId = "after-dispose",
            Version = 1,
            Source = ChangeSource.System,
            Timestamp = DateTimeOffset.UnixEpoch,
        });

        broadcaster.SubscriberCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Workspace")]
    public async Task CreateOwned_builds_a_working_single_window_workspace()
    {
        var document = new DiagramDocument("owned", DiagramKind.Flowchart, Direction.TB);
        await using var workspace = DiagramWorkspace.CreateOwned(
            document,
            new SimpleSessionProvider("tester", SessionIds.Gui("w1")),
            DiagramCommandBusOptions.ForGui());

        workspace.CommandBus.Execute(new AddNodeCommand(new NodeDef { Id = "a" })
            .WithContext(ChangeContext.For(ChangeSource.Human)));

        workspace.Document.Nodes.Should().HaveCount(1);
        workspace.Document.Version.Should().Be(1);
    }

    private static DiagramCommandBusContext Context(IChangeBroadcaster broadcaster) =>
        DiagramCommandBusContext.Create(
            new DiagramDocument("workspace-doc"),
            new SimpleSessionProvider("tester", SessionIds.Gui("w1")),
            broadcaster,
            DiagramCommandBusOptions.ForGui());
}
