using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// P1 判据 #6 / #7：每个命令必须原子。
/// 起点是「命令总线在失败时必须恢复文档」这一条，而不是「命令写得小心」。
/// </summary>
public sealed class AtomicityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Atomicity")]
    public void Partial_write_is_rolled_back(bool throwAfterPartialWrite)
    {
        using var harness = new Harness();
        harness.AddNode("a", "A");

        var before = harness.Snapshot();
        var versionBefore = harness.Document.Version;
        var hashBefore = harness.Document.StructuralHash;

        var act = () => harness.Bus.Execute(
            new PartialWriteCommand(new NodeDef { Id = "b" }, throwAfterPartialWrite)
                .WithContext(ChangeContext.For(ChangeSource.Llm, "llm-1")));

        if (throwAfterPartialWrite)
        {
            act.Should().Throw<InvalidOperationException>();
        }
        else
        {
            act().IsSuccess.Should().BeFalse();
        }

        // 文档必须逐字节回到调用前
        harness.Snapshot().Should().Be(before);
        harness.Document.Version.Should().Be(versionBefore);
        harness.Document.StructuralHash.Should().Be(hashBefore);
        harness.Document.Nodes.Should().HaveCount(1);

        // 历史与版本日志都不得记录失败的操作
        harness.Context.History.UndoCount.Should().Be(1);
        harness.Context.History.RedoCount.Should().Be(0);
        harness.Context.VersionLog.Count.Should().Be(1);

        var audit = harness.Context.AuditLog.All();
        audit.Should().ContainSingle(e => e.Kind == AuditKind.Failed);
        audit[^1].Errors.Should().OnlyContain(e => e.Code == ErrorCodes.InternalError);

        if (throwAfterPartialWrite)
        {
            // AGENTS.md 约定 9：异常类型名只写应用日志，绝不进 AuditLog 的 Payload。
            audit[^1].Errors!.Single().Payload.Should().BeNull();
        }
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Throwing_command_rolls_back_to_identical_document()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.Connect("e1", "a", "a");

        var before = harness.Snapshot();

        var act = () => harness.Bus.Execute(new ThrowingCommand().WithContext(ChangeContext.For(ChangeSource.Mcp)));

        act.Should().Throw<InvalidOperationException>();
        harness.Snapshot().Should().Be(before);
        harness.Context.History.UndoCount.Should().Be(2);
        harness.Context.AuditLog.All()[^1].Kind.Should().Be(AuditKind.Failed);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Rejected_commands_never_touch_the_document()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var before = harness.Snapshot();
        var context = ChangeContext.For(ChangeSource.Human, "tester");

        // DUPLICATE_ID
        harness.Bus.Execute(new AddNodeCommand(new NodeDef { Id = "a" }).WithContext(context))
            .Errors.Should().ContainSingle(e => e.Code == ErrorCodes.DuplicateId);

        // NODE_MISSING
        harness.Bus.Execute(new RemoveNodeCommand("ghost").WithContext(context))
            .Errors.Should().ContainSingle(e => e.Code == ErrorCodes.NodeMissing);

        // EDGE_TARGET_MISSING
        harness.Bus.Execute(new ConnectEdgeCommand(new EdgeDef { Id = "e1", From = "a", To = "ghost" }).WithContext(context))
            .Errors.Should().ContainSingle(e => e.Code == ErrorCodes.EdgeTargetMissing);

        // EDGE_SOURCE_MISSING 与 DUPLICATE_ID 可以同时命中，所以用一个不冲突的 id 单独断言
        harness.Bus.Execute(new ConnectEdgeCommand(new EdgeDef { Id = "e2", From = "ghost", To = "a" }).WithContext(context))
            .Errors.Should().ContainSingle(e => e.Code == ErrorCodes.EdgeSourceMissing);

        harness.Snapshot().Should().Be(before);
        harness.Document.Version.Should().Be(1);
        harness.Context.VersionLog.Count.Should().Be(1);
        harness.Context.AuditLog.All().Count(e => e.Kind == AuditKind.Rejected).Should().Be(4);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Connect_edge_reports_all_precondition_failures_at_once()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var result = harness.Bus.Execute(
            new ConnectEdgeCommand(new EdgeDef { Id = "e1", From = "ghost-src", To = "ghost-dst" })
                .WithContext(ChangeContext.For(ChangeSource.Llm)));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Select(e => e.Code)
            .Should().BeEquivalentTo([ErrorCodes.EdgeSourceMissing, ErrorCodes.EdgeTargetMissing]);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Nested_execute_rolls_back_the_outer_command()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var before = harness.Snapshot();

        var act = () => harness.Bus.Execute(
            new NestedExecuteCommand(harness.Bus, new NodeDef { Id = "inner" }, crossThread: false)
                .WithContext(ChangeContext.For(ChangeSource.Llm)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Nested Execute is not allowed*");

        harness.Snapshot().Should().Be(before);
        harness.Document.Nodes.Should().HaveCount(1);
        harness.Context.AuditLog.All()[^1].Kind.Should().Be(AuditKind.Failed);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void NoOp_does_not_advance_version_history_or_audit_executed()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var versionBefore = harness.Document.Version;
        var result = harness.Bus.Execute(new AlwaysNoOpCommand().WithContext(ChangeContext.For(ChangeSource.System)));

        result.IsSuccess.Should().BeTrue();
        result.IsNoOp.Should().BeTrue();
        result.IsEffectiveSuccess.Should().BeFalse();

        harness.Document.Version.Should().Be(versionBefore);
        harness.Context.History.UndoCount.Should().Be(1);
        harness.Context.VersionLog.Count.Should().Be(1);
    }
}
