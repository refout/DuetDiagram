using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 组合的框：四种形态各自画出可辨的记号，而且不靠颜色。
/// </summary>
/// <remarks>
/// <para>
/// 判据一律落在几何上（形状、圆角、线型、记号的位置），不落在颜色上——
/// 色觉障碍、黑白打印、深浅主题都会把颜色抹掉，而"这是分组还是泳道"是结构信息。
/// </para>
/// <para>
/// 坐标不写死：框在哪一块由成员的位置与主题的内边距算出来，
/// 断言只看框与记号之间的相对关系。写死的话，改一次内边距，用例就红一次。
/// </para>
/// </remarks>
public sealed class CompositeFrameTests
{
    /// <summary>分组：实线圆角框，画在成员下面。</summary>
    [Fact]
    [Trait("Category", "CompositeFrame")]
    public void A_group_draws_a_solid_rounded_frame_below_its_members()
    {
        var commands = Scene(new GroupDef { Id = "g", Label = "组", Members = ["a", "b"] });

        var frame = commands.OfType<DrawShape>().Single(command => command.ElementId == "g");
        frame.Shape.Should().Be(NodeShape.Rounded);
        frame.Border.Should().Be(LineStyle.Solid);
        frame.Radius.Should().BeGreaterThan(0, "分组靠圆角与别的形态分开");

        // 框画在成员下面：成员的形状在绘制次序里更靠后，半透明的框才不会罩在成员上。
        var member = commands.OfType<DrawShape>().First(command => command.ElementId == "a");
        commands.ToList().IndexOf(frame).Should().BeLessThan(commands.ToList().IndexOf(member));
    }

    /// <summary>泳道：直角框，池头带顺着流向的那一侧，带上有一个指向流向的箭头。</summary>
    [Fact]
    [Trait("Category", "CompositeFrame")]
    public void A_lane_holds_its_pool_head_on_the_upstream_side()
    {
        // 横向流（LR）：池头在左，箭头向右。
        var horizontal = Scene(new LaneDef { Id = "lane", Label = "泳道", Members = ["a", "b"] });
        var laneFrame = horizontal.OfType<DrawShape>().Single(command => command.ElementId == "lane");
        laneFrame.Shape.Should().Be(NodeShape.Rect, "泳道是一条一条并排的条带，圆角会让相邻两条的角上出现缝");
        laneFrame.Radius.Should().Be(0);

        var arrow = ArrowIn(horizontal, "lane");
        arrow.Should().NotBeNull("池头带上要有一个指向流向的箭头");
        var halfWidth = laneFrame.Rect.X + (laneFrame.Rect.Width / 2);
        arrow!.Points.Should().OnlyContain(point => point.X < halfWidth, "横向流的池头带在左边");
        arrow.Points[1].X.Should().Be(arrow.Points.Max(point => point.X), "三个点里中间那个是尖，横向流的尖在最右");

        // 纵向流（TB）：池头在上，箭头向下。
        var vertical = Scene(new LaneDef { Id = "lane", Label = "泳道", Members = ["a", "b"] }, Direction.TB);
        var topFrame = vertical.OfType<DrawShape>().Single(command => command.ElementId == "lane");
        var halfHeight = topFrame.Rect.Y + (topFrame.Rect.Height / 2);
        var downward = ArrowIn(vertical, "lane")!;
        downward.Points.Should().OnlyContain(
            point => point.Y < halfHeight,
            "纵向流的池头带在上边——位置放错的话读者会把道序看反");
        downward.Points[1].Y.Should().Be(downward.Points.Max(point => point.Y), "纵向流的尖在最下");
    }

    /// <summary>子流程：双线框，右上角有展开／折叠标记，折起来时是加号。</summary>
    [Fact]
    [Trait("Category", "CompositeFrame")]
    public void A_subflow_shows_its_state_in_the_toggle_marker()
    {
        var expanded = Scene(new SubflowDef { Id = "sf", Label = "子流程", Members = ["a", "b"] });

        // 外框与右上角的标记方块都挂在组合的标识下；外框先画，取第一块。
        var frame = expanded.OfType<DrawShape>().First(command => command.ElementId == "sf");

        // 内缩一圈的第二条线：一条闭合的折线，比框小一圈。
        var outline = expanded.OfType<DrawPolyline>().Single(
            command => command.ElementId == "sf" && command.Points.Count == 5);
        outline.Points.Min(point => point.X).Should().BeApproximately(frame.Rect.X + 4, 0.01, "第二条线内缩一圈");

        // 展开时只有一条横杠（减号）。
        Bars(expanded, "sf").Should().HaveCount(1, "展开时是减号");

        var collapsed = Scene(new SubflowDef { Id = "sf", Label = "子流程", Collapsed = true, Members = ["a", "b"] });
        Bars(collapsed, "sf").Should().HaveCount(2, "折起来时是加号——多出一条竖杠");
    }

    /// <summary>组合框：虚线框。它是纯标注，虚线同时把它与分组分开。</summary>
    [Fact]
    [Trait("Category", "CompositeFrame")]
    public void A_combo_is_drawn_dashed()
    {
        var commands = Scene(new ComboDef { Id = "cb", Label = "框", Members = ["a", "b"] });

        var frame = commands.OfType<DrawShape>().Single(command => command.ElementId == "cb");
        frame.Border.Should().Be(LineStyle.Dashed);
    }

    /// <summary>标题带里有组合的名字。</summary>
    [Fact]
    [Trait("Category", "CompositeFrame")]
    public void A_composite_carries_its_label_in_the_band()
    {
        var commands = Scene(new GroupDef { Id = "g", Label = "核心层", Members = ["a", "b"] });

        commands.OfType<DrawText>()
            .Where(command => command.ElementId == "g")
            .Should().Contain(text => text.Text.Contains("核心层"), "标题带要说清这一块是什么");
    }

    /// <summary>没有成员的组合不出现在画面上：画一个空框，用户会以为那是个可以放东西的位置。</summary>
    [Fact]
    [Trait("Category", "CompositeFrame")]
    public void An_empty_composite_draws_nothing()
    {
        var commands = Scene(new GroupDef { Id = "g", Label = "空的", Members = [] });

        commands.Should().NotContain(command => command.ElementId == "g");
    }

    #region 场景

    /// <summary>两个成员节点加一个组合，出一份绘制列表。成员按标识 a、b 预先摆好。</summary>
    private static IReadOnlyList<DrawCommand> Scene(CompositeDef composite, Direction direction = Direction.LR)
    {
        var document = DiagramDocument.CreateFromContent(
            "doc",
            DiagramKind.Flowchart,
            direction,
            nodes:
            [
                new NodeDef { Id = "a", Label = "甲" },
                new NodeDef { Id = "b", Label = "乙" },
            ],
            composites: [composite]);

        var layout = Layouts.Result(
            [Layouts.Node("a", 20, 60), Layouts.Node("b", 220, 60)],
            [],
            420,
            200);

        return SceneBuilder.Build(document, layout, Theme.Default, new FakeTextMeasurer()).Commands;
    }

    /// <summary>组合框里的那条箭头折线（三个点，无箭头样式）。</summary>
    private static DrawPolyline? ArrowIn(IReadOnlyList<DrawCommand> commands, string elementId) =>
        commands.OfType<DrawPolyline>().FirstOrDefault(
            command => command.ElementId == elementId && command.Points.Count == 3);

    /// <summary>展开／折叠标记里的杠：两个点的折线。减号一条，加号两条。</summary>
    private static IReadOnlyList<DrawPolyline> Bars(IReadOnlyList<DrawCommand> commands, string elementId) =>
        [.. commands.OfType<DrawPolyline>().Where(command => command.ElementId == elementId && command.Points.Count == 2)];

    #endregion
}
