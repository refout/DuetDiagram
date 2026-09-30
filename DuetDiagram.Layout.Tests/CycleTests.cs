using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Layout.Tests;

/// <summary>
/// 带环的图能排出来。
/// </summary>
/// <remarks>
/// <para>
/// 层序本身要求无环，所以带环的输入必然要被引擎改一处：把环上某条边反过来。
/// 引擎做这件事的时候会给那条边起一个名字，而带名字的边在非多图模式下会被直接拒绝——
/// 于是整张图一条坐标都算不出来。这组用例就是钉住这条路径的。
/// </para>
/// <para>
/// 只断言"没抛异常"是不够的：把环上的边悄悄丢掉同样不会抛异常，而那样排出来的图是错的。
/// 所以每一份带环的输入都要同时满足两件事——输入的边一条不少地留在结果里，
/// 且环上各节点落在互不相同的层上。
/// </para>
/// </remarks>
public sealed class CycleTests
{
    private static readonly ConstraintLayoutEngine Engine = new();

    private static EngineLayoutResult Compute(LayoutRequest request) =>
        Engine.Layout(request, TestContext.Current.CancellationToken);

    #region 环

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [Trait("Category", "Layout")]
    public void A_cycle_is_broken_at_exactly_one_edge_and_loses_none(int count)
    {
        var result = Compute(Graphs.Cycle(count));

        result.Nodes.Should().HaveCount(count);
        result.Diagnostics.SatisfiesHardGuarantees.Should().BeTrue();

        // 一条边都不能少。丢掉环上的边是"排出来了"最容易伪装成的样子。
        result.Edges.Select(edge => edge.Id)
            .Should().BeEquivalentTo(Enumerable.Range(0, count).Select(index => $"e{index}"));

        AssertExactlyOneBackEdge(result, count);
    }

    /// <summary>
    /// 环上各节点各占一层，且顺着环走一圈恰好有一次"往下走"。
    /// </summary>
    /// <remarks>
    /// 层数互不相同证明环上的节点被排成了一条链；恰好一次下降证明断开的确实只有一条边。
    /// 只数层数不够——两条边被同时丢掉时，剩下的也是一条链。
    /// </remarks>
    private static void AssertExactlyOneBackEdge(EngineLayoutResult result, int count)
    {
        var ranks = Enumerable.Range(0, count).Select(index => result.Find($"c{index}")!.Y).ToArray();

        ranks.Distinct().Should().HaveCount(count, "环上每个节点都该占一个不同的层");

        var descents = Enumerable
            .Range(0, count)
            .Count(index => ranks[(index + 1) % count] < ranks[index]);

        descents.Should().Be(1, "环上恰好有一条边逆着层序，那一条就是被断开的");
    }

    [Fact]
    [Trait("Category", "Layout")]
    public void A_cycle_routes_its_back_edge_outside_the_node_columns()
    {
        // 回边逆着层序，两条竖段本来要跨过中间那些层、压在中间层的节点上。
        // 钉的是"确实走了外侧"——折线里出现一个比所有节点都靠左的点——
        // 而不是"恰好没压上"：后者靠中间层恰好没占住那个位置也能过，那不算修好。
        var result = Compute(Graphs.Cycle(4));

        result.Diagnostics.EdgesCrossingNodes.Should().Be(0);
        result.Diagnostics.SatisfiesHardGuarantees.Should().BeTrue();

        var back = result.Edges.Single(edge => edge.Points[0].Y > edge.Points[^1].Y);
        var leftmostNode = result.Nodes.Min(node => node.X);

        back.Points.Should().Contain(
            point => point.X < leftmostNode,
            "回边要走到所有节点列之外，而不是在层间直上直下");

        // 通道贴着节点列外缘再让开一段。这一条钉的是记录在案的那个读数——
        // 换一个位置照样不压节点，但布局评分里那份外接框的读数会跟着漂。
        back.Points.Should().Contain(
            point => Math.Abs(point.X - (leftmostNode - 16)) < 0.01,
            "外侧通道离节点列外缘的距离是固定的那一段");
    }

    [Fact]
    [Trait("Category", "Layout")]
    public void A_cycle_in_a_horizontal_layout_routes_outside_too()
    {
        // 层左右并排时是同一件事换轴：绕行通道在上下，穿节点照样要消掉。
        // 节点高取 80 而不是默认的 40——横向布局里引擎会把同一行里的节点错开半个行高，
        // 行矮的时候回边那条横线恰好擦着中间行的下边界（那不算穿过），
        // 行高到一定程度才会真的压进去。语料里的横向图全是无环的，这一条是横向那一支唯一的覆盖。
        var nodes = Enumerable.Range(0, 4)
            .Select(index => new LayoutNode($"c{index}", 80, 80, null, null, null))
            .ToArray();
        var edges = Enumerable.Range(0, 4)
            .Select(index => new LayoutEdge($"e{index}", $"c{index}", $"c{(index + 1) % 4}"))
            .ToArray();

        var result = Compute(new LayoutRequest(nodes, edges, new LayoutOptions(Direction.LR)));

        result.Diagnostics.EdgesCrossingNodes.Should().Be(0);
        result.Diagnostics.SatisfiesHardGuarantees.Should().BeTrue();

        var back = result.Edges.Single(edge => edge.Points[0].X > edge.Points[^1].X);
        var topmostNode = result.Nodes.Min(node => node.Y);

        back.Points.Should().Contain(
            point => point.Y < topmostNode,
            "回边要走到所有节点行之外，而不是在列间直来直去");
    }

    #endregion

    #region 自环与混合约束

    [Fact]
    [Trait("Category", "Layout")]
    public void A_self_loop_keeps_its_node_and_its_edge()
    {
        // 自环量下来**不**走断环那条路：引擎在去环之前先把自环摘掉，排完再挂回去，
        // 所以那条被命名、需要多图模式放行的边在这里根本不会出现。
        // 留着这一条是当护栏用——引擎的摘挂自环那一步不能被这次改动带坏。
        var result = Compute(Graphs.Cycle(1));

        result.Nodes.Should().HaveCount(1);
        result.Edges.Should().ContainSingle(edge => edge.Id == "e0");
        result.Diagnostics.SatisfiesHardGuarantees.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Layout")]
    public void A_cycle_beside_a_same_rank_group_and_a_pin_still_lays_out()
    {
        // 三种输入叠在一张图上：一条回边、一组同层约束、一个固定坐标。
        // 环要走断环那条路，同层约束要走收缩那条路，两者都会改写层号，
        // 而固定坐标是唯一的硬保证——它必须原封不动地活到最后。
        var request = new LayoutRequest(
            [
                Graphs.Node("start"),
                Graphs.Node("input"),
                Graphs.Node("check", 420, 200),
                Graphs.Node("ok"),
                Graphs.Node("bad"),
            ],
            [
                new LayoutEdge("e1", "start", "input"),
                new LayoutEdge("e2", "input", "check"),
                new LayoutEdge("e3", "check", "bad"),
                new LayoutEdge("e4", "check", "ok"),
                new LayoutEdge("e5", "bad", "input"),
            ],
            new LayoutOptions(Direction.TB, SameRankGroups: [["bad", "ok"]]));

        var result = Compute(request);

        result.Nodes.Should().HaveCount(5);
        result.Edges.Select(edge => edge.Id).Should().BeEquivalentTo(["e1", "e2", "e3", "e4", "e5"]);

        var pinned = result.Find("check")!;
        pinned.X.Should().Be(420);
        pinned.Y.Should().Be(200);

        result.Diagnostics.SatisfiesHardGuarantees.Should().BeTrue();
    }

    #endregion
}
