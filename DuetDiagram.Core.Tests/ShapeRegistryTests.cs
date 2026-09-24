using System.Reflection;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Shapes;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 形状库：八个内置形状齐不齐、名字与枚举对不对得上、重复注册挡不挡得住。
/// </summary>
/// <remarks>
/// 这一组盯的是"加一个形状只改一处"能不能成立。形状表是三处（渲染、工具、属性面板）
/// 共同的上游，它少一个形状或者多一个名字，都会在别处变成静默故障。
/// </remarks>
public sealed class ShapeRegistryTests
{
    #region 注册表的内容（Category=ShapeRegistry）

    /// <summary>八个内置形状都在，名字与枚举逐条对得上。</summary>
    [Fact]
    [Trait("Category", "ShapeRegistry")]
    public void The_builtin_table_covers_every_enum_value_with_the_same_names()
    {
        var registry = ShapeRegistry.Default;

        registry.All.Should().HaveCount(8);

        registry.All.Select(definition => definition.Name)
            .Should().Equal(Enum.GetNames<NodeShape>(), "名字直接取枚举成员名，两处不该有第二种写法");

        foreach (var shape in Enum.GetValues<NodeShape>())
        {
            registry.Find(shape).Shape.Should().Be(shape);
        }
    }

    /// <summary>每个形状的几何是它该有的那一种。</summary>
    [Fact]
    [Trait("Category", "ShapeRegistry")]
    public void Each_builtin_shape_carries_the_geometry_it_is_named_after()
    {
        var registry = ShapeRegistry.Default;

        // 圆角矩形族：三种只差"圆角从哪来"。
        registry.Find(NodeShape.Rect).Geometry
            .Should().Be(new RoundedRectOutline(CornerRadiusMode.None));
        registry.Find(NodeShape.Rounded).Geometry
            .Should().Be(new RoundedRectOutline(CornerRadiusMode.FromStyle));
        registry.Find(NodeShape.Stadium).Geometry
            .Should().Be(new RoundedRectOutline(CornerRadiusMode.HalfMinSide));

        registry.Find(NodeShape.Circle).Geometry.Should().Be(new EllipseOutline());

        // 多边形族：顶点在单位框里，比例对了就行，绝对尺寸由外接矩形给。
        var diamond = registry.Find(NodeShape.Diamond).Geometry.Should().BeOfType<PolygonOutline>().Subject;
        diamond.Points.Should().Equal(
            new ShapePoint(0.5, 0), new ShapePoint(1, 0.5), new ShapePoint(0.5, 1), new ShapePoint(0, 0.5));

        var hexagon = registry.Find(NodeShape.Hexagon).Geometry.Should().BeOfType<PolygonOutline>().Subject;
        hexagon.Points.Should().HaveCount(6, "六边形有六个顶点");
        hexagon.Points.Should().OnlyContain(
            point => point.X >= 0 && point.X <= 1 && point.Y >= 0 && point.Y <= 1,
            "顶点写在单位框里，超出去就是画到框外了");

        var parallelogram = registry.Find(NodeShape.Parallelogram).Geometry
            .Should().BeOfType<PolygonOutline>().Subject;
        parallelogram.Points.Should().HaveCount(4);

        // 圆柱是唯一带弧的，走路径。
        var cylinder = registry.Find(NodeShape.Cylinder).Geometry.Should().BeOfType<PathOutline>().Subject;
        cylinder.Segments.Should().HaveCount(3, "两段弧加一条竖边");
        cylinder.Segments.OfType<PathArc>().Should().HaveCount(2);
        cylinder.Segments.OfType<PathLine>().Should().HaveCount(1);
    }

    /// <summary>顶点不同的两个多边形不相等——快照与缓存都靠这条。</summary>
    [Fact]
    [Trait("Category", "ShapeRegistry")]
    public void Geometry_compares_by_value_not_by_reference()
    {
        var one = new PolygonOutline([new ShapePoint(0, 0), new ShapePoint(1, 0), new ShapePoint(0, 1)]);
        var same = new PolygonOutline([new ShapePoint(0, 0), new ShapePoint(1, 0), new ShapePoint(0, 1)]);
        var other = new PolygonOutline([new ShapePoint(0, 0), new ShapePoint(1, 0), new ShapePoint(1, 1)]);

        one.Should().Be(same, "顶点一样就是同一份几何");
        one.GetHashCode().Should().Be(same.GetHashCode());
        one.Should().NotBe(other);

        var path = new PathOutline(new ShapePoint(0, 0), [new PathLine(new ShapePoint(1, 1))]);
        var samePath = new PathOutline(new ShapePoint(0, 0), [new PathLine(new ShapePoint(1, 1))]);

        path.Should().Be(samePath);
        path.GetHashCode().Should().Be(samePath.GetHashCode());
    }

    #endregion

    #region 注册时的校验（Category=ShapeRegistry）

    /// <summary>同一个形状注册两次会被拒。</summary>
    [Fact]
    [Trait("Category", "ShapeRegistry")]
    public void Registering_one_shape_twice_is_refused()
    {
        var twice = new FakeProvider(
        [
            new ShapeDefinition(NodeShape.Rect, new RoundedRectOutline(CornerRadiusMode.None)),
            new ShapeDefinition(NodeShape.Rect, new EllipseOutline()),
        ]);

        var build = () => new ShapeRegistry(twice);

        build.Should().Throw<InvalidOperationException>()
            .WithMessage("*Rect*两次*", "后注册的把先注册的顶掉，是那种「看起来这一轮没做」的静默故障");
    }

    /// <summary>枚举里有值没人提供会被拒——注册表必须覆盖枚举，否则画到那个形状时无从下手。</summary>
    [Fact]
    [Trait("Category", "ShapeRegistry")]
    public void A_table_that_misses_an_enum_value_is_refused()
    {
        var partial = new FakeProvider(
            [new ShapeDefinition(NodeShape.Rect, new RoundedRectOutline(CornerRadiusMode.None))]);

        var build = () => new ShapeRegistry(partial);

        build.Should().Throw<InvalidOperationException>()
            .WithMessage("*没有人提供*");
    }

    /// <summary>按枚举取值：注册表里有的能取到，取不到也不会静默给一个默认形状。</summary>
    [Fact]
    [Trait("Category", "ShapeRegistry")]
    public void Looking_up_a_shape_never_silently_falls_back()
    {
        var registry = ShapeRegistry.Default;

        registry.TryFind(NodeShape.Diamond, out var found).Should().BeTrue();
        found.Name.Should().Be("Diamond");

        // 表里没有的值只可能来自"枚举加了成员而没人提供"，构造时已经挡住了；
        // 万一绕过去，这里要抛而不是给个矩形——退回矩形的表现是"形状变了"，
        // 而用户看到的是"认不出来"才对。
        registry.TryFind((NodeShape)9999, out _).Should().BeFalse();
        var look = () => registry.Find((NodeShape)9999);
        look.Should().Throw<KeyNotFoundException>();
    }

    #endregion

    #region 沙箱面（Category=ShapeRegistry）

    /// <summary>
    /// 接口上不给任何 IO 能力。
    /// </summary>
    /// <remarks>
    /// 第三方形状提供者是要被关起来的：它只该回答"我提供哪些形状"，不该读文件、连网络。
    /// 这条判据要的是"它做不到"而不是"它不该做"：接口上只要出现一个能读文件或连网络的
    /// 成员，第三方实现就多了一条出口，而那种出口在代码评审里看得见、在运行时看不见。
    /// 所以这里数的是接口自己声明了什么。
    /// </remarks>
    [Fact]
    [Trait("Category", "ShapeRegistry")]
    public void The_provider_interface_hands_out_nothing_but_shapes()
    {
        var declared = typeof(IShapeProvider)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(member => member.MemberType != MemberTypes.Method
                || !member.Name.StartsWith("get_", StringComparison.Ordinal))
            .ToArray();

        declared.Should().ContainSingle(
            "提供者只该回答「我提供哪些形状」；多一个成员就多一条沙箱出口")
            .Which.Name.Should().Be(nameof(IShapeProvider.Shapes));
    }

    #endregion

    /// <summary>测试用的提供者：给什么就是什么。</summary>
    private sealed class FakeProvider(IReadOnlyList<ShapeDefinition> shapes) : IShapeProvider
    {
        public IReadOnlyList<ShapeDefinition> Shapes { get; } = shapes;
    }
}
