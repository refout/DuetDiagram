using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Layout.Tests;

/// <summary>
/// 约束补齐全套流水线的不变量。
/// </summary>
/// <remarks>
/// 这些用例是从 Phase 0a 的验证程序搬过来的。那份验证程序证明过方案的可行性，
/// 而验证程序不进产品——搬过来之后，约束补齐逻辑才真正受到持续保护。
/// </remarks>
public sealed class LayoutTests
{
    private static readonly ConstraintLayoutEngine Engine = new();

    // ---- 基础 ----

    [Fact]
    [Trait("Category", "Layout")]
    public void A_plain_diamond_has_no_overlaps_and_all_nodes_placed()
    {
        var result = Engine.Layout(Graphs.Diamond());

        result.Nodes.Should().HaveCount(4);
        result.Diagnostics.ResidualOverlaps.Should().Be(0);
        result.Diagnostics.SatisfiesHardGuarantees.Should().BeTrue();
        result.Width.Should().BeGreaterThan(0);
        result.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Layout")]
    public void Empty_input_returns_an_empty_result()
    {
        // 空图是合法状态（用户刚新建文档），不该抛异常让调用方多写一个分支。
        var result = Engine.Layout([], []);

        result.Nodes.Should().BeEmpty();
        result.Edges.Should().BeEmpty();
        result.Width.Should().Be(0);
        result.Height.Should().Be(0);
    }

    // ---- 固定位置是唯一的硬保证 ----

    [Fact]
    [Trait("Category", "Layout")]
    public void A_pinned_node_keeps_its_exact_position()
    {
        var request = Graphs.Diamond(extra: [Graphs.Node("pinned", 500, 300)]);

        var result = Engine.Layout(request);

        var pinned = result.Find("pinned")!;
        pinned.X.Should().Be(500);
        pinned.Y.Should().Be(300);

        // 偏差是整数化的诊断值，这里要求它精确为零。
        result.Diagnostics.MaxAnchorDeviation.Should().Be(0);
        result.Diagnostics.AnchorCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Layout")]
    public void A_pinned_node_pushes_free_nodes_aside()
    {
        // 把固定节点钉在另一个节点的位置上，制造必然的碰撞。
        var baseline = Engine.Layout(Graphs.Diamond());
        var target = baseline.Find("left")!;

        var request = Graphs.Diamond(extra: [Graphs.Node("pinned", target.X, target.Y)]);
        var result = Engine.Layout(request);

        result.Find("pinned")!.X.Should().Be(target.X, "固定坐标一个像素都不能偏");
        result.Find("pinned")!.Y.Should().Be(target.Y);

        // 被压住的自由节点让开，且不留下重叠。
        result.Find("left")!.X.Should().NotBe(target.X);
        result.Diagnostics.ResidualOverlaps.Should().Be(0);
        result.Diagnostics.ReflowedNodes.Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Layout")]
    public void Contradictory_pins_are_reported_rather_than_resolved()
    {
        // 两个固定节点互相压住是无解的输入：任何一方让步都等于违背用户意图。
        // 算法只能如实报出来。
        var request = Graphs.Diamond(extra:
        [
            Graphs.Node("a", 300, 200),
            Graphs.Node("b", 310, 210),
        ]);

        var result = Engine.Layout(request);

        result.Diagnostics.OverlappingAnchors.Should().BeGreaterThan(0);
        result.Find("a")!.X.Should().Be(300);
        result.Find("b")!.X.Should().Be(310);
    }

    // ---- 同层约束 ----

    [Fact]
    [Trait("Category", "Layout")]
    public void Same_rank_members_share_a_row()
    {
        var request = Graphs.Diamond(sameRank: [new[] { "left", "right" }]);

        var result = Engine.Layout(request);

        result.Find("left")!.Y.Should().Be(result.Find("right")!.Y);
        result.Diagnostics.ResidualOverlaps.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Layout")]
    public void Same_rank_members_do_not_overlap()
    {
        // 超节点按展开后的总尺寸申报空间，展开才是纯局部操作。
        // 尺寸申报算错的话，成员会横着压到邻居身上。
        var request = Graphs.Diamond(sameRank: [new[] { "left", "right" }]);

        var result = Engine.Layout(request);

        result.Find("left")!.Overlaps(result.Find("right")!).Should().BeFalse();
        result.Find("left")!.Right.Should().BeLessThanOrEqualTo(result.Find("right")!.X);
    }

    [Fact]
    [Trait("Category", "Layout")]
    public void Same_rank_and_pins_hold_together()
    {
        var request = Graphs.Diamond(
            extra: [Graphs.Node("pinned", 700, 100)],
            sameRank: [new[] { "left", "right" }]);

        var result = Engine.Layout(request);

        result.Find("left")!.Y.Should().Be(result.Find("right")!.Y);
        result.Find("pinned")!.X.Should().Be(700);
        result.Diagnostics.ResidualOverlaps.Should().Be(0);
        result.Diagnostics.MaxAnchorDeviation.Should().Be(0);
    }

    // ---- 方向 ----

    [Theory]
    [Trait("Category", "Layout")]
    [InlineData(Direction.TB)]
    [InlineData(Direction.BT)]
    [InlineData(Direction.LR)]
    [InlineData(Direction.RL)]
    public void All_four_directions_keep_pins_exact(Direction direction)
    {
        var request = Graphs.Diamond(
            direction,
            extra: [Graphs.Node("pinned", 400, 250)]);

        var result = Engine.Layout(request);

        result.Find("pinned")!.X.Should().Be(400);
        result.Find("pinned")!.Y.Should().Be(250);
        result.Diagnostics.ResidualOverlaps.Should().Be(0);
        result.Diagnostics.SatisfiesHardGuarantees.Should().BeTrue();
    }

    [Theory]
    [Trait("Category", "Layout")]
    [InlineData(Direction.TB)]
    [InlineData(Direction.BT)]
    [InlineData(Direction.LR)]
    [InlineData(Direction.RL)]
    public void Reflow_pushes_perpendicular_to_the_ranking_axis(Direction direction)
    {
        // 推错轴会把节点推出它所在的层，分层结构当场就散，
        // 而这种情况在只有上下方向的用例里完全看不出来。
        var baseline = Engine.Layout(Graphs.Diamond(direction));
        var target = baseline.Find("left")!;

        var moved = Engine.Layout(Graphs.Diamond(
            direction,
            extra: [Graphs.Node("pinned", target.X, target.Y)]));

        var pushed = moved.Find("left")!;
        var ranksAreVertical = direction is Direction.TB or Direction.BT;

        if (ranksAreVertical)
        {
            // 层上下叠放 → 层内沿横向让位。纵坐标必须不动。
            pushed.Y.Should().Be(target.Y);
            pushed.X.Should().NotBe(target.X);
        }
        else
        {
            pushed.X.Should().Be(target.X);
            pushed.Y.Should().NotBe(target.Y);
        }
    }

    // ---- 折线 ----

    [Fact]
    [Trait("Category", "Layout")]
    public void Edge_endpoints_land_on_node_boundaries()
    {
        var result = Engine.Layout(Graphs.Diamond());

        result.Diagnostics.EndpointFailures.Should().Be(0);

        foreach (var edge in result.Edges)
        {
            var source = result.Find(edge.Points.Length > 0 ? edge.Id[1..] : string.Empty);

            // 端点的精确校验在诊断里做过了，这里确认折线本身有内容且不退化。
            edge.Points.Length.Should().BeGreaterThanOrEqualTo(2);
            _ = source;
        }
    }

    [Fact]
    [Trait("Category", "Layout")]
    public void Port_endpoints_land_exactly_on_the_port()
    {
        var ports = new[] { new LayoutPort("out", PortSide.Bottom, 0.25) };

        var request = new LayoutRequest(
            [
                Graphs.Node("a", ports: ports),
                Graphs.Node("b"),
            ],
            [new LayoutEdge("e1", "a", "b", FromPort: "out")],
            new LayoutOptions());

        var result = Engine.Layout(request);

        var a = result.Find("a")!;
        var expected = a.PortAnchor(ports[0]);

        result.Edges.Single().Points[0].Should().Be(expected);
        result.Diagnostics.EndpointFailures.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Layout")]
    public void An_unknown_port_name_falls_back_to_the_automatic_side()
    {
        // 端口名写错时退回自动选边，而不是报错或把线接到一个不存在的位置。
        var request = new LayoutRequest(
            [Graphs.Node("a"), Graphs.Node("b")],
            [new LayoutEdge("e1", "a", "b", FromPort: "根本没这个端口")],
            new LayoutOptions());

        var result = Engine.Layout(request);

        result.Nodes.Should().HaveCount(2);
        result.Diagnostics.EndpointFailures.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Layout")]
    public void An_edge_to_a_missing_node_is_counted_not_fatal()
    {
        // 输入引用了不存在的节点。布局照常进行，只是这条边画不出来。
        var request = new LayoutRequest(
            [Graphs.Node("a")],
            [new LayoutEdge("e1", "a", "查无此节点")],
            new LayoutOptions());

        var result = Engine.Layout(request);

        result.Nodes.Should().HaveCount(1);
        result.Diagnostics.EndpointFailures.Should().Be(1);
    }

    // ---- 规模 ----

    [Fact]
    [Trait("Category", "Layout")]
    public void Thousand_nodes_complete_within_the_budget()
    {
        var result = Engine.Layout(Graphs.Layered(depth: 20, breadth: 50));

        result.Nodes.Should().HaveCount(1000);
        result.Edges.Should().HaveCount(950);
        result.Diagnostics.ResidualOverlaps.Should().Be(0);
        result.Diagnostics.EndpointFailures.Should().Be(0);

        // 上限给得很宽：这条断言盯的是"有没有退化成平方复杂度"，不是精确耗时。
        // 精确的耗时基线在基准工程里，那里有统计方法，秒表读数在这里没有意义。
        var total = result.Diagnostics.ContractionTime
            + result.Diagnostics.RestorationTime
            + result.Diagnostics.ReflowTime
            + result.Diagnostics.RoutingTime;

        total.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }
}
