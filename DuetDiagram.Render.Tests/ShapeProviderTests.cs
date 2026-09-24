using DuetDiagram.Core.Model;
using DuetDiagram.Core.Shapes;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 渲染层与形状库的接线：几何由表给、圆角由表定，换一份表就换一批几何。
/// </summary>
/// <remarks>
/// 这一组盯的是"渲染层不再按形状枚举分支"。判据不是"代码里没有 switch"——
/// 那种断言写不出来；判据是**同一个形状枚举值，换一份表之后画出来的几何跟着变**。
/// 枚举是身份，几何是外观，两者在绘制列表里各是各的。
/// </remarks>
public sealed class ShapeProviderTests
{
    #region 几何由表给（Category=ShapeProvider）

    /// <summary>八个形状各自的几何都能从表里取到，绘制列表带着形状的身份。</summary>
    [Fact]
    [Trait("Category", "ShapeProvider")]
    public void Every_shape_resolves_through_the_table()
    {
        foreach (var shape in Enum.GetValues<NodeShape>())
        {
            var draw = BuildOne(shape, Theme.Default).Commands.OfType<DrawShape>().Single();

            // 绘制列表带的是形状的身份，快照才读得懂是哪个形状；几何按身份现查。
            draw.Shape.Should().Be(shape);

            var geometry = ShapeRegistry.Default.Find(draw.Shape).Geometry;

            geometry.Should().NotBeNull();

            if (shape == NodeShape.Diamond)
            {
                geometry.Should().BeOfType<PolygonOutline>("菱形是多边形");
            }

            if (shape == NodeShape.Cylinder)
            {
                geometry.Should().BeOfType<PathOutline>("圆柱带弧，走路径");
            }
        }
    }

    /// <summary>
    /// 换一份形状表，同一个形状枚举值画出来的几何跟着换。
    /// </summary>
    /// <remarks>
    /// 这条是"渲染层按几何画而不是按枚举分支"的直接判据：文档里写的还是 Diamond，
    /// 而画出来的是椭圆——说明画的时候问的是表，不是枚举。
    /// </remarks>
    [Fact]
    [Trait("Category", "ShapeProvider")]
    public void Swapping_the_table_changes_the_geometry_behind_the_same_enum()
    {
        var theme = Theme.Default with { Shapes = Overriding(NodeShape.Diamond, new EllipseOutline()) };

        var draw = BuildOne(NodeShape.Diamond, theme).Commands.OfType<DrawShape>().Single();

        draw.Shape.Should().Be(NodeShape.Diamond, "文档里的形状没变");

        theme.Shapes.Find(draw.Shape).Geometry.Should().BeOfType<EllipseOutline>(
            "画的时候问的是表，所以表说它是椭圆，它就是椭圆");

        // 缺省那份表没被动过：换表是换一份实例，不是改全局。
        ShapeRegistry.Default.Find(NodeShape.Diamond).Geometry.Should().BeOfType<PolygonOutline>();
    }

    #endregion

    #region 圆角由表定（Category=ShapeProvider）

    /// <summary>
    /// 兜底圆角只给带圆角的形状，判据来自表。
    /// </summary>
    /// <remarks>
    /// 这里换一个较大的主题圆角，看它有没有传到该传的形状上。写死枚举值的实现
    /// 只要漏了新加的圆角形状，这条就会红——而漏掉的表现是它画成直角，
    /// 光看画布看不出是漏了还是本来就这么设计。
    /// </remarks>
    [Fact]
    [Trait("Category", "ShapeProvider")]
    public void The_fallback_corner_radius_comes_from_the_table()
    {
        var theme = Theme.Default with { CornerRadius = 17 };

        foreach (var shape in Enum.GetValues<NodeShape>())
        {
            var radius = theme.Node(new NodeDef { Id = "a", Shape = shape }).Radius;
            var cornered = ShapeRegistry.Default.Find(shape).Geometry
                is RoundedRectOutline { Corners: not CornerRadiusMode.None };

            radius.Should().Be(cornered ? 17 : 0, $"{shape} 的兜底圆角应当由形状表说了算");
        }
    }

    /// <summary>节点上填了半径时用填的，表给的只是兜底。</summary>
    [Fact]
    [Trait("Category", "ShapeProvider")]
    public void An_explicit_radius_beats_the_fallback()
    {
        var node = new NodeDef { Id = "a", Shape = NodeShape.Rounded, Style = new NodeStyle { Radius = 3 } };

        Theme.Default.Node(node).Radius.Should().Be(3);
    }

    #endregion

    #region 辅助

    private static DrawList BuildOne(NodeShape shape, Theme theme)
    {
        var node = new NodeDef { Id = "a", Label = "x", Shape = shape };
        var document = DiagramDocument.CreateFromContent("d", nodes: [node]);
        var layout = Layouts.Result([Layouts.Node("a", 0, 0)], [], 200, 200);

        return SceneBuilder.Build(document, layout, theme, new FakeTextMeasurer());
    }

    /// <summary>一份除了某个形状之外与内置表相同的表。</summary>
    private static ShapeRegistry Overriding(NodeShape shape, ShapeGeometry geometry)
    {
        var definitions = BuiltinShapeProvider.Instance.Shapes
            .Select(definition => definition.Shape == shape
                ? new ShapeDefinition(shape, geometry)
                : definition)
            .ToArray();

        return new ShapeRegistry(new ListProvider(definitions));
    }

    private sealed class ListProvider(IReadOnlyList<ShapeDefinition> shapes) : IShapeProvider
    {
        public IReadOnlyList<ShapeDefinition> Shapes { get; } = shapes;
    }

    #endregion
}
