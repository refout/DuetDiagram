using DuetDiagram.Core.Model;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 剔除的判据与绘制列表的空间索引。
/// </summary>
/// <remarks>
/// 这一层测的是"哪些该画"。判错的两种方向后果不同：多画只是慢，
/// 少画则是画面上真的少了一块，而那种缺失只在特定视口下出现，很难复现。
/// </remarks>
public sealed class CullingPolicyTests
{
    #region 判据

    [Fact]
    [Trait("Category", "CullingPolicy")]
    public void The_default_policy_uses_the_documented_numbers()
    {
        CullingPolicy.Default.Threshold.Should().Be(500);
        CullingPolicy.Default.PrefetchMargin.Should().Be(100);
    }

    [Theory]
    [InlineData(0, RenderMode.Immediate)]
    [InlineData(499, RenderMode.Immediate)]
    [InlineData(500, RenderMode.Virtualized)]
    [InlineData(1000, RenderMode.Virtualized)]
    [Trait("Category", "CullingPolicy")]
    public void The_threshold_boundary_is_inclusive(int elementCount, RenderMode expected)
    {
        // 边界差一个元素会让"什么时候切换"变得不可预测，所以它被钉在测试里。
        CullingPolicy.Default.ModeFor(elementCount).Should().Be(expected);
    }

    [Fact]
    [Trait("Category", "CullingPolicy")]
    public void Prefetch_expands_the_viewport_on_all_sides()
    {
        var expanded = CullingPolicy.Default.VisibleArea(new SpatialRect(100, 100, 50, 50));

        expanded.X.Should().Be(0);
        expanded.Y.Should().Be(0);
        expanded.Width.Should().Be(250);
        expanded.Height.Should().Be(250);
    }

    [Fact]
    [Trait("Category", "CullingPolicy")]
    public void A_policy_uses_its_own_margin()
    {
        var policy = new CullingPolicy(threshold: 10, prefetchMargin: 0);

        policy.VisibleArea(new SpatialRect(100, 100, 50, 50)).Should().Be(new SpatialRect(100, 100, 50, 50));
        policy.ModeFor(10).Should().Be(RenderMode.Virtualized);
    }

    [Fact]
    [Trait("Category", "CullingPolicy")]
    public void A_threshold_below_one_is_rejected()
    {
        var zero = () => new CullingPolicy(threshold: 0);
        var negative = () => new CullingPolicy(threshold: -1);

        zero.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    [Trait("Category", "CullingPolicy")]
    public void A_negative_margin_is_rejected()
    {
        var negative = () => new CullingPolicy(prefetchMargin: -1);

        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region 索引

    [Fact]
    [Trait("Category", "CullingPolicy")]
    public void An_area_covering_the_content_keeps_everything()
    {
        var index = new CullingIndex(List(("a", 0, 0), ("b", 400, 0), ("c", 800, 0)));

        var visible = index.Visible(new SpatialRect(0, 0, 1000, 1000));

        visible.Should().HaveCount(3);
        index.CullRate.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "CullingPolicy")]
    public void Only_the_elements_inside_the_area_come_back()
    {
        var index = new CullingIndex(List(("a", 0, 0), ("b", 400, 0), ("c", 800, 0)));

        var visible = index.Visible(new SpatialRect(380, -10, 200, 100));

        visible.Select(c => c.ElementId).Should().Equal("b");
        index.VisibleCommands.Should().Be(1);
        index.CulledCommands.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "CullingPolicy")]
    public void An_element_straddling_the_edge_is_kept()
    {
        // 这是四叉树那一层最容易出错的地方，在剔除这一层同样要验：
        // 少画的后果是"某些元素一半画出来一半没有"。
        var index = new CullingIndex(List(("a", 0, 0)));

        // 元素的框是 (0,0,100,40)，这里只取右半边。
        index.Visible(new SpatialRect(50, 0, 200, 40)).Should().HaveCount(1);
        index.Visible(new SpatialRect(-200, 0, 250, 40)).Should().HaveCount(1);
    }

    [Fact]
    [Trait("Category", "CullingPolicy")]
    public void An_element_just_outside_the_viewport_survives_the_prefetch()
    {
        // 预取的用处：贴边的元素在快速平移时要提前一帧画出来，否则边缘会闪。
        var index = new CullingIndex(List(("a", 0, 0)));
        var viewport = new SpatialRect(150, 0, 400, 400);

        index.Visible(viewport).Should().BeEmpty("不扩边距时它确实在视口外");

        index.Visible(CullingPolicy.Default.VisibleArea(viewport)).Should().HaveCount(
            1,
            "扩了一百像素的边距之后它就在范围里了");
    }

    [Fact]
    [Trait("Category", "CullingPolicy")]
    public void The_kept_commands_keep_the_original_order()
    {
        // 顺序就是层叠顺序。剔除只是少画几条，不能把留下来的换先后——
        // 那会让连线跑到节点上面去。
        var list = List(("a", 0, 0), ("b", 400, 0), ("c", 800, 0));

        var visible = new CullingIndex(list).Visible(new SpatialRect(0, 0, 1200, 100));

        visible.Should().Equal(list.Commands);
    }

    [Fact]
    [Trait("Category", "CullingPolicy")]
    public void All_the_commands_of_a_visible_element_come_back()
    {
        // 一个节点对应好几条指令。按指令建索引的话，同一个位置会被存好几遍，
        // 而按元素建则要在取结果时把它们一起带回来。
        var list = WithLabels(("a", 0, 0), ("b", 900, 0));

        var visible = new CullingIndex(list).Visible(new SpatialRect(0, 0, 200, 200));

        visible.Select(c => c.ElementId).Should().Equal("a", "a", "a", "a");
    }

    [Fact]
    [Trait("Category", "CullingPolicy")]
    public void A_polyline_is_indexed_by_the_box_of_its_points()
    {
        var list = new DrawList(
            [
                new DrawPolyline(
                    "e",
                    [new DrawPoint(0, 0), new DrawPoint(500, 0), new DrawPoint(500, 400)],
                    "#000000",
                    1,
                    LineStyle.Solid,
                    ArrowStyle.Arrow),
            ],
            600,
            500,
            "#ffffff");

        var index = new CullingIndex(list);

        // 折线从左上走到右下，中间那一块虽然线上没经过，但包围盒盖住了。
        index.Visible(new SpatialRect(200, 150, 50, 50)).Should().HaveCount(
            1,
            "包围盒只会多取一点，多取的部分画出来也不显眼；少取则会漏画");
        index.Visible(new SpatialRect(600, 0, 50, 50)).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "CullingPolicy")]
    public void The_cull_rate_counts_commands_not_elements()
    {
        var list = WithLabels(("a", 0, 0), ("b", 900, 0));
        var index = new CullingIndex(list);

        index.ElementCount.Should().Be(2);
        list.Commands.Should().HaveCount(8, "两个形状加每个节点的三行文本");

        index.Visible(new SpatialRect(0, 0, 200, 200));

        // 剔掉的是元素 b 的四条指令，占一半。
        index.CullRate.Should().BeApproximately(4.0 / 8.0, 1e-9);
    }

    [Fact]
    [Trait("Category", "CullingPolicy")]
    public void Querying_twice_does_not_accumulate()
    {
        var index = new CullingIndex(List(("a", 0, 0), ("b", 900, 0)));

        index.Visible(new SpatialRect(0, 0, 200, 200)).Should().HaveCount(1);
        index.Visible(new SpatialRect(0, 0, 200, 200)).Should().HaveCount(1);
        index.Visible(new SpatialRect(0, 0, 1200, 200)).Should().HaveCount(2);
    }

    [Fact]
    [Trait("Category", "CullingPolicy")]
    public void An_empty_list_yields_nothing_and_a_zero_rate()
    {
        var index = new CullingIndex(DrawList.Empty);

        index.ElementCount.Should().Be(0);
        index.Visible(new SpatialRect(0, 0, 100, 100)).Should().BeEmpty();
        index.CullRate.Should().Be(0, "没有东西可剔，比例按零算而不是除以零");
    }

    [Fact]
    [Trait("Category", "CullingPolicy")]
    public void The_element_count_is_cached_but_still_correct()
    {
        var list = WithLabels(("a", 0, 0), ("b", 900, 0));

        list.ElementCount.Should().Be(2);
        list.ElementCount.Should().Be(2);
        DrawList.Empty.ElementCount.Should().Be(0);
    }

    #endregion

    /// <summary>一串同尺寸的矩形节点。</summary>
    private static DrawList List(params (string Id, double X, double Y)[] nodes)
    {
        var commands = nodes
            .Select(node => (DrawCommand)Shape(node.Id, node.X, node.Y))
            .ToArray();

        return new DrawList(commands, 1000, 1000, "#ffffff");
    }

    /// <summary>每个节点带三行文本，用来验"一个元素的指令要一起带回来"。</summary>
    private static DrawList WithLabels(params (string Id, double X, double Y)[] nodes)
    {
        var commands = new List<DrawCommand>();

        foreach (var node in nodes)
        {
            commands.Add(Shape(node.Id, node.X, node.Y));

            for (var line = 0; line < 3; line++)
            {
                commands.Add(new DrawText(
                    node.Id,
                    $"第 {line} 行",
                    new SpatialRect(node.X + 8, node.Y + (line * 12), 60, 12),
                    "#000000",
                    "Inter",
                    12,
                    FontWeight.Normal));
            }
        }

        return new DrawList(commands, 1000, 1000, "#ffffff");
    }

    private static DrawShape Shape(string id, double x, double y) =>
        new(
            id,
            NodeShape.Rect,
            new SpatialRect(x, y, 100, 40),
            "#ffffff",
            "#000000",
            1,
            LineStyle.Solid,
            0,
            1);
}
