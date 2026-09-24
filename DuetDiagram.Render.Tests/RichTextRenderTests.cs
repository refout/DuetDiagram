using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 富文本节点在绘制列表里长什么样。
/// </summary>
/// <remarks>
/// 验收的是"每个 run 一段、样式逐段带上"：分行与行内切段都在构建列表那一步做完，
/// 绘制方拿到的每一段都带着自己的字号、颜色与修饰，不必再读一遍 IR。
/// </remarks>
public sealed class RichTextRenderTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    [Trait("Category", "SceneSnapshot")]
    public void Every_run_gets_its_own_text_command()
    {
        var list = Draw(Rich(
            Paragraph(Run("a"), Run("b", new RichRunStyle { Bold = true }), Run("c"))));

        var texts = Texts(list, "n1");

        texts.Select(text => text.Text).Should().Equal("a", "b", "c");
    }

    [Fact]
    [Trait("Category", "SceneSnapshot")]
    public void The_style_travels_with_each_segment()
    {
        var list = Draw(Rich(Paragraph(
            Run("b", new RichRunStyle { Bold = true }),
            Run("i", new RichRunStyle { Italic = true }),
            Run("u", new RichRunStyle { Underline = true }),
            Run("s", new RichRunStyle { Strikethrough = true }),
            Run("c", new RichRunStyle { Color = "#ff0000" }))));

        var texts = Texts(list, "n1");

        texts[0].Weight.Should().Be(FontWeight.Bold);
        texts[1].Italic.Should().BeTrue();
        texts[2].Underline.Should().BeTrue();
        texts[3].Strikethrough.Should().BeTrue();
        texts[4].Color.Should().Be("#ff0000");
    }

    [Fact]
    [Trait("Category", "SceneSnapshot")]
    public void A_paragraph_becomes_several_lines()
    {
        var list = Draw(Rich(Paragraph(Run("first")), Paragraph(Run("second"))));

        var texts = Texts(list, "n1");

        texts.Should().HaveCount(2);
        texts[0].Text.Should().Be("first");
        texts[1].Text.Should().Be("second");
        texts[1].Box.Y.Should().BeGreaterThan(texts[0].Box.Y);
    }

    [Fact]
    [Trait("Category", "SceneSnapshot")]
    public void A_rich_node_still_gets_its_shape()
    {
        var list = Draw(Rich(Paragraph(Run("x"))));

        list.Commands.OfType<DrawShape>().Should().ContainSingle(shape => shape.ElementId == "n1");
    }

    [Fact]
    [Trait("Category", "SceneSnapshot")]
    public void An_empty_paragraph_draws_nothing()
    {
        var list = Draw(Rich(new RichParagraph(), Paragraph(Run("x"))));

        // 空行只占高度，不出指令：一条宽度为零的文本指令画不出东西。
        Texts(list, "n1").Should().ContainSingle().Which.Text.Should().Be("x");
    }

    [Fact]
    [Trait("Category", "SceneSnapshot")]
    public void A_plain_node_carries_no_decoration_in_its_description()
    {
        // 三项修饰都没设时不往规范文本里加任何字，否则所有既有快照会一起变红。
        var node = new NodeDef { Id = "n1", Label = "plain" };
        var text = Texts(Draw(node), "n1").Should().ContainSingle().Subject;

        text.Describe().Should().NotContain("italic").And.NotContain("underline").And.NotContain("strikethrough");
    }

    [Fact]
    [Trait("Category", "SceneSnapshot")]
    public void A_decorated_segment_says_so_in_its_description()
    {
        var node = Rich(Paragraph(Run("x", new RichRunStyle { Underline = true })));
        var text = Texts(Draw(node), "n1").Should().ContainSingle().Subject;

        text.Describe().Should().Contain("underline=True");
    }

    [Fact]
    [Trait("Category", "SceneSnapshot")]
    public void The_scene_matches_its_snapshot()
    {
        Snapshot.Match("11-richtext", Draw(Rich(
            Paragraph(
                Run("普通 "),
                Run("粗体", new RichRunStyle { Bold = true }),
                Run(" 下划线", new RichRunStyle { Underline = true })),
            new RichParagraph
            {
                Runs = [Run("大字", new RichRunStyle { FontSize = 20, Color = "#c0392b" })],
                Align = TextAlign.End,
            })).ToText());
    }

    #region 夹具

    private static DrawList Draw(NodeDef node)
    {
        var theme = Theme.Default;
        var size = SceneBuilder.MeasureNode(node, theme, Measurer);
        var placed = new[] { new PlacedNode(node.Id, 40, 40, size.Width, size.Height) };
        var document = DiagramDocument.CreateFromContent("richtext", nodes: [node]);

        return SceneBuilder.Build(document, Layouts.Result(placed, [], 400, 300), theme, Measurer);
    }

    private static DrawText[] Texts(DrawList list, string elementId) =>
        [.. list.Commands.OfType<DrawText>().Where(text => text.ElementId == elementId)];

    private static NodeDef Rich(params RichParagraph[] paragraphs)
    {
        var content = new RichTextContent { Paragraphs = paragraphs };

        return new NodeDef
        {
            Id = "n1",
            Label = content.PlainText,
            RichText = true,
            RichLabel = content,
        };
    }

    private static RichParagraph Paragraph(params RichRun[] runs) => new() { Runs = runs };

    private static RichRun Run(string text, RichRunStyle? style = null) => new() { Text = text, Style = style };

    #endregion
}
