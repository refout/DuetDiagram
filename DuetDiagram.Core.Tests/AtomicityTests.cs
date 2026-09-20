using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 失败必须整体回滚。
/// </summary>
/// <remarks>
/// <para>
/// 这里断言的是命令总线的兜底能力，而不是"命令实现写得很小心"。
/// 两者都要有，但只有后者的话，任何一个命令作者的疏漏都会变成一次静默的数据损坏。
/// </para>
/// <para>
/// 判断"有没有被改脏"统一用完整序列化结果做比较。
/// 只检查集合元素个数是不够的——顺序、标签、哈希字段都可能被改动而数量不变。
/// </para>
/// </remarks>
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

        // 两种失败方式都必须回滚：返回失败结果，和直接把异常抛出来。
        if (throwAfterPartialWrite)
        {
            act.Should().Throw<InvalidOperationException>();
        }
        else
        {
            act().IsSuccess.Should().BeFalse();
        }

        harness.Snapshot().Should().Be(before);
        harness.Document.Version.Should().Be(versionBefore);
        harness.Document.StructuralHash.Should().Be(hashBefore);
        harness.Document.Nodes.Should().HaveCount(1);

        // 失败的尝试不能留下任何痕迹：版本日志、撤销栈、重做栈都不该变。
        harness.Context.History.UndoCount.Should().Be(1);
        harness.Context.History.RedoCount.Should().Be(0);
        harness.Context.VersionLog.Count.Should().Be(1);

        var audit = harness.Context.AuditLog.All();
        audit.Should().ContainSingle(e => e.Kind == AuditKind.Failed);
        audit[^1].Errors.Should().OnlyContain(e => e.Code == ErrorCodes.InternalError);

        if (throwAfterPartialWrite)
        {
            // 异常那条路径看不到异常对象，所以记录里不该有任何细节；只有命令自己主动返回
            // 失败时才允许带说明。这条断言防止将来有人顺手把异常类型名写进审计记录。
            audit[^1].Errors!.Single().Payload.Should().BeNull();
        }
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Throwing_command_rolls_back_to_identical_document()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        // 自环是合法结构，用它覆盖"边引用自身节点"这个容易被校验误伤的边界。
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

        harness.Bus.Execute(new AddNodeCommand(new NodeDef { Id = "a" }).WithContext(context))
            .Errors.Should().ContainSingle(e => e.Code == ErrorCodes.DuplicateId);

        harness.Bus.Execute(new RemoveNodeCommand("ghost").WithContext(context))
            .Errors.Should().ContainSingle(e => e.Code == ErrorCodes.NodeMissing);

        harness.Bus.Execute(new ConnectEdgeCommand(new EdgeDef { Id = "e1", From = "a", To = "ghost" }).WithContext(context))
            .Errors.Should().ContainSingle(e => e.Code == ErrorCodes.EdgeTargetMissing);

        harness.Bus.Execute(new ConnectEdgeCommand(new EdgeDef { Id = "e2", From = "ghost", To = "a" }).WithContext(context))
            .Errors.Should().ContainSingle(e => e.Code == ErrorCodes.EdgeSourceMissing);

        harness.Snapshot().Should().Be(before);
        harness.Document.Version.Should().Be(1);
        harness.Context.VersionLog.Count.Should().Be(1);

        // 被拒绝的尝试仍然要留审计痕迹，否则"为什么我的操作没生效"将无从追查。
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

        // 两个端点都缺失时要一次报全。只报一个会让调用方改一处试一次，来回好几轮。
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

        // 内层被拦住之后，外层命令也失败了，所以外层同样不能留下痕迹。
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

        // 这个组合很关键：算成功，但不算"有效成功"。
        // 界面提示要用前者（不弹错误），触发副作用要用后者（不刷新、不入栈）。
        result.IsEffectiveSuccess.Should().BeFalse();

        harness.Document.Version.Should().Be(versionBefore);
        harness.Context.History.UndoCount.Should().Be(1);
        harness.Context.VersionLog.Count.Should().Be(1);
    }
}
