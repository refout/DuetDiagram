using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 绘制列表与它的构建过程。
/// </summary>
public sealed class DrawListTests
{
    [Fact]
    [Trait("Category", "DrawList")]
    public void Composites_come_first_then_edges_then_nodes()
    {
        var document = DiagramDocument.CreateFromContent(
            "d",
            nodes: [new NodeDef { Id = "a", Label = "甲" }, new NodeDef { Id = "b", Label = "乙" }],
            edges: [new EdgeDef { Id = "e", From = "b", To = "a" }],
            composites: [new GroupDef { Id = "g", Label = "组", Members = ["a"] }]);

        var layout = Layouts.Result(
            [Layouts.Node("a", 40, 60), Layouts.Node("b", 40, 200)],
            [Layouts.Horizontal("e", Layouts.Node("b", 40, 200), Layouts.Node("a", 40, 60))],
            200,
            300);

        var list = SceneBuilder.Build(document, layout, Theme.Default, new FakeTextMeasurer());

        // 组合的框与标题、连线的折线、两个节点的框与标签。节点按声明顺序。
        list.Commands.Select(c => c.ElementId).Should().Equal("g", "g", "e", "a", "a", "b", "b");
    }

    [Fact]
    [Trait("Category", "DrawList")]
    public void Node_size_comes_from_the_injected_measurer()
    {
        var node = new NodeDef { Id = "a", Label = "abcd" };

        var narrow = SceneBuilder.MeasureNode(node, Theme.Default, new FakeTextMeasurer());
        var wide = SceneBuilder.MeasureNode(node, Theme.Default, new DoublingMeasurer());

        // 四个字符、字号十四、每字符零点六个字号，再加两侧留白。上下限不参与。
        narrow.Width.Should().BeApproximately(4 * 14 * 0.6 + (16 * 2), 1e-9);
        narrow.Height.Should().BeApproximately((14 * 1.35) + (10 * 2), 1e-9);

        wide.Width.Should().BeGreaterThan(narrow.Width);
    }

    [Fact]
    [Trait("Category", "DrawList")]
    public void A_short_label_still_gets_a_usable_box()
    {
        var single = SceneBuilder.MeasureNode(
            new NodeDef { Id = "a", Label = "x" },
            Theme.Default,
            new FakeTextMeasurer());

        // 一个字符加上留白也宽不过下限，所以宽度被抬到下限。
        single.Width.Should().Be(Theme.Default.MinNodeWidth);
        single.Height.Should().BeGreaterThanOrEqualTo(Theme.Default.MinNodeHeight);

        // 没有标签时块高为零，高度由下限兜住。
        var empty = SceneBuilder.MeasureNode(
            new NodeDef { Id = "a" },
            Theme.Default,
            new FakeTextMeasurer());

        empty.Height.Should().Be(Theme.Default.MinNodeHeight);
    }

    [Fact]
    [Trait("Category", "DrawList")]
    public void Palette_tokens_are_resolved_before_the_list_is_built()
    {
        var theme = Theme.Default.WithPalette(PaletteWithPrimary());
        var node = new NodeDef { Id = "a", Label = "x", StyleToken = "primary" };

        var list = BuildSingleNode(node, theme);

        var shape = list.Commands.OfType<DrawShape>().Single();
        shape.Fill.Should().Be("#dbeafe");
        shape.Stroke.Should().Be("#2563eb");

        list.Commands.OfType<DrawText>().Single().Color.Should().Be("#1e3a8a");

        // 列表里不该留下任何令牌名——留下就等于要求每个绘制方各自再查一次表。
        list.ToText().Should().NotContain("primary");
    }

    [Fact]
    [Trait("Category", "DrawList")]
    public void An_explicit_style_beats_the_token_field_by_field()
    {
        var theme = Theme.Default.WithPalette(PaletteWithPrimary());
        var node = new NodeDef
        {
            Id = "a",
            Label = "x",
            StyleToken = "primary",
            Style = new NodeStyle { Fill = "#000000" },
        };

        var shape = BuildSingleNode(node, theme).Commands.OfType<DrawShape>().Single();

        shape.Fill.Should().Be("#000000");
        shape.Stroke.Should().Be("#2563eb");
    }

    [Fact]
    [Trait("Category", "DrawList")]
    public void A_misspelled_token_is_treated_as_a_colour_literal()
    {
        var theme = Theme.Default.WithPalette(PaletteWithPrimary());

        var literal = BuildSingleNode(new NodeDef { Id = "a", Label = "x", Style = new NodeStyle { Fill = "#123456" } }, theme);
        literal.Commands.OfType<DrawShape>().Single().Fill.Should().Be("#123456");

        // 令牌名拼错时落到主题兜底，而不是变成空字符串。空字符串会让整块变透明，
        // 看起来像渲染坏了，而其实只是拼错了一个词。
        var unknown = BuildSingleNode(new NodeDef { Id = "a", Label = "x", StyleToken = "nope" }, theme);
        unknown.Commands.OfType<DrawShape>().Single().Fill.Should().Be(theme.NodeFill);
    }

    [Fact]
    [Trait("Category", "DrawList")]
    public void A_multiline_label_emits_one_command_per_non_empty_line()
    {
        var node = new NodeDef { Id = "a", Label = "第一行\n\n第三行" };
        var list = BuildSingleNode(node, Theme.Default);

        var texts = list.Commands.OfType<DrawText>().ToArray();

        texts.Should().HaveCount(2);
        texts.Select(t => t.Text).Should().Equal("第一行", "第三行");

        // 空行不出指令但仍然占一行的高度，所以两行之间隔了两个行高。
        var lineHeight = 14 * 1.35;
        (texts[1].Box.Y - texts[0].Box.Y).Should().BeApproximately(lineHeight * 2, 1e-9);
    }

    [Fact]
    [Trait("Category", "DrawList")]
    public void Text_lines_are_centred_inside_their_block()
    {
        var node = new NodeDef { Id = "a", Label = "很长的一行\n短" };
        var list = BuildSingleNode(node, Theme.Default);

        var texts = list.Commands.OfType<DrawText>().ToArray();
        var longCentre = texts[0].Box.CenterX;
        var shortCentre = texts[1].Box.CenterX;

        shortCentre.Should().BeApproximately(longCentre, 1e-9);
    }

    [Fact]
    [Trait("Category", "DrawList")]
    public void Edge_labels_follow_the_declared_position()
    {
        var from = Layouts.Node("a", 0, 0, 100, 40);
        var to = Layouts.Node("b", 300, 0, 100, 40);

        var start = EdgeLabelBox(LabelPosition.Start, from, to);
        var middle = EdgeLabelBox(LabelPosition.Middle, from, to);
        var end = EdgeLabelBox(LabelPosition.End, from, to);

        start.CenterX.Should().BeLessThan(middle.CenterX);
        middle.CenterX.Should().BeLessThan(end.CenterX);

        // 起点与终点让开一段，标签不该压在端点上。
        start.CenterX.Should().BeGreaterThan(from.Right);
        end.CenterX.Should().BeLessThan(to.X);
    }

    [Fact]
    [Trait("Category", "DrawList")]
    public void A_composite_box_wraps_its_members_with_padding_and_a_header()
    {
        var document = DiagramDocument.CreateFromContent(
            "d",
            nodes: [new NodeDef { Id = "a", Label = "甲" }],
            composites: [new GroupDef { Id = "g", Label = "组", Members = ["a"] }]);

        var node = Layouts.Node("a", 100, 100, 120, 44);
        var layout = Layouts.Result([node], [], 400, 400);

        var shape = SceneBuilder
            .Build(document, layout, Theme.Default, new FakeTextMeasurer())
            .Commands.OfType<DrawShape>()
            .Single(s => s.ElementId == "g");

        var padding = Theme.Default.CompositePadding;
        var header = Theme.Default.CompositeHeader;

        shape.Rect.X.Should().BeApproximately(100 - padding, 1e-9);
        shape.Rect.Y.Should().BeApproximately(100 - padding - header, 1e-9);
        shape.Rect.Width.Should().BeApproximately(120 + (padding * 2), 1e-9);
        shape.Rect.Height.Should().BeApproximately(44 + (padding * 2) + header, 1e-9);
    }

    [Fact]
    [Trait("Category", "DrawList")]
    public void A_composite_without_placed_members_is_not_drawn()
    {
        var document = DiagramDocument.CreateFromContent(
            "d",
            nodes: [new NodeDef { Id = "a", Label = "甲" }],
            composites: [new GroupDef { Id = "g", Label = "组", Members = ["a"] }]);

        // 布局结果里没有 a，组合就没有范围可画。
        var list = SceneBuilder.Build(document, Layouts.Result([], [], 0, 0), Theme.Default, new FakeTextMeasurer());

        list.Commands.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "DrawList")]
    public void Elements_missing_from_the_layout_are_skipped()
    {
        var document = DiagramDocument.CreateFromContent(
            "d",
            nodes: [new NodeDef { Id = "a", Label = "甲" }, new NodeDef { Id = "b", Label = "乙" }]);

        var list = SceneBuilder.Build(
            document,
            Layouts.Result([Layouts.Node("a", 0, 0)], [], 100, 100),
            Theme.Default,
            new FakeTextMeasurer());

        list.Commands.Select(c => c.ElementId).Should().Equal("a", "a");
    }

    [Fact]
    [Trait("Category", "DrawList")]
    public void The_same_input_gives_an_equal_list_and_the_same_text()
    {
        var document = DiagramDocument.CreateFromContent(
            "d",
            nodes: [new NodeDef { Id = "a", Label = "甲" }],
            edges: [],
            composites: [new GroupDef { Id = "g", Label = "组", Members = ["a"] }]);

        var layout = Layouts.Result([Layouts.Node("a", 40, 60)], [], 200, 200);
        var measurer = new FakeTextMeasurer();

        var first = SceneBuilder.Build(document, layout, Theme.Default, measurer);
        var second = SceneBuilder.Build(document, layout, Theme.Default, measurer);

        second.Should().Be(first);
        second.ToText().Should().Be(first.ToText());
    }

    [Fact]
    [Trait("Category", "DrawList")]
    public void The_canonical_text_has_one_line_per_command_after_the_canvas_line()
    {
        var list = BuildSingleNode(new NodeDef { Id = "a", Label = "甲" }, Theme.Default);

        var lines = list.ToText().TrimEnd('\n').Split('\n');

        lines.Should().HaveCount(list.Commands.Count + 1);
        lines[0].Should().StartWith("canvas ");
        lines[1].Should().StartWith("shape a ");
        lines[2].Should().StartWith("text a ");
    }

    private static DrawList BuildSingleNode(NodeDef node, Theme theme)
    {
        var document = DiagramDocument.CreateFromContent("d", nodes: [node]);
        var layout = Layouts.Result([Layouts.Node(node.Id, 0, 0)], [], 200, 200);

        return SceneBuilder.Build(document, layout, theme, new FakeTextMeasurer());
    }

    private static SpatialRect EdgeLabelBox(LabelPosition position, PlacedNode from, PlacedNode to)
    {
        var edge = new EdgeDef { Id = "e", From = "a", To = "b", Label = "标签", Style = new EdgeStyle { LabelPosition = position } };
        var document = DiagramDocument.CreateFromContent(
            "d",
            nodes: [new NodeDef { Id = "a", Label = "甲" }, new NodeDef { Id = "b", Label = "乙" }],
            edges: [edge]);

        var layout = Layouts.Result([from, to], [Layouts.Horizontal("e", from, to)], 400, 100);
        var list = SceneBuilder.Build(document, layout, Theme.Default, new FakeTextMeasurer());

        return list.Commands.OfType<DrawText>().Single(t => t.ElementId == "e").Box;
    }

    private static Palette PaletteWithPrimary() => new()
    {
        Entries = new Dictionary<string, PaletteEntry>(StringComparer.Ordinal)
        {
            ["primary"] = new PaletteEntry
            {
                Name = "primary",
                Fill = "#dbeafe",
                Stroke = "#2563eb",
                Text = "#1e3a8a",
            },
        },
    };

    /// <summary>比假度量器宽一倍的度量器。用来证明尺寸确实来自注入的那个。</summary>
    private sealed class DoublingMeasurer : ITextMeasurer
    {
        public Size Measure(string text, string fontFamily, double fontSize, FontWeight weight) =>
            new(text.Length * fontSize * 1.2, fontSize);
    }
}
