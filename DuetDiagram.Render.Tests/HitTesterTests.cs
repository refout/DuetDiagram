using DuetDiagram.Core.Model;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 按绘制列表命中元素。
/// </summary>
/// <remarks>
/// 这一组盯的是两件事：**从绘制列表来**，以及**倒着找**。
/// 前者决定命中的位置与画出来的位置是不是同一套算法，后者决定点在最上面的东西上
/// 会不会选中背后的另一个。
/// </remarks>
public sealed class HitTesterTests
{
    [Fact]
    [Trait("Category", "HitTest")]
    public void A_point_inside_a_shape_finds_that_element()
    {
        var commands = new DrawCommand[] { Shape("a", 0, 0, 100, 50) };

        HitTester.Hit(commands, new DrawPoint(50, 25)).Should().Be("a");
    }

    [Fact]
    [Trait("Category", "HitTest")]
    public void A_point_outside_every_shape_finds_nothing()
    {
        var commands = new DrawCommand[] { Shape("a", 0, 0, 100, 50) };

        HitTester.Hit(commands, new DrawPoint(200, 200)).Should().BeNull();
    }

    [Fact]
    [Trait("Category", "HitTest")]
    public void The_topmost_element_wins()
    {
        // 两个元素叠在一起。列表顺序就是层叠顺序，后画的在上面。
        var commands = new DrawCommand[]
        {
            Shape("under", 0, 0, 100, 100),
            Shape("over", 40, 40, 100, 100),
        };

        HitTester.Hit(commands, new DrawPoint(50, 50)).Should().Be("over");
        HitTester.Hit(commands, new DrawPoint(10, 10)).Should().Be("under");
    }

    [Fact]
    [Trait("Category", "HitTest")]
    public void A_label_hits_the_element_that_owns_it()
    {
        // 一个节点的标签与它的形状共用一个元素标识。标签落在形状之外时，
        // 按外接矩形判形状会漏掉它——那正是"点在字上却没反应"。
        var commands = new DrawCommand[]
        {
            Shape("a", 0, 0, 100, 50),
            Text("a", 10, 60, 80, 20),
        };

        HitTester.Hit(commands, new DrawPoint(50, 70)).Should().Be("a");
    }

    [Fact]
    [Trait("Category", "HitTest")]
    public void An_edge_is_hit_only_near_the_line_itself()
    {
        // 一条从左上到右下的折线。它的外接矩形覆盖整个方框，
        // 按外接矩形判会让右上角与左下角这两个空白处也选中这条边。
        var commands = new DrawCommand[]
        {
            Polyline("e", new DrawPoint(0, 0), new DrawPoint(100, 100)),
        };

        HitTester.Hit(commands, new DrawPoint(50, 50), tolerance: 2).Should().Be("e");
        HitTester.Hit(commands, new DrawPoint(95, 5), tolerance: 2).Should().BeNull();
    }

    [Fact]
    [Trait("Category", "HitTest")]
    public void The_tolerance_is_measured_from_the_nearest_segment()
    {
        var commands = new DrawCommand[]
        {
            Polyline("e", new DrawPoint(0, 0), new DrawPoint(100, 0), new DrawPoint(100, 100)),
        };

        // 落在折点外侧一点点，离两段都只有几个单位。
        HitTester.Hit(commands, new DrawPoint(103, 3), tolerance: 5).Should().Be("e");
        HitTester.Hit(commands, new DrawPoint(103, 3), tolerance: 1).Should().BeNull();

        // 容差为零时只有正好落在线上才算命中。
        HitTester.Hit(commands, new DrawPoint(50, 0), tolerance: 0).Should().Be("e");
        HitTester.Hit(commands, new DrawPoint(50, 1), tolerance: 0).Should().BeNull();
    }

    [Fact]
    [Trait("Category", "HitTest")]
    public void A_segment_end_does_not_extend_past_the_line()
    {
        // 点到线段的距离要把投影参数夹在线段之内。不夹的话算的是
        // "到无限长直线"的距离，线段两端之外的远处也会得到零距离。
        var commands = new DrawCommand[]
        {
            Polyline("e", new DrawPoint(0, 0), new DrawPoint(100, 0)),
        };

        HitTester.Hit(commands, new DrawPoint(500, 0), tolerance: 5).Should().BeNull();
        HitTester.Hit(commands, new DrawPoint(-500, 0), tolerance: 5).Should().BeNull();
    }

    [Fact]
    [Trait("Category", "HitTest")]
    public void An_empty_list_finds_nothing()
    {
        HitTester.Hit([], new DrawPoint(0, 0)).Should().BeNull();
    }

    [Fact]
    [Trait("Category", "HitTest")]
    public void An_empty_polyline_finds_nothing()
    {
        HitTester.Hit([Polyline("e")], new DrawPoint(0, 0), tolerance: 100).Should().BeNull();
    }

    [Fact]
    [Trait("Category", "HitTest")]
    public void A_single_point_polyline_still_hits()
    {
        // 退化成一点的折线在列表里不该出现，但真出现了也不能让整次命中崩掉。
        HitTester.Hit([Polyline("e", new DrawPoint(10, 10))], new DrawPoint(10, 12), tolerance: 5)
            .Should().Be("e");
    }

    private static DrawShape Shape(string id, double x, double y, double width, double height) =>
        new(id, NodeShape.Rect, new SpatialRect(x, y, width, height), "#ffffff", "#000000", 1, LineStyle.Solid, 0, 1);

    private static DrawText Text(string id, double x, double y, double width, double height) =>
        new(id, "标签", new SpatialRect(x, y, width, height), "#000000", "sans", 12, FontWeight.Normal);

    private static DrawPolyline Polyline(string id, params DrawPoint[] points) =>
        new(id, points, "#000000", 1, LineStyle.Solid, ArrowStyle.None);
}
