using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 执行、撤销、重做三条路径的版本一致性与记账行为。
/// </summary>
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

        // 命令对象在这里构造，时间戳取的是执行那一刻的时钟值。
        // 测试里两者相同，因为时钟是手动的、不会自己走。
        harness.AddNode("a");

        harness.Context.VersionLog.Snapshot()[0].Timestamp.Should().Be(expected);
        harness.Context.AuditLog.All()[0].Timestamp.Should().Be(expected);
    }

    [Fact]
    [Trait("Category", "UndoRedoVersion")]
    public void Timestamp_ignores_whatever_the_caller_declared()
    {
        using var harness = new Harness();
        var expected = harness.Clock.UtcNow;

        // 调用方故意声明一个离谱的时间戳，总线必须覆盖掉它。
        // 允许调用方决定时间戳意味着审计日志记录的是"构造时刻"而不是"生效时刻"，
        // 在批量构造、延迟提交的场景下这个差别很大。
        harness.Bus.Execute(new AddNodeCommand(new NodeDef { Id = "a" })
            .WithContext(ChangeContext.For(ChangeSource.Human, "tester") with
            {
                Timestamp = DateTimeOffset.UnixEpoch,
            }));

        harness.Context.VersionLog.Snapshot()[0].Timestamp.Should().Be(expected);
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

        // 撤销也占一个版本号：它是一个新状态，必须能被对端从版本区间里推算出来。
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

        // 重做必须把内容还原得一模一样，包括顺序和两个哈希。
        // 哈希对不上说明两次算出来的内容有差异，即使肉眼看起来相同。
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

        // 新变更让"接下来会发生什么"的假设失效，继续重做会把文档带到没人预期的状态。
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

        // 空操作没有改变任何东西，版本号必须原地不动。
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

        // 删掉 b 之后，挂在它身上的两条边也必须一起消失。
        harness.Document.Nodes.Select(n => n.Id).Should().Equal("a", "c");
        harness.Document.Edges.Select(e => e.Id).Should().Equal("e3");

        harness.Bus.Undo().IsEffectiveSuccess.Should().BeTrue();

        // 还原后的顺序必须与删除前一致：节点按原位插回，三条边也按原索引插回。
        harness.Document.Nodes.Select(n => n.Id).Should().Equal("a", "b", "c");
        harness.Document.Edges.Select(e => e.Id).Should().Equal("e1", "e2", "e3");

        // 两个哈希回到删除前的值，说明内容是真的完整还原了，而不是"看起来差不多"。
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

        // 连接关系相同、只是文字不同：结构哈希必须相同（不需要重布局），
        // 视觉哈希必须不同（需要重绘）。这一条直接决定了改文字会不会触发全图重排。
        left.Document.StructuralHash.Should().Be(right.Document.StructuralHash);
        left.Document.VisualHash.Should().NotBe(right.Document.VisualHash);
    }

    [Fact]
    [Trait("Category", "McpMode")]
    public void Version_checked_mode_requires_a_version_declaration()
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
    public void Version_checked_mode_accepts_a_matching_version_and_rejects_a_stale_one()
    {
        using var harness = new Harness(DiagramCommandBusOptions.ForMcp());

        var accepted = harness.Bus.Execute(
            new AddNodeCommand(new NodeDef { Id = "a" }).WithContext(ChangeContext.For(ChangeSource.Mcp)),
            new VersionCheckRequest { ClientVersion = 0 });

        accepted.IsEffectiveSuccess.Should().BeTrue();
        harness.Document.Version.Should().Be(1);

        // 调用方仍停留在版本 0，而文档已经是 1，这次写入必须被拒绝而不是静默覆盖。
        var stale = harness.Bus.Execute(
            new AddNodeCommand(new NodeDef { Id = "b" }).WithContext(ChangeContext.For(ChangeSource.Mcp)),
            new VersionCheckRequest { ClientVersion = 0 });

        stale.IsSuccess.Should().BeFalse();
        stale.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.VersionConflict);

        // 版本冲突是可以重试的：同步到最新版本之后再发一次就能成功。
        stale.IsRetryable.Should().BeTrue();
        harness.Document.Nodes.Select(n => n.Id).Should().Equal("a");
    }

    [Fact]
    [Trait("Category", "McpMode")]
    public void Version_checked_mode_refuses_a_no_op_broadcaster()
    {
        var context = DiagramCommandBusContext.Create(
            new DiagramDocument("d"),
            new SimpleSessionProvider(),
            Broadcasting.NullChangeBroadcaster.Instance,
            DiagramCommandBusOptions.ForMcp());

        var act = () => new DiagramCommandBus(context);

        // 判据是"是不是那个空实现类型"而不是"是不是空引用"。空实现永远不是空引用，
        // 只判空引用等于这道防线不存在，后果是另一个进程永远收不到变更且毫无提示。
        act.Should().Throw<ArgumentException>().WithMessage("*real broadcaster*");
    }

    [Fact]
    [Trait("Category", "McpMode")]
    public void Single_process_mode_accepts_a_no_op_broadcaster()
    {
        var context = DiagramCommandBusContext.Create(
            new DiagramDocument("d"),
            new SimpleSessionProvider(),
            Broadcasting.NullChangeBroadcaster.Instance,
            DiagramCommandBusOptions.ForGui());

        using var bus = new DiagramCommandBus(context);

        // 单进程场景没有订阅者，用空实现是合理选择，必须允许。
        bus.Execute(new AddNodeCommand(new NodeDef { Id = "a" }).WithContext(ChangeContext.For(ChangeSource.Human)))
            .IsEffectiveSuccess.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "McpMode")]
    public void Version_checked_mode_never_records_a_conflicting_write()
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

        // 冲突的写入必须完全不留痕迹，否则对端同步回来会看到一段自己没做过的变更。
        harness.Document.Version.Should().Be(versionBefore);
        harness.Context.History.UndoCount.Should().Be(historyBefore);
        harness.Context.VersionLog.Count.Should().Be(versionBefore);
    }
}
