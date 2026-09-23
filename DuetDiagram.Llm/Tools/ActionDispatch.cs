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
        new("set-layer-visible", "set-layer-visible"),
        new("set-layer-locked", "set-layer-locked"),
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

    /// <summary>
    /// 改布局的那一组动作。
    /// </summary>
    /// <remarks>
    /// 增删约束的动作名比命令标识短，两者不同名是有意的：说明里举的例子就是
    /// <c>add-constraint</c>，而命令标识带着「layout」这个前缀是为了在命令清单里与
    /// 别的 add 区分开。
    /// </remarks>
    public static IReadOnlyList<ToolAction> Layout { get; } =
    [
        new("set-direction", "set-direction"),
        new("set-spacing", "set-spacing"),
        new("add-constraint", "add-layout-constraint"),
        new("remove-constraint", "remove-layout-constraint"),
        new("set-place", "set-place"),
    ];

    /// <summary>管理组合的那一组动作。</summary>
    public static IReadOnlyList<ToolAction> Composite { get; } =
    [
        new("create", "create-composite"),
        new("move-into", "move-into-composite"),
        new("dissolve", "dissolve-composite"),
    ];

    /// <summary>取某个工具的动作表。还没接上的工具返回空。</summary>
    public static IReadOnlyList<ToolAction> For(string tool) => tool switch
    {
        DiagramToolset.Edit => Edit,
        DiagramToolset.Style => Style,
        DiagramToolset.Layout => Layout,
        DiagramToolset.Composite => Composite,
        _ => [],
    };

    /// <summary>动作表里已经接上的工具，用来逐个动作地调一遍。</summary>
    public static IReadOnlyList<string> Wired { get; } =
        [DiagramToolset.Edit, DiagramToolset.Style, DiagramToolset.Layout, DiagramToolset.Composite];

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

    /// <summary>
    /// 这一次点名的页面不在文档里时的答复。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **与元素上那个归属字段的处理刻意不同。** 归属指向一个不存在的页面时按缺省页处理
    /// （那是文档内部的引用，按缺省处理不会出错）；而参数里点的页面是调用方要读或要导出的
    /// 东西——认下来按别的页回，它拿到的是一张不是它要的图，而它看不出来。
    /// </para>
    /// <para>
    /// 现有页面列在这里而不是写进修复建议那张表：有哪些页面随文档变，
    /// 静态表里写不出来，写死了就是错的。
    /// </para>
    /// </remarks>
    /// <returns>要拒绝时返回一条失败，没点名或页面存在时返回空。</returns>
    public static ToolResult? PageMissing(DiagramToolContext context, string? pageId)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (pageId is null)
        {
            return null;
        }

        var pages = context.Document.Pages;

        if (pages.Any(page => string.Equals(page.Id, pageId, StringComparison.Ordinal)))
        {
            return null;
        }

        var known = pages.Count == 0
            ? "这份文档一页都没有"
            : $"现有的页面：{string.Join('、', pages
                .OrderBy(page => page.Order)
                .ThenBy(page => page.Id, StringComparer.Ordinal)
                .Select(page => page.Id))}";

        return ToolResult.Fail(ToolError.Of(
            ErrorCodes.PageMissing,
            $"文档里没有 {pageId} 这一页，这一次读的不是你以为的那一份东西",
            "pageId",
            known));
    }

    /// <summary>
    /// 这一次写入点名的图层许不许碰。
    /// </summary>
    /// <param name="context">这次调用的上下文，权限从它取。</param>
    /// <param name="layerId">写入点名的图层标识。没点名时为空。</param>
    /// <param name="parameter">图层标识写在哪个参数上，用来告诉调用方该改哪一个。</param>
    /// <remarks>
    /// <para>
    /// 判定放在这里而不是传输层：一次写入有没有点名图层、点名的是哪个，藏在动作参数里——
    /// 改节点归属时图层写在 <c>value</c> 上，建图层与改图层时写在 <c>id</c> 上。
    /// 传输层要判就得把这张对照表抄一份，而抄漏的那一格会静默放行。
    /// </para>
    /// <para>
    /// 许的时候返回空，调用方接着发命令；不许的时候返回一条失败，调用方直接把它交出去。
    /// 用空表示"没被挡住"而不是用布尔，是为了让调用点写成一行短路，少一处
    /// "先判布尔再决定返什么"的分叉。
    /// </para>
    /// </remarks>
    public static ToolResult? DeniedLayer(DiagramToolContext context, string? layerId, string parameter)
    {
        ArgumentNullException.ThrowIfNull(context);

        var permissions = context.Permissions();

        if (permissions.AllowsLayer(layerId))
        {
            return null;
        }

        // 可用的图层列在这里，而不是写进修复建议那张表：允许哪些图层随凭据变，
        // 静态表里写不出来，写死了就是错的。
        var allowed = permissions.Layers.Count == 0
            ? "这份凭据一个图层都不许改"
            : $"允许的图层：{string.Join('、', permissions.Layers.Order(StringComparer.Ordinal))}";

        return ToolResult.Fail(ToolError.Of(
            ErrorCodes.LayerForbidden,
            $"这份凭据不许改图层 {layerId}，这一次改动没有发出去",
            parameter,
            allowed));
    }

    /// <summary>发一条命令，并把结果翻译成工具结果。</summary>
    /// <remarks>
    /// 版本声明在发出去之前现读一次。带版本检查的通路上，不带声明会被判成
    /// 「缺少版本声明」而整条命令发不出去；不带版本检查的通路上它本来就是空，
    /// 读一次等于没读。
    /// </remarks>
    public static ToolResult Execute(DiagramToolContext context, IDiagramCommand command)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(command);

        var result = context.Bus.Execute(command, context.ExpectedVersion?.Invoke());

        return result.IsSuccess ? Succeeded(context, result) : Failed(result);
    }

    /// <summary>
    /// 把一条被拒的命令翻译成工具结果。
    /// </summary>
    /// <remarks>
    /// 出错参数与期望形式这一轮留空：把错误码映射成「哪个参数错了、怎么改」是另一件事，
    /// 在这里先写一遍会让那张表有两份，而两份迟早给模型两种说法。
    /// </remarks>
    public static ToolResult Failed(CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

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

    /// <summary>
    /// 按显式的值拼一条成功答复。
    /// </summary>
    /// <remarks>
    /// 撤销重做那一路用它：它走的是历史栈上的条目，没有一条命令结果可以翻译。
    /// 变更标志在这一路报真——撤销与重做都会让画面变，而要不要重排由宿主自己按
    /// 受影响标识判断，工具层不替它猜。
    /// </remarks>
    public static ToolResult Outcome(
        DiagramToolContext context,
        string message,
        IReadOnlyList<string> affected)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(affected);

        var outcome = new CommandOutcome
        {
            Version = context.Document.Version,
            NoOp = affected.Count == 0,
            AffectedIds = [.. affected],
            StructuralChanged = affected.Count > 0,
            VisualChanged = affected.Count > 0,
            Message = message,
        };

        return ToolResult.Ok(
            JsonSerializer.SerializeToElement(outcome, ToolJsonContext.Default.CommandOutcome),
            message);
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
}
