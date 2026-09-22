using DuetDiagram.Core.Bus;

namespace DuetDiagram.Llm.Tools;

/// <summary>
/// <c>diagram_undo_redo</c> 收到的参数。
/// </summary>
/// <param name="Action">要做的事：undo 或 redo。</param>
/// <param name="Steps">撤几步。留空表示一步。</param>
internal sealed record HistoryArguments(string Action, int? Steps = null);

/// <summary>
/// 撤销与重做。
/// </summary>
/// <remarks>
/// <para>
/// **人和模型共用同一条历史栈。** 分成两条的话，用户按 Ctrl+Z 撤掉的可能是模型十步之前的
/// 改动，而界面上看不出这件事——共用一个栈正是「人和 LLM 能力对等」的直接体现。
/// </para>
/// <para>
/// 它也不进动作表：动作表里每一条都发一条命令，而撤销重做走的是历史栈。
/// </para>
/// <para>
/// **多步要能整体回滚。** 一次工具调用要么全部成功要么全部回滚；某一步失败时，
/// 把已经成功的那些反向做回去。半途停下的文档比整条失败更难收拾。
/// </para>
/// </remarks>
internal static class HistoryTool
{
    public static ToolResult Run(DiagramToolContext context, HistoryArguments args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Action is not ("undo" or "redo"))
        {
            return ActionDispatch.Reject(
                string.IsNullOrWhiteSpace(args.Action)
                    ? "要给出要做的事"
                    : $"{args.Action} 不是它认得的事",
                "action",
                "可用取值：undo、redo");
        }

        if (args.Steps is { } steps && steps < 1)
        {
            return ActionDispatch.Reject(
                $"steps 要给一个正整数，收到的是 {steps}",
                "steps",
                "留空表示一步");
        }

        return Run(context, args.Action, args.Steps ?? 1);
    }

    private static ToolResult Run(DiagramToolContext context, string action, int steps)
    {
        var undone = new List<string>(steps);
        var mine = 0;
        var exhausted = false;

        for (var index = 0; index < steps; index++)
        {
            // 先窥栈顶再动手：撤销与重做的返回值里没有「被撤的是哪一条」，
            // 而模型需要知道自己刚才那一步被撤了，否则它会以为文档还停在自己写完之后的状态。
            var next = action == "undo" ? context.Bus.Context.History.PeekUndo() : context.Bus.Context.History.PeekRedo();

            if (next is null)
            {
                exhausted = true;
                break;
            }

            var result = action == "undo" ? context.Bus.Undo() : context.Bus.Redo();

            // 栈空时返回的是空操作而不是失败。撞上它就是「到头了」，
            // 报成失败会让模型以为整条调用没生效。
            if (result.IsNoOp)
            {
                exhausted = true;
                break;
            }

            if (!result.IsSuccess)
            {
                Rollback(context, action, undone.Count);

                return ActionDispatch.Failed(result);
            }

            undone.Add(next.Command.CommandId);

            if (string.Equals(next.SessionId, context.Bus.Context.Session.CurrentSessionId, StringComparison.Ordinal))
            {
                mine++;
            }
        }

        return ActionDispatch.Outcome(context, Describe(action, undone, mine, exhausted), undone);
    }

    /// <summary>
    /// 把这一步的结果说清楚。
    /// </summary>
    /// <remarks>
    /// 名称要写出来：模型需要知道自己刚才那一步被撤了，否则它会以为文档还停在自己
    /// 写完之后的状态。栈见底时也要说清实际做了几步——报成失败的话模型会以为整条调用没生效。
    /// </remarks>
    private static string Describe(string action, List<string> steps, int mine, bool exhausted)
    {
        if (steps.Count == 0)
        {
            return action == "undo" ? "撤销栈是空的，没有可撤销的操作" : "重做栈是空的，没有可重做的操作";
        }

        var what = action == "undo" ? "撤销" : "重做";
        var text = $"{what}了 {steps.Count} 步：{string.Join('、', steps)}";

        if (exhausted)
        {
            text += $"，{(action == "undo" ? "撤销" : "重做")}栈已见底";
        }

        return mine > 0 ? $"{text}；其中 {mine} 步是你自己改的" : text;
    }

    /// <summary>
    /// 把已经成功的那几步反向做回去。
    /// </summary>
    /// <remarks>
    /// 撤销步用重做回滚，重做步用撤销回滚。回滚自己失败时不再往下追——
    /// 那说明历史栈本身已经坏了，再翻一次只会把状态搅得更乱。
    /// </remarks>
    private static void Rollback(DiagramToolContext context, string action, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var result = action == "undo" ? context.Bus.Redo() : context.Bus.Undo();

            if (!result.IsEffectiveSuccess)
            {
                return;
            }
        }
    }
}
