using System.Globalization;
using System.Xml.Linq;
using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 绘制列表导出成 SVG。
/// </summary>
/// <remarks>
/// <para>
/// **这一组盯的是"每一类绘制命令都有对应的元素"。** 漏一类的话，导出少画一样东西
/// 而没有任何报错——这类静默丢失在本仓已经踩过一次，所以判据写成逐类核对，
/// 不是"导出来的文件里有东西"。
/// </para>
/// <para>
/// 导出消费的是绘制列表，所以这一组拿的是与画布同一份列表：先把文档走完整条链路，
/// 再把列表交给导出器。逐条比对的是元素个数与属性，不比字符串——
/// 比字符串的话，改一处缩进就要跟着改期望值，而那种红与"漏了一类"分不清。
/// </para>
/// </remarks>
public sealed class SvgExportTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    #region 每一类指令都有对应的元素

    /// <summary>八种内置形状各导出一次，每一种都要落成一个元素。</summary>
    [Fact]
    [Trait("Category", "SvgExport")]
    public void Every_shape_geometry_has_an_element()
    {
        var nodes = Enum.GetValues<NodeShape>()
            .Select((shape, index) => new NodeDef { Id = $"n{index}", Label = "x", Shape = shape })
            .ToArray();

        var svg = Root(Build(nodes));
        var shapes = svg.Descendants().Count(element => IsShape(element));

        shapes.Should().Be(nodes.Length, "八种形状一个都不能少");
    }

    /// <summary>圆形走椭圆那一个元素：外接框是正方形时它就是圆。</summary>
    [Fact]
    [Trait("Category", "SvgExport")]
    public void A_circle_becomes_an_ellipse_element()
    {
        var svg = Root(Build([new NodeDef { Id = "n1", Label = "x", Shape = NodeShape.Circle }]));
        var ellipse = svg.Descendants().Single(element => element.Name.LocalName == "ellipse");

        double.Parse(Attr(ellipse, "rx"), CultureInfo.InvariantCulture).Should().BePositive();
        double.Parse(Attr(ellipse, "ry"), CultureInfo.InvariantCulture).Should().BePositive();
    }

    /// <summary>自定义形状带上来的几何直接写成 path。</summary>
    [Fact]
    [Trait("Category", "SvgExport")]
    public void A_custom_shape_becomes_a_path()
    {
        var svg = Root(Build(
        [
            new NodeDef { Id = "n1", Label = "x", ShapePath = "M 0 0 L 1 0 L 1 1 L 0 1 Z" },
        ]));
        var path = svg.Descendants().Single(element => element.Name.LocalName == "path");

        Attr(path, "d").Should().StartWith("M").And.EndWith("Z");
    }

    /// <summary>折线总是一条 polyline，端点箭头各有各的记号。</summary>
    [Theory]
    [Trait("Category", "SvgExport")]
    [InlineData(ArrowStyle.None, 0)]
    [InlineData(ArrowStyle.Arrow, 1)]
    [InlineData(ArrowStyle.OpenArrow, 1)]
    [InlineData(ArrowStyle.Circle, 1)]
    [InlineData(ArrowStyle.Cross, 1)]
    public void An_arrow_style_has_its_own_mark(ArrowStyle arrow, int marks)
    {
        var svg = Root(Edge(arrow));

        svg.Descendants().Count(element => element.Name.LocalName == "polyline")
            .Should().Be(1, "连线本身总是一条折线");

        svg.Descendants().Count(element => element.Name.LocalName is "polygon" or "circle" or "path")
            .Should().Be(marks, $"{arrow} 这一档要么不画箭头，要么正好画一笔");
    }

    /// <summary>线型写成虚线段，两种线型各有各的写法。</summary>
    [Fact]
    [Trait("Category", "SvgExport")]
    public void A_line_style_becomes_a_dash_pattern()
    {
        var solid = Dash(Edge(ArrowStyle.None, LineStyle.Solid));
        var dashed = Dash(Edge(ArrowStyle.None, LineStyle.Dashed));
        var dotted = Dash(Edge(ArrowStyle.None, LineStyle.Dotted));

        solid.Should().BeNull("实线不写虚线段");
        dashed.Should().NotBeNull();
        dotted.Should().NotBeNull().And.NotBe(dashed);
    }

    /// <summary>文字是 text 元素，字体、字号、字重与两项修饰都带上。</summary>
    [Fact]
    [Trait("Category", "SvgExport")]
    public void Text_carries_its_font_and_decorations()
    {
        var svg = Root(Build(
        [
            new NodeDef
            {
                Id = "n1",
                Label = "标题",
                Text = new TextStyle { FontWeight = FontWeight.Bold },
                RichText = true,
                RichLabel = new RichTextContent
                {
                    Paragraphs =
                    [
                        new RichParagraph
                        {
                            Runs =
                            [
                                new RichRun
                                {
                                    Text = "标题",
                                    Style = new RichRunStyle { Italic = true, Underline = true },
                                },
                            ],
                        },
                    ],
                },
            },
        ]));

        var text = svg.Descendants().Single(element => element.Name.LocalName == "text");

        text.Value.Should().Be("标题");
        Attr(text, "font-family").Should().NotBeEmpty();
        Attr(text, "font-size").Should().NotBeEmpty();
        Attr(text, "font-weight").Should().Be("bold");
        Attr(text, "font-style").Should().Be("italic");
        Attr(text, "text-decoration").Should().Contain("underline");
    }

    /// <summary>文字里的尖括号与引号要转义，不然导出的文件根本解析不了。</summary>
    [Fact]
    [Trait("Category", "SvgExport")]
    public void Markup_in_a_label_is_escaped()
    {
        var svg = Root(Build([new NodeDef { Id = "n1", Label = "<a & b> \"c\"" }]));
        var text = svg.Descendants().Single(element => element.Name.LocalName == "text");

        text.Value.Should().Be("<a & b> \"c\"");
    }

    #endregion

    #region 坐标系与确定性

    /// <summary>viewBox 要显式写出来，尺寸取绘制列表的宽高。</summary>
    [Fact]
    [Trait("Category", "SvgExport")]
    public void The_view_box_is_written_out()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "x" }]);

        Attr(Root(list), "viewBox").Should().Be(
            $"0 0 {Number(list.Width)} {Number(list.Height)}",
            "不写 viewBox 的话，不同查看器里的尺寸不一样，而用户以为是导出错了");
    }

    /// <summary>留白同时改 viewBox 与内容的位置，两者不许各走各的。</summary>
    [Fact]
    [Trait("Category", "SvgExport")]
    public void Padding_shifts_the_view_box_and_the_content_together()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "x" }]);
        var plain = Shape(Root(list));
        var padded = Shape(Root(list, new SvgOptions { Padding = 10 }));

        (double.Parse(Attr(padded, "x"), CultureInfo.InvariantCulture)
            - double.Parse(Attr(plain, "x"), CultureInfo.InvariantCulture))
            .Should().Be(10, "形状跟着留白一起挪");

        Attr(Root(list, new SvgOptions { Padding = 10 }), "viewBox")
            .Should().Be($"0 0 {Number(list.Width + 20)} {Number(list.Height + 20)}");
    }

    /// <summary>不铺背景时没有那一个文档级的矩形。</summary>
    [Fact]
    [Trait("Category", "SvgExport")]
    public void The_background_can_be_left_out()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "x" }]);

        Root(list).Descendants().Count(element => Attr(element, "fill") == list.Background)
            .Should().Be(1, "背景是一个与文档等大的矩形");

        Root(list, new SvgOptions { IncludeBackground = false })
            .Descendants().Count(element => Attr(element, "fill") == list.Background)
            .Should().Be(0);
    }

    /// <summary>
    /// 同一份绘制列表导出两次逐字节相同。
    /// </summary>
    /// <remarks>
    /// 带时间戳或随机标识的话，SVG 没法进版本控制，
    /// 而"导出的图有没有变"这个问题就永远答不了。
    /// </remarks>
    [Fact]
    [Trait("Category", "SvgExport")]
    [Trait("Category", "SceneSnapshot")]
    public void The_same_list_exports_the_same_text()
    {
        var list = Build(
        [
            new NodeDef { Id = "n1", Label = "开始", Shape = NodeShape.Stadium },
            new NodeDef { Id = "n2", Label = "校验", Shape = NodeShape.Diamond },
        ]);

        SvgExporter.Export(list).Svg.Should().Be(SvgExporter.Export(list).Svg);
    }

    /// <summary>导出的东西是能解析的 XML，而且一条指令一个元素。</summary>
    [Fact]
    [Trait("Category", "SvgExport")]
    public void The_export_is_parseable_and_covers_every_command()
    {
        var list = Build(
        [
            new NodeDef { Id = "n1", Label = "开始", Shape = NodeShape.Stadium },
            new NodeDef { Id = "n2", Label = "校验", Shape = NodeShape.Diamond },
            new NodeDef { Id = "n3", Label = "结束" },
        ]);

        list.Commands.Should().NotBeEmpty();

        Root(list, new SvgOptions { IncludeBackground = false })
            .Descendants().Count(element => element.Name.LocalName != "svg")
            .Should().Be(
                list.Commands.Count,
                "一条指令一个元素；少一个就是某一类被静默跳过了");
    }

    #endregion

    #region 丢失清单

    /// <summary>丢失清单要原样带给调用方：文字留成 text 是有代价的。</summary>
    [Fact]
    [Trait("Category", "SvgExport")]
    public void The_loss_report_says_what_the_text_choice_costs()
    {
        var loss = SvgExporter.Export(Build([new NodeDef { Id = "n1", Label = "x" }]))
            .Dropped.Should().ContainSingle().Subject;

        loss.Feature.Should().Contain("文字");
        loss.Reason.Should().NotBeEmpty("只说丢了什么、不说为什么，等于把问题原样丢回去");
    }

    #endregion

    #region 夹具

    /// <summary>形状元素。背景那一个矩形没有描边，不算形状。</summary>
    private static bool IsShape(XElement element) =>
        element.Name.LocalName is "rect" or "ellipse" or "polygon" or "path"
        && element.Attribute("stroke") is not null;

    /// <summary>那个节点框：有描边的形状元素，背景那一个没有描边。</summary>
    private static XElement Shape(XElement svg) => svg.Descendants().First(IsShape);

    private static string? Dash(DrawList list) =>
        Root(list).Descendants().Single(element => element.Name.LocalName == "polyline")
            .Attribute("stroke-dasharray")?.Value;

    private static string Attr(XElement element, string name) =>
        element.Attribute(name)?.Value ?? throw new InvalidOperationException($"元素里没有 {name} 这个属性");

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static XElement Root(DrawList list, SvgOptions? options = null) =>
        XElement.Parse(SvgExporter.Export(list, options).Svg);

    private static DrawList Build(NodeDef[] nodes)
    {
        var theme = Theme.Default;
        var placed = new PlacedNode[nodes.Length];

        for (var index = 0; index < nodes.Length; index++)
        {
            var size = SceneBuilder.MeasureNode(nodes[index], theme, Measurer);

            placed[index] = new PlacedNode(nodes[index].Id, 40 + (index * 160), 40, size.Width, size.Height);
        }

        var document = DiagramDocument.CreateFromContent("svg", nodes: nodes);

        return SceneBuilder.Build(document, Layouts.Result(placed, [], 600, 300), theme, Measurer);
    }

    private static DrawList Edge(ArrowStyle arrow, LineStyle line = LineStyle.Solid)
    {
        var theme = Theme.Default;
        var nodes = new[]
        {
            new NodeDef { Id = "a", Label = "甲" },
            new NodeDef { Id = "b", Label = "乙" },
        };
        var placed = new[]
        {
            new PlacedNode("a", 40, 40, 80, 40),
            new PlacedNode("b", 40, 160, 80, 40),
        };
        var document = DiagramDocument.CreateFromContent(
            "svg",
            nodes: nodes,
            edges:
            [
                new EdgeDef
                {
                    Id = "e",
                    From = "a",
                    To = "b",
                    Style = new EdgeStyle { Arrow = arrow, Line = line },
                },
            ]);

        var layout = Layouts.Result(placed, [Layouts.Edge("e", (80, 80), (80, 160))], 300, 300);

        return SceneBuilder.Build(document, layout, theme, Measurer);
    }

    #endregion
}
