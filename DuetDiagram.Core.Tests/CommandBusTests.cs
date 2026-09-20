using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>P1 判据 #3 / #4：撤销重做与版本一致。</summary>
public sealed class CommandBusTests
{
    [Fact]
    [Trait("Category", "UndoRedoVersion")]
    public void Execute_advances_version_and_records_everything()
    {
        using var harness = new Harness();
        harness.AddNode("a", "A", source: ChangeSource.Llm);

        harness.Document.Version.Should().Be(1);
        harness.Context.History.UndoCount.Should().Be(1);
        harness.Context.History.RedoCount.Should().Be(0);
        harness.Context.VersionLog.Count.Should().Be(1);
        harness.Context.VersionLog.LatestVersion.Should().Be(1);
        harness.Context.VersionLog.Snapshot()[0].Source.Should().Be(ChangeSource.Llm);
        harness.Context.AuditLog.All().Should().ContainSingle(e => e.Kind == AuditKind.Executed);
        harness.Document.StructuralHash.Should().NotBeEmpty();
        harness.Document.VisualHash.Should().NotBeEmpty();
    }

    [Fact]
    [Trait("Category", "UndoRedoVersion")]
    public void Timestamp_is_filled_by_the_bus_from_the_clock()
    {
        using var harness = new Harness();
        var expected = harness.Clock.UtcNow;

        harness.AddNode("a");

        harness.Context.VersionLog.Snapshot()[0].Timestamp.Should().Be(expected);
        harness.Context.AuditLog.All()[0].Timestamp.Should().Be(expected);
    }

    [Fact]
    [Trait("Category", "UndoRedoVersion")]
    public void Undo_restores_document_and_advances_version()
    {
        using var harness = new Harness();
        harness.AddNode("a", "A");

        var result = harness.Bus.Undo();

        result.IsEffectiveSuccess.Should().BeTrue();
        harness.Document.Nodes.Should().BeEmpty();
        harness.Document.Version.Should().Be(2);
        harness.Context.History.UndoCount.Should().Be(0);
        harness.Context.History.RedoCount.Should().Be(1);
        harness.Context.AuditLog.All()[^1].Kind.Should().Be(AuditKind.Undone);
        harness.Context.AuditLog.All()[^1].Reason.Should().Be("undo of add-node");
        harness.Context.VersionLog.Snapshot()[^1].Source.Should().Be(ChangeSource.Undo);
    }

    [Fact]
    [Trait("Category", "UndoRedoVersion")]
    public void Redo_replays_the_command_after_an_undo()
    {
        using var harness = new Harness();
        harness.AddNode("a", "A");
        harness.Bus.Undo();

        var result = harness.Bus.Redo();

        result.IsEffectiveSuccess.Should().BeTrue();
        harness.Document.Nodes.Should().HaveCount(1);
        harness.Document.Nodes[0].Label.Should().Be("A");
        harness.Document.Version.Should().Be(3);
        harness.Context.History.UndoCount.Should().Be(1);
        harness.Context.History.RedoCount.Should().Be(0);
        harness.Context.VersionLog.Snapshot()[^1].Source.Should().Be(ChangeSource.Redo);
        harness.Context.AuditLog.All()[^1].Kind.Should().Be(AuditKind.Redone);
    }

    [Fact]
    [Trait("Category", "UndoRedoVersion")]
    public void Redo_is_deterministic_across_a_full_cycle()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.Connect("e1", "a", "b", "是");

        var expectedNodes = harness.Document.Nodes;
        var expectedEdges = harness.Document.Edges;
        var expectedStructuralHash = harness.Document.StructuralHash;
        var expectedVisualHash = harness.Document.VisualHash;

        harness.Bus.Undo();
        harness.Bus.Undo();
        harness.Bus.Undo();
        harness.Document.Nodes.Should().BeEmpty();

        harness.Bus.Redo();
        harness.Bus.Redo();
        harness.Bus.Redo();

        harness.Document.Nodes.Should().Equal(expectedNodes);
        harness.Document.Edges.Should().Equal(expectedEdges);
        harness.Document.StructuralHash.Should().Be(expectedStructuralHash);
        harness.Document.VisualHash.Should().Be(expectedVisualHash);
    }

    [Fact]
    [Trait("Category", "UndoRedoVersion")]
    public void New_command_clears_the_redo_stack()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.Bus.Undo();
        harness.Context.History.RedoCount.Should().Be(1);

        harness.AddNode("b");

        harness.Context.History.RedoCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "UndoRedoVersion")]
    public void Undo_and_redo_on_empty_history_are_NoOp()
    {
        using var harness = new Harness();

        var undo = harness.Bus.Undo();
        undo.IsSuccess.Should().BeTrue();
        undo.IsNoOp.Should().BeTrue();
        undo.Message.Should().Be("无可撤销操作");

        var redo = harness.Bus.Redo();
        redo.IsSuccess.Should().BeTrue();
        redo.IsNoOp.Should().BeTrue();
        redo.Message.Should().Be("无可重做操作");

        harness.Document.Version.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "UndoRedoVersion")]
    public void Undo_of_remove_node_restores_edges_in_original_order()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AddNode("c");
        harness.Connect("e1", "a", "b");
        harness.Connect("e2", "b", "c");
        harness.Connect("e3", "a", "c");

        var hashBefore = harness.Document.StructuralHash;
        var visualBefore = harness.Document.VisualHash;

        harness.Bus.Execute(new RemoveNodeCommand("b").WithContext(ChangeContext.For(ChangeSource.Human)))
            .IsEffectiveSuccess.Should().BeTrue();

        harness.Document.Nodes.Select(n => n.Id).Should().Equal("a", "c");
        harness.Document.Edges.Select(e => e.Id).Should().Equal("e3");

        harness.Bus.Undo().IsEffectiveSuccess.Should().BeTrue();

        harness.Document.Nodes.Select(n => n.Id).Should().Equal("a", "b", "c");
        harness.Document.Edges.Select(e => e.Id).Should().Equal("e1", "e2", "e3");
        harness.Document.StructuralHash.Should().Be(hashBefore);
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    [Fact]
    [Trait("Category", "UndoRedoVersion")]
    public void Structural_hash_ignores_labels_but_visual_hash_does_not()
    {
        using var left = new Harness();
        left.AddNode("n1", "第一");
        left.AddNode("n2", "第二");
        left.Connect("e1", "n1", "n2");

        using var right = new Harness();
        right.AddNode("n1", "壹");
        right.AddNode("n2", "贰");
        right.Connect("e1", "n1", "n2");

        // 同样的连接关系 → 不需要重布局；不同的标签 → 需要重绘。
        left.Document.StructuralHash.Should().Be(right.Document.StructuralHash);
        left.Document.VisualHash.Should().NotBe(right.Document.VisualHash);
    }

    [Fact]
    [Trait("Category", "McpMode")]
    public void Mcp_mode_requires_a_version_check_request()
    {
        using var harness = new Harness(DiagramCommandBusOptions.ForMcp());

        var result = harness.Bus.Execute(new AddNodeCommand(new NodeDef { Id = "a" })
            .WithContext(ChangeContext.For(ChangeSource.Mcp)));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.ExpectedVersionRequired);
        harness.Document.Nodes.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "McpMode")]
    public void Mcp_mode_accepts_a_matching_version_and_rejects_a_stale_one()
    {
        using var harness = new Harness(DiagramCommandBusOptions.ForMcp());

        var accepted = harness.Bus.Execute(
            new AddNodeCommand(new NodeDef { Id = "a" }).WithContext(ChangeContext.For(ChangeSource.Mcp)),
            new VersionCheckRequest { ClientVersion = 0 });

        accepted.IsEffectiveSuccess.Should().BeTrue();
        harness.Document.Version.Should().Be(1);

        var stale = harness.Bus.Execute(
            new AddNodeCommand(new NodeDef { Id = "b" }).WithContext(ChangeContext.For(ChangeSource.Mcp)),
            new VersionCheckRequest { ClientVersion = 0 });

        stale.IsSuccess.Should().BeFalse();
        stale.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.VersionConflict);
        stale.IsRetryable.Should().BeTrue();
        harness.Document.Nodes.Select(n => n.Id).Should().Equal("a");
    }

    [Fact]
    [Trait("Category", "McpMode")]
    public void Mcp_mode_refuses_a_null_broadcaster()
    {
        var context = DiagramCommandBusContext.Create(
            new DiagramDocument("d"),
            new SimpleSessionProvider(),
            Broadcasting.NullChangeBroadcaster.Instance,
            DiagramCommandBusOptions.ForMcp());

        var act = () => new DiagramCommandBus(context);

        act.Should().Throw<ArgumentException>().WithMessage("*real broadcaster*");
    }

    [Fact]
    [Trait("Category", "McpMode")]
    public void Gui_mode_accepts_a_null_broadcaster()
    {
        var context = DiagramCommandBusContext.Create(
            new DiagramDocument("d"),
            new SimpleSessionProvider(),
            Broadcasting.NullChangeBroadcaster.Instance,
            DiagramCommandBusOptions.ForGui());

        using var bus = new DiagramCommandBus(context);

        bus.Execute(new AddNodeCommand(new NodeDef { Id = "a" }).WithContext(ChangeContext.For(ChangeSource.Human)))
            .IsEffectiveSuccess.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "McpMode")]
    public void Mcp_mode_never_records_a_rejected_or_conflicting_write()
    {
        using var harness = new Harness(DiagramCommandBusOptions.ForMcp());

        harness.Bus.Execute(
            new AddNodeCommand(new NodeDef { Id = "a" }).WithContext(ChangeContext.For(ChangeSource.Mcp)),
            new VersionCheckRequest { ClientVersion = 0 });

        var versionBefore = harness.Document.Version;
        var historyBefore = harness.Context.History.UndoCount;

        harness.Bus.Execute(
            new AddNodeCommand(new NodeDef { Id = "b" }).WithContext(ChangeContext.For(ChangeSource.Mcp)),
            new VersionCheckRequest { ClientVersion = 0 });

        harness.Document.Version.Should().Be(versionBefore);
        harness.Context.History.UndoCount.Should().Be(historyBefore);
        harness.Context.VersionLog.Count.Should().Be(versionBefore);
    }
}
