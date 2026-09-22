using System.Text.Json;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Llm.Tools;

/// <summary>
/// <c>diagram_validate</c> 收到的参数。
/// </summary>
/// <param name="Scope">校验范围。留空表示整份文档。</param>
internal sealed record ValidateArguments(string? Scope = null);

/// <summary>
/// 一次校验的产物。
/// </summary>
/// <param name="IssueCount">问题条数。为零表示这份文档通过了整体校验。</param>
/// <param name="Issues">逐条问题，带错误码、相关标识与修复建议。</param>
internal sealed record ValidatePayload(int IssueCount, IReadOnlyList<ValidationIssue> Issues);

/// <summary>
/// 整体校验当前文档。
/// </summary>
/// <remarks>
/// <para>
/// 它只报告不修改，修是调用方的事。走的是整体校验器那条纯函数路径，
/// 不经过命令总线——所以版本号不动、历史栈不进。
/// </para>
/// <para>
/// **修复建议照抄校验器给出的那一份，这里不另写一张按错误码查的表。**
/// 校验器逐条填了 <see cref="ValidationIssue.Suggestion"/>，那就是「映射表放在一处」里的那一处；
/// 这里再写一张的话，同一个码会在两个入口给出两种建议，而调用方按哪一条都可能是错的。
/// </para>
/// </remarks>
internal static class ValidateTool
{
    public static ToolResult Run(DiagramToolContext context, ValidateArguments args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Scope is not null)
        {
            return ToolResult.Fail(ToolError.Of(
                ToolErrorCodes.NotSupported,
                $"按范围校验还没接上：现在只做整份文档，收到的是 {args.Scope}",
                "scope",
                "不带 scope 可以校验整份文档"));
        }

        var issues = DiagramValidator.Validate(context.Document);
        var payload = new ValidatePayload(issues.Count, issues);

        return ToolResult.Ok(
            JsonSerializer.SerializeToElement(payload, ToolJsonContext.Default.ValidatePayload),
            issues.Count == 0
                ? "整份文档通过了整体校验"
                : $"发现 {issues.Count} 个问题");
    }
}
