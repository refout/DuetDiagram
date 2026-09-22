using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 变更高亮的三种手段：各自独立可辨、叠加时不互相盖住、颜色只是辅助线索。
/// </summary>
/// <remarks>
/// <para>
/// 这些断言盯的是"不靠颜色也能认出被标了"。只靠颜色的话，色觉障碍用户看到的是一张
/// 没标过的图，而这一点无法事后补救——所以三种手段各有各的形状与位置：
/// 角标是右上角的圆点、轮廓是元素外面一圈虚线、脉冲是元素边框的粗线。
/// </para>
/// <para>
/// 高亮是叠加层，不进绘制列表的几何。构建是纯计算：给一批标记与一份基准列表、
/// 一个相位，算出这一帧要叠哪些指令。这里不读时钟、不读文档，所以断言是逐字节确定的。
/// </para>
/// </remarks>
public sealed class HighlightTests
{
    private static readonly IReadOnlyList<DrawCommand> Base =
    [
        new DrawShape("a", NodeShape.Rect, new SpatialRect(100, 50, 120, 60), "#ffffff", "#39424f", 1.5, LineStyle.Solid, 0, 1),
    ];

    private static ElementHighlight Mark(
        params HighlightKind[] kinds) =>
        new("a", ChangeSource.Human, new HashSet<HighlightKind>(kinds));

    /// <summary>三种手段一起开时，各画各的，谁也没被谁顶掉。</summary>
    [Fact]
    [Trait("Category", "Highlight")]
    public void Three_means_are_independently_identifiable()
    {
        var commands = Highlight.Build(
            [Mark(HighlightKind.Pulse, HighlightKind.Badge, HighlightKind.Outline)],
            Base,
            Theme.Default,
            pulsePhase: 0.25);

        commands.Should().ContainSingle(c => c.ElementId == Highlight.PulsePrefix + "a");
        commands.Should().ContainSingle(c => c.ElementId == Highlight.OutlinePrefix + "a");
        commands.Should().ContainSingle(c => c.ElementId == Highlight.BadgePrefix + "a");
    }

    /// <summary>角标落在元素外面，与脉冲、轮廓都不重叠——叠加时不互相盖住。</summary>
    [Fact]
    [Trait("Category", "Highlight")]
    public void Means_do_not_occlude_each_other()
    {
        var commands = Highlight.Build(
            [Mark(HighlightKind.Pulse, HighlightKind.Badge, HighlightKind.Outline)],
            Base,
            Theme.Default,
            pulsePhase: 0.25);

        var badge = Shape(commands, Highlight.BadgePrefix + "a");
        var outline = Shape(commands, Highlight.OutlinePrefix + "a");
        var pulse = Shape(commands, Highlight.PulsePrefix + "a");

        badge.Rect.Intersects(pulse.Rect).Should().BeFalse("角标在元素右上角之外");
        badge.Rect.Intersects(outline.Rect).Should().BeFalse("角标在虚线轮廓之外");
        outline.Rect.Contains(new SpatialRect(100, 50, 120, 60)).Should().BeTrue("轮廓把元素整个框住");
        pulse.Rect.Contains(new SpatialRect(100, 50, 120, 60)).Should().BeTrue("脉冲也把元素整个框住");
    }

    /// <summary>只开一种手段时，只画那一种。</summary>
    [Theory]
    [InlineData(HighlightKind.Badge, Highlight.BadgePrefix)]
    [InlineData(HighlightKind.Outline, Highlight.OutlinePrefix)]
    [InlineData(HighlightKind.Pulse, Highlight.PulsePrefix)]
    [Trait("Category", "Highlight")]
    public void Each_means_builds_alone(HighlightKind kind, string prefix)
    {
        var commands = Highlight.Build([Mark(kind)], Base, Theme.Default, pulsePhase: 0.25);

        commands.Should().ContainSingle();
        commands[0].ElementId.Should().Be(prefix + "a");
    }

    /// <summary>脉冲不改填充色，只动边框。</summary>
    [Fact]
    [Trait("Category", "Highlight")]
    public void Pulse_only_touches_the_border()
    {
        var pulse = Shape(
            Highlight.Build([Mark(HighlightKind.Pulse)], Base, Theme.Default, pulsePhase: 0.25),
            Highlight.PulsePrefix + "a");

        pulse.Fill.Should().Be("transparent", "改填充色会与样式面板里用户选的颜色打架");
        pulse.Weight.Should().BeGreaterThan(Theme.Default.StrokeWeight, "脉冲是加粗的边框");
    }

    /// <summary>轮廓是虚线；角标是实心的圆点。</summary>
    [Fact]
    [Trait("Category", "Highlight")]
    public void Outline_is_dashed_and_badge_is_a_filled_dot()
    {
        var outline = Shape(
            Highlight.Build([Mark(HighlightKind.Outline)], Base, Theme.Default, pulsePhase: 0.25),
            Highlight.OutlinePrefix + "a");
        var badge = Shape(
            Highlight.Build([Mark(HighlightKind.Badge)], Base, Theme.Default, pulsePhase: 0.25),
            Highlight.BadgePrefix + "a");

        outline.Border.Should().Be(LineStyle.Dashed);
        badge.Shape.Should().Be(NodeShape.Circle);
        badge.Fill.Should().NotBe("transparent", "角标是实心的，靠形状与位置就能认出");
    }

    /// <summary>不在脉冲中（相位为负）时，脉冲那一条不画。</summary>
    [Fact]
    [Trait("Category", "Highlight")]
    public void Pulse_is_dropped_when_not_animating()
    {
        var commands = Highlight.Build(
            [Mark(HighlightKind.Pulse, HighlightKind.Badge)],
            Base,
            Theme.Default,
            pulsePhase: -1);

        commands.Should().NotContain(c => c.ElementId == Highlight.PulsePrefix + "a");
        commands.Should().ContainSingle(c => c.ElementId == Highlight.BadgePrefix + "a");
    }

    /// <summary>撤销带 ↶，重做带 ↷，颜色沿用原来源。</summary>
    [Fact]
    [Trait("Category", "Highlight")]
    public void Undo_and_redo_draw_their_symbols()
    {
        var undo = Highlight.Build(
            [new ElementHighlight("a", ChangeSource.Llm, new HashSet<HighlightKind> { HighlightKind.Badge }, IsUndo: true)],
            Base,
            Theme.Default,
            pulsePhase: -1);
        var redo = Highlight.Build(
            [new ElementHighlight("a", ChangeSource.Llm, new HashSet<HighlightKind> { HighlightKind.Badge }, IsRedo: true)],
            Base,
            Theme.Default,
            pulsePhase: -1);

        Symbol(undo).Text.Should().Be("↶");
        Symbol(redo).Text.Should().Be("↷");
        Symbol(undo).Color.Should().Be(Highlight.SourceColor(ChangeSource.Llm), "继承原命令的来源色");
    }

    /// <summary>列表里找不到那个元素时跳过，不猜一个位置画上去。</summary>
    [Fact]
    [Trait("Category", "Highlight")]
    public void Missing_element_is_skipped()
    {
        var commands = Highlight.Build(
            [new ElementHighlight("不存在", ChangeSource.Human, new HashSet<HighlightKind> { HighlightKind.Badge })],
            Base,
            Theme.Default,
            pulsePhase: 0.25);

        commands.Should().BeEmpty();
    }

    /// <summary>脉冲强度落在 0 到 1 之间，且次数由主题决定。</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.125)]
    [InlineData(0.5)]
    [InlineData(0.999)]
    [Trait("Category", "Highlight")]
    public void Strength_stays_within_range(double phase)
    {
        var strength = Highlight.Strength(phase, Theme.Default.HighlightPulseCount);

        strength.Should().BeInRange(0, 1);
    }

    /// <summary>人工与 LLM 的来源色一眼分得开。</summary>
    [Fact]
    [Trait("Category", "Highlight")]
    public void Source_colors_distinguish_human_from_llm()
    {
        Highlight.SourceColor(ChangeSource.Human)
            .Should().NotBe(Highlight.SourceColor(ChangeSource.Llm));
    }

    private static DrawShape Shape(IReadOnlyList<DrawCommand> commands, string elementId) =>
        commands.OfType<DrawShape>().Single(c => c.ElementId == elementId);

    private static DrawText Symbol(IReadOnlyList<DrawCommand> commands) =>
        commands.OfType<DrawText>().Single();
}
