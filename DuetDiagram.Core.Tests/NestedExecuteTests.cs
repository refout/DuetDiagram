using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// P1 判据 #10 / #11 / #33：嵌套 Execute 检测与异步并发。
/// </summary>
public sealed class NestedExecuteTests
{
    [Fact]
    [Trait("Category", "NestedExecute")]
    public void Nested_execute_on_the_same_thread_is_rejected()
    {
        using var harness = new Harness();

        var act = () => harness.Bus.Execute(
            new NestedExecuteCommand(harness.Bus, new NodeDef { Id = "inner" }, crossThread: false));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Nested Execute is not allowed*");
        harness.Document.Nodes.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "NestedExecuteCrossThread")]
    public void Nested_execute_from_task_run_is_still_rejected()
    {
        using var harness = new Harness();

        // AsyncLocal 会流入 Task.Run —— 这是**有意**的保守行为：
        // 宁可误报，也不允许命令在命令内部重入（AGENTS.md 约定 7）。
        var act = () => harness.Bus.Execute(
            new NestedExecuteCommand(harness.Bus, new NodeDef { Id = "inner" }, crossThread: true));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Nested Execute is not allowed*");
        harness.Document.Nodes.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "NestedExecute")]
    public void Depth_is_released_even_when_a_command_throws()
    {
        using var harness = new Harness();

        var act = () => harness.Bus.Execute(new ThrowingCommand());
        act.Should().Throw<InvalidOperationException>();

        // 门锁与深度都必须已经释放，否则后续所有命令都会失败。
        harness.AddNode("a").IsEffectiveSuccess.Should().BeTrue();
        harness.Bus.Undo().IsEffectiveSuccess.Should().BeTrue();
        harness.Bus.Redo().IsEffectiveSuccess.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "NestedExecute")]
    public async Task Execute_async_runs_and_advances_the_version()
    {
        using var harness = new Harness();

        var result = await harness.Bus.ExecuteAsync(
            new AddNodeCommand(new NodeDef { Id = "a" }).WithContext(ChangeContext.For(ChangeSource.Llm)),
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsEffectiveSuccess.Should().BeTrue();
        harness.Document.Version.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "NestedExecute")]
    public async Task Concurrent_async_commands_serialise_without_losing_a_version()
    {
        // 200 条命令同时抢门锁：版本递增、哈希更新、历史入栈必须在锁内串行完成。
        using var harness = new Harness();

        var tasks = Enumerable.Range(0, 200).Select(i => Task.Run(async () =>
            await harness.Bus.ExecuteAsync(
                new AddNodeCommand(new NodeDef { Id = $"n{i}" }).WithContext(ChangeContext.For(ChangeSource.Mcp))),
            TestContext.Current.CancellationToken)).ToArray();

        var results = await Task.WhenAll(tasks);

        results.Should().NotContain(r => !r.IsSuccess);
        harness.Document.Nodes.Should().HaveCount(200);
        harness.Document.Version.Should().Be(200);
        harness.Context.History.UndoCount.Should().Be(200);
        harness.Context.VersionLog.Count.Should().Be(100);
    }

    [Fact]
    [Trait("Category", "NestedExecute")]
    public async Task Cancellation_while_waiting_for_the_gate_is_observed()
    {
        using var harness = new Harness();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await harness.Bus.ExecuteAsync(
            new AddNodeCommand(new NodeDef { Id = "a" }).WithContext(ChangeContext.For(ChangeSource.Llm)),
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        harness.Document.Nodes.Should().BeEmpty();
        harness.Document.Version.Should().Be(0);
    }
}
