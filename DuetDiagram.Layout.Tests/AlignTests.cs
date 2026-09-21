using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Layout.Tests;

/// <summary>
/// 逐元素对齐约束：让一组节点在垂直于分层方向的那个轴上取同一个坐标。
/// </summary>
/// <remarks>
/// <para>
/// 对齐轴与让位轴是**同一个**：都垂直于分层方向。选它而不是另一个轴的理由很直接——
/// 对齐若动分层轴上的坐标，节点就会跑到别的层去，而分层是另一条约束管的事。
/// </para>
/// <para>
/// 由此推出一件必须说清楚的事：**同一层的两个节点无法在层内轴上取齐**，
/// 取齐就等于让它们重叠。这种组会如实报成冲突。对齐的用场在跨层：
/// "这几个节点排成一列"。
/// </para>
/// </remarks>
public sealed class AlignTests
{
    private static readonly ConstraintLayoutEngine Engine = new();

    private static EngineLayoutResult Compute(LayoutRequest request) =>
        Engine.Layout(request, TestContext.Current.CancellationToken);

    private static double Along(PlacedNode node, bool ranksAreVertical) =>
        ranksAreVertical ? node.X : node.Y;

    private static double Across(PlacedNode node, bool ranksAreVertical) =>
        ranksAreVertical ? node.Y : node.X;

    #region 取齐

    [Fact]
    [Trait("Category", "OrderAlign")]
    public void Members_in_different_layers_end_up_on_the_same_coordinate()
    {
        var request = Graphs.Diamond() with
        {
            Options = new LayoutOptions(Direction.TB, AlignGroups: [new[] { "start", "left" }]),
        };

        var result = Compute(request);

        result.Find("start")!.X.Should().Be(result.Find("left")!.X);
        result.Diagnostics.ConstraintConflicts.Should().BeEmpty();

        // 被让开的邻居不能因此压到别人身上。
        result.Diagnostics.ResidualOverlaps.Should().Be(0);
        result.Find("left")!.Overlaps(result.Find("right")!).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "OrderAlign")]
    public void Aligning_only_changes_the_in_layer_axis()
    {
        var baseline = Compute(Graphs.Fork());
        var aligned = Compute(Graphs.Fork(align: [new[] { "p", "a" }]));

        aligned.Find("p")!.X.Should().Be(aligned.Find("a")!.X);
        aligned.Find("p")!.Y.Should().Be(baseline.Find("p")!.Y, "分层轴上的坐标不许动");
        aligned.Find("a")!.Y.Should().Be(baseline.Find("a")!.Y);
    }

    [Theory]
    [Trait("Category", "OrderAlign")]
    [InlineData(Direction.TB)]
    [InlineData(Direction.BT)]
    [InlineData(Direction.LR)]
    [InlineData(Direction.RL)]
    public void The_alignment_axis_follows_the_ranking_direction(Direction direction)
    {
        // 层左右并排时对齐的是纵轴。两处推错一个，节点就会被推出它所在的层，
        // 而那种错误在只有上下方向的用例里完全看不出来。
        var ranksAreVertical = direction is Direction.TB or Direction.BT;
        var baseline = Compute(Graphs.Fork(direction));
        var aligned = Compute(Graphs.Fork(direction, align: [new[] { "p", "a" }]));

        Along(aligned.Find("p")!, ranksAreVertical)
            .Should().Be(Along(aligned.Find("a")!, ranksAreVertical));

        foreach (var id in new[] { "p", "a" })
        {
            Across(aligned.Find(id)!, ranksAreVertical)
                .Should().Be(Across(baseline.Find(id)!, ranksAreVertical), "对齐不该改变节点在第几层");
        }
    }

    #endregion

    #region 与固定位置同时成立

    [Fact]
    [Trait("Category", "OrderAlign")]
    public void A_single_pinned_member_becomes_the_target()
    {
        // 有一个固定成员时以它为准：固定坐标是用户直接给出的意图，
        // 取别的坐标等于让用户的意图去迁就自动计算。
        var request = new LayoutRequest(
            [
                Graphs.Node("start"),
                Graphs.Node("left", 500, 130),
                Graphs.Node("right"),
                Graphs.Node("end"),
            ],
            Graphs.Diamond().Edges,
            new LayoutOptions(Direction.TB, AlignGroups: [new[] { "start", "left" }]));

        var result = Compute(request);

        result.Find("left")!.X.Should().Be(500, "固定坐标一个像素都不能偏");
        result.Find("start")!.X.Should().Be(500);
        result.Diagnostics.ConstraintConflicts.Should().BeEmpty();
        result.Diagnostics.MaxAnchorDeviation.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "OrderAlign")]
    public void Two_pinned_members_apart_from_each_other_are_reported_exactly_once()
    {
        // 两个固定节点都不许动，而它们的坐标不同，这条约束无解。
        // 求解阶段判过之后核对阶段不该再报一遍——第二句会写成"被同层的让位推开了"，
        // 而实际上这两个节点压根没被动过，那句话是错的。
        var request = new LayoutRequest(
            [Graphs.Node("a", 100, 20), Graphs.Node("b", 300, 130), Graphs.Node("c")],
            [new LayoutEdge("e1", "a", "b"), new LayoutEdge("e2", "b", "c")],
            new LayoutOptions(Direction.TB, AlignGroups: [new[] { "a", "b" }]));

        var result = Compute(request);

        result.Diagnostics.ConstraintConflicts.Should().ContainSingle();
        result.Diagnostics.ConstraintConflicts[0].Should().Contain("固定位置");

        result.Find("a")!.X.Should().Be(100);
        result.Find("b")!.X.Should().Be(300);
        result.Diagnostics.MaxAnchorDeviation.Should().Be(0);
    }

    #endregion

    #region 如实报出而不是静默丢弃

    [Fact]
    [Trait("Category", "OrderAlign")]
    public void Members_of_one_layer_cannot_be_aligned_and_that_is_reported()
    {
        // 同一层的两个节点在层内轴上取齐就等于让它们重叠。这是约束本身无解，
        // 不是算法没做到——报出来，界面才能告诉用户"这两条冲突"。
        // 悄悄丢掉这条约束的表现是"我设了但没生效"，用户会去反复重设。
        var request = Graphs.Diamond() with
        {
            Options = new LayoutOptions(Direction.TB, AlignGroups: [new[] { "left", "right" }]),
        };

        var result = Compute(request);

        result.Diagnostics.ConstraintConflicts.Should().ContainSingle();
        result.Diagnostics.ConstraintConflicts[0].Should().Contain("让位");

        // 报出来之后仍然要交出一张能看的图：不重叠、固定坐标不偏。
        result.Diagnostics.ResidualOverlaps.Should().Be(0);
        result.Diagnostics.SatisfiesHardGuarantees.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "OrderAlign")]
    public void Names_that_are_not_in_the_document_are_ignored()
    {
        var baseline = Compute(Graphs.Fork());
        var stale = Compute(Graphs.Fork(align: [new[] { "查无此节点", "也没有这个" }]));

        stale.Diagnostics.ConstraintConflicts.Should().BeEmpty();
        stale.Nodes.Select(n => (n.Id, n.X, n.Y))
            .Should().Equal(baseline.Nodes.Select(n => (n.Id, n.X, n.Y)));
    }

    [Fact]
    [Trait("Category", "OrderAlign")]
    public void A_group_with_one_present_member_is_not_a_conflict()
    {
        var result = Compute(Graphs.Fork(align: [new[] { "a" }]));

        result.Diagnostics.ConstraintConflicts.Should().BeEmpty();
    }

    #endregion
}
