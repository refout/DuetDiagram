using DuetDiagram.Core.Model;
using SkiaSharp;

namespace DuetDiagram.Render;

/// <summary>
/// 把绘制列表写成 PDF。
/// </summary>
/// <remarks>
/// <para>
/// **它只消费绘制列表，不重新遍历文档**，与 SVG、PNG 两个导出同一条口径。
/// 重新遍历的话，导出与画布成了两条绘制路径，而两条迟早会对不上。
/// </para>
/// <para>
/// **画法不是这里定的，是 <see cref="CanvasPainter"/> 定的。** 这一份只做三件事：
/// 开一份 PDF 文档、按范围给每一页定纸张、把画布交给它。所以"PDF 与 PNG 画出来不一样"
/// 这种偏差在结构上就不可能发生——两边调用的是同一段代码。
/// </para>
/// <para>
/// **它是矢量的，这是它相对位图那一档的全部意义。** 线条是路径、文字是真文字，
/// 放大不糊、能选中、能搜索。代价写在丢失清单里：字体要整份嵌进文件，
/// 含中文的文档通常十几兆。
/// </para>
/// <para>
/// **它是无头的。** 写 PDF 直接写进一块内存流，不经过任何窗口平台。
/// 命令行、服务端与模型那条通路都没有窗口平台。
/// </para>
/// <para>
/// **确定性。** 同一批绘制列表与同一组选项导出两次逐字节相同。这一条不是白来的：
/// 底层写文件头的那个库在调用方不显式给日期时**不写日期**，而不是写当前时刻——
/// 换成写当前时刻的话，同一份文档两次导出就不可能相同。所以这里也不给它日期。
/// </para>
/// <para>
/// **它认页。** 一份文档的每一页各占一页，而不是挤在同一张纸上。这是 PDF 与 SVG、PNG
/// 唯一一处结构上的差别：后两者的"页"要靠投影掉，而 PDF 自己就有页的概念。
/// </para>
/// <para>
/// **纸张会被取整到整点。** 底层写入器写纸张尺寸时丢掉小数，1123×794 的页面折成
/// 842.25×595.5，落到文件里是 842×595。差不到一个点，打印出来看不出来；
/// 但要是哪天有人要一个精确到小数点的纸张，先得知道这一层办不到。
/// </para>
/// </remarks>
public static class PdfExporter
{
    /// <summary>
    /// 一个文档单位折成多少点。
    /// </summary>
    /// <remarks>
    /// 文档单位是 96 像素每英寸的那一像素，而 PDF 的点是 72 每英寸。96 分之 72 是四分之三，
    /// 所以这个系数是单位定义推出来的，不是选出来的。
    /// </remarks>
    public const double PointsPerUnit = 0.75;

    /// <summary>
    /// 导出。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 产物用的是 Core 那份记录，不是这里另立一个：工具层要把它原样交给调用方，
    /// 而工具层看不见渲染层。
    /// </para>
    /// <para>
    /// **按内容外接框定范围时，各页取同一个尺寸**，取的是各页里最大的那个宽与最大的那个高。
    /// 各页各按自己的内容定的话，一份三页的文件会有三种纸张大小，翻页时页面跳来跳去。
    /// </para>
    /// </remarks>
    /// <param name="pages">每一页要画的东西，按页序。至少一页。</param>
    /// <param name="options">选择。为空时用默认。</param>
    /// <exception cref="ArgumentException">一页都没给，或者按页面裁却没给页面尺寸。</exception>
    public static PdfExport Export(IReadOnlyList<DrawList> pages, PdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pages);

        if (pages.Count == 0)
        {
            throw new ArgumentException("至少要有一页。", nameof(pages));
        }

        var settings = options ?? new PdfOptions();
        var (frameWidth, frameHeight) = Frame(pages, settings);
        var pageWidth = (float)(frameWidth * PointsPerUnit);
        var pageHeight = (float)(frameHeight * PointsPerUnit);
        using var stream = new MemoryStream();

        // 元数据一个字都不给：给了标题或作者的话，同一份文档两次导出会因为调用方传了什么
        // 而不同，而那不是导出该管的事。日期更不给——理由见类型上的说明。
        var metadata = new SKDocumentPdfMetadata(RasterDpi);

        using (var document = SKDocument.CreatePdf(stream, metadata)
            ?? throw new InvalidOperationException("开不出一份 PDF。"))
        {
            foreach (var list in pages)
            {
                var canvas = document.BeginPage(pageWidth, pageHeight);

                // 先缩放再画：绘制列表里的坐标是文档单位，而纸上要的是点。
                canvas.Scale((float)PointsPerUnit);
                CanvasPainter.Background(canvas, list.Background, transparent: false);
                CanvasPainter.Commands(canvas, list.Commands);

                document.EndPage();
            }
        }

        return new PdfExport(stream.ToArray(), pages.Count, Losses(pages, settings, frameWidth, frameHeight));
    }

    /// <summary>
    /// 嵌进来的位图按多少像素每英寸算。
    /// </summary>
    /// <remarks>
    /// 这一份导出全是矢量，没有一个位图，所以这个值目前不起作用。
    /// 取 72 而不是 96：它只在将来真的嵌了位图时才算数，而那时"按纸张的点来算"比
    /// "按屏幕的像素来算"更不容易让人意外。
    /// </remarks>
    private const float RasterDpi = 72;

    #region 范围

    /// <summary>纸张多大，单位是文档单位。</summary>
    private static (double Width, double Height) Frame(IReadOnlyList<DrawList> pages, PdfOptions settings) =>
        settings.Crop switch
        {
            PdfCrop.Page => Page(settings),
            _ => (pages.Max(page => page.Width), pages.Max(page => page.Height)),
        };

    private static (double Width, double Height) Page(PdfOptions settings) =>
        settings.PageSize.Width > 0 && settings.PageSize.Height > 0
            ? (settings.PageSize.Width, settings.PageSize.Height)
            : throw new ArgumentException("按页面裁要给出页面尺寸。", nameof(settings));

    /// <summary>
    /// 这份导出丢了什么。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 第一条是"矢量"这条路的代价，也是它与位图那一档的取舍：换来的是可缩放、可选中、
    /// 可搜索，付出的是文件大。**这一条必须如实报出来**——不报的话，用户拿到一份十几兆的
    /// 文件会以为是自己哪里写错了。
    /// </para>
    /// <para>
    /// 第二条只在按页面裁、而且内容比页面大时出现，与位图那一档同一条口径。
    /// </para>
    /// </remarks>
    private static DroppedFeature[] Losses(
        IReadOnlyList<DrawList> pages,
        PdfOptions settings,
        double width,
        double height)
    {
        var losses = new List<DroppedFeature>
        {
            new(
                "字体的整份嵌入",
                [],
                "字体是整份嵌进去的，不是只嵌用到的那些字：含中文的文档通常十几兆。"
                + "换来的是收件人不必装那份字体，而且线条与文字都是矢量的——放大不糊、能选中、能搜索。"
                + "要小文件就走 PNG 那一档，但那样文字搜不到了。"),
        };

        if (settings.Crop == PdfCrop.Page)
        {
            var widest = pages.Max(page => page.Width);
            var tallest = pages.Max(page => page.Height);

            if (widest > width || tallest > height)
            {
                losses.Add(new(
                    "超出页面的内容",
                    [],
                    $"按页面尺寸裁掉了超出纸张的部分：内容最大 {Numbers.Format(widest)}×{Numbers.Format(tallest)}，"
                    + $"页面 {Numbers.Format(width)}×{Numbers.Format(height)}。要完整内容就按内容外接框那一档导。"));
            }
        }

        return [.. losses];
    }

    #endregion
}
