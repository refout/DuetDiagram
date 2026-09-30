using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Layout.Tests;

/// <summary>
/// 折线的主方向段压到节点上时，改走一段不含节点的空隙。
/// </summary>
/// <remarks>
/// <para>
/// 层间那条横向的空隙救不了跨层的那两段竖线——空隙是横向的，跨过去的是纵向的。
/// 能救它们的是垂直于主方向的空当：最外侧那两条带，以及列与列之间的空当。
/// </para>
/// <para>
/// 同侧有多条边时它们各占一条通道，不再叠在一起。这一组量的就是这两件事：
/// 通道挑得对不对（走列间空当而不是一律绕到图外），以及多条边有没有分开。
/// </para>
/// </remarks>
public sealed class ChannelRoutingTests
{
    private static readonly ConstraintLayoutEngine Engine = new();

    private static EngineLayoutResult Compute(LayoutRequest request) =>
        Engine.Layout(request, TestContext.Current.CancellationToken);

    [Fact]
    [Trait("Category", "Layout")]
    public void Two_back_edges_on_the_same_side_take_two_lanes()
    {
        // 四节点环加一条 c2 -> c0 的弦，留下两条回边，两条都从左边出去。
        // 挤在同一条通道上的话它们会从头到尾叠在一起，所以钉的是"各占一条"——
        // 两条回边竖段的横坐标不同——而不是"恰好没重叠"。
        var nodes = Enumerable.Range(0, 4).Select(index => Graphs.Node($"c{index}")).ToArray();
        var edges = Enumerable.Range(0, 4)
            .Select(index => new LayoutEdge($"e{index}", $"c{index}", $"c{(index + 1) % 4}"))
            .Append(new LayoutEdge("chord", "c2", "c0"))
            .ToArray();

        var result = Compute(new LayoutRequest(nodes, edges, new LayoutOptions(Direction.TB)));

        result.Diagnostics.EdgesCrossingNodes.Should().Be(0);
        result.Diagnostics.SatisfiesHardGuarantees.Should().BeTrue();

        var backEdges = result.Edges.Where(edge => edge.Points[0].Y > edge.Points[^1].Y).ToArray();
        backEdges.Should().HaveCount(2, "环加一条弦会留下两条逆着层序的边");

        var leftmostNode = result.Nodes.Min(node => node.X);

        // 绕行通道落在所有节点列之外，所以竖段上那几个点的横坐标就是通道坐标。
        var lanes = backEdges
            .Select(edge => edge.Points
                .Where(point => point.X < leftmostNode)
                .Select(point => point.X)
                .Distinct()
                .Single())
            .ToArray();

        lanes.Should().OnlyHaveUniqueItems("同侧的两条回边该各占一条通道，不该挤在同一条上");
    }

    [Fact]
    [Trait("Category", "Layout")]
    public void An_edge_pulled_across_layers_by_an_anchor_routes_through_a_column_gap()
    {
        // 锚点把一个节点钉到别的层的带上之后，连到它的边要跨过中间那几层。
        // 那条边在层序上是正向的，但它和回边一样压在中间层的节点上，同样需要绕行。
        // 它该走的不是图外那条外侧通道，而是两列之间的空当：绕行短，也不撑大整张图的范围。
        var graph = Graphs.Layered(depth: 6, breadth: 3);
        var baseline = Compute(graph);
        var top = baseline.Find("n0-0")!.Y;
        var moved = baseline.Find("n3-1")!;

        var request = graph with
        {
            Nodes =
            [
                .. graph.Nodes.Select(node =>
                    node.Id == "n3-1" ? node with { Pinned = new LayoutPoint(moved.X, top) } : node),
            ],
        };

        var result = Compute(request);

        result.Diagnostics.SatisfiesHardGuarantees.Should().BeTrue();
        result.Find("n3-1")!.Y.Should().Be(top, "固定坐标一个像素都不能偏");
        result.Diagnostics.EdgesCrossingNodes.Should().Be(0);

        var source = result.Find("n3-1")!;
        var leftmostNode = result.Nodes.Min(node => node.X);
        var crossing = result.Edges.Single(edge => edge.Id == "e3-1");

        crossing.Points.Should().Contain(
            point => point.X < source.X && point.X > leftmostNode,
            "绕行该走两列之间的空当，而不是走到整张图之外");
    }

    [Fact]
    [Trait("Category", "Layout")]
    public void A_crossing_on_the_channel_segment_is_still_counted_honestly()
    {
        // 层间那条横带被一个固定节点占满时，折线的横段只能压过去。
        // 这种穿越不走绕行：绕行走的是主方向上的空隙，它修不好横段上的穿越，
        // 硬绕出去只会把一条短边拉成一条绕到图外的远路。如实计数才是对的。
        var request = new LayoutRequest(
            [
                new LayoutNode("a", 80, 40, new LayoutPoint(40, 20), null, null),
                new LayoutNode("b", 80, 40, new LayoutPoint(200, 200), null, null),
                new LayoutNode("p", 80, 200, new LayoutPoint(120, 40), null, null),
            ],
            [new LayoutEdge("e1", "a", "b")],
            new LayoutOptions(Direction.TB));

        var result = Compute(request);

        result.Diagnostics.SatisfiesHardGuarantees.Should().BeTrue();
        result.Diagnostics.EdgesCrossingNodes.Should().Be(1, "空隙被占满时如实计数，不假装绕开");

        var leftmostNode = result.Nodes.Min(node => node.X);
        var rightmostNode = result.Nodes.Max(node => node.Right);

        result.Edges.Single().Points.Should().OnlyContain(
            point => point.X >= leftmostNode - 0.01 && point.X <= rightmostNode + 0.01,
            "横段上的穿越不该被一条绕到节点列之外的远路顶掉");
    }
}
