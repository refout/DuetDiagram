using DuetDiagram.Core.History;
using DuetDiagram.Core.Logging;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 长时间反复撤销重做之后，状态仍然一致、占用仍然有上限。
/// </summary>
/// <remarks>
/// 这类测试的意义不在于"跑一千次能不能通过"，而在于验证容量上限生效之后
/// 各种计数是否还自洽。上限生效意味着"部分操作会失败"，
/// 而失败的那部分很容易让计数器与实际状态脱节。
/// </remarks>
public sealed class UndoStressTests
{
    [Fact]
    [Trait("Category", "UndoStress")]
    public void A_thousand_undo_calls_stay_consistent()
    {
        using var harness = new Harness();

        const int commands = 600;

        for (var i = 0; i < commands; i++)
        {
            harness.AddNode($"n{i}").IsEffectiveSuccess.Should().BeTrue();
        }

        // 命令数超过了撤销栈容量，所以最早的记录已经被挤掉。
        harness.Context.History.UndoCount.Should().Be(HistoryStack.Capacity);
        harness.Document.Version.Should().Be(commands);

        var effective = 0;

        for (var i = 0; i < 1000; i++)
        {
            if (harness.Bus.Undo().IsEffectiveSuccess)
            {
                effective++;
            }
        }

        // 只有容量那么多条真的被撤销，其余都返回"无可撤销"。
        effective.Should().Be(HistoryStack.Capacity);

        // 三个计数必须互相自洽：剩下的节点数、版本推进量、两个栈的大小。
        harness.Document.Nodes.Should().HaveCount(commands - HistoryStack.Capacity);
        harness.Document.Version.Should().Be(commands + HistoryStack.Capacity);
        harness.Context.History.UndoCount.Should().Be(0);
        harness.Context.History.RedoCount.Should().Be(HistoryStack.Capacity);

        // 两份日志都是环形缓冲：跑再多命令，占用都不增长。
        harness.Context.VersionLog.Count.Should().Be(VersionLogLimits.MaxEntries);
        harness.Context.AuditLog.Count.Should().Be(AuditLog.Capacity);
    }

    [Fact]
    [Trait("Category", "UndoStress")]
    public void Repeated_undo_redo_cycles_do_not_drift()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.Connect("e1", "a", "b");

        var structural = harness.Document.StructuralHash;
        var visual = harness.Document.VisualHash;

        // 每次重做都会重新捕获快照。如果快照捕获得不对，
        // 误差会随着循环次数累积，最终表现为内容与最初不一致。
        for (var i = 0; i < 100; i++)
        {
            harness.Bus.Undo().IsEffectiveSuccess.Should().BeTrue();
            harness.Bus.Redo().IsEffectiveSuccess.Should().BeTrue();
        }

        harness.Document.StructuralHash.Should().Be(structural);
        harness.Document.VisualHash.Should().Be(visual);

        // 每次撤销重做各推进一个版本号，总共两百次。
        harness.Document.Version.Should().Be(3 + 200);
        harness.Context.History.UndoCount.Should().Be(3);
    }

    [Fact]
    [Trait("Category", "UndoStress")]
    public void Undoing_a_full_history_empties_the_document()
    {
        using var harness = new Harness();

        for (var i = 0; i < 100; i++)
        {
            harness.AddNode($"n{i}");
        }

        for (var i = 0; i < 99; i++)
        {
            harness.Connect($"e{i}", $"n{i}", $"n{i + 1}");
        }

        // 一百个节点加九十九条边，全部撤销之后应当回到完全空白。
        for (var i = 0; i < 199; i++)
        {
            harness.Bus.Undo().IsEffectiveSuccess.Should().BeTrue();
        }

        harness.Document.Nodes.Should().BeEmpty();
        harness.Document.Edges.Should().BeEmpty();
        harness.Document.Version.Should().Be(199 + 199);
    }
}
