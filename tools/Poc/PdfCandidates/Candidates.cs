using System.Text;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;

namespace DuetDiagram.Poc.PdfCandidates;

/// <summary>
/// 四个 PDF 写入候选，各出同一份内容。
/// </summary>
/// <remarks>
/// <para>
/// **四个候选画的是同一张图**：一个矩形、一段西文、一段中文。判据要靠对比才说明问题，
/// 各画各的就没法比。中文那一行是关键——本仓的用户与内容都以中文为主，
/// 一个画不出汉字的候选不管别的多好都不能用。
/// </para>
/// <para>
/// **每个候选返回的就是字节，不在这里判断好坏。** 判断交给 <see cref="Facts"/>，
/// 它把文件自己的属性读出来；结论写在报告里。混在一起的话，
/// 换一条判据就要改四个候选。
/// </para>
/// </remarks>
internal static class Candidates
{
    private const string Latin = "Hello DuetDiagram";

    private const string Chinese = "中文标签：开始 → 校验";

    public static IReadOnlyList<string> Names { get; } = ["skia", "quest", "pdfsharp", "bitmap"];

    /// <summary>候选名对应的许可与它的条件。这一条不看代码，看的是许可证正文。</summary>
    public static string License(string name) => name switch
    {
        "skia" => "MIT（SkiaSharp 本体；Skia 是 BSD 三条款）",
        "quest" => "双许可：社区版免费，但仅限年营收不足一百万美元的组织等六类情形；本仓没有 LICENSE，够不上「开源项目」那一类",
        "pdfsharp" => "MIT（但它依赖 SixLabors.ImageSharp 与 SixLabors.Fonts，随它分发时按 Apache-2.0）",
        _ => "MIT（SkiaSharp 本体）",
    };

    /// <summary>画一张图。候选自己搞不定时返回空，把话说在 <paramref name="failure"/> 里。</summary>
    public static byte[]? Render(string name, out string? failure)
    {
        failure = null;

        try
        {
            return name switch
            {
                "skia" => Skia(),
                "quest" => Quest(out failure),
                "pdfsharp" => PdfSharp(out failure),
                "bitmap" => Bitmap(),
                _ => throw new ArgumentException($"认不出的候选：{name}", nameof(name)),
            };
        }
        catch (Exception exception)
        {
            failure = $"{exception.GetType().Name}: {exception.Message}";

            return null;
        }
    }

    /// <summary>候选一：SkiaSharp 自带的 PDF 写入器。仓库里已经有 SkiaSharp，它不引入新依赖。</summary>
    private static byte[] Skia()
    {
        using var stream = new MemoryStream();

        // 不给日期。给了反而要挑一个值，而"挑哪个值"是调用方的事，不是写入器的事；
        // 而它默认不写日期，于是同一份内容两次导出逐字节相同。
        using var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata(72));

        var canvas = document.BeginPage(595, 842);

        Shape(canvas);

        using var latin = Font("Segoe UI");
        using var cjk = Font("Microsoft YaHei UI");

        canvas.DrawText(Latin, 48, 180, SKTextAlign.Left, latin, Black());
        canvas.DrawText(Chinese, 48, 220, SKTextAlign.Left, cjk, Black());

        document.EndPage();
        document.Close();

        return stream.ToArray();
    }

    /// <summary>
    /// 候选二：QuestPDF。
    /// </summary>
    /// <remarks>
    /// 它自己有一套"文档由什么组成"的模型（页、段落、表格），而不是"往一块画布上画"。
    /// 唯一的自由绘制入口是 <c>Canvas</c>，而那个入口在 2024.3.0 就标了废弃。
    /// </remarks>
    private static byte[] Quest(out string? failure)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        using var stream = new MemoryStream();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);

                // 这一条刻意不屏蔽那条"已废弃"的警告：它本身就是证据之一。
                // 建的时候会在这一行报一条 CS0618，而运行时会在这里抛异常。
                page.Content().Canvas((_, _) => { });
            });
        }).GeneratePdf(stream);

        failure = null;

        return stream.ToArray();
    }

    /// <summary>
    /// 候选三：PdfSharpCore。
    /// </summary>
    /// <remarks>
    /// 它没有字体解析器，得自己写一个；而且只认单个字体文件，
    /// 字体集合（本机的中文字体正是集合）它读不了。
    /// </remarks>
    private static byte[] PdfSharp(out string? failure)
    {
        GlobalFontSettings.FontResolver = new Resolver();

        using var document = new PdfDocument();
        var page = document.AddPage();

        page.Width = XUnit.FromPoint(595);
        page.Height = XUnit.FromPoint(842);

        using var graphics = XGraphics.FromPdfPage(page);

        graphics.DrawRectangle(XBrushes.LightBlue, new XRect(40, 40, 220, 80));
        graphics.DrawString(Latin, new XFont("Segoe UI", 18), XBrushes.Black, new XPoint(48, 180));

        failure = null;

        try
        {
            graphics.DrawString(Chinese, new XFont("Microsoft YaHei UI", 18), XBrushes.Black, new XPoint(48, 220));
        }
        catch (Exception exception)
        {
            failure = $"中文那一行画不出来：{exception.GetType().Name}: {exception.Message}";
        }

        using var stream = new MemoryStream();

        document.Save(stream, false);

        return stream.ToArray();
    }

    /// <summary>
    /// 候选四：位图嵌入。把整张图当一张位图塞进 PDF。
    /// </summary>
    /// <remarks>
    /// 它什么都过得了——没有新依赖、许可干净、字不字体根本无所谓——但它与 PNG 导出重复了，
    /// 而 PDF 相对 PNG 的意义正在于矢量。列在这里是为了让"为什么不选它"有个可跑的对照。
    /// </remarks>
    private static byte[] Bitmap()
    {
        using var surface = SKSurface.Create(new SKImageInfo(595, 842));

        surface.Canvas.Clear(SKColors.White);
        Shape(surface.Canvas);

        using var latin = Font("Segoe UI");
        using var cjk = Font("Microsoft YaHei UI");

        surface.Canvas.DrawText(Latin, 48, 180, SKTextAlign.Left, latin, Black());
        surface.Canvas.DrawText(Chinese, 48, 220, SKTextAlign.Left, cjk, Black());

        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();

        using (var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata(72)))
        {
            var canvas = document.BeginPage(595, 842);

            using var bitmap = SKBitmap.Decode(png!.ToArray());

            canvas.DrawBitmap(bitmap, new SKRect(0, 0, 595, 842), new SKSamplingOptions());

            document.EndPage();
        }

        return stream.ToArray();
    }

    private static void Shape(SKCanvas canvas)
    {
        using var fill = new SKPaint { Color = new SKColor(0x33, 0x66, 0xff), IsAntialias = true };
        using var pen = new SKPaint { Style = SKPaintStyle.Stroke, Color = SKColors.Black, StrokeWidth = 2 };

        canvas.DrawRect(new SKRect(40, 40, 260, 120), fill);
        canvas.DrawRect(new SKRect(40, 40, 260, 120), pen);
    }

    private static SKPaint Black() => new() { Color = SKColors.Black, IsAntialias = true };

    private static SKFont Font(string family) =>
        new(SKTypeface.FromFamilyName(family) ?? SKTypeface.Default, 18) { Hinting = SKFontHinting.None };

    /// <summary>PdfSharpCore 不带字体解析器，必须自己给一个。</summary>
    private sealed class Resolver : IFontResolver
    {
        public string DefaultFontName => "Segoe UI";

        public byte[]? GetFont(string faceName) => Read(faceName switch
        {
            "Microsoft YaHei UI" => @"C:\Windows\Fonts\simhei.ttf",
            _ => @"C:\Windows\Fonts\segoeui.ttf",
        });

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) => new(familyName);

        private static byte[]? Read(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}
