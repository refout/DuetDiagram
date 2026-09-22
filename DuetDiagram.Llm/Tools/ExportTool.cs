using System.Text.Json;
using DuetDiagram.Mermaid.Export;

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
/// <param name="Format">实际导出的格式。</param>
/// <param name="Text">导出的文本。同一份文档两次导出逐字节相同。</param>
/// <param name="Dropped">这次导出丢了什么。空清单不等于无损，只等于没有东西落进已知的丢失清单。</param>
internal sealed record ExportPayload(string Format, string Text, IReadOnlyList<DroppedFeature> Dropped);

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
    private const string Formats = "mermaid、dsl、svg、png、pdf";

    public static ToolResult Run(DiagramToolContext context, ExportArguments args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (string.IsNullOrWhiteSpace(args.Format))
        {
            return ActionDispatch.Missing("要给出导出格式", "format");
        }

        // 页面还没有消费方，整份文档就是当前页。认下这个参数而按整份文档回，
        // 会让调用方以为它导出的是某一页——一个静默的错误答案比一句「还没接上」糟得多。
        if (args.PageId is not null)
        {
            return ToolResult.Fail(ToolError.Of(
                ToolErrorCodes.NotSupported,
                "按页导出还没接上：页面现在还没有消费方，导出取的是整份文档",
                "pageId",
                "不带 pageId 可以导出整份文档"));
        }

        return args.Format switch
        {
            "mermaid" => Mermaid(context),

            // 这一条不是「还没排到」，是**不该现在做**：DSL 的导出方向还没有实现，
            // 而 DSL 去留那个决策门还开着——判掉之后写出来的导出器要整个删掉。
            "dsl" => NotYet("dsl", "DSL 的导出方向还没实现，而 DSL 去留还没有定论"),

            "svg" or "png" or "pdf" => NotYet(args.Format, "位图与 PDF 要依赖渲染层或排版库，还没有排到"),

            _ => ActionDispatch.Reject(
                $"{args.Format} 不是它认得的导出格式",
                "format",
                $"可用格式：{Formats}"),
        };
    }

    private static ToolResult Mermaid(DiagramToolContext context)
    {
        var result = MermaidExporter.Export(context.Document, new ExportOptions());

        var payload = new ExportPayload("mermaid", result.Text, result.Report.Dropped);

        var message = result.Report.Dropped.Count == 0
            ? "已导出 Mermaid 文本"
            : $"已导出 Mermaid 文本，有 {result.Report.Dropped.Count} 类内容写不进去，见 dropped";

        return ToolResult.Ok(
            JsonSerializer.SerializeToElement(payload, ToolJsonContext.Default.ExportPayload),
            message);
    }

    private static ToolResult NotYet(string format, string missing) => ToolResult.Fail(ToolError.Of(
        ToolErrorCodes.NotSupported,
        $"导出成 {format} 还没接上：{missing}",
        "format",
        $"现在能用的格式：mermaid"));
}
