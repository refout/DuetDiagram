using System.Text.Json;
using DuetDiagram.Core.Model;
using DuetDiagram.Dsl.Export;

namespace DuetDiagram.Llm.Tools;

/// <summary>
/// <c>diagram_export</c> 收到的参数。
/// </summary>
/// <param name="Format">导出格式。</param>
/// <param name="PageId">要导出的页面。</param>
internal sealed record ExportArguments(string Format, string? PageId = null);

/// <summary>
/// 一次导出的产物。
/// </summary>
/// <remarks>
/// <para>
/// **文本与字节分两个字段，按格式二选一。** 文本格式给 <see cref="Text"/>，
/// 位图格式给 <see cref="Base64"/>。合成一个字段的话，调用方拿到一串字符
/// 无从分辨"这是图的文本"还是"这是图本身编了码"，而这两种的处置完全不同。
/// </para>
/// <para>
/// **位图只能编码成 base64。** 工具结果的载荷是 JSON，装不下裸字节；
/// 而这条通路上的调用方本来就要把它落盘或转发，编码是绕不开的一步。
/// </para>
/// </remarks>
/// <param name="Format">实际导出的格式。</param>
/// <param name="Text">导出的文本。文本格式给这一项，同一份文档两次导出逐字节相同。</param>
/// <param name="Dropped">这次导出丢了什么。空清单不等于无损，只等于没有东西落进已知的丢失清单。</param>
/// <param name="Base64">导出的位图，base64 编码。位图格式给这一项。</param>
internal sealed record ExportPayload(
    string Format,
    string? Text,
    IReadOnlyList<DroppedFeature> Dropped,
    string? Base64 = null);

/// <summary>
/// 把当前图导出成文本。
/// </summary>
/// <remarks>
/// <para>
/// 导出是只读的：它不经过命令总线，所以版本号不动、历史栈不进、广播不发。
/// 这也正是它不进动作表的原因——动作表里每一条都发一条命令。
/// </para>
/// <para>
/// **丢失清单要原样带给调用方。** 静默丢失会让模型以为导出的文本就是全部内容，
/// 而 IR 的表达力严格强于任何一种目标格式。
/// </para>
/// </remarks>
internal static class ExportTool
{
    private const string Formats = "dsl、svg、png、pdf";

    public static ToolResult Run(DiagramToolContext context, ExportArguments args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (string.IsNullOrWhiteSpace(args.Format))
        {
            return ActionDispatch.Missing("要给出导出格式", "format");
        }

        // 点名的页面不在文档里要如实说，理由与按页读那一条相同。
        if (ActionDispatch.PageMissing(context, args.PageId) is { } missing)
        {
            return missing;
        }

        return args.Format switch
        {
            "dsl" => Dsl(context, args.PageId),

            "svg" => Svg(context, args.PageId),

            "png" => Png(context, args.PageId),

            "pdf" => Pdf(context, args.PageId),

            _ => ActionDispatch.Reject(
                $"{args.Format} 不是它认得的导出格式",
                "format",
                $"可用格式：{Formats}"),
        };
    }

    /// <summary>
    /// 导出 DSL。
    /// </summary>
    /// <remarks>
    /// <para>
    /// DSL 导出器只吃文档、只依赖 Core，所以直接调，不绕注入那一道。
    /// 按页导出也是先拿一份投影再导——DSL 描述的是图，
    /// 不认识"页"这个概念，而"这一页上有谁"那套口径在 Core 里只有一份。
    /// </para>
    /// <para>
    /// **固定位置写不出来，这一层要说出来。** 绝对坐标在人工产物的 sidecar 里，
    /// 而这一层只拿到固定的节点标识、拿不到坐标。少了它们，用户拖过的位置
    /// 会回到自动结果——那是要付的代价，但必须说清付了什么。
    /// </para>
    /// </remarks>
    private static ToolResult Dsl(DiagramToolContext context, string? pageId)
    {
        var document = PageMembership.Project(context.Document, pageId);
        var result = DslExporter.Export(document);

        var dropped = result.Report.Dropped;

        if (context.PinnedNodes.Count > 0)
        {
            dropped =
            [
                .. dropped,
                new DroppedFeature(
                    "固定位置",
                    [.. context.PinnedNodes],
                    "固定坐标在人工产物里，这一层只拿到标识、拿不到坐标，pin 写不出来。"),
            ];
        }

        var payload = new ExportPayload("dsl", result.Text, dropped);

        var message = dropped.Count == 0
            ? pageId is null ? "已导出 DSL 文本" : $"已导出 {pageId} 这一页的 DSL 文本"
            : $"已导出 DSL 文本，有 {dropped.Count} 类内容写不进去，见 dropped";

        return ToolResult.Ok(
            JsonSerializer.SerializeToElement(payload, ToolJsonContext.Default.ExportPayload),
            message);
    }

    /// <summary>
    /// 导出 SVG。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **渲染由宿主做，这一层只转发。** 要出 SVG 得先把文档排成布局、再按字体量出
    /// 标签尺寸，而那两步都在渲染层里，工具层不引它——引了的话，这一层连同它的每个宿主
    /// 都要带上原生绘图库。所以渲染器是宿主喂进来的。
    /// </para>
    /// <para>
    /// 按页过滤不在这里先做一遍：它同时被布局与绘制列表构建用到，
    /// 而那一套口径在 Core 里只有一份。这里只把页面标识原样交给渲染器。
    /// </para>
    /// </remarks>
    private static ToolResult Svg(DiagramToolContext context, string? pageId)
    {
        if (context.SvgExporter is null)
        {
            return HostMissing("SVG");
        }

        if (context.SvgExporter(context.Document, pageId) is not { } result)
        {
            return RenderFailed("SVG");
        }

        var payload = new ExportPayload("svg", result.Svg, result.Dropped);

        var message = result.Dropped.Count == 0
            ? pageId is null ? "已导出 SVG" : $"已导出 {pageId} 这一页的 SVG"
            : $"已导出 SVG，有 {result.Dropped.Count} 类内容没按原样写出，见 dropped";

        return ToolResult.Ok(
            JsonSerializer.SerializeToElement(payload, ToolJsonContext.Default.ExportPayload),
            message);
    }

    /// <summary>
    /// 导出 PNG。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与 SVG 那一条同一口径：渲染由宿主做，这一层只转发。工具层不引渲染层，
    /// 引了的话它连同它的每个宿主都要带上原生绘图库。
    /// </para>
    /// <para>
    /// **位图按 base64 走载荷。** 工具结果是要交给模型与代理的 JSON，装不下裸字节；
    /// 而调用方本来就要把它落盘或转发，编码是绕不开的一步。放进一个叫 <c>base64</c>
    /// 的字段而不是塞进 <c>text</c>：调用方拿到 <c>text</c> 会以为那是图的文本。
    /// </para>
    /// </remarks>
    private static ToolResult Png(DiagramToolContext context, string? pageId)
    {
        if (context.BitmapExporter is null)
        {
            return HostMissing("PNG");
        }

        if (context.BitmapExporter(context.Document, pageId) is not { } result)
        {
            return RenderFailed("PNG");
        }

        var payload = new ExportPayload("png", null, result.Dropped, Convert.ToBase64String(result.Png));

        var message = result.Dropped.Count == 0
            ? pageId is null ? $"已导出 PNG（{result.Width}×{result.Height}）" : $"已导出 {pageId} 这一页的 PNG（{result.Width}×{result.Height}）"
            : $"已导出 PNG（{result.Width}×{result.Height}），有 {result.Dropped.Count} 类内容没按原样写出，见 dropped";

        return ToolResult.Ok(
            JsonSerializer.SerializeToElement(payload, ToolJsonContext.Default.ExportPayload),
            message);
    }

    /// <summary>
    /// 导出 PDF。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与前两条同一口径：渲染由宿主做，这一层只转发。
    /// </para>
    /// <para>
    /// **它与前两条有一处不同：页面标识为空时，出来的可能不止一页。** 一份文档有哪几页
    /// 由文档自己说了算，而"这一页上有谁"那套口径在 Core 里只有一份、在宿主那一侧。
    /// 这一层只把标识原样转过去，然后把页数报出来。
    /// </para>
    /// <para>
    /// **PDF 也按 base64 走载荷。** 它同样是二进制，理由与 PNG 那一条完全相同；
    /// 放进 <c>base64</c> 而不是 <c>text</c>，是因为调用方拿到 <c>text</c> 会以为那是图的文本。
    /// </para>
    /// </remarks>
    private static ToolResult Pdf(DiagramToolContext context, string? pageId)
    {
        if (context.PdfExporter is null)
        {
            return HostMissing("PDF");
        }

        if (context.PdfExporter(context.Document, pageId) is not { } result)
        {
            return RenderFailed("PDF");
        }

        var payload = new ExportPayload("pdf", null, result.Dropped, Convert.ToBase64String(result.Pdf));
        var pages = $"{result.Pages} 页";

        var message = result.Dropped.Count == 0
            ? pageId is null ? $"已导出 PDF（{pages}）" : $"已导出 {pageId} 这一页的 PDF（{pages}）"
            : $"已导出 PDF（{pages}），有 {result.Dropped.Count} 类内容没按原样写出，见 dropped";

        return ToolResult.Ok(
            JsonSerializer.SerializeToElement(payload, ToolJsonContext.Default.ExportPayload),
            message);
    }

    /// <summary>这个宿主没接上渲染层。失败的原因归宿主，不归调用方的参数。</summary>
    private static ToolResult HostMissing(string format) => ToolResult.Fail(ToolError.Of(
        ToolErrorCodes.NotSupported,
        $"这个宿主没有接上渲染层，导出不了 {format}",
        "format",
        "这个宿主现在能用的格式：dsl"));

    /// <summary>
    /// 渲染层拿到了文档却排不出结果。
    /// </summary>
    /// <remarks>
    /// 失败的原因归宿主：这一层看不见布局引擎的异常类型，也就无从分辨
    /// "排不出来"与"程序坏了"。所以它只说排不出来，并指向校验那一条。
    /// </remarks>
    private static ToolResult RenderFailed(string format) => ToolResult.Fail(ToolError.Of(
        ToolErrorCodes.NotSupported,
        $"渲染层拿到了这份文档却排不出结果，导不了 {format}",
        "format",
        "先让这份文档能排出来——布局问题可以用 diagram_validate 查"));
}
