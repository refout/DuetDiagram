using DuetDiagram.Core.Model;
using DuetDiagram.Core.Sidecar;
using DuetDiagram.Dsl.Export;
using DuetDiagram.Render;

namespace DuetDiagram.App.Services;

/// <summary>
/// 一次界面导出的结果。
/// </summary>
/// <remarks>
/// **它是给界面看的，不是给调用方看的。** 工具那一条路的产物是文本或字节，
/// 由调用方自己处置；界面这一条路的产物已经写进文件了，要说的只是"写没写成、丢了什么"。
/// </remarks>
/// <param name="File">写到哪个文件名。不含路径。</param>
/// <param name="Headline">一句话说清这次导出了什么。</param>
/// <param name="Notes">要逐条交代的那些话。可以是空的。</param>
public sealed record ExportOutcome(string File, string Headline, IReadOnlyList<string> Notes);

/// <summary>
/// 把当前这一份绘制列表写成文件。
/// </summary>
/// <remarks>
/// <para>
/// **它不碰会话，只收现成的东西。** 绘制列表、文档、固定位置都由调用方给，
/// 于是这一段不依赖窗口就能单独走一遍。反过来让服务自己去读会话的话，
/// 这一层与窗口那一层就会各判一次"要导哪一页"，而两处的判据迟早会不一样。
/// </para>
/// <para>
/// **写盘先写临时文件再替换。** 与文档那两条写路径同一套：直接往目标文件上写的话，
/// 写到一半断电留下的是一份截断的内容，而用户手上没有第二份可以退回去。
/// </para>
/// <para>
/// **界面这一条路只出当前页。** 拿到的就是画布上这一份绘制列表；
/// 工具那一条路的 PDF 会按文档声明的页序一页一张纸。同一套词汇、不同的页范围。
/// </para>
/// </remarks>
internal static class ExportService
{
    /// <summary>写临时文件时用的后缀。它与目标文件同目录，替换才是同一卷内的改名。</summary>
    private const string TempSuffix = ".tmp";

    public static ExportOutcome Write(
        string path,
        ExportRequest request,
        DrawList drawList,
        DiagramDocument document,
        IReadOnlyDictionary<string, Anchor> pins)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(drawList);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pins);

        var file = Path.GetFileName(path);

        return request.Format switch
        {
            ExportFormats.Svg => WriteText(path, file, "SVG", Svg(drawList)),

            ExportFormats.Png => WriteBytes(path, file, "PNG", Png(drawList, document, request)),

            ExportFormats.Pdf => WriteBytes(path, file, "PDF", Pdf(drawList, document, request)),

            ExportFormats.Dsl => WriteDsl(path, file, document, pins),

            _ => throw new ArgumentException($"{request.Format} 不是认得的导出格式", nameof(request)),
        };
    }

    #region 四种格式

    private static SvgExport Svg(DrawList drawList) => SvgExporter.Export(drawList);

    /// <summary>
    /// 导位图。
    /// </summary>
    /// <remarks>
    /// 底一律不透明。缺省透明的话，导出的 PNG 贴进白底文档里会变成黑底，
    /// 而那是"导出看起来坏了"里最常见的一种。纸张尺寸从文档的画布设置里读——
    /// 导出器只认绘制列表，它读不到文档。
    /// </remarks>
    private static BitmapExport Png(DrawList drawList, DiagramDocument document, ExportRequest request) =>
        BitmapExporter.Export(drawList, new BitmapOptions
        {
            Scale = request.Scale,
            Crop = request.Range == ExportRanges.Page ? BitmapCrop.Page : BitmapCrop.Content,
            PageSize = document.Canvas.PageSize,
        });

    /// <summary>导一份只有当前页的 PDF。倍数那一项不存在——PDF 的单位是物理长度。</summary>
    private static PdfExport Pdf(DrawList drawList, DiagramDocument document, ExportRequest request) =>
        PdfExporter.Export([drawList], new PdfOptions
        {
            Crop = request.Range == ExportRanges.Page ? PdfCrop.Page : PdfCrop.Content,
            PageSize = document.Canvas.PageSize,
        });

    /// <summary>
    /// 导 DSL 文本。
    /// </summary>
    /// <remarks>
    /// **界面这一层拿得到固定位置，工具那一层拿不到。** 前者手里有会话，
    /// 后者只拿到标识、拿不到坐标，所以工具那条路的导出会把"固定位置写不出来"
    /// 记进丢失清单。这里能写出来，就不该报成丢了。
    /// </remarks>
    private static ExportOutcome WriteDsl(
        string path,
        string file,
        DiagramDocument document,
        IReadOnlyDictionary<string, Anchor> pins)
    {
        var result = DslExporter.Export(document, new UserSidecar
        {
            DocumentId = document.Id,
            PinnedNodes = pins,
        });

        WriteTextAtomic(path, result.Text);

        return new ExportOutcome(file, $"已导出 {file} 的 DSL 文本", NotesOf(result.Report.Dropped));
    }

    #endregion

    #region 落盘

    private static ExportOutcome WriteText(string path, string file, string label, SvgExport export)
    {
        WriteTextAtomic(path, export.Svg);

        return new ExportOutcome(file, $"已导出 {label}", NotesOf(export.Dropped));
    }

    private static ExportOutcome WriteBytes(string path, string file, string label, BitmapExport export)
    {
        WriteBytesAtomic(path, export.Png);

        return new ExportOutcome(file, $"已导出 {label}（{export.Width}×{export.Height}）", NotesOf(export.Dropped));
    }

    private static ExportOutcome WriteBytes(string path, string file, string label, PdfExport export)
    {
        WriteBytesAtomic(path, export.Pdf);

        return new ExportOutcome(file, $"已导出 {label}（{export.Pages} 页）", NotesOf(export.Dropped));
    }

    private static void WriteTextAtomic(string path, string content) =>
        WriteBytesAtomic(path, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content));

    /// <summary>
    /// 先写临时文件再改名。
    /// </summary>
    /// <remarks>
    /// 覆盖式改名，而不是"先删目标再改名"：后者在删掉之后、改名之前的那一瞬间，
    /// 磁盘上两份都不在，而那一刻正好断电就是彻底的丢失。
    /// </remarks>
    private static void WriteBytesAtomic(string path, byte[] content)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + TempSuffix;

        File.WriteAllBytes(temporary, content);
        File.Move(temporary, path, overwrite: true);
    }

    #endregion

    /// <summary>把丢失清单说成人话。</summary>
    /// <remarks>
    /// 同一类丢失往往涉及几十个元素，逐个列出来之后报告会长到没人看。
    /// 所以按类收拢成一句，涉及几个元素说个数。
    /// </remarks>
    private static IReadOnlyList<string> NotesOf(IReadOnlyList<DroppedFeature> dropped) =>
        [.. dropped.Select(feature => feature.Ids.Count == 0
            ? $"{feature.Feature}：{feature.Reason}"
            : $"{feature.Feature}（{feature.Ids.Count} 处）：{feature.Reason}")];
}
