using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Llm.Tools;

/// <summary>
/// <c>diagram_layout</c> 收到的参数。
/// </summary>
/// <param name="Action">要做的事。</param>
/// <param name="Kind">约束种类。</param>
/// <param name="Direction">主方向。</param>
/// <param name="NodeSpacing">同层节点间距。</param>
/// <param name="LayerSpacing">层与层之间的间距。</param>
/// <param name="Id">要摆位的节点。</param>
/// <param name="RelativeTo">参照节点。</param>
/// <param name="Relation">相对位置。</param>
/// <param name="Owner">这条约束归谁。</param>
/// <param name="MemberIds">约束的成员。</param>
/// <param name="Subject">层内次序的主语节点。</param>
internal sealed record LayoutArguments(
    string Action,
    string? Kind = null,
    string? Direction = null,
    double? NodeSpacing = null,
    double? LayerSpacing = null,
    string? Id = null,
    string? RelativeTo = null,
    string? Relation = null,
    string? Owner = null,
    IReadOnlyList<string>? MemberIds = null,
    string? Subject = null);

/// <summary>
/// 改布局的那一组动作。
/// </summary>
/// <remarks>
/// <para>
/// 这一组只改布局参数与约束，**不搬运坐标**。改完之后要不要重排由返回结果里的
/// 结构变更标志告诉宿主：工具层不引布局引擎，也没有宿主可以回调。
/// </para>
/// <para>
/// 三类约束合成两条命令，种类由 <c>kind</c> 给出。层内次序的形状与另外两类不同
/// （一个主语加它的出边），所以 <c>kind</c> 决定走哪个工厂方法。
/// </para>
/// </remarks>
internal static class LayoutTool
{
    private const string KindValues = "same-rank、align、order";

    private const string RelationValues = "right-of、left-of、above、below";

    public static ToolResult Run(DiagramToolContext context, LayoutArguments args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (!ActionTable.For(DiagramToolset.Layout).Any(entry => string.Equals(entry.Name, args.Action, StringComparison.Ordinal)))
        {
            return ActionDispatch.UnknownAction(DiagramToolset.Layout, args.Action);
        }

        return args.Action switch
        {
            "set-direction" => SetDirection(context, args),
            "set-spacing" => SetSpacing(context, args),
            "add-constraint" => AddConstraint(context, args),
            "remove-constraint" => RemoveConstraint(context, args),
            "set-place" => SetPlace(context, args),
            _ => ToolResult.NotSupported(DiagramToolset.Layout, args.Action, "动作表里有它，但执行体还没接上"),
        };
    }

    #region 方向与间距

    private static ToolResult SetDirection(DiagramToolContext context, LayoutArguments args)
    {
        if (string.IsNullOrWhiteSpace(args.Direction))
        {
            return ActionDispatch.Missing("set-direction 要给出方向", "direction");
        }

        // 四个方向按惯例写成两个大写字母。不写 `lr` 那种小写形式：它在图上与
        // 数字 1、字母 l 混在一起看不出来，而方向是这一组里最常手写的一个值。
        if (ParseDirection(args.Direction) is not { } direction)
        {
            return ActionDispatch.Reject(
                $"{args.Direction} 不是它认得的主方向",
                "direction",
                "可用取值：LR、TB、RL、BT");
        }

        return ActionDispatch.Execute(context, new SetDirectionCommand(direction));
    }

    private static ToolResult SetSpacing(DiagramToolContext context, LayoutArguments args) =>
        args.NodeSpacing is null && args.LayerSpacing is null
            // 两个都不给就是一次什么都不做的调用。命令层也会拒，但那句话说的是
            // 「这一次调用没有要改的东西」，与调用方看到的参数对不上。
            ? ActionDispatch.Missing(
                "set-spacing 至少要给出 nodeSpacing 或 layerSpacing 里的一个",
                "nodeSpacing")
            : ActionDispatch.Execute(context, new SetSpacingCommand(args.NodeSpacing, args.LayerSpacing));

    #endregion

    #region 约束

    private static ToolResult AddConstraint(DiagramToolContext context, LayoutArguments args)
    {
        if (Spec(args) is { } error)
        {
            return error;
        }

        if (Owner(args) is { } ownerError)
        {
            return ownerError;
        }

        return ActionDispatch.Execute(
            context,
            new AddLayoutConstraintCommand(SpecOf(args)!, OwnerOf(args)));
    }

    private static ToolResult RemoveConstraint(DiagramToolContext context, LayoutArguments args)
    {
        if (Spec(args) is { } error)
        {
            return error;
        }

        if (Owner(args) is { } ownerError)
        {
            return ownerError;
        }

        return ActionDispatch.Execute(
            context,
            new RemoveLayoutConstraintCommand(SpecOf(args)!, OwnerOf(args)));
    }

    /// <summary>
    /// 按 <c>kind</c> 校验成员形状，不成立时给出错误。
    /// </summary>
    /// <remarks>
    /// 三类的形状不同，所以 <c>kind</c> 不只是个标签：同层与对齐是一组平级节点，
    /// 层内次序是一个主语加它的出边。少了主语那一项时要说清是缺它，
    /// 而不是让命令层回一句「至少两条出边」——那句话与调用方看到的参数对不上。
    /// </remarks>
    private static ToolResult? Spec(LayoutArguments args)
    {
        if (args.MemberIds is not { Count: > 0 })
        {
            return ActionDispatch.Missing("这个动作要给出成员", "memberIds");
        }

        if (args.Kind is "same-rank" or "align")
        {
            return null;
        }

        if (args.Kind == "order")
        {
            return string.IsNullOrWhiteSpace(args.Subject)
                ? ActionDispatch.Missing("层内次序要给出主语节点", "subject")
                : null;
        }

        return ActionDispatch.Reject(
            string.IsNullOrWhiteSpace(args.Kind)
                ? "这个动作要给出约束种类"
                : $"{args.Kind} 不是它认得的约束种类",
            "kind",
            $"可用种类：{KindValues}");
    }

    /// <summary>校验通过之后才调，所以这里直接按 <c>kind</c> 造规格。</summary>
    private static LayoutConstraintSpec SpecOf(LayoutArguments args) => args.Kind switch
    {
        "same-rank" => LayoutConstraintSpec.SameRank(args.MemberIds!),
        "align" => LayoutConstraintSpec.Align(args.MemberIds!),
        _ => LayoutConstraintSpec.Order(args.Subject!, args.MemberIds!),
    };

    /// <summary>
    /// 归属方这一项的校验。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 缺省写 <c>llm</c>：调用这一层的就是模型那条路径，所以这是事实而不是猜测。
    /// </para>
    /// <para>
    /// **<c>human</c> 要拒绝。** 归属方是降级矩阵的输入，人工定的比模型提的更值得保留；
    /// 放行的话模型可以伪造最高优先级的归属，把用户自己设的约束在降级时挤掉。
    /// 用户从面板上设的约束由面板自己写，不经过这里。
    /// </para>
    /// <para>
    /// <c>auto</c> 放行：它是更弱的一档，模型表达「这只是个建议」是合理用法。
    /// </para>
    /// </remarks>
    private static ToolResult? Owner(LayoutArguments args) =>
        string.IsNullOrWhiteSpace(args.Owner)
        || string.Equals(args.Owner, "llm", StringComparison.OrdinalIgnoreCase)
        || string.Equals(args.Owner, "auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : string.Equals(args.Owner, "human", StringComparison.OrdinalIgnoreCase)
                ? ActionDispatch.Reject(
                    "human 是用户从界面上设的约束的归属，工具层写不了",
                    "owner",
                    "可用取值：llm、auto")
                : ActionDispatch.Reject(
                    $"{args.Owner} 不是它认得的归属方",
                    "owner",
                    "可用取值：llm、auto");

    private static ConstraintOwner OwnerOf(LayoutArguments args) =>
        string.Equals(args.Owner, "auto", StringComparison.OrdinalIgnoreCase)
            ? ConstraintOwner.Auto
            : ConstraintOwner.Llm;

    #endregion

    #region 相对位置

    private static ToolResult SetPlace(DiagramToolContext context, LayoutArguments args)
    {
        if (string.IsNullOrWhiteSpace(args.Id))
        {
            return ActionDispatch.Missing("set-place 要给出要摆位的节点", "id");
        }

        if (string.IsNullOrWhiteSpace(args.RelativeTo))
        {
            return ActionDispatch.Missing("set-place 要给出参照节点", "relativeTo");
        }

        // 位置关系留空表示清除这一条。相对位置本身只有四个取值，空着正好用来表达「清掉」；
        // 而空引用在别的动作里是「这一项不动」，两者不是一回事。
        PlaceRelation? relation = null;

        if (!string.IsNullOrWhiteSpace(args.Relation))
        {
            if (Relation(args.Relation) is not { } parsed)
            {
                return ActionDispatch.Reject(
                    $"{args.Relation} 不是它认得的相对位置",
                    "relation",
                    $"可用取值：{RelationValues}");
            }

            relation = parsed;
        }

        if (Owner(args) is { } ownerError)
        {
            return ownerError;
        }

        var result = ActionDispatch.Execute(
            context,
            new SetPlaceCommand(args.Id, args.RelativeTo, relation, OwnerOf(args)));

        // 这一类约束求解器还不消费它：设完之后坐标不变。不说的话，调用方会把
        // 「命令没生效」当成一次失败，然后换个做法重试。
        return result.IsSuccess && relation is not null
            ? result with { Message = $"{result.Message}；这一类约束求解器还不消费它，坐标不会因此改变" }
            : result;
    }

    private static PlaceRelation? Relation(string value) => value switch
    {
        "right-of" => PlaceRelation.RightOf,
        "left-of" => PlaceRelation.LeftOf,
        "above" => PlaceRelation.Above,
        "below" => PlaceRelation.Below,
        _ => null,
    };

    private static Direction? ParseDirection(string value) => value switch
    {
        "LR" => Direction.LR,
        "TB" => Direction.TB,
        "RL" => Direction.RL,
        "BT" => Direction.BT,
        _ => null,
    };

    #endregion
}
