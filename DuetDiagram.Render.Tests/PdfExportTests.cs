using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 绘制列表写成 PDF。
/// </summary>
/// <remarks>
/// <para>
/// **这一组盯的是四样：纸张大小、页数、矢量还是位图、以及确定性。** 前两样是 PDF
/// 与 SVG、PNG 两档唯一的结构差别——它自己就有页的概念，而那两个要靠投影掉。
/// 后两样是这一档存在的理由：矢量意味着放大不糊、文字能选中，而代价写在丢失清单里。
/// </para>
/// <para>
/// **判据尽量落在 PDF 自己的结构上，不落在我们写的代码上。** 页数数
/// <c>/Type /Page</c>、纸张大小读 <c>/MediaBox</c>、是不是位图看有没有
/// <c>/Subtype /Image</c>、字体嵌没嵌看有没有 <c>/FontFile</c>——
/// 这些都是文件的属性，换一个写入器它们照样成立。只有"文字是不是真文字"
/// 必须把内容流解压出来看操作符，那是唯一一处得读内容的。
/// </para>
/// <para>
/// **内容流要解压才看得见。** 底层写入器会把内容流压成 zlib，所以直接搜字节搜到的是
/// 压缩后的噪声——搜出来的"有 Tj"可能只是两个字节碰巧长得像。
/// 凡是读内容的地方都先解压。
/// </para>
/// </remarks>
public sealed class PdfExportTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    #region 纸张大小

    /// <summary>按内容外接框裁时，纸张就是内容的宽高折成点。</summary>
    [Fact]
    [Trait("Category", "PdfExport")]
    public void The_page_is_the_content_bounds_by_default()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "开始" }]);
        var export = PdfExporter.Export([list]);

        PageSizes(export).Should().ContainSingle().Which
            .Should().Be((list.Width * PdfExporter.PointsPerUnit, list.Height * PdfExporter.PointsPerUnit));
    }

    /// <summary>
    /// 按页面裁时，纸张是页面尺寸，不是内容尺寸。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 容差是一位，因为底层写入器把纸张取整到整点：1123×794 折成 842.25×595.5，
    /// 写进文件是 842×595。差不到一个点，落在纸上分不出来，但"这里会取整"这件事
    /// 得写下来——哪天有人要一个精确到小数点的纸张，先要知道它办不到。
    /// </para>
    /// <para>
    /// 末一条断言才是这个用例真正盯的：纸张比内容大得多，说明定范围的那一档是页面
    /// 而不是内容外接框。只比一个数的话，两档取到同一个值也照样绿。
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Category", "PdfExport")]
    public void Cropping_to_the_page_uses_the_page_size()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "开始" }]);
        var export = PdfExporter.Export(
            [list],
            new PdfOptions { Crop = PdfCrop.Page, PageSize = new Size(1123, 794) });

        var (width, height) = PageSizes(export).Single();

        width.Should().BeApproximately(1123 * PdfExporter.PointsPerUnit, 1);
        height.Should().BeApproximately(794 * PdfExporter.PointsPerUnit, 1);
        width.Should().BeGreaterThan(list.Width * PdfExporter.PointsPerUnit);
    }

    /// <summary>
    /// 文档单位折成点是四分之三，而这不是随手挑的一个数。
    /// </summary>
    /// <remarks>
    /// 缺省页面尺寸 1123×794 是 A4 横放在 96 像素每英寸下的像素数，折成点正好是
    /// A4 的 842×595。所以一份按缺省页面尺寸导出的文档，打印出来正好铺满一张 A4——
    /// 如果哪天有人把这个系数改成 1，这一条会红，而"打印出来差了一圈"是那种
    /// 只有真的打印才发现得了的问题。
    /// </remarks>
    [Fact]
    [Trait("Category", "PdfExport")]
    public void The_default_page_size_lands_on_a4()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "开始" }]);
        var export = PdfExporter.Export(
            [list],
            new PdfOptions { Crop = PdfCrop.Page, PageSize = new Size(1123, 794) });

        var (width, height) = PageSizes(export).Single();

        // A4 的长边 297 毫米是 841.89 点。差不到一个点就是同一个纸张。
        width.Should().BeApproximately(841.89, 1);
        height.Should().BeApproximately(595.28, 1);
    }

    /// <summary>
    /// 按页面裁又裁掉了内容时要如实报出来。
    /// </summary>
    /// <remarks>
    /// 静默裁掉一块的话，用户拿到一份缺了角的文件，而翻到那一页之前根本看不出来。
    /// 与位图那一档同一条口径。
    /// </remarks>
    [Fact]
    [Trait("Category", "PdfExport")]
    public void Cropping_to_the_page_reports_what_it_cut()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "开始" }]);
        var export = PdfExporter.Export(
            [list],
            new PdfOptions { Crop = PdfCrop.Page, PageSize = new Size(1, 1) });

        export.Dropped.Should().Contain(loss => loss.Feature.Contains("超出页面"));
        export.Dropped.Single(loss => loss.Feature.Contains("超出页面")).Reason
            .Should().Contain("内容最大", "只说裁了、不说裁掉多少，等于把问题原样丢回去");
    }

    /// <summary>内容装得下页面时不该多报一条。</summary>
    [Fact]
    [Trait("Category", "PdfExport")]
    public void Cropping_to_a_page_that_fits_reports_nothing_extra()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "开始" }]);
        var export = PdfExporter.Export(
            [list],
            new PdfOptions { Crop = PdfCrop.Page, PageSize = new Size(10000, 10000) });

        export.Dropped.Should().NotContain(loss => loss.Feature.Contains("超出页面"));
    }

    /// <summary>按页面裁却不给页面尺寸，是调用方的编程错误，当场拒掉。</summary>
    [Fact]
    [Trait("Category", "PdfExport")]
    public void Cropping_to_the_page_without_a_page_size_is_refused()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "开始" }]);

        var act = () => PdfExporter.Export([list], new PdfOptions { Crop = PdfCrop.Page });

        act.Should().Throw<ArgumentException>().WithMessage("*页面尺寸*");
    }

    #endregion

    #region 多页

    /// <summary>一份绘制列表一页。</summary>
    [Fact]
    [Trait("Category", "PdfExport")]
    public void Each_draw_list_becomes_one_page()
    {
        var export = PdfExporter.Export([Page("甲"), Page("乙"), Page("丙")]);

        export.Pages.Should().Be(3);
        PageCount(export).Should().Be(3, "报出来的页数要与文件里真的页数对得上");
    }

    /// <summary>
    /// 按内容外接框裁时，各页取同一个尺寸。
    /// </summary>
    /// <remarks>
    /// 各页各按自己的内容定的话，一份三页的文件会有三种纸张大小，翻页时页面跳来跳去，
    /// 打印时每一页的缩放比还不一样。取各页里最大的那个宽与最大的那个高，
    /// 小的那几页多留一点空白——这是唯一一个能让各页一样大的取法。
    /// </remarks>
    [Fact]
    [Trait("Category", "PdfExport")]
    public void The_pages_of_a_multi_page_export_share_one_size()
    {
        var small = Build([new NodeDef { Id = "n1", Label = "甲" }]);
        var large = Build(
        [
            new NodeDef { Id = "n1", Label = "甲" },
            new NodeDef { Id = "n2", Label = "乙" },
            new NodeDef { Id = "n3", Label = "丙" },
            new NodeDef { Id = "n4", Label = "丁" },
        ]);

        var export = PdfExporter.Export([small, large]);
        var sizes = PageSizes(export);

        sizes.Should().HaveCount(2);
        sizes.Distinct().Should().ContainSingle("各页尺寸不一样的话，翻页时页面会跳");
        sizes[0].Should().Be((large.Width * PdfExporter.PointsPerUnit, large.Height * PdfExporter.PointsPerUnit));
    }

    /// <summary>按页面裁时各页本来就一样大，多页也只是页数多。</summary>
    [Fact]
    [Trait("Category", "PdfExport")]
    public void Cropping_to_the_page_makes_every_page_the_same_size()
    {
        var export = PdfExporter.Export(
            [Page("甲"), Page("乙")],
            new PdfOptions { Crop = PdfCrop.Page, PageSize = new Size(1123, 794) });

        PageSizes(export).Distinct().Should().ContainSingle();
    }

    /// <summary>一页都不给是调用方的编程错误：PDF 至少要有一页。</summary>
    [Fact]
    [Trait("Category", "PdfExport")]
    public void An_empty_page_list_is_refused()
    {
        var act = () => PdfExporter.Export([]);

        act.Should().Throw<ArgumentException>().WithMessage("*一页*");
    }

    #endregion

    #region 矢量还是位图

    /// <summary>
    /// 这份导出是矢量的：线条是路径、文字是真文字，没有一个位图。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 判据是"文件里没有位图对象、而有嵌入的字体"。位图那一档导出来的文件正好相反：
    /// 一个大的位图对象、一个字体都没有。所以这一条能真的把两条路分开，
    /// 而不是只证明"我们写了 PDF"。
    /// </para>
    /// <para>
    /// 中文文档还要多一条：字体是 CID 型、按 Identity-H 编码嵌进去的。
    /// 少了这一条，汉字到了没有那份字体的机器上就是方块。
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Category", "PdfExport")]
    public void The_pdf_is_vector_and_not_an_image()
    {
        var pdf = Latin1(PdfExporter.Export([Build([new NodeDef { Id = "n1", Label = "开始" }])]));

        pdf.Should().NotContain("/Subtype /Image", "有一条位图对象就说明这条路退回了位图");
        pdf.Should().Contain("/FontFile", "字体没嵌进去，收件人机器上没有那份字体");
        pdf.Should().Contain("/Type0", "汉字要按 CID 型字体嵌，否则到了别处是方块");
        pdf.Should().Contain("Identity-H");
    }

    /// <summary>
    /// 文字是用文字操作符画出来的，不是转成路径的。
    /// </summary>
    /// <remarks>
    /// 转成路径的话，放大不糊这一条照样成立，但文字搜不到、选不中——
    /// 而"能选中"正是 PDF 相对位图那一档的意义之一。所以要把内容流解压出来，
    /// 看里面是不是真的有一对 <c>BT</c>…<c>ET</c>。
    /// </remarks>
    [Fact]
    [Trait("Category", "PdfExport")]
    public void The_text_is_written_with_text_operators()
    {
        var content = PlainStreams(PdfExporter.Export([Page("甲")]))
            .Single(stream => stream.Contains("BT", StringComparison.Ordinal));

        content.Should().Contain("ET", "一对文字块操作符才说明这段是当文字画的");
        content.Should().Contain("Tj");
    }

    /// <summary>
    /// 每个字都带一份"这个字形是哪个字"的映射表，所以文字不只是看得见，还能搜、能复制。
    /// </summary>
    /// <remarks>
    /// 只把字形画上去的话，看起来一模一样，但搜"甲"一个字也搜不到——而"能搜"是
    /// 这一档相对位图那一档的另一个意义。映射表按字给出码位，判据取汉字自己的码位。
    /// </remarks>
    [Fact]
    [Trait("Category", "PdfExport")]
    public void The_text_can_be_searched_and_copied()
    {
        var streams = PlainStreams(PdfExporter.Export([Page("甲")]));

        // "甲"是 U+7532。
        streams.Should().Contain(
            stream => stream.Contains("beginbfchar", StringComparison.Ordinal)
                && stream.Contains("<7532>", StringComparison.Ordinal),
            "少了这份映射表，文件里的文字搜不到、复制出来是乱码");
    }

    /// <summary>三种指令都画一遍而不抛异常：形状、折线、文本。</summary>
    [Fact]
    [Trait("Category", "PdfExport")]
    public void Every_command_kind_is_drawn()
    {
        var list = Edge(ArrowStyle.Arrow);

        list.Commands.Should().Contain(command => command is DrawShape);
        list.Commands.Should().Contain(command => command is DrawPolyline);
        list.Commands.Should().Contain(command => command is DrawText);

        PdfExporter.Export([list]).Pdf.Should().NotBeEmpty();
    }

    /// <summary>认不出的指令抛异常，不静默跳过。与 SVG、位图两个导出同一条口径。</summary>
    [Fact]
    [Trait("Category", "PdfExport")]
    public void An_unknown_command_throws()
    {
        var list = Build([new NodeDef { Id = "n1", Label = "开始" }]) with
        {
            Commands = [new FakeCommand("n1")],
        };

        var act = () => PdfExporter.Export([list]);

        act.Should().Throw<NotSupportedException>();
    }

    /// <summary>八种内置形状各画一遍，一种都不能漏。</summary>
    [Fact]
    [Trait("Category", "PdfExport")]
    public void Every_built_in_shape_is_drawn()
    {
        var nodes = Enum.GetValues<NodeShape>()
            .Select((shape, index) => new NodeDef { Id = $"n{index}", Label = "x", Shape = shape })
            .ToArray();

        PdfExporter.Export([Build(nodes)]).Pdf.Should().NotBeEmpty();
    }

    /// <summary>五种箭头各画一遍。</summary>
    [Theory]
    [Trait("Category", "PdfExport")]
    [InlineData(ArrowStyle.None)]
    [InlineData(ArrowStyle.Arrow)]
    [InlineData(ArrowStyle.OpenArrow)]
    [InlineData(ArrowStyle.Circle)]
    [InlineData(ArrowStyle.Cross)]
    public void Every_arrow_style_is_drawn(ArrowStyle arrow)
    {
        PdfExporter.Export([Edge(arrow)]).Pdf.Should().NotBeEmpty();
    }

    /// <summary>三种线型各画一遍。</summary>
    [Theory]
    [Trait("Category", "PdfExport")]
    [InlineData(LineStyle.Solid)]
    [InlineData(LineStyle.Dashed)]
    [InlineData(LineStyle.Dotted)]
    public void Every_line_style_is_drawn(LineStyle line)
    {
        PdfExporter.Export([Edge(ArrowStyle.None, line)]).Pdf.Should().NotBeEmpty();
    }

    /// <summary>底色真的写进了文件里：换一个底色，导出的字节就不一样。</summary>
    [Fact]
    [Trait("Category", "PdfExport")]
    public void The_background_reaches_the_file()
    {
        var white = Build([new NodeDef { Id = "n1", Label = "开始" }]);
        var blue = white with { Background = "#3366ff" };

        PdfExporter.Export([blue]).Pdf.Should().NotEqual(PdfExporter.Export([white]).Pdf);
    }

    #endregion

    #region 确定性

    /// <summary>
    /// 同一批绘制列表与同一组选项导出两次逐字节相同。
    /// </summary>
    /// <remarks>
    /// 带时间戳或随机标识的话，"导出的文件有没有变"这个问题就永远答不了，
    /// 而 PDF 也没法进版本控制。比字节而不是比页数：内容有一个像素的差别
    /// 也应当让这一条红。
    /// </remarks>
    [Fact]
    [Trait("Category", "PdfExport")]
    public void Two_exports_are_byte_identical()
    {
        DrawList[] pages =
        [
            Build([new NodeDef { Id = "n1", Label = "开始", Shape = NodeShape.Stadium }]),
            Build([new NodeDef { Id = "n2", Label = "校验", Shape = NodeShape.Diamond }]),
        ];

        PdfExporter.Export(pages).Pdf.Should().Equal(PdfExporter.Export(pages).Pdf);
    }

    /// <summary>
    /// 文件里没有时间戳。
    /// </summary>
    /// <remarks>
    /// 这是上一条能成立的原因，单拎出来守：底层写入器在调用方不给日期时**不写日期**，
    /// 而不是写当前时刻。哪天它改成了写当前时刻，上一条会红，而这一条会直接说出为什么。
    /// </remarks>
    [Fact]
    [Trait("Category", "PdfExport")]
    public void The_file_carries_no_timestamp()
    {
        var pdf = Latin1(PdfExporter.Export([Page("甲")]));

        pdf.Should().NotContain("/CreationDate");
        pdf.Should().NotContain("/ModDate");
    }

    #endregion

    #region 丢失清单

    /// <summary>矢量这一档的代价要如实报出来：字体整份嵌入，文件大。</summary>
    [Fact]
    [Trait("Category", "PdfExport")]
    public void The_loss_report_says_what_embedding_the_whole_font_costs()
    {
        var loss = PdfExporter.Export([Page("甲")]).Dropped.Should().ContainSingle().Subject;

        loss.Feature.Should().Contain("字体");
        loss.Reason.Should().Contain("矢量", "只说代价、不说换来了什么，用户没法判断值不值");
        loss.Reason.Should().NotBeEmpty("只说丢了什么、不说为什么，等于把问题原样丢回去");
    }

    #endregion

    #region 夹具

    private sealed record FakeCommand(string Id) : DrawCommand(Id)
    {
        public override string Describe() => "fake";
    }

    /// <summary>一页，上面一个节点。</summary>
    private static DrawList Page(string label) => Build([new NodeDef { Id = "n1", Label = label }]);

    private static DrawList Build(NodeDef[] nodes)
    {
        var theme = Theme.Default;
        var placed = new PlacedNode[nodes.Length];

        for (var index = 0; index < nodes.Length; index++)
        {
            var size = SceneBuilder.MeasureNode(nodes[index], theme, Measurer);

            placed[index] = new PlacedNode(nodes[index].Id, 40 + (index * 160), 40, size.Width, size.Height);
        }

        var document = DiagramDocument.CreateFromContent("pdf", nodes: nodes);

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
            "pdf",
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

    private static string Latin1(PdfExport export) => Encoding.Latin1.GetString(export.Pdf);

    /// <summary>
    /// 文件里每一页的纸张大小，单位是点。
    /// </summary>
    /// <remarks>
    /// 读的是每页对象上的 <c>/MediaBox</c>。它是未压缩的字典项，所以不必解压内容流，
    /// 而它是"这一页多大"这个问题在 PDF 里的唯一答案。
    /// </remarks>
    private static IReadOnlyList<(double Width, double Height)> PageSizes(PdfExport export) =>
    [
        .. Regex.Matches(Latin1(export), @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)\s*\]")
            .Select(match => (
                double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))),
    ];

    /// <summary>文件里有几页。数的是 <c>/Type /Page</c>，把 <c>/Type /Pages</c> 那棵目录树排除掉。</summary>
    private static int PageCount(PdfExport export) =>
        Regex.Matches(Latin1(export), @"/Type\s*/Page(?![s])").Count;

    /// <summary>
    /// 把解得出纯文本的那些流都取出来。
    /// </summary>
    /// <remarks>
    /// 底层写入器把内容流压成 zlib，所以直接搜字节搜到的可能是碰巧长得像的噪声。
    /// 这里扫出每一个 <c>stream</c> 段、试着解压，能解成纯文本的就留下。
    /// **留下的不止内容流**：字形到文字的映射表也是纯文本，而它是另一件事的证据，
    /// 所以这一份不替调用方分类，谁要什么谁自己挑。
    /// 嵌进来的字体也是流，但它解出来是二进制，过不了纯文本这一关。
    /// </remarks>
    private static IReadOnlyList<string> PlainStreams(PdfExport export)
    {
        var raw = Latin1(export);
        var streams = new List<string>();
        var at = 0;

        while (at < raw.Length)
        {
            var marker = raw.IndexOf("stream", at, StringComparison.Ordinal);

            if (marker < 0)
            {
                break;
            }

            var from = marker + "stream".Length;

            if (from < raw.Length && raw[from] == '\r')
            {
                from++;
            }

            if (from < raw.Length && raw[from] == '\n')
            {
                from++;
            }

            var to = raw.IndexOf("endstream", from, StringComparison.Ordinal);

            if (to < 0)
            {
                break;
            }

            var body = Encoding.Latin1.GetBytes(raw[from..to]);

            if (Inflate(body) is { } decoded && IsText(decoded))
            {
                streams.Add(decoded);
            }

            at = to + "endstream".Length;
        }

        return streams;
    }

    private static string? Inflate(byte[] data)
    {
        try
        {
            using var input = new MemoryStream(data);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            zlib.CopyTo(output);

            return Encoding.Latin1.GetString(output.ToArray());
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static bool IsText(string value) =>
        value.Length > 0 && value.All(c => c is >= ' ' and <= '~' || c is '\r' or '\n' or '\t');

    #endregion
}
