using System.Text.Json;
using DuetDiagram.Core.Commands;

namespace DuetDiagram.Llm.Tools;

/// <summary>
/// 一个动作，以及它落到的那条命令。
/// </summary>
/// <remarks>
/// <see cref="CommandId"/> 是**声明**，不是从别处推出来的：动作表与命令清单的对照靠它，
/// 而对照由测试逐行核。不记下来的话，「这个动作到底改的是哪条命令」只能靠读实现，
/// 而读实现的人正是要核对这件事的人。
/// </remarks>
/// <param name="Name">动作名，模型填在 <c>action</c> 参数里的那个。</param>
/// <param name="CommandId">落到的那条命令的标识。</param>
public sealed record ToolAction(string Name, string CommandId);

/// <summary>
/// 各工具的动作表。
/// </summary>
/// <remarks>
/// 动作名与命令标识的对照都写在这里，执行体在各自工具里按动作名分发。
/// 两处会分叉，所以有一条测试逐个动作地调一遍——表里列了而实现没接上的动作，
/// 会在那条测试里露出来，而不是等模型调用时才发现。
/// </remarks>
public static class ActionTable
{
    /// <summary>改结构的那一组动作。</summary>
    public static IReadOnlyList<ToolAction> Edit { get; } =
    [
        new("add-node", "add-node"),
        new("remove-node", "remove-node"),
        new("connect-edge", "connect-edge"),
        new("disconnect-edge", "disconnect-edge"),
        new("reconnect-edge", "reconnect-edge"),
        new("set-node-field", "set-node-field"),
        new("set-edge-field", "set-edge-field"),
        new("create-page", "create-page"),
        new("delete-page", "delete-page"),
        new("create-layer", "create-layer"),
        new("rename-layer", "rename-layer"),
        new("reorder-layer", "reorder-layer"),
        new("add-tag", "add-tag"),
        new("remove-tag", "remove-tag"),
        new("add-action", "add-action"),
        new("remove-action", "remove-action"),
        new("set-kind", "set-kind"),
    ];

    /// <summary>改外观的那一组动作。</summary>
    public static IReadOnlyList<ToolAction> Style { get; } =
    [
        new("set-shape", "set-node-field"),
        new("set-style", "set-node-field"),
        new("set-text", "set-node-field"),
        new("set-canvas", "set-canvas-settings"),
        new("define-palette-entry", "define-palette-entry"),
        new("update-palette-entry", "update-palette-entry"),
        new("remove-palette-entry", "remove-palette-entry"),
    ];

    /// <summary>取某个工具的动作表。还没接上的工具返回空。</summary>
    public static IReadOnlyList<ToolAction> For(string tool) => tool switch
    {
        DiagramToolset.Edit => Edit,
        DiagramToolset.Style => Style,
        _ => [],
    };

    /// <summary>某个工具的动作名，用来填「不认得这个动作」那条错误。</summary>
    public static string[] Names(string tool) => [.. For(tool).Select(action => action.Name)];
}

/// <summary>
/// 动作分发的公共部分：找动作、发命令、把结果翻译成工具结果。
/// </summary>
/// <remarks>
/// <para>
/// **一个动作发一条命令。** 所以「一个工具调用要么全部成功要么全部回滚」在这里是现成的：
/// 命令自己的 <c>Apply</c> 已经是原子的。需要跨命令原子的场合现在没有。
/// </para>
/// <para>
/// 命令被拒时返回的是**命令层的错误码**，不是工具层那一套。这两套码分开是因为处置不同
/// （改文档还是改这一次调用），而「命令被拒」说的是文档里的值不对——正是命令层那套码的
/// 含义。另起一套的话，按命令层错误码建的修复建议表会漏掉所有经由工具层的失败。
/// </para>
/// </remarks>
internal static class ActionDispatch
{
    /// <summary>动作名不认识时的统一答复。</summary>
    public static ToolResult UnknownAction(string tool, string action) => ToolResult.Fail(ToolError.Of(
        ToolErrorCodes.ArgumentInvalid,
        $"{tool} 不认得动作 {action}",
        "action",
        $"可用动作：{string.Join('、', ActionTable.Names(tool))}"));

    /// <summary>参数不够或者不适用时的统一答复。</summary>
    public static ToolResult Reject(string message, string parameter, string? expected = null) =>
        ToolResult.Fail(ToolError.Of(ToolErrorCodes.ArgumentInvalid, message, parameter, expected));

    /// <summary>参数没给。</summary>
    public static ToolResult Missing(string message, string parameter) =>
        ToolResult.Fail(ToolError.Of(ToolErrorCodes.ArgumentMissing, message, parameter));

    /// <summary>发一条命令，并把结果翻译成工具结果。</summary>
    public static ToolResult Execute(DiagramToolContext context, IDiagramCommand command)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(command);

        var result = context.Bus.Execute(command);

        return result.IsSuccess ? Succeeded(context, result) : Rejected(result);
    }

    /// <summary>
    /// 成功时的答复。
    /// </summary>
    /// <remarks>
    /// 空操作也走这里：它是成功，只是版本号没动。报成失败会让模型以为要换个做法重试，
    /// 而它其实已经把想做的事做成了（或者本来就没什么可做）。
    /// </remarks>
    private static ToolResult Succeeded(DiagramToolContext context, CommandResult result)
    {
        var outcome = new CommandOutcome
        {
            Version = context.Document.Version,
            NoOp = result.IsNoOp,
            AffectedIds = result.AffectedIds,
            StructuralChanged = result.StructuralChanged,
            VisualChanged = result.VisualChanged,
            Message = result.Message,
        };

        var message = result.IsNoOp
            ? result.Message ?? "命令合法，但没什么可做"
            : result.Message ?? $"已执行，版本 {outcome.Version}";

        return ToolResult.Ok(
            JsonSerializer.SerializeToElement(outcome, ToolJsonContext.Default.CommandOutcome),
            message);
    }

    /// <summary>
    /// 失败时的答复。
    /// </summary>
    /// <remarks>
    /// 出错参数与期望形式这一轮留空：把错误码映射成「哪个参数错了、怎么改」是另一件事，
    /// 在这里先写一遍会让那张表有两份，而两份迟早给模型两种说法。
    /// </remarks>
    private static ToolResult Rejected(CommandResult result)
    {
        if (result.Errors.Length == 0)
        {
            return ToolResult.Fail(ToolError.Of(
                ErrorCodes.InternalError,
                result.Message ?? "命令被拒，但没有给出错误码"));
        }

        return ToolResult.Fail([.. result.Errors.Select(error => ToolError.Of(
            error.Code,
            error.Payload is null ? error.Code : $"{error.Code}：{error.Payload}"))]);
    }
}
