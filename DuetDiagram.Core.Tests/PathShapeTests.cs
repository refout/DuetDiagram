using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Core.Shapes;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 自定义形状的路径：认得的语法、认不出时的结构化错误、以及与内置形状的合流点。
/// </summary>
/// <remarks>
/// <para>
/// 这一组的重心在**认不出时给什么**。跳过一条认不出的指令，画出来的形状缺一块，
/// 而用户以为是自己写错了——所以每条错误都要带行号与那一行的原文，
/// 少了任何一样，路径长起来之后就定位不到。
/// </para>
/// <para>
/// 另一半是合流点：内置形状与自定义形状最后要是同一份几何类型、走同一个绘制方。
/// 判据是同一个路径文本在任何空白写法下解析出**同一份**轮廓——哈希与缓存都靠这条。
/// </para>
/// </remarks>
public sealed class PathShapeTests
{
    /// <summary>菱形。四个顶点，正好把 M / L / Z 三条指令都用上。</summary>
    private const string Diamond = "M 0.5 0 L 1 0.5 L 0.5 1 L 0 0.5 Z";

    #region 认得的语法（Category=PathShape）

    /// <summary>最省的写法：一个起点加一段线。</summary>
    [Fact]
    [Trait("Category", "PathShape")]
    public void A_start_and_a_line_is_a_valid_path()
    {
        var outline = PathParser.Parse("M 0 0 L 1 1");

        outline.Start.Should().Be(new ShapePoint(0, 0));
        outline.Segments.Should().ContainSingle().Which.Should().Be(new PathLine(new ShapePoint(1, 1)));
    }

    /// <summary>逗号与空白两种分隔都认——手写的人两种都会用。</summary>
    [Fact]
    [Trait("Category", "PathShape")]
    public void Commas_and_whitespace_are_both_separators()
    {
        var outline = PathParser.Parse("M,0,0 L 1,0.5 L,0.5,1 L 0 0.5 Z");

        outline.Segments.Should().HaveCount(3);
        outline.Segments.Should().OnlyContain(segment => segment is PathLine);
    }

    /// <summary>几条指令挤在一行里也行——模型写路径时常常这么写。</summary>
    [Fact]
    [Trait("Category", "PathShape")]
    public void Several_commands_may_share_one_line()
    {
        var outline = PathParser.Parse("M 0 0 L 1 0 L 1 1 L 0 1 Z");

        outline.Segments.Should().HaveCount(3);
    }

    /// <summary>一行一条也行，且解析结果与挤在一行完全相同。</summary>
    [Fact]
    [Trait("Category", "PathShape")]
    public void One_command_per_line_parses_the_same_way()
    {
        var oneLine = PathParser.Parse(Diamond);
        var manyLines = PathParser.Parse("M 0.5 0\nL 1 0.5\nL 0.5 1\nL 0 0.5\nZ");

        manyLines.Should().Be(oneLine, "空白只影响可读性，不该影响解析出来的几何");
    }

    /// <summary>弧带着半径与方向。</summary>
    [Fact]
    [Trait("Category", "PathShape")]
    public void An_arc_carries_its_radii_and_direction()
    {
        var outline = PathParser.Parse("M 0 0 A 0.5 0.25 0 0 1 1 1");

        var arc = outline.Segments.Should().ContainSingle().Which.Should().BeOfType<PathArc>().Subject;

        arc.To.Should().Be(new ShapePoint(1, 1));
        arc.RadiusX.Should().Be(0.5);
        arc.RadiusY.Should().Be(0.25);
        arc.Clockwise.Should().BeTrue("第五个参数为 1 是顺时针");
    }

    /// <summary>第五个参数为 0 是逆时针。</summary>
    [Fact]
    [Trait("Category", "PathShape")]
    public void The_sweep_flag_picks_the_direction()
    {
        PathParser.Parse("M 0 0 A 0.5 0.5 0 0 0 1 1").Segments
            .Should().ContainSingle().Which.Should().BeOfType<PathArc>()
            .Which.Clockwise.Should().BeFalse();
    }

    /// <summary>闭合的小写 z 与大写 Z 是同一个意思。</summary>
    [Fact]
    [Trait("Category", "PathShape")]
    public void A_lowercase_z_closes_the_same_way()
    {
        PathParser.Parse("M 0.5 0 L 1 0.5 z")
            .Should().Be(PathParser.Parse("M 0.5 0 L 1 0.5 Z"));
    }

    /// <summary>Z 可以省；省了也按闭合画，几何与写了 Z 相同。</summary>
    [Fact]
    [Trait("Category", "PathShape")]
    public void The_closing_command_is_optional()
    {
        PathParser.Parse("M 0.5 0 L 1 0.5")
            .Should().Be(PathParser.Parse("M 0.5 0 L 1 0.5 Z"), "闭合是画法，不是几何的一部分");
    }

    #endregion

    #region 认不出的给结构化错误（Category=PathShape）

    /// <summary>认不出的指令：报出它的行号与那一行的原文，而不是跳过。</summary>
    [Fact]
    [Trait("Category", "PathShape")]
    public void An_unknown_command_is_rejected_with_its_line_and_source()
    {
        var error = Reject("M 0 0\nQ 1 1");

        error.Line.Should().Be(2);
        error.LineText.Should().Be("Q 1 1");
        error.Detail.Should().Contain("Q");
    }

    /// <summary>相对坐标的小写一律拒绝——本仓只有单位框一套坐标，"相对谁"没有答案。</summary>
    [Theory]
    [InlineData("m 0 0")]
    [InlineData("M 0 0\nl 1 1")]
    [InlineData("M 0 0\na 0.5 0.5 0 0 1 1 1")]
    [Trait("Category", "PathShape")]
    public void A_lowercase_relative_command_is_rejected(string pathData)
    {
        Reject(pathData).Detail.Should().Contain("大写");
    }

    /// <summary>坐标越出单位框：报在**那个数值**所在的行，而不是整段路径。</summary>
    [Theory]
    [InlineData("M 0 0\nL 1.2 0.5", 2)]
    [InlineData("M -0.1 0 L 1 1", 1)]
    [InlineData("M 0 0\nL 0.5 0.5\nL 1 1.5", 3)]
    [Trait("Category", "PathShape")]
    public void A_coordinate_outside_the_unit_box_is_rejected_with_its_line(string pathData, int line)
    {
        var error = Reject(pathData);

        error.Line.Should().Be(line);
        error.Detail.Should().Contain("0 到 1");
    }

    /// <summary>不是数的参数：同样报在它所在的那一行。</summary>
    [Fact]
    [Trait("Category", "PathShape")]
    public void A_non_number_is_rejected_with_its_line()
    {
        var error = Reject("M 0 0\nL x 0.5");

        error.Line.Should().Be(2);
        error.LineText.Should().Be("L x 0.5");
        error.Detail.Should().Contain("不是一个数");
    }

    /// <summary>
    /// 参数不够：报在**指令那一行**。
    /// </summary>
    /// <remarks>
    /// 出问题的是这条指令少写了参数，而不是它后面那个东西。报在别处会让人去改一个没写错的地方。
    /// </remarks>
    [Fact]
    [Trait("Category", "PathShape")]
    public void A_command_with_too_few_arguments_is_reported_on_the_command_line()
    {
        var error = Reject("M 0 0\nL 0.5");

        error.Line.Should().Be(2);
        error.LineText.Should().Be("L 0.5");
        error.Detail.Should().Contain("2 个参数");
    }

    /// <summary>第二个 M 会被拒——路径只有一个起点。</summary>
    [Fact]
    [Trait("Category", "PathShape")]
    public void A_second_start_is_rejected()
    {
        var error = Reject("M 0 0\nM 1 1");

        error.Line.Should().Be(2);
        error.Detail.Should().Contain("只能有一个 M");
    }

    /// <summary>还没有起点就开始画线：第一条指令要是 M。</summary>
    [Fact]
    [Trait("Category", "PathShape")]
    public void Drawing_before_a_start_is_rejected()
    {
        var error = Reject("L 0.5 0.5");

        error.Line.Should().Be(1);
        error.Detail.Should().Contain("M");
    }

    /// <summary>Z 不带参数。</summary>
    [Fact]
    [Trait("Category", "PathShape")]
    public void A_closing_command_with_arguments_is_rejected()
    {
        var error = Reject("M 0 0 L 1 1 Z 0.5");

        error.Line.Should().Be(1);
        error.LineText.Should().Be("M 0 0 L 1 1 Z 0.5");
        error.Detail.Should().Contain("Z 不带参数");
    }

    /// <summary>闭合之后不能再有指令——那些指令落在形状之外，静默收下等于画了个看不出来的东西。</summary>
    [Fact]
    [Trait("Category", "PathShape")]
    public void A_command_after_closing_is_rejected()
    {
        var error = Reject("M 0 0\nL 1 1\nZ\nL 0 0");

        error.Line.Should().Be(4);
        error.Detail.Should().Contain("Z 之后");
    }

    /// <summary>只有起点、没有线段：画不出东西。</summary>
    [Fact]
    [Trait("Category", "PathShape")]
    public void A_path_with_only_a_start_is_rejected()
    {
        Reject("M 0.5 0.5").Detail.Should().Contain("至少要有一段");
    }

    /// <summary>空路径：至少要有一条 M。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    [Trait("Category", "PathShape")]
    public void An_empty_path_is_rejected(string pathData)
    {
        Reject(pathData).Detail.Should().Contain("空的");
    }

    /// <summary>弧的旋转、大小弧与方向三项几何模型里没有的取值都要单独拒。</summary>
    [Theory]
    [InlineData("M 0 0 A 0.5 0.5 0.5 0 1 1 1", "旋转")]
    [InlineData("M 0 0 A 0.5 0.5 0 0.5 1 1 1", "小弧")]
    [InlineData("M 0 0 A 0.5 0.5 0 0 0.5 1 1", "0 或 1")]
    [Trait("Category", "PathShape")]
    public void An_arc_flag_outside_what_the_geometry_supports_is_rejected(string pathData, string expected)
    {
        Reject(pathData).Detail.Should().Contain(expected);
    }

    /// <summary>
    /// 抛出来的那条路带的信息与 TryParse 一样多。
    /// </summary>
    /// <remarks>
    /// 字段写入走的是抛；整体校验走的是 TryParse。两条路的错误文本要是分叉了，
    /// 用户在面板上看到的与校验报告里看到的就是两句话，而它们说的是同一处错。
    /// </remarks>
    [Fact]
    [Trait("Category", "PathShape")]
    public void Parse_throws_the_same_error_it_would_report()
    {
        var thrower = () => PathParser.Parse("M 0 0\nQ 1 1");

        var error = thrower.Should().Throw<PathSyntaxException>().Which;

        error.Line.Should().Be(2);
        error.LineText.Should().Be("Q 1 1");
        error.Message.Should().Contain("Q 1 1").And.Contain("2");
    }

    #endregion

    #region 合流点（Category=PathShape）

    /// <summary>有路径文本就是自定义形状，只有空白不算。</summary>
    [Fact]
    [Trait("Category", "PathShape")]
    public void Only_a_non_blank_path_counts_as_custom()
    {
        PathShape.IsCustom(new NodeDef { Id = "a", ShapePath = Diamond }).Should().BeTrue();
        PathShape.IsCustom(new NodeDef { Id = "a", ShapePath = "   " }).Should().BeFalse();
        PathShape.IsCustom(new NodeDef { Id = "a" }).Should().BeFalse();
    }

    /// <summary>
    /// 内置与自定义两条路给出同一份几何类型。
    /// </summary>
    /// <remarks>
    /// 这是"渲染层没有第二条画路径的代码"的判据：绘制方拿到的东西只有一种形状，
    /// 它无从知道这份几何是表里查的还是节点自己写的。
    /// </remarks>
    [Fact]
    [Trait("Category", "PathShape")]
    public void The_path_wins_over_the_builtin_shape_at_the_merge_point()
    {
        var custom = new NodeDef { Id = "a", Shape = NodeShape.Diamond, ShapePath = Diamond };
        var builtin = custom with { ShapePath = null };

        PathShape.GeometryOf(custom, ShapeRegistry.Default).Should().BeOfType<PathOutline>();
        PathShape.GeometryOf(builtin, ShapeRegistry.Default).Should().BeOfType<PolygonOutline>();
    }

    /// <summary>坏路径在合流点抛出来，而不是悄悄退回内置形状。</summary>
    [Fact]
    [Trait("Category", "PathShape")]
    public void A_broken_path_throws_instead_of_falling_back()
    {
        var node = new NodeDef { Id = "a", Shape = NodeShape.Diamond, ShapePath = "M 0 0 Q 1 1" };

        var resolve = () => PathShape.GeometryOf(node, ShapeRegistry.Default);

        resolve.Should().Throw<PathSyntaxException>(
            "退回矩形或内置形状的表现是「形状变了」，而用户看到的是「认不出来」才对");
    }

    /// <summary>
    /// 路径进视觉哈希、不进结构哈希。
    /// </summary>
    /// <remarks>
    /// 换轮廓不改节点坐标：尺寸由标签量出来，端口位置由所在边与偏移算出来，两者都不看形状。
    /// 把它算进结构哈希会让每次换形状都白白重排一次，而重排的代价是整张图重算坐标。
    /// </remarks>
    [Fact]
    [Trait("Category", "PathShape")]
    public void A_custom_path_moves_the_visual_hash_but_not_the_structural_hash()
    {
        var plain = IrFixtures.Base();
        var shaped = IrFixtures.WithNode(plain, new NodeDef { Id = "a", Label = "甲", ShapePath = Diamond });

        shaped.StructuralHash.Should().Be(plain.StructuralHash, "形状不改坐标");
        shaped.VisualHash.Should().NotBe(plain.VisualHash, "换了轮廓画出来就不一样");
    }

    /// <summary>
    /// 换一个空白写法不改变几何，也就不该让坐标失效。
    /// </summary>
    /// <remarks>
    /// 只断言结构哈希：路径文本是按原样进视觉哈希的（与标签、样式那些字符串同一处置），
    /// 所以换写法会让视觉哈希动一下，那只是多一次重绘。要紧的是别让它动到布局。
    /// </remarks>
    [Fact]
    [Trait("Category", "PathShape")]
    public void Rewriting_a_path_with_other_whitespace_does_not_invalidate_layout()
    {
        var oneLine = IrFixtures.WithNode(IrFixtures.Base(), new NodeDef { Id = "a", Label = "甲", ShapePath = Diamond });
        var manyLines = IrFixtures.WithNode(
            IrFixtures.Base(),
            new NodeDef { Id = "a", Label = "甲", ShapePath = "M 0.5 0\nL 1 0.5\nL 0.5 1\nL 0 0.5\nZ" });

        manyLines.StructuralHash.Should().Be(oneLine.StructuralHash, "空白只影响可读性，不影响坐标");
    }

    /// <summary>路径文本随文档往返，逐字段不丢。</summary>
    [Fact]
    [Trait("Category", "PathShape")]
    public void A_custom_path_survives_a_round_trip()
    {
        var document = IrFixtures.WithNode(
            IrFixtures.Base(),
            new NodeDef { Id = "a", Label = "甲", ShapePath = Diamond });

        var restored = DiagramSerializer.DeserializeFull(DiagramSerializer.SerializeFull(document));

        restored.Nodes.Should().Equal(document.Nodes);
        restored.VisualHash.Should().Be(document.VisualHash);
    }

    #endregion

    /// <summary>解析一段该失败的路径，把结构化错误取出来。</summary>
    private static PathSyntaxException Reject(string pathData)
    {
        PathParser.TryParse(pathData, out var outline, out var error).Should().BeFalse();

        outline.Should().BeNull();
        error.Should().NotBeNull();

        return error!;
    }
}
