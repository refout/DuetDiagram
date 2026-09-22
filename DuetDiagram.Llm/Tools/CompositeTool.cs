using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Llm.Tools;

/// <summary>
/// <c>diagram_composite</c> 收到的参数。
/// </summary>
/// <param name="Action">要做的事。</param>
/// <param name="Id">组合标识，或要搬动的成员标识。</param>
/// <param name="Kind">组合种类。</param>
/// <param name="MemberIds">成员标识。</param>
/// <param name="TargetId">移入的目标组合。</param>
/// <param name="Label">组合的显示名。</param>
internal sealed record CompositeArguments(
    string Action,
    string? Id = null,
    string? Kind = null,
    IReadOnlyList<string>? MemberIds = null,
    string? TargetId = null,
    string? Label = null);

/// <summary>
/// 管理组合的那一组动作。
/// </summary>
/// <remarks>
/// <para>
/// 组合是分组、泳道、子流程与组合框的共同抽象：四个派生记录的字段形状完全一样，
/// 差别只在类型名。所以 <c>kind</c> 要在这里翻译成对应的记录类型，
/// 而不是给每一类各开一个动作。
/// </para>
/// <para>
/// **嵌套深度不在这里判。** 上限是命令层与整体校验器共用的那一个常量，
/// 工具层再写一个数的话，会出现「工具层放行、命令层拒绝」或者反过来。
/// </para>
/// </remarks>
internal static class CompositeTool
{
    public static ToolResult Run(DiagramToolContext context, CompositeArguments args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (!ActionTable.For(DiagramToolset.Composite).Any(entry => string.Equals(entry.Name, args.Action, StringComparison.Ordinal)))
        {
            return ActionDispatch.UnknownAction(DiagramToolset.Composite, args.Action);
        }

        return args.Action switch
        {
            "create" => Create(context, args),
            "move-into" => MoveInto(context, args),
            "dissolve" => Dissolve(context, args),
            _ => ToolResult.NotSupported(DiagramToolset.Composite, args.Action, "动作表里有它，但执行体还没接上"),
        };
    }

    /// <summary>
    /// 新建一个组合。
    /// </summary>
    /// <remarks>
    /// 新建的一律在顶层——参数表里没有「父级」这一项。要嵌进别的组合里就再发一次
    /// <c>move-into</c>：归属只有那一个入口，绕开它会留下一份成员列表与父级字段
    /// 互相矛盾的文档。
    /// </remarks>
    private static ToolResult Create(DiagramToolContext context, CompositeArguments args)
    {
        if (string.IsNullOrWhiteSpace(args.Id))
        {
            return ActionDispatch.Missing("create 要给出组合标识", "id");
        }

        if (args.Kind is not ("group" or "lane" or "subflow" or "combo"))
        {
            return ActionDispatch.Reject(
                string.IsNullOrWhiteSpace(args.Kind)
                    ? "create 要给出组合种类"
                    : $"{args.Kind} 不是它认得的组合种类",
                "kind",
                "可用种类：group、lane、subflow、combo");
        }

        var members = args.MemberIds ?? [];
        var label = args.Label ?? string.Empty;

        CompositeDef composite = args.Kind switch
        {
            "lane" => new LaneDef { Id = args.Id, Label = label, Members = members },
            "subflow" => new SubflowDef { Id = args.Id, Label = label, Members = members },
            "combo" => new ComboDef { Id = args.Id, Label = label, Members = members },
            _ => new GroupDef { Id = args.Id, Label = label, Members = members },
        };

        return ActionDispatch.Execute(context, new CreateCompositeCommand(composite));
    }

    private static ToolResult MoveInto(DiagramToolContext context, CompositeArguments args) =>
        string.IsNullOrWhiteSpace(args.Id)
            ? ActionDispatch.Missing("move-into 要给出要搬动的成员标识", "id")
            // targetId 留空表示搬到顶层。这一项真的可以不给，所以不报「缺参数」。
            : ActionDispatch.Execute(context, new MoveIntoCompositeCommand(args.Id, args.TargetId));

    private static ToolResult Dissolve(DiagramToolContext context, CompositeArguments args) =>
        string.IsNullOrWhiteSpace(args.Id)
            ? ActionDispatch.Missing("dissolve 要给出组合标识", "id")
            : ActionDispatch.Execute(context, new DissolveCompositeCommand(args.Id));
}
