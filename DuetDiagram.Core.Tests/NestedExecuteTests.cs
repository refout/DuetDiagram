using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 命令内部不得再发起命令，以及并发执行时的串行化保证。
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

        // 执行上下文局部变量会沿着任务边界自动传递，所以另起线程同样会被判定为嵌套。
        // 这是有意的保守策略：命令内部重入几乎没有正当理由，
        // 而漏检会让门锁自我等待、整个进程卡死，代价远大于偶尔误报。
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

        // 门锁与深度必须在异常路径上同样被释放。漏掉任何一个，之后所有命令都会永久失败。
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
        // 200 条命令同时抢门锁。版本递增、哈希重算、历史入栈必须在锁内串行完成，
        // 任何一处漏在锁外都会表现为版本号与实际内容数量对不上。
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

        // 版本日志有容量上限，跑再多命令占用也不会增长。
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

        // 取消发生在拿锁之前，所以命令根本没被执行，文档必须完全没有被碰过。
        harness.Document.Nodes.Should().BeEmpty();
        harness.Document.Version.Should().Be(0);
    }
}
