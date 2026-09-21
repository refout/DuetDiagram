using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Layout.Tests;

/// <summary>
/// 层内次序约束：把同一层里的一组节点按给定先后换位。
/// </summary>
/// <remarks>
/// <para>
/// 要验的核心是两件事，它们分别对应两种写错了都很难发现的实现：
/// </para>
/// <list type="number">
/// <item>**次序被保持**——不是"按给定次序把整层重排"，而是只动组内这几个。
/// 重排整层会顺手打乱组外节点的左右关系，而左右关系决定连线的交叉与阅读顺序，
/// 打乱它比整层变宽更糟。</item>
/// <item>**与固定位置同时成立**——固定节点占住它自己的位置，自由成员填进剩下的位置。
/// 两者不可能同时满足时如实报出来，不静默丢掉其中一条。</item>
/// </list>
/// </remarks>
public sealed class OrderTests
{
    private static readonly ConstraintLayoutEngine Engine = new();

    private static EngineLayoutResult Compute(LayoutRequest request) =>
        Engine.Layout(request, TestContext.Current.CancellationToken);

    /// <summary>层内轴上的坐标。层上下叠放时是横轴，层左右并排时是纵轴。</summary>
    private static double Along(PlacedNode node, bool ranksAreVertical) =>
        ranksAreVertical ? node.X : node.Y;

    /// <summary>分层轴上的坐标。它是"节点在第几层"的落点，次序约束不许动它。</summary>
    private static double Across(PlacedNode node, bool ranksAreVertical) =>
        ranksAreVertical ? node.Y : node.X;

    #region 次序被保持

    [Fact]
    [Trait("Category", "OrderAlign")]
    public void A_swap_reverses_the_two_members_and_leaves_the_third_alone()
    {
        // 三个分支原本按声明先后从左往右排。把 a 与 c 对调，b 必须还在中间——
        // 它没有被点名，就不该动。
        var baseline = Compute(Graphs.Fork());
        var swapped = Compute(Graphs.Fork(order: [new[] { "c", "a" }]));

        Along(swapped.Find("c")!, true).Should().BeLessThan(Along(swapped.Find("b")!, true));
        Along(swapped.Find("b")!, true).Should().BeLessThan(Along(swapped.Find("a")!, true));

        Along(swapped.Find("b")!, true)
            .Should().Be(Along(baseline.Find("b")!, true), "没被点名的节点不该被挪动");
    }

    [Fact]
    [Trait("Category", "OrderAlign")]
    public void A_reorder_reuses_the_same_slots_so_the_layer_does_not_get_wider()
    {
        // 换位用的是原来那几个位置，所以整层的横向跨度一点没变。
        // 若实现是"把节点搬到新位置再往右推"，整层会被撑宽——用户只是想换两个人的左右关系，
        // 却看到整层变长了，那看起来像是布局出了问题。
        var baseline = Compute(Graphs.Fork());
        var swapped = Compute(Graphs.Fork(order: [new[] { "b", "c", "a" }]));

        var before = baseline.Nodes.Select(n => n.X).Order().ToArray();
        var after = swapped.Nodes.Select(n => n.X).Order().ToArray();

        after.Should().Equal(before);
    }

    [Fact]
    [Trait("Category", "OrderAlign")]
    public void Nodes_outside_the_group_keep_their_exact_position()
    {
        var baseline = Compute(Graphs.Fork());
        var swapped = Compute(Graphs.Fork(order: [new[] { "c", "b" }]));

        Along(swapped.Find("a")!, true)
            .Should().Be(Along(baseline.Find("a")!, true), "组外的节点一个都不该动");
    }

    [Theory]
    [Trait("Category", "OrderAlign")]
    [InlineData(Direction.TB)]
    [InlineData(Direction.BT)]
    [InlineData(Direction.LR)]
    [InlineData(Direction.RL)]
    public void The_order_holds_along_the_in_layer_axis_and_the_ranks_do_not_move(Direction direction)
    {
        // 层左右并排时，层内轴是纵轴而不是横轴。做错轴的话节点会被推出它所在的层，
        // 而那种错误在只有上下方向的用例里完全看不出来。
        var ranksAreVertical = direction is Direction.TB or Direction.BT;
        var baseline = Compute(Graphs.Fork(direction));
        var swapped = Compute(Graphs.Fork(direction, order: [new[] { "b", "a" }]));

        Along(swapped.Find("b")!, ranksAreVertical)
            .Should().BeLessThan(Along(swapped.Find("a")!, ranksAreVertical));

        foreach (var id in new[] { "a", "b" })
        {
            Across(swapped.Find(id)!, ranksAreVertical)
                .Should().Be(Across(baseline.Find(id)!, ranksAreVertical), "次序约束不该改变节点在第几层");
        }
    }

    #endregion

    #region 与固定位置同时成立

    [Fact]
    [Trait("Category", "OrderAlign")]
    public void A_pinned_member_keeps_its_position_and_the_rest_fill_in_around_it()
    {
        // 固定节点占住它自己的位置，自由成员依次填进剩下的位置。
        // 这里 c 被钉在最右边，要求 b 在 a 左边、a 在 c 左边——三者能同时成立。
        var request = Graphs.Fork(
            nodes:
            [
                Graphs.Node("p"),
                Graphs.Node("a"),
                Graphs.Node("b"),
                Graphs.Node("c", 280, 130),
            ],
            order: [new[] { "b", "a", "c" }]);

        var result = Compute(request);

        result.Diagnostics.ConstraintConflicts.Should().BeEmpty();
        result.Find("c")!.X.Should().Be(280, "固定坐标一个像素都不能偏");
        result.Find("b")!.X.Should().BeLessThan(result.Find("a")!.X);
        result.Find("a")!.X.Should().BeLessThan(result.Find("c")!.X);
        result.Diagnostics.MaxAnchorDeviation.Should().Be(0);
        result.Diagnostics.ResidualOverlaps.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "OrderAlign")]
    public void An_order_that_the_pins_make_impossible_is_reported_exactly_once()
    {
        // 两个固定节点已经把左右次序定死了，再要求反过来是无解的输入。
        // 求解阶段判过之后核对阶段不该再报一遍：那第二句会写成"被让位推回了原次序"，
        // 而实际上这条约束压根没被动过，那句话是错的。
        var request = Graphs.Fork(
            nodes:
            [
                Graphs.Node("p"),
                Graphs.Node("a", 160, 130),
                Graphs.Node("b", 40, 130),
                Graphs.Node("c"),
            ],
            order: [new[] { "a", "b" }]);

        var result = Compute(request);

        result.Diagnostics.ConstraintConflicts.Should().ContainSingle();
        result.Diagnostics.ConstraintConflicts[0].Should().Contain("固定位置");

        // 无解时两个固定节点都不动，而不是挑一个让步。
        result.Find("a")!.X.Should().Be(160);
        result.Find("b")!.X.Should().Be(40);
        result.Diagnostics.MaxAnchorDeviation.Should().Be(0);
    }

    #endregion

    #region 如实报出而不是静默丢弃

    [Fact]
    [Trait("Category", "OrderAlign")]
    public void Members_spread_over_two_layers_are_reported_rather_than_pulled_together()
    {
        // 层内次序只在同一层之内有意义。把跨层的成员硬拉到一层会连带改变分层，
        // 而分层是另一条约束管的事——那种"顺手帮忙"会让两条约束互相打架。
        var request = new LayoutRequest(
            [Graphs.Node("p"), Graphs.Node("a"), Graphs.Node("b"), Graphs.Node("c")],
            [
                new LayoutEdge("e1", "p", "a"),
                new LayoutEdge("e2", "p", "b"),
                new LayoutEdge("e3", "b", "c"),
            ],
            new LayoutOptions(Direction.TB, OrderGroups: [new[] { "c", "a" }]));

        var result = Compute(request);

        result.Diagnostics.ConstraintConflicts.Should().ContainSingle();
        result.Diagnostics.ConstraintConflicts[0].Should().Contain("同一层");

        // 分层没有被改动。
        result.Find("c")!.Y.Should().BeGreaterThan(result.Find("a")!.Y);
    }

    [Fact]
    [Trait("Category", "OrderAlign")]
    public void Names_that_are_not_in_the_document_are_ignored()
    {
        // 图变了而约束没跟着变是常见情形。把它报成冲突只会淹没真正的矛盾，
        // 而图与约束的一致性由校验器负责，不是布局该管的。
        var baseline = Compute(Graphs.Fork());
        var stale = Compute(Graphs.Fork(order: [new[] { "查无此节点", "也没有这个" }]));

        stale.Diagnostics.ConstraintConflicts.Should().BeEmpty();
        stale.Nodes.Select(n => (n.Id, n.X, n.Y))
            .Should().Equal(baseline.Nodes.Select(n => (n.Id, n.X, n.Y)));
    }

    [Fact]
    [Trait("Category", "OrderAlign")]
    public void A_group_with_one_present_member_is_not_a_conflict()
    {
        // 只剩一个成员时无所谓次序：一条边指向的节点就一个，没有任何"谁在谁左边"可言。
        var result = Compute(Graphs.Fork(order: [new[] { "a" }]));

        result.Diagnostics.ConstraintConflicts.Should().BeEmpty();
    }

    #endregion
}
