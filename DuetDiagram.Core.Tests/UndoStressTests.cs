using DuetDiagram.Core.History;
using DuetDiagram.Core.Logging;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>P1 判据 #3 / #4：长时间撤销重做不泄漏、不漂移。</summary>
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

        effective.Should().Be(HistoryStack.Capacity);
        harness.Document.Nodes.Should().HaveCount(commands - HistoryStack.Capacity);
        harness.Document.Version.Should().Be(commands + HistoryStack.Capacity);
        harness.Context.History.UndoCount.Should().Be(0);
        harness.Context.History.RedoCount.Should().Be(HistoryStack.Capacity);

        // 版本日志是环形缓冲：无论跑多少命令，占用都不增长。
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

        for (var i = 0; i < 100; i++)
        {
            harness.Bus.Undo().IsEffectiveSuccess.Should().BeTrue();
            harness.Bus.Redo().IsEffectiveSuccess.Should().BeTrue();
        }

        harness.Document.StructuralHash.Should().Be(structural);
        harness.Document.VisualHash.Should().Be(visual);
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

        for (var i = 0; i < 199; i++)
        {
            harness.Bus.Undo().IsEffectiveSuccess.Should().BeTrue();
        }

        harness.Document.Nodes.Should().BeEmpty();
        harness.Document.Edges.Should().BeEmpty();
        harness.Document.Version.Should().Be(199 + 199);
    }
}
