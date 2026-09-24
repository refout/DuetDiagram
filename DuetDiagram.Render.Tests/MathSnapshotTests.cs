using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 行内与独立成行两档排出来的差别。
/// </summary>
/// <remarks>
/// <para>
/// **两档的差别全在基线上。** 行内的公式要与周围文字对齐——它的基线落在文字基线上；
/// 独立成行的公式自己占一块，整块居中。两档用同一套处理的话，
/// 带分数的行内公式会把整行文字顶起来，而那种错看着像行高算错了。
/// </para>
/// <para>
/// 一条用例把行内公式的基线与**同一个字排成的纯文本**的基线对起来：
/// 两边都由"行高乘一个比例"推出来，对不上就说明其中一条路自己另立了一套。
/// </para>
/// </remarks>
public sealed class MathSnapshotTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    /// <summary>行内公式的基线落在同一行文字的基线上。</summary>
    [Fact]
    [Trait("Category", "MathSnapshot")]
    public void An_inline_formula_sits_on_the_text_baseline()
    {
        var plain = Only(Draw(new NodeDef { Id = "n1", Label = "x" }));
        var inline = Only(Draw(new NodeDef { Id = "n1", Label = "x", MathMode = MathMode.Inline }));

        Baseline(inline).Should().BeApproximately(
            Baseline(plain),
            0.001,
            "行内公式与它旁边那个字要落在同一条基线上");
    }

    /// <summary>独立成行的公式整块居中，不按基线摆。</summary>
    [Fact]
    [Trait("Category", "MathSnapshot")]
    public void A_block_formula_is_centered_as_a_block()
    {
        var node = new NodeDef { Id = "n1", Label = @"\frac{a}{b}", MathMode = MathMode.Block };
        var theme = Theme.Default;
        var size = SceneBuilder.MeasureNode(node, theme, Measurer);
        var texts = Texts(Draw(node));

        var top = texts.Min(text => text.Box.Y);
        var bottom = texts.Max(text => text.Box.Bottom);

        (top - 40).Should().BeApproximately(
            40 + size.Height - bottom,
            0.001,
            "整块在框里上下居中");
    }

    /// <summary>同一个公式，两档摆的位置不一样。</summary>
    [Fact]
    [Trait("Category", "MathSnapshot")]
    public void The_two_modes_place_the_same_formula_differently()
    {
        var inline = Draw(new NodeDef { Id = "n1", Label = @"\frac{a}{b}", MathMode = MathMode.Inline });
        var block = Draw(new NodeDef { Id = "n1", Label = @"\frac{a}{b}", MathMode = MathMode.Block });

        var inlineTop = Texts(inline)[0].Box.Y;
        var blockTop = Texts(block)[0].Box.Y;

        inlineTop.Should().NotBeApproximately(blockTop, 0.001, "两档的摆法必须真的不同");
        inlineTop.Should().BeGreaterThan(
            blockTop,
            "公式比一行字高，按基线摆时整块会比居中时更靠下");
    }

    /// <summary>行内那一档的快照。</summary>
    [Fact]
    [Trait("Category", "MathSnapshot")]
    public void The_inline_scene_matches_its_snapshot()
    {
        Snapshot.Match("12-math-inline", Draw(new NodeDef
        {
            Id = "n1",
            Label = @"a^2 + \frac{b}{c} \le \sqrt{d}",
            MathMode = MathMode.Inline,
        }).ToText());
    }

    /// <summary>独立成行那一档的快照。</summary>
    [Fact]
    [Trait("Category", "MathSnapshot")]
    public void The_block_scene_matches_its_snapshot()
    {
        Snapshot.Match("13-math-block", Draw(new NodeDef
        {
            Id = "n1",
            Label = @"a^2 + \frac{b}{c} \le \sqrt{d}",
            MathMode = MathMode.Block,
        }).ToText());
    }

    #region 夹具

    /// <summary>排一个节点，框放在 (40, 40)。</summary>
    private static DrawList Draw(NodeDef node)
    {
        var theme = Theme.Default;
        var size = SceneBuilder.MeasureNode(node, theme, Measurer);
        var placed = new[] { new PlacedNode(node.Id, 40, 40, size.Width, size.Height) };
        var document = DiagramDocument.CreateFromContent("math", nodes: [node]);

        return SceneBuilder.Build(document, Layouts.Result(placed, [], 400, 300), theme, Measurer);
    }

    private static DrawText[] Texts(DrawList list) =>
        [.. list.Commands.OfType<DrawText>().Where(text => text.ElementId == "n1")];

    private static DrawText Only(DrawList list) => Texts(list).Should().ContainSingle().Subject;

    /// <summary>一个文本段的基线：框顶往下 0.8 个框高。与排版那一层的约定同一份。</summary>
    private static double Baseline(DrawText text) => text.Box.Y + (text.Box.Height * 0.8);

    #endregion
}
