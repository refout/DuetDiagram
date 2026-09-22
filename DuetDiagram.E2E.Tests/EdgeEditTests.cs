using DuetDiagram.App;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Model;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 边的编辑：端点重连走一条命令、边标签编辑走一条命令、折点增删各算一条人工操作（撤销重做一步退一步）。
/// </summary>
/// <remarks>
/// <para>
/// 端点与标签改的是 IR，所以"发一条命令"由总线历史条目数恰好加一来保证；
/// 折点改的是人工产物（<see cref="UserSidecar.PinnedEdges"/>），不进 IR、不走命令总线，
/// 与节点固定同构——它走自己的快照栈，每一次增删算一步。
/// </para>
/// <para>
/// 把折点拆成"删一段再补一段"会变成两步撤销，而用户眼里那一次拖拽是一下子的事，
/// 所以这里断言一次增删只往折点撤销栈压一条。
/// </para>
/// </remarks>
public sealed class EdgeEditTests
{
    /// <summary>重连端点是一条命令：边改接到新目标，历史条目加一。</summary>
    [Fact]
    [Trait("Category", "EdgeEdit")]
    public async Task Reconnecting_an_endpoint_emits_one_command()
    {
        await HeadlessFixture.Run(() =>
        {
            using var session = new DiagramSession(SampleDiagram.Document());
            var before = session.Bus.Context.History.UndoEntries().Count;

            var result = session.ReconnectEdge("e1", "start", null, "pass", null);

            result.IsEffectiveSuccess.Should().BeTrue();
            session.Bus.Context.History.UndoEntries().Count.Should().Be(before + 1, "重连只发一条命令");
            session.Document.Edges.Should().ContainSingle(e => e.Id == "e1" && e.To == "pass");
        });
    }

    /// <summary>编辑边标签是一条命令：标签变了，历史条目加一。</summary>
    [Fact]
    [Trait("Category", "EdgeEdit")]
    public async Task Editing_an_edge_label_emits_one_command()
    {
        await HeadlessFixture.Run(() =>
        {
            using var session = new DiagramSession(SampleDiagram.Document());
            var before = session.Bus.Context.History.UndoEntries().Count;

            var result = session.ApplyEdgeField("e2", "label", "通过");

            result.IsEffectiveSuccess.Should().BeTrue();
            session.Bus.Context.History.UndoEntries().Count.Should().Be(before + 1, "改标签只发一条命令");
            session.Document.Edges.Should().ContainSingle(e => e.Id == "e2" && e.Label == "通过");
        });
    }

    /// <summary>加一条折点是一次操作：进折点撤销栈一步，不碰命令总线。</summary>
    [Fact]
    [Trait("Category", "EdgeEdit")]
    public async Task Adding_a_bend_is_one_undoable_operation()
    {
        await HeadlessFixture.Run(() =>
        {
            using var session = new DiagramSession(SampleDiagram.Document());
            var before = session.Bus.Context.History.UndoEntries().Count;

            var mid = MidpointOf(session, "start", "check");
            session.SetEdgeBends("e1", [mid with { X = mid.X + 40 }]);

            session.PinnedEdges.Should().ContainKey("e1", "折点写进了人工产物");
            session.PinnedEdges["e1"].Should().HaveCount(1);
            session.CanUndoBend.Should().BeTrue();
            session.Bus.Context.History.UndoEntries().Count.Should().Be(before, "折点不进命令总线");

            session.UndoBend();
            session.PinnedEdges.Should().NotContainKey("e1", "整次增删一步退回");
        });
    }

    /// <summary>删一条折点是一次操作，且与加折点各自独立。</summary>
    [Fact]
    [Trait("Category", "EdgeEdit")]
    public async Task Removing_a_bend_is_one_undoable_operation()
    {
        await HeadlessFixture.Run(() =>
        {
            using var session = new DiagramSession(SampleDiagram.Document());
            var mid = MidpointOf(session, "start", "check");

            session.SetEdgeBends("e1", [mid]);
            session.ClearEdgeBends("e1");

            session.PinnedEdges.Should().NotContainKey("e1", "清折点后交回自动路由");
            session.CanUndoBend.Should().BeTrue();

            // 两次操作各压一条：先退掉清除（折点回来），再退掉添加（折点没啦）。
            session.UndoBend();
            session.PinnedEdges.Should().ContainKey("e1", "退回清除这一步，折点复原");
            session.UndoBend();
            session.PinnedEdges.Should().NotContainKey("e1", "再退添加这一步，回到没有折点");
        });
    }

    /// <summary>重连端点走命令总线，撤销由总线完成，与折点撤销互不干扰。</summary>
    [Fact]
    [Trait("Category", "EdgeEdit")]
    public async Task Reconnect_undo_goes_through_the_command_bus()
    {
        await HeadlessFixture.Run(() =>
        {
            using var session = new DiagramSession(SampleDiagram.Document());
            var original = session.Document.Edges.Single(e => e.Id == "e1").To;

            session.ReconnectEdge("e1", "start", null, "pass", null);
            session.Document.Edges.Single(e => e.Id == "e1").To.Should().Be("pass");

            session.Bus.Undo().IsSuccess.Should().BeTrue();
            session.Document.Edges.Single(e => e.Id == "e1").To.Should().Be(original, "总线撤销把端点改回原目标");
        });
    }

    /// <summary>两个节点 doc 坐标的中点。</summary>
    private static DrawPoint MidpointOf(DiagramSession session, string from, string to)
    {
        var a = session.Scene.Layout.Find(from)
            ?? throw new InvalidOperationException($"布局结果里没有 {from}");
        var b = session.Scene.Layout.Find(to)
            ?? throw new InvalidOperationException($"布局结果里没有 {to}");

        return new DrawPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    }
}
