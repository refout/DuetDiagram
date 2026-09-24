using DuetDiagram.Core.Model;
using DuetDiagram.Core.Shapes;
using DuetDiagram.Layout;
using DuetDiagram.Render;
using FluentAssertions;
using SkiaSharp;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 绘制列表光栅化成 PNG。
/// </summary>
/// <remarks>
/// <para>
/// **这一组盯的是"同一份列表导两次逐像素相同"与"三种指令都被消费掉"。** 前者是确定性，
/// 后者是完整性——位图里没法逐类数元素，所以"少画了一类"这件事只能靠"所有指令都走一遍
/// 而不抛异常"来守，认不出的指令一律抛异常而不是跳过。
/// </para>
/// <para>
/// 尺寸、缩放、背景与裁剪范围各有用例：这四样是导出选项的全部，少验一样，
/// 那一档选项写反了也看不出来（例如把"倍数"当成了"目标像素宽度"）。
/// </para>
/// </remarks>
public sealed class BitmapExportTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    #region 尺寸与缩放

    /// <summary>按内容外接框裁时，位图尺寸就是绘制列表的宽高。</summary>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void The_bitmap_has_the_content_size()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "开始" }]);
        var export = BitmapExporter.Export(list);

        export.Width.Should().Be((int)Math.Ceiling(list.Width));
        export.Height.Should().Be((int)Math.Ceiling(list.Height));
        export.Png.Should().NotBeEmpty();
    }

    /// <summary>
    /// 缩放是倍数，不是目标像素宽度。
    /// </summary>
    /// <remarks>
    /// 给宽度的话，同一份文档导两次（一次 1000 像素、一次 2000 像素）会各自算出一个
    /// 缩放系数，而用户以为自己哪一步写错了。这一条把"倍数"钉死成两倍就是两倍。
    /// </remarks>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void Scaling_doubles_both_dimensions()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "开始" }]);

        var once = BitmapExporter.Export(list);
        var twice = BitmapExporter.Export(list, new BitmapOptions { Scale = 2 });

        twice.Width.Should().Be(once.Width * 2);
        twice.Height.Should().Be(once.Height * 2);
    }

    /// <summary>留白把位图撑大，同时把内容整体挪进去。</summary>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void Padding_grows_the_bitmap_and_shifts_the_content()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "开始" }]);

        var plain = BitmapExporter.Export(list);
        var padded = BitmapExporter.Export(list, new BitmapOptions { Padding = 10 });

        padded.Width.Should().Be(plain.Width + 20);
        padded.Height.Should().Be(plain.Height + 20);

        // 左上角那一片在没留白时是形状的描边，留白之后是底色。
        Decode(padded).GetPixel(1, 1).Should().Be(Decode(plain).GetPixel(11, 11));
    }

    /// <summary>缩放倍数为零或负数要当场拒掉，不能导出一张一像素的图。</summary>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void A_non_positive_scale_is_refused()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "开始" }]);

        var act = () => BitmapExporter.Export(list, new BitmapOptions { Scale = 0 });

        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region 背景与透明度

    /// <summary>
    /// 缺省不透明，底色取绘制列表的背景色。
    /// </summary>
    /// <remarks>
    /// 缺省透明的话，导出的 PNG 贴进白底文档里会变成黑底——这是"导出看起来坏了"
    /// 里最常见的一种，所以缺省必须是不透明的。
    /// </remarks>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void The_background_is_opaque_by_default()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "开始" }]);
        var corner = Decode(BitmapExporter.Export(list)).GetPixel(0, 0);

        corner.Alpha.Should().Be(255);
        corner.Should().Be(SKColor.Parse(list.Background));
    }

    /// <summary>要透明时才透明，角落那个像素的通道值是零。</summary>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void A_transparent_bitmap_leaves_the_corner_empty()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "开始" }]);
        var corner = Decode(BitmapExporter.Export(list, new BitmapOptions { Transparent = true })).GetPixel(0, 0);

        corner.Alpha.Should().Be(0);
    }

    #endregion

    #region 裁剪范围

    /// <summary>按页面裁时，位图尺寸是页面尺寸，不是内容尺寸。</summary>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void Cropping_to_the_page_uses_the_page_size()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "开始" }]);
        var export = BitmapExporter.Export(
            list,
            new BitmapOptions { Crop = BitmapCrop.Page, PageSize = new Size(1123, 794) });

        export.Width.Should().Be(1123);
        export.Height.Should().Be(794);
    }

    /// <summary>
    /// 按页面裁又裁掉了内容时要如实报出来。
    /// </summary>
    /// <remarks>
    /// 静默裁掉一块的话，用户拿到一张缺了角而"看起来正常"的图，而那种缺失在缩略图上
    /// 根本看不出来。所以这一档必须进丢失清单，并且说清内容实际多大。
    /// </remarks>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void Cropping_to_the_page_reports_what_it_cut()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "开始" }]);
        var export = BitmapExporter.Export(
            list,
            new BitmapOptions { Crop = BitmapCrop.Page, PageSize = new Size(1, 1) });

        export.Dropped.Should().Contain(loss => loss.Feature.Contains("超出页面"));
        export.Dropped.Single(loss => loss.Feature.Contains("超出页面")).Reason
            .Should().Contain("内容实际", "只说裁了、不说裁掉多少，等于把问题原样丢回去");
    }

    /// <summary>内容装得下页面时不该多报一条。</summary>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void Cropping_to_a_page_that_fits_reports_nothing_extra()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "开始" }]);
        var export = BitmapExporter.Export(
            list,
            new BitmapOptions { Crop = BitmapCrop.Page, PageSize = new Size(10000, 10000) });

        export.Dropped.Should().NotContain(loss => loss.Feature.Contains("超出页面"));
    }

    /// <summary>按页面裁却不给页面尺寸，是调用方的编程错误，当场拒掉。</summary>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void Cropping_to_the_page_without_a_page_size_is_refused()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "开始" }]);

        var act = () => BitmapExporter.Export(list, new BitmapOptions { Crop = BitmapCrop.Page });

        act.Should().Throw<ArgumentException>().WithMessage("*页面尺寸*");
    }

    #endregion

    #region 确定性

    /// <summary>
    /// 同一份绘制列表与同一组选项导出两次逐像素相同。
    /// </summary>
    /// <remarks>
    /// 带时间戳或随机标识的话，"导出的图有没有变"这个问题就永远答不了。
    /// 比的是像素而不是文件字节：像素相同才是真的同一张图，
    /// 编码器换个压缩档位不该让这条判据变红。
    /// </remarks>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void Two_exports_are_pixel_identical()
    {
        var list = Build(
        [
            new NodeDef { Id = "n1", Label = "开始", Shape = NodeShape.Stadium },
            new NodeDef { Id = "n2", Label = "校验", Shape = NodeShape.Diamond },
            new NodeDef { Id = "n3", Label = "结束", Shape = NodeShape.Circle },
        ]);

        using var first = Decode(BitmapExporter.Export(list, new BitmapOptions { Scale = 2 }));
        using var second = Decode(BitmapExporter.Export(list, new BitmapOptions { Scale = 2 }));

        first.GetPixelSpan().ToArray().Should().Equal(second.GetPixelSpan().ToArray());
    }

    #endregion

    #region 每一类指令都被消费

    /// <summary>三种指令都画一遍而不抛异常：形状、折线、文本。</summary>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void Every_command_kind_is_drawn()
    {
        var list = Edge(ArrowStyle.Arrow);

        list.Commands.Should().Contain(command => command is DrawShape);
        list.Commands.Should().Contain(command => command is DrawPolyline);
        list.Commands.Should().Contain(command => command is DrawText);

        var export = BitmapExporter.Export(list);

        export.Width.Should().BePositive();
        export.Png.Should().NotBeEmpty();
    }

    /// <summary>
    /// 认不出的指令抛异常，不静默跳过。
    /// </summary>
    /// <remarks>
    /// 跳过的话，加一种绘制指令之后导出会少画一样东西而没有任何报错。
    /// 画布与 SVG 导出那两边同一条口径。
    /// </remarks>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void An_unknown_command_throws()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "开始" }]) with
        {
            Commands = [new FakeCommand("n1")],
        };

        var act = () => BitmapExporter.Export(list);

        act.Should().Throw<NotSupportedException>();
    }

    /// <summary>八种内置形状各画一遍，一种都不能漏。</summary>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void Every_built_in_shape_is_drawn()
    {
        var nodes = Enum.GetValues<NodeShape>()
            .Select((shape, index) => new NodeDef { Id = $"n{index}", Label = "x", Shape = shape })
            .ToArray();

        var export = BitmapExporter.Export(Build(nodes));

        export.Png.Should().NotBeEmpty();
    }

    /// <summary>自定义形状带上来的路径也画得出来，与内置几何走同一段画法。</summary>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void A_custom_shape_is_drawn()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "x", ShapePath = "M 0 0 L 1 0 L 1 1 L 0 1 Z" }]);

        var export = BitmapExporter.Export(list);

        export.Png.Should().NotBeEmpty();
    }

    /// <summary>五种箭头各画一遍。</summary>
    [Theory]
    [Trait("Category", "BitmapExport")]
    [InlineData(ArrowStyle.None)]
    [InlineData(ArrowStyle.Arrow)]
    [InlineData(ArrowStyle.OpenArrow)]
    [InlineData(ArrowStyle.Circle)]
    [InlineData(ArrowStyle.Cross)]
    public void Every_arrow_style_is_drawn(ArrowStyle arrow)
    {
        BitmapExporter.Export(Edge(arrow)).Png.Should().NotBeEmpty();
    }

    /// <summary>三种线型各画一遍。</summary>
    [Theory]
    [Trait("Category", "BitmapExport")]
    [InlineData(LineStyle.Solid)]
    [InlineData(LineStyle.Dashed)]
    [InlineData(LineStyle.Dotted)]
    public void Every_line_style_is_drawn(LineStyle line)
    {
        BitmapExporter.Export(Edge(ArrowStyle.None, line)).Png.Should().NotBeEmpty();
    }

    /// <summary>加粗、倾斜与两项装饰都画得出来。</summary>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void Text_decorations_are_drawn()
    {
        var list = Build(
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
        ]);

        BitmapExporter.Export(list).Png.Should().NotBeEmpty();
    }

    /// <summary>空文字与不足两点的折线画不出东西，跳过它们不算漏画。</summary>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void Empty_text_and_short_polylines_are_skipped()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "" }]) with
        {
            Commands =
            [
                new DrawText("n1", string.Empty, new SpatialRect(10, 10, 40, 20), "#000000", "Segoe UI", 14, FontWeight.Normal),
                new DrawPolyline("e1", [new DrawPoint(0, 0)], "#000000", 1, LineStyle.Solid, ArrowStyle.None),
            ],
        };

        BitmapExporter.Export(list).Png.Should().NotBeEmpty();
    }

    /// <summary>认不出的颜色退回兜底色，而不是整张图导不出来。</summary>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void An_unparseable_color_does_not_stop_the_export()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "开始" }]) with { Background = "not-a-color" };

        var export = BitmapExporter.Export(list);

        Decode(export).GetPixel(0, 0).Should().Be(SKColors.White);
    }

    /// <summary>
    /// 中文标签要画出真的字形，不能是空白方块。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 文档点名的家族常常认不出汉字（一份中文流程图配上西文家族就是）。不挑字体的话，
    /// 那些字会画成空白方块，而看的人第一反应是"程序坏了"。
    /// </para>
    /// <para>
    /// 判据取"整字宽"：一个汉字在它自己的字号下占满一个正方，画出来的墨迹横向也就接近
    /// 一个字号那么宽；空白方块走的是西文兜底字形的宽度，比字号窄得多。
    /// 只画一段文字、不带任何形状，量到的墨迹就全是这段文字。
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void Chinese_labels_are_drawn_as_real_glyphs()
    {
        const int Size = 14;
        var ink = InkWidth(Text("开始", Size));

        ink.Should().BeGreaterThan(
            Size * 3 / 2,
            "两个汉字横着占大约两个字号的宽度；画成空白方块的话，宽度只有西文兜底字形那么宽");
    }

    /// <summary>西文标签走点名的家族，不用回退字体。</summary>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void Latin_labels_are_drawn()
    {
        InkWidth(Text("check", 14)).Should().BePositive();
    }

    /// <summary>中西混排的标签整段画得出来。</summary>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void Mixed_scripts_are_drawn()
    {
        InkWidth(Text("甲 check", 14)).Should().BeGreaterThan(14 * 3 / 2);
    }

    #endregion

    #region 丢失清单

    /// <summary>位图这一档永远有一条代价：所有东西都变成像素，不能再编辑。</summary>
    [Fact]
    [Trait("Category", "BitmapExport")]
    public void The_loss_report_says_the_bitmap_costs_editability()
    {
        var loss = BitmapExporter.Export(Build([new NodeDef { Id = "n1", Label = "x" }]))
            .Dropped.Should().ContainSingle().Subject;

        loss.Feature.Should().Contain("可编辑");
        loss.Reason.Should().NotBeEmpty("只说丢了什么、不说为什么，等于把问题原样丢回去");
    }

    #endregion

    #region 夹具

    private sealed record FakeCommand(string Id) : DrawCommand(Id)
    {
        public override string Describe() => "fake";
    }

    /// <summary>只画一段文字的一份绘制列表，用来单独量文字画成了什么样。</summary>
    private static DrawList Text(string value, int size)
    {
        var box = new SpatialRect(4, 4, size * value.Length, size * 1.6);

        return new DrawList(
            [new DrawText("t", value, box, "#000000", "Segoe UI", size, FontWeight.Normal)],
            (size * (value.Length + 2)) + 8,
            (size * 3) + 8,
            "#ffffff");
    }

    /// <summary>墨迹横向占多少像素。底色是白的，所以非白的就是画出来的。</summary>
    private static int InkWidth(DrawList list)
    {
        using var bitmap = Decode(BitmapExporter.Export(list));

        var left = int.MaxValue;
        var right = int.MinValue;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y) == SKColors.White)
                {
                    continue;
                }

                left = Math.Min(left, x);
                right = Math.Max(right, x);
            }
        }

        return right < left ? 0 : right - left + 1;
    }

    private static SKBitmap Decode(BitmapExport export) =>
        SKBitmap.Decode(export.Png) ?? throw new InvalidOperationException("导出的 PNG 解不开");

    private static DrawList Build(NodeDef[] nodes)
    {
        var theme = Theme.Default;
        var placed = new PlacedNode[nodes.Length];

        for (var index = 0; index < nodes.Length; index++)
        {
            var size = SceneBuilder.MeasureNode(nodes[index], theme, Measurer);

            placed[index] = new PlacedNode(nodes[index].Id, 40 + (index * 160), 40, size.Width, size.Height);
        }

        var document = DiagramDocument.CreateFromContent("bitmap", nodes: nodes);

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
            "bitmap",
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
