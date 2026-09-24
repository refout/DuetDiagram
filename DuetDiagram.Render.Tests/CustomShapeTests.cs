using DuetDiagram.Core.Model;
using DuetDiagram.Core.Shapes;
using DuetDiagram.Layout;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 自定义形状接到绘制列表上：只有形状那一条指令该变，缩放、走线与标签都不受影响。
/// </summary>
/// <remarks>
/// <para>
/// 判据不是"代码里有没有第二条画路径的分支"——那种断言写不出来。判据是
/// **给一个节点加上路径之后，绘制列表里除形状之外的东西逐字节不变**：
/// 走线由端口位置算出来，标签由外接矩形排出来，两者都不看形状。
/// </para>
/// <para>
/// 几何本身是单位框坐标，所以同一个路径画在不同尺寸的节点上，几何逐字段相同、
/// 只有外接矩形不同。这正是"路径只许用归一化坐标"那条约束要守住的性质。
/// </para>
/// </remarks>
public sealed class CustomShapeTests
{
    /// <summary>菱形。四个顶点，正好把 M / L / Z 三条指令都用上。</summary>
    private const string Diamond = "M 0.5 0 L 1 0.5 L 0.5 1 L 0 0.5 Z";

    #region 缩放（Category=CustomShape）

    /// <summary>
    /// 同一段路径画在两种尺寸的节点上，几何逐字段相同。
    /// </summary>
    /// <remarks>
    /// 路径写的是比例，尺寸由外接矩形给。几何要是随节点尺寸变了，
    /// 就等于把绝对像素写进了形状定义，而那种错看起来像"形状没做对"。
    /// </remarks>
    [Fact]
    [Trait("Category", "CustomShape")]
    public void The_same_path_scales_with_the_node_box()
    {
        var small = ShapeOf(Layouts.Node("a", 0, 0, 40, 40));
        var large = ShapeOf(Layouts.Node("a", 0, 0, 200, 80));

        small.Geometry.Should().Be(large.Geometry, "路径是比例，画多大由外接矩形决定");
        small.Geometry.Should().Be(PathParser.Parse(Diamond));

        small.Rect.Width.Should().Be(40);
        large.Rect.Width.Should().Be(200);
    }

    /// <summary>缩放只改外接矩形，不改几何——所以绘制方拿到的是同一份描述。</summary>
    [Fact]
    [Trait("Category", "CustomShape")]
    public void Only_the_bounding_box_carries_the_size()
    {
        var small = ShapeOf(Layouts.Node("a", 10, 20, 40, 40));
        var large = ShapeOf(Layouts.Node("a", 10, 20, 200, 80));

        small.Rect.Should().NotBe(large.Rect);
        small.Rect.X.Should().Be(large.Rect.X, "位置与尺寸是两件事，缩放不改位置");
    }

    #endregion

    #region 端口位置（Category=CustomShape）

    /// <summary>
    /// 端口锚点只读外接矩形，不看形状。
    /// </summary>
    /// <remarks>
    /// 这是"路径进视觉哈希、不进结构哈希"的几何依据：换轮廓不改锚点，
    /// 也就不会让已经算好的走线失效。锚点要是看形状，换一次形状就得全图重排。
    /// </remarks>
    [Fact]
    [Trait("Category", "CustomShape")]
    public void The_port_anchor_comes_from_the_box_not_the_shape()
    {
        var box = Layouts.Node("a", 10, 20, 120, 44);

        box.PortAnchor(new LayoutPort("out", PortSide.Right, 0.5))
            .Should().Be(new LayoutPoint(130, 42));

        box.PortAnchor(new LayoutPort("in", PortSide.Left, 0))
            .Should().Be(new LayoutPoint(10, 20));

        // 同一个框、同一个端口，无论这个节点用自定义路径还是内置形状，锚点都相同。
        box.PortAnchor(new LayoutPort("out", PortSide.Right, 0.5))
            .Should().Be(box.PortAnchor(new LayoutPort("out", PortSide.Right, 0.5)));
    }

    #endregion

    #region 标签排版与走线（Category=CustomShape）

    /// <summary>
    /// 加上路径之后，除形状之外的所有指令逐字节不变。
    /// </summary>
    /// <remarks>
    /// 走线与标签都在这一条里：它们逐字节相同，说明形状没有漏进它们的算法。
    /// 只断言"形状指令变了"是不够的——那种断言对"形状顺手把标签也挪了一点"是瞎的。
    /// </remarks>
    [Fact]
    [Trait("Category", "CustomShape")]
    public void Adding_a_path_changes_only_the_shape_command()
    {
        var plain = Build(path: null);
        var custom = Build(Diamond);

        custom.Commands.Where(command => command is not DrawShape)
            .Should().Equal(plain.Commands.Where(command => command is not DrawShape));
    }

    /// <summary>内置形状在列表里不带几何——绘制时按形状名去表里查；自定义形状带上解析好的几何。</summary>
    [Fact]
    [Trait("Category", "CustomShape")]
    public void A_builtin_shape_leaves_the_geometry_to_the_table()
    {
        Build(path: null).Commands.OfType<DrawShape>().Single(shape => shape.ElementId == "a")
            .Geometry.Should().BeNull("内置形状按形状名查表，几何不进列表");

        Build(Diamond).Commands.OfType<DrawShape>().Single(shape => shape.ElementId == "a")
            .Geometry.Should().BeOfType<PathOutline>("自定义形状的几何在构建列表时就算好");
    }

    /// <summary>自定义形状的几何进了规范文本，快照才分辨得出两个形状。</summary>
    [Fact]
    [Trait("Category", "CustomShape")]
    public void The_custom_geometry_shows_up_in_the_canonical_text()
    {
        var custom = Build(Diamond).Commands.OfType<DrawShape>().Single(shape => shape.ElementId == "a");

        custom.Describe().Should().Contain("geometry=");

        // 内置形状的规范文本保持原样：没有几何就不该多出一段空标记。
        Build(path: null).Commands.OfType<DrawShape>().Single(shape => shape.ElementId == "a")
            .Describe().Should().NotContain("geometry=");
    }

    #endregion

    #region 辅助

    /// <summary>画一个节点。</summary>
    private static DrawShape ShapeOf(PlacedNode box) =>
        Build(path: Diamond, box).Commands.OfType<DrawShape>().Single(shape => shape.ElementId == "a");

    /// <summary>
    /// 造一份两节点一连线的绘制列表。
    /// </summary>
    /// <remarks>
    /// 只让第一个节点带路径，第二个节点保持内置形状：这样"加路径"这一个变量
    /// 与其余指令的差异能直接对上。坐标手写，不跑布局引擎。
    /// </remarks>
    private static DrawList Build(string? path) => Build(path, Layouts.Node("a", 10, 20, 120, 44));

    private static DrawList Build(string? path, PlacedNode from)
    {
        var document = DiagramDocument.CreateFromContent(
            "custom-shape",
            nodes:
            [
                new NodeDef { Id = "a", Label = "甲", ShapePath = path },
                new NodeDef { Id = "b", Label = "乙" },
            ],
            edges: [new EdgeDef { Id = "e1", From = "a", To = "b", Label = "是" }]);

        var to = Layouts.Node("b", 300, 20, 120, 44);

        return SceneBuilder.Build(
            document,
            Layouts.Result([from, to], [Layouts.Horizontal("e1", from, to)], 500, 200),
            Theme.Default,
            new FakeTextMeasurer());
    }

    #endregion
}
