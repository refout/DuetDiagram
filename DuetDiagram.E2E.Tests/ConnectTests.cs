using DuetDiagram.App;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Model;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 从端口拖出连线：按下那一刻记死端口、拖到另一个元素松手创建一条边、拖到空白取消、拖到自身被拒。
/// </summary>
/// <remarks>
/// <para>
/// 连线是一次性创建新边，所以"发一条命令"由总线历史条目数恰好加一来保证；
/// 取消与被拒都不会进历史，文档里的边数与之前完全相同。端口名只随按下那一刻记下来，
/// 不等到松手再去猜用户从哪个端口出发——自动端口压在哪一侧只有按在边界上那一刻才看得准。
/// </para>
/// <para>
/// 端点允许是组合：布局侧已经能把"层流向层"落到包围盒边界上，交互不得只认节点，
/// 否则从分组拖出来的线画得出来却连不上。
/// </para>
/// </remarks>
public sealed class ConnectTests
{
    /// <summary>从端口拖到另一个节点，创建恰好一条边，边带上记下的端口名。</summary>
    [Fact]
    [Trait("Category", "Connect")]
    public async Task Dragging_from_a_port_to_another_node_creates_one_edge()
    {
        await HeadlessFixture.Run(() =>
        {
            using var session = new DiagramSession(SampleDiagram.Document());
            var before = session.Bus.Context.History.UndoEntries().Count;
            var start = PositionOf(session, "start");

            session.BeginConnect("start", "out", start);
            session.UpdateConnect(new DrawPoint(start.X + 50, start.Y + 10));
            var outcome = session.CommitConnect("end", null);

            outcome.Should().Be(DiagramSession.ConnectOutcome.Connected);
            session.Bus.Context.History.UndoEntries().Count.Should().Be(before + 1, "连线只发一条命令");
            session.Document.Edges
                .Should().ContainSingle(e => e.From == "start" && e.To == "end" && e.FromPort == "out");
        });
    }

    /// <summary>拖到空白松开取消连线，不弹窗、不进历史、不新增边。</summary>
    [Fact]
    [Trait("Category", "Connect")]
    public async Task Releasing_on_empty_cancels_without_creating_an_edge()
    {
        await HeadlessFixture.Run(() =>
        {
            using var session = new DiagramSession(SampleDiagram.Document());
            var before = session.Bus.Context.History.UndoEntries().Count;
            var start = PositionOf(session, "start");

            session.BeginConnect("start", "out", start);
            var outcome = session.CommitConnect(null, null);

            outcome.Should().Be(DiagramSession.ConnectOutcome.Cancelled);
            session.Bus.Context.History.UndoEntries().Count.Should().Be(before, "取消不该进历史");
            session.Document.Edges.Should().HaveCount(5, "没有新增边");
        });
    }

    /// <summary>拖回到连线起点自身，当场被拒，不创建一条指向自己的边。</summary>
    [Fact]
    [Trait("Category", "Connect")]
    public async Task Releasing_on_the_source_node_is_rejected()
    {
        await HeadlessFixture.Run(() =>
        {
            using var session = new DiagramSession(SampleDiagram.Document());
            var before = session.Bus.Context.History.UndoEntries().Count;
            var start = PositionOf(session, "start");

            session.BeginConnect("start", null, start);
            var outcome = session.CommitConnect("start", null);

            outcome.Should().Be(DiagramSession.ConnectOutcome.Rejected);
            session.Bus.Context.History.UndoEntries().Count.Should().Be(before, "被拒不该进历史");
            session.Document.Edges.Should().HaveCount(5);
        });
    }

    /// <summary>端点是组合时也能连上：从分组拖出来的线落回文档，而不是只能连节点。</summary>
    [Fact]
    [Trait("Category", "Connect")]
    public async Task Connecting_to_a_composite_endpoint_is_allowed()
    {
        await HeadlessFixture.Run(() =>
        {
            var document = DiagramDocument.CreateFromContent(
                "composite",
                DiagramKind.Flowchart,
                Direction.LR,
                nodes: [new NodeDef { Id = "a" }, new NodeDef { Id = "b" }],
                composites: [new GroupDef { Id = "g", Members = ["a"] }]);

            using var session = new DiagramSession(document);
            var start = PositionOf(session, "a");

            session.BeginConnect("a", null, start);
            var outcome = session.CommitConnect("g", null);

            outcome.Should().Be(DiagramSession.ConnectOutcome.Connected, "端点是组合也连得上");
            session.Document.Edges.Should().ContainSingle(e => e.From == "a" && e.To == "g");
        });
    }

    /// <summary>元素当前的左上角，文档坐标。</summary>
    private static DrawPoint PositionOf(DiagramSession session, string id)
    {
        var placed = session.Scene.Layout.Find(id)
            ?? throw new InvalidOperationException($"布局结果里没有 {id}");

        return new DrawPoint(placed.X, placed.Y);
    }
}
