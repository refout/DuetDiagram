using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 富文本的折行、行内样式混排、段间距与对齐。
/// </summary>
/// <remarks>
/// <para>
/// 判据一律写成可数的（行数、每行的文字、块尺寸），不写成"看起来对"——
/// 排版错了只表现为"这几行字看着有点怪"，写不成数就验不出来。
/// </para>
/// <para>
/// 度量用 <see cref="FakeTextMeasurer"/>：字号 10、行高倍数 1 时，每个字符宽 6、
/// 每行高 10，期望值可以口算。真实字体的度量随机器变化，算不出期望值。
/// </para>
/// </remarks>
public sealed class RichTextLayoutTests
{
    /// <summary>字号 10、行高倍数 1 的基准外观：每字宽 6、每行高 10。</summary>
    private static readonly TextAppearance Baseline = new(
        "#000000",
        "Inter",
        10,
        FontWeight.Normal,
        TextAlign.Start,
        VerticalAlign.Middle,
        1);

    private static readonly FakeTextMeasurer Measurer = new();

    #region 折行

    [Fact]
    [Trait("Category", "RichTextLayout")]
    public void A_paragraph_without_a_width_limit_stays_on_one_line()
    {
        // 不折行时一段就是一行，与纯文本同一口径。
        var block = Layout(Content(Paragraph(Run("hello world"))));

        block.Lines.Should().ContainSingle();
        Lines(block).Should().Equal("hello world");
        block.Width.Should().Be(11 * 6);
        block.Height.Should().Be(10);
    }

    [Fact]
    [Trait("Category", "RichTextLayout")]
    public void A_long_latin_line_wraps_at_spaces_without_cutting_words()
    {
        // alpha 宽 30、空格宽 6、beta 宽 24。限宽 36 时一个词加空格再加一个词就超了。
        var block = Layout(Content(Paragraph(Run("alpha beta gamma"))), maxWidth: 36);

        Lines(block).Should().Equal("alpha", "beta", "gamma");
    }

    [Fact]
    [Trait("Category", "RichTextLayout")]
    public void A_word_wider_than_the_line_is_cut_by_characters()
    {
        // 十二个字母宽 72，限宽 36 只能放六个——一个词比整行还宽，此时才许硬切。
        var block = Layout(Content(Paragraph(Run("abcdefghijkl"))), maxWidth: 36);

        Lines(block).Should().Equal("abcdef", "ghijkl");
        block.Width.Should().Be(36);
    }

    [Fact]
    [Trait("Category", "RichTextLayout")]
    public void Chinese_wraps_between_characters()
    {
        // 表意文字逐字成原子，所以中文按字断行——这与"英文单词中间断行"是两回事。
        var block = Layout(Content(Paragraph(Run("你好世界你好"))), maxWidth: 18);

        Lines(block).Should().Equal("你好世", "界你好");
    }

    [Fact]
    [Trait("Category", "RichTextLayout")]
    public void Spaces_at_a_line_break_are_dropped()
    {
        // 断行处的空格留在行尾会撑宽整块，留在行首会看起来像缩进。
        var block = Layout(Content(Paragraph(Run("alpha beta gamma"))), maxWidth: 36);

        block.Lines.SelectMany(line => line.Segments)
            .Should().OnlyContain(segment => !segment.Text.StartsWith(' ') && !segment.Text.EndsWith(' '));
    }

    [Fact]
    [Trait("Category", "RichTextLayout")]
    public void A_paragraph_shorter_than_the_limit_is_not_split()
    {
        var block = Layout(Content(Paragraph(Run("short"))), maxWidth: 600);

        Lines(block).Should().Equal("short");
    }

    #endregion

    #region 行内样式

    [Fact]
    [Trait("Category", "RichTextLayout")]
    public void The_line_height_is_the_tallest_run()
    {
        // 取平均或取第一个的话，混排大字号的那一行会与下一行叠在一起。
        var block = Layout(Content(Paragraph(Run("a"), Run("b", new RichRunStyle { FontSize = 20 }))));

        block.Lines[0].Height.Should().Be(20);
        block.Lines[0].Segments.Should().OnlyContain(segment => segment.Height == 20);
    }

    [Fact]
    [Trait("Category", "RichTextLayout")]
    public void Every_run_carries_its_own_style()
    {
        var block = Layout(Content(Paragraph(
            Run("b", new RichRunStyle { Bold = true }),
            Run("i", new RichRunStyle { Italic = true, Underline = true }))));

        var segments = block.Lines[0].Segments;

        segments.Should().HaveCount(2);
        segments[0].Appearance.Weight.Should().Be(FontWeight.Bold);
        segments[1].Appearance.Italic.Should().BeTrue();
        segments[1].Appearance.Underline.Should().BeTrue();
        segments[1].Appearance.Weight.Should().Be(FontWeight.Normal);
    }

    [Fact]
    [Trait("Category", "RichTextLayout")]
    public void An_unset_style_member_inherits_the_node_level()
    {
        // 节点上写着字号 20，一段没有样式的片段就该跟着是 20，而不是回到主题兜底。
        var baseline = Baseline with { FontSize = 20 };
        var block = RichTextLayout.Layout(
            Measurer,
            Content(Paragraph(Run("a", new RichRunStyle { Bold = true }))),
            baseline,
            style => Resolve(style, baseline));

        block.Lines[0].Segments[0].Appearance.FontSize.Should().Be(20);
        block.Lines[0].Segments[0].Appearance.Weight.Should().Be(FontWeight.Bold);
    }

    [Fact]
    [Trait("Category", "RichTextLayout")]
    public void Bold_can_be_turned_back_off_inside_a_bold_node()
    {
        // Bold 写成 false 与"没写"不一样：前者是明确要求不加粗。
        var baseline = Baseline with { Weight = FontWeight.Bold };
        var block = RichTextLayout.Layout(
            Measurer,
            Content(Paragraph(Run("a", new RichRunStyle { Bold = false }))),
            baseline,
            style => Resolve(style, baseline));

        block.Lines[0].Segments[0].Appearance.Weight.Should().Be(FontWeight.Normal);
    }

    [Fact]
    [Trait("Category", "RichTextLayout")]
    public void A_bold_run_is_measured_wider_than_a_plain_one()
    {
        // 量的时候按常规体、画的时候按粗体，最后几个字会压到边框上。
        var plain = Layout(Content(Paragraph(Run("aaaa"))));
        var bold = Layout(Content(Paragraph(Run("aaaa", new RichRunStyle { Bold = true }))));

        bold.Width.Should().BeGreaterThan(plain.Width);
    }

    #endregion

    #region 段落与对齐

    [Fact]
    [Trait("Category", "RichTextLayout")]
    public void Paragraphs_become_separate_lines()
    {
        var block = Layout(Content(Paragraph(Run("第一段")), Paragraph(Run("第二段"))));

        Lines(block).Should().Equal("第一段", "第二段");
    }

    [Fact]
    [Trait("Category", "RichTextLayout")]
    public void An_empty_paragraph_occupies_a_line()
    {
        // 跳过它会让两个段落之间的空行消失，而写内容的人以为空行是有效的。
        var block = Layout(Content(new RichParagraph(), Paragraph(Run("x"))));

        block.Lines.Should().HaveCount(2);
        block.Lines[0].Segments.Should().BeEmpty();
        block.Lines[0].Height.Should().Be(10);
        Lines(block).Should().Equal(string.Empty, "x");
    }

    [Fact]
    [Trait("Category", "RichTextLayout")]
    public void A_paragraph_alignment_places_the_line_within_the_block()
    {
        var block = Layout(Content(
            new RichParagraph { Runs = [Run("aa")], Align = TextAlign.End },
            new RichParagraph { Runs = [Run("aaaa")] }));

        // 块宽取最长的一行（24）；靠右的那一行从 24 - 12 开始。
        block.Width.Should().Be(24);
        block.Lines[0].Segments[0].X.Should().Be(12);
        block.Lines[1].Segments[0].X.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "RichTextLayout")]
    public void A_paragraph_without_its_own_alignment_follows_the_node()
    {
        var baseline = Baseline with { Align = TextAlign.Center };
        var block = RichTextLayout.Layout(
            Measurer,
            Content(Paragraph(Run("aa")), Paragraph(Run("aaaa"))),
            baseline,
            style => Resolve(style, baseline));

        block.Lines[0].Segments[0].X.Should().Be(6);
        block.Lines[1].Segments[0].X.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "RichTextLayout")]
    public void A_wrapped_paragraph_keeps_its_alignment_on_every_line()
    {
        var block = Layout(
            Content(new RichParagraph { Runs = [Run("alpha beta gamma")], Align = TextAlign.End }),
            maxWidth: 36);

        // 三行宽度不一样（30 / 24 / 30），靠右时每行的右缘都落在块宽上。
        block.Lines.Should().HaveCount(3);
        block.Lines.Should().OnlyContain(line =>
            Math.Abs(line.Segments.Last().X + line.Segments.Last().Width - block.Width) < 1e-9);
    }

    #endregion

    #region 确定性

    [Fact]
    [Trait("Category", "RichTextLayout")]
    public void The_same_content_lays_out_the_same_way_every_time()
    {
        // 用哈希做缓存键的地方全靠这一条：抖一次就是一次多余的重绘。
        var content = Content(
            Paragraph(Run("alpha "), Run("beta", new RichRunStyle { Bold = true }), Run(" gamma")),
            new RichParagraph { Runs = [Run("中文也要一样")], Align = TextAlign.End });

        Dump(RichTextLayout.Layout(Measurer, content, Baseline, style => Resolve(style, Baseline), 60))
            .Should().Be(Dump(RichTextLayout.Layout(Measurer, content, Baseline, style => Resolve(style, Baseline), 60)));
    }

    [Fact]
    [Trait("Category", "RichTextLayout")]
    public void An_empty_content_lays_out_to_nothing()
    {
        var block = Layout(new RichTextContent());

        block.Lines.Should().BeEmpty();
        block.Width.Should().Be(0);
        block.Height.Should().Be(0);
    }

    #endregion

    #region 度量与画出来的一致

    [Fact]
    [Trait("Category", "TextMeasurement")]
    public void The_measured_block_matches_the_drawn_content()
    {
        // 量错的表现是文字溢出节点边框，而量出来的宽度正是布局的输入。
        var node = Node(
            Paragraph(Run("alpha "), Run("beta", new RichRunStyle { Bold = true, FontSize = 16 })));

        var block = RichTextLayout.Layout(
            Measurer,
            node.RichLabel!,
            Appearance(node),
            style => Resolve(style, Appearance(node)));

        var drawn = Drawn(node);
        var left = drawn.Min(text => text.Box.X);
        var right = drawn.Max(text => text.Box.Right);
        var top = drawn.Min(text => text.Box.Y);
        var bottom = drawn.Max(text => text.Box.Bottom);

        (right - left).Should().BeApproximately(block.Width, 1e-9);
        (bottom - top).Should().BeApproximately(block.Height, 1e-9);
    }

    [Fact]
    [Trait("Category", "TextMeasurement")]
    public void The_drawn_text_fits_inside_the_node_box()
    {
        var node = Node(Paragraph(Run("a longer line")), Paragraph(Run("and another")));
        var theme = Theme.Default;
        var size = SceneBuilder.MeasureNode(node, theme, Measurer);
        var drawn = Drawn(node, size);

        // 绘制列表里的框是画布坐标，节点摆在 (40, 40)，所以比的是"相对节点原点"。
        drawn.Min(text => text.Box.X).Should().BeGreaterThanOrEqualTo(Origin + theme.NodePaddingX - 1e-9);
        drawn.Min(text => text.Box.Y).Should().BeGreaterThanOrEqualTo(Origin + theme.NodePaddingY - 1e-9);
        drawn.Max(text => text.Box.Right).Should().BeLessThanOrEqualTo(Origin + size.Width - theme.NodePaddingX + 1e-9);
        drawn.Max(text => text.Box.Bottom).Should().BeLessThanOrEqualTo(Origin + size.Height - theme.NodePaddingY + 1e-9);
    }

    [Fact]
    [Trait("Category", "TextMeasurement")]
    public void The_content_wins_over_the_label_when_measuring()
    {
        // 内容在的时候它是权威，标签只是它的投影。按标签量的话，混排大字号的那一块会量矮。
        var plain = new NodeDef { Id = "n1", Label = "short" };
        var rich = Node(Paragraph(Run("a much longer line than the label")));

        SceneBuilder.MeasureNode(rich, Theme.Default, Measurer).Width
            .Should().BeGreaterThan(SceneBuilder.MeasureNode(plain, Theme.Default, Measurer).Width);
    }

    [Fact]
    [Trait("Category", "TextMeasurement")]
    public void A_bigger_run_makes_the_node_taller()
    {
        var small = Node(Paragraph(Run("x")));
        var large = Node(Paragraph(Run("x", new RichRunStyle { FontSize = 40 })));

        SceneBuilder.MeasureNode(large, Theme.Default, Measurer).Height
            .Should().BeGreaterThan(SceneBuilder.MeasureNode(small, Theme.Default, Measurer).Height);
    }

    #endregion

    #region 夹具

    private static TextBody Layout(RichTextContent content, double? maxWidth = null) =>
        RichTextLayout.Layout(Measurer, content, Baseline, style => Resolve(style, Baseline), maxWidth);

    private static TextAppearance Resolve(RichRunStyle? style, TextAppearance baseline) =>
        Theme.Default.Run(style, baseline);

    private static TextAppearance Appearance(NodeDef node) =>
        Theme.Default.Text(node.Text, Theme.Default.Node(node).Text);

    /// <summary>节点在画布上的位置。绘制列表里的框是绝对坐标，比的时候要减掉它。</summary>
    private const double Origin = 40;

    /// <summary>一个带富文本内容的节点。标签取投影，与写入那条路的口径一致。</summary>
    private static NodeDef Node(params RichParagraph[] paragraphs)
    {
        var content = Content(paragraphs);

        return new NodeDef
        {
            Id = "n1",
            Label = content.PlainText,
            RichText = true,
            RichLabel = content,
        };
    }

    /// <summary>把这个节点画一遍，取出它的文本指令。</summary>
    private static DrawText[] Drawn(NodeDef node, Size? size = null)
    {
        var theme = Theme.Default;
        var measured = size ?? SceneBuilder.MeasureNode(node, theme, Measurer);
        var placed = new[] { new PlacedNode(node.Id, Origin, Origin, measured.Width, measured.Height) };
        var document = DiagramDocument.CreateFromContent("richtext", nodes: [node]);

        var list = SceneBuilder.Build(
            document,
            Layouts.Result(placed, [], 200, 200),
            theme,
            Measurer);

        return [.. list.Commands.OfType<DrawText>().Where(text => text.ElementId == node.Id)];
    }

    private static RichTextContent Content(params RichParagraph[] paragraphs) =>
        new() { Paragraphs = paragraphs };

    private static RichParagraph Paragraph(params RichRun[] runs) => new() { Runs = runs };

    private static RichRun Run(string text, RichRunStyle? style = null) => new() { Text = text, Style = style };

    /// <summary>每行接成一个字符串。空行接出来是空串。</summary>
    private static string[] Lines(TextBody block) =>
        [.. block.Lines.Select(line => string.Concat(line.Segments.Select(segment => segment.Text)))];

    /// <summary>整块的规范文本，供确定性断言比较。</summary>
    private static string Dump(TextBody block) =>
        string.Join(
            '\n',
            block.Lines.Select(line => string.Join(
                '|',
                line.Segments.Select(segment =>
                    $"{segment.Text}@{segment.X},{segment.Y},{segment.Width},{segment.Height}"))));

    #endregion
}
