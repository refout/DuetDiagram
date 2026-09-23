using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Llm.Tools;

/// <summary>
/// <c>diagram_edit</c> 收到的参数。
/// </summary>
/// <param name="Action">要做的事。</param>
/// <param name="Id">操作针对的元素标识。</param>
/// <param name="From">连线起点，可带端口。</param>
/// <param name="To">连线终点，可带端口。</param>
/// <param name="Field">要写的字段名。</param>
/// <param name="Value">字段要写成的值。</param>
/// <param name="Label">显示文本。</param>
/// <param name="MemberIds">成员标识。</param>
/// <param name="Index">插入位置或次序。</param>
/// <param name="Event">动作的触发事件名。</param>
/// <param name="Kind">动作的类型名。</param>
/// <param name="TargetId">动作作用的对象。</param>
internal sealed record EditArguments(
    string Action,
    string? Id = null,
    string? From = null,
    string? To = null,
    string? Field = null,
    string? Value = null,
    string? Label = null,
    IReadOnlyList<string>? MemberIds = null,
    int? Index = null,
    string? Event = null,
    string? Kind = null,
    string? TargetId = null);

/// <summary>
/// 改结构的那一组动作。
/// </summary>
/// <remarks>
/// <para>
/// 每个动作发一条命令，参数原样交给命令层去校验。工具层**不复制字段表**：
/// 复制的后果是命令层明明支持某个字段，模型却总被工具层挡住，而两处各自都自洽。
/// </para>
/// <para>
/// 动作名到命令的对照在 <see cref="ActionTable"/> 里，这里只负责把参数摆成命令要的形状。
/// 表里列了而这里没接上的动作会返回结构化的「还没接上」，且有一条测试逐个动作地调一遍。
/// </para>
/// </remarks>
internal static class EditTool
{
    public static ToolResult Run(DiagramToolContext context, EditArguments args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (!ActionTable.For(DiagramToolset.Edit).Any(entry => string.Equals(entry.Name, args.Action, StringComparison.Ordinal)))
        {
            return ActionDispatch.UnknownAction(DiagramToolset.Edit, args.Action);
        }

        return args.Action switch
        {
            "add-node" => AddNode(context, args),
            "remove-node" => RemoveNode(context, args),
            "connect-edge" => ConnectEdge(context, args),
            "disconnect-edge" => DisconnectEdge(context, args),
            "reconnect-edge" => ReconnectEdge(context, args),
            "set-node-field" => SetNodeField(context, args),
            "set-edge-field" => SetEdgeField(context, args),
            "create-page" => CreatePage(context, args),
            "delete-page" => DeletePage(context, args),
            "create-layer" => CreateLayer(context, args),
            "rename-layer" => RenameLayer(context, args),
            "reorder-layer" => ReorderLayer(context, args),
            "set-layer-visible" => SetLayerVisible(context, args),
            "set-layer-locked" => SetLayerLocked(context, args),
            "add-tag" => AddTag(context, args),
            "remove-tag" => RemoveTag(context, args),
            "add-action" => AddAction(context, args),
            "remove-action" => RemoveAction(context, args),
            "set-kind" => SetKind(context, args),
            _ => DeclaredButNotWired(args.Action),
        };
    }

    /// <summary>动作在表里，但执行体没接上。与「不认得这个动作」分开，两者的处置不同。</summary>
    private static ToolResult DeclaredButNotWired(string action) =>
        ToolResult.NotSupported(DiagramToolset.Edit, action, "动作表里有它，但执行体还没接上");

    #region 节点

    private static ToolResult AddNode(DiagramToolContext context, EditArguments args)
    {
        if (Missing(args.Id, "id", "节点标识") is { } error)
        {
            return error;
        }

        // 标签缺省时回退到标识。与两个导入映射层同一口径：`label` 为空串是「没写」，
        // 而「没写」的节点在画面上是一个空方框，没人要那个。
        var node = new NodeDef { Id = args.Id!, Label = args.Label ?? args.Id! };

        return ActionDispatch.Execute(context, new AddNodeCommand(node, args.Index));
    }

    private static ToolResult RemoveNode(DiagramToolContext context, EditArguments args) =>
        Missing(args.Id, "id", "节点标识") is { } error
            ? error
            : ActionDispatch.Execute(context, new RemoveNodeCommand(args.Id!));

    private static ToolResult SetNodeField(DiagramToolContext context, EditArguments args)
    {
        if (Missing(args.Id, "id", "节点标识") is { } error)
        {
            return error;
        }

        if (Missing(args.Field, "field", "字段名") is { } fieldError)
        {
            return fieldError;
        }

        // 改图层归属是一条图层级写入，而它点名的图层写在 value 上：field 是 layer 时，
        // value 才是图层标识。别处那几条图层命令点的是 id。
        if (string.Equals(args.Field, FieldNames.Layer, StringComparison.Ordinal)
            && ActionDispatch.DeniedLayer(context, args.Value, "value") is { } denied)
        {
            return denied;
        }

        return ActionDispatch.Execute(context, new SetNodeFieldCommand(args.Id!, args.Field!, args.Value));
    }

    #endregion

    #region 边

    private static ToolResult ConnectEdge(DiagramToolContext context, EditArguments args)
    {
        if (Missing(args.Id, "id", "边标识") is { } error)
        {
            return error;
        }

        if (Missing(args.From, "from", "起点") is { } fromError)
        {
            return fromError;
        }

        if (Missing(args.To, "to", "终点") is { } toError)
        {
            return toError;
        }

        var from = PortParser.Parse(args.From!);
        var to = PortParser.Parse(args.To!);

        var edge = new EdgeDef
        {
            Id = args.Id!,
            From = from.Id,
            To = to.Id,
            FromPort = from.Port,
            ToPort = to.Port,
            Label = args.Label ?? string.Empty,
        };

        return ActionDispatch.Execute(context, new ConnectEdgeCommand(edge));
    }

    private static ToolResult DisconnectEdge(DiagramToolContext context, EditArguments args) =>
        Missing(args.Id, "id", "边标识") is { } error
            ? error
            : ActionDispatch.Execute(context, new DisconnectEdgeCommand(args.Id!));

    private static ToolResult ReconnectEdge(DiagramToolContext context, EditArguments args)
    {
        if (Missing(args.Id, "id", "边标识") is { } error)
        {
            return error;
        }

        if (Missing(args.From, "from", "起点") is { } fromError)
        {
            return fromError;
        }

        if (Missing(args.To, "to", "终点") is { } toError)
        {
            return toError;
        }

        var from = PortParser.Parse(args.From!);
        var to = PortParser.Parse(args.To!);

        return ActionDispatch.Execute(
            context,
            new ReconnectEdgeCommand(args.Id!, from.Id, from.Port, to.Id, to.Port));
    }

    private static ToolResult SetEdgeField(DiagramToolContext context, EditArguments args)
    {
        if (Missing(args.Id, "id", "边标识") is { } error)
        {
            return error;
        }

        return Missing(args.Field, "field", "字段名") is { } fieldError
            ? fieldError
            : ActionDispatch.Execute(context, new SetEdgeFieldCommand(args.Id!, args.Field!, args.Value));
    }

    #endregion

    #region 页面与图层

    private static ToolResult CreatePage(DiagramToolContext context, EditArguments args) =>
        Missing(args.Id, "id", "页面标识") is { } error
            ? error
            : ActionDispatch.Execute(context, new CreatePageCommand(args.Id!, args.Label ?? string.Empty));

    private static ToolResult DeletePage(DiagramToolContext context, EditArguments args) =>
        Missing(args.Id, "id", "页面标识") is { } error
            ? error
            : ActionDispatch.Execute(context, new DeletePageCommand(args.Id!));

    private static ToolResult CreateLayer(DiagramToolContext context, EditArguments args) =>
        Missing(args.Id, "id", "图层标识") is { } error
            ? error
            : ActionDispatch.DeniedLayer(context, args.Id, "id")
                ?? ActionDispatch.Execute(context, new CreateLayerCommand(args.Id!, args.Label ?? string.Empty));

    private static ToolResult RenameLayer(DiagramToolContext context, EditArguments args)
    {
        if (Missing(args.Id, "id", "图层标识") is { } error)
        {
            return error;
        }

        if (ActionDispatch.DeniedLayer(context, args.Id, "id") is { } denied)
        {
            return denied;
        }

        // 名字允许是空串（那是"改成一个空名字"），但不能没给：没给多半是参数写漏了。
        return args.Label is null
            ? ActionDispatch.Missing("这个动作要给出新的图层名", "label")
            : ActionDispatch.Execute(context, new RenameLayerCommand(args.Id!, args.Label));
    }

    private static ToolResult ReorderLayer(DiagramToolContext context, EditArguments args)
    {
        if (Missing(args.Id, "id", "图层标识") is { } error)
        {
            return error;
        }

        if (ActionDispatch.DeniedLayer(context, args.Id, "id") is { } denied)
        {
            return denied;
        }

        return args.Index is null
            ? ActionDispatch.Missing("这个动作要给出新的次序", "index")
            : ActionDispatch.Execute(context, new ReorderLayerCommand(args.Id!, args.Index.Value));
    }

    /// <summary>
    /// 把一个图层藏起来或者放出来。
    /// </summary>
    /// <remarks>
    /// 布尔写在 <c>value</c> 上，与 <c>set-node-field</c> 写标志位那条路同一个写法
    /// （界面上的三态开关提交的也是 <c>"true"</c> / <c>"false"</c>）。
    /// 另开一个布尔参数等于给"布尔怎么写"开第二种写法，而模型会在两者之间随机挑一个。
    /// </remarks>
    private static ToolResult SetLayerVisible(DiagramToolContext context, EditArguments args) =>
        LayerSwitch(context, args, "set-layer-visible", locked: false);

    /// <summary>锁上一个图层或者解锁。</summary>
    private static ToolResult SetLayerLocked(DiagramToolContext context, EditArguments args) =>
        LayerSwitch(context, args, "set-layer-locked", locked: true);

    private static ToolResult LayerSwitch(
        DiagramToolContext context,
        EditArguments args,
        string action,
        bool locked)
    {
        if (Missing(args.Id, "id", "图层标识") is { } error)
        {
            return error;
        }

        if (Missing(args.Value, "value", "true 或 false") is { } valueError)
        {
            return valueError;
        }

        if (!bool.TryParse(args.Value, out var on))
        {
            return ActionDispatch.Reject($"{action} 的 value 要填 true 或 false", "value", "true 或 false");
        }

        // 点名的图层写在 id 上：这两条动的是"哪一层"，不是"改成哪一层"。
        if (ActionDispatch.DeniedLayer(context, args.Id, "id") is { } denied)
        {
            return denied;
        }

        return ActionDispatch.Execute(
            context,
            locked ? new SetLayerLockedCommand(args.Id!, on) : new SetLayerVisibleCommand(args.Id!, on));
    }

    #endregion

    #region 标签与动作

    private static ToolResult AddTag(DiagramToolContext context, EditArguments args)
    {
        if (Missing(args.Id, "id", "标签标识") is { } error)
        {
            return error;
        }

        var tag = new TagDef
        {
            Id = args.Id!,
            Label = args.Label ?? string.Empty,
            Members = args.MemberIds ?? [],
        };

        return ActionDispatch.Execute(context, new AddTagCommand(tag));
    }

    private static ToolResult RemoveTag(DiagramToolContext context, EditArguments args) =>
        Missing(args.Id, "id", "标签标识") is { } error
            ? error
            : ActionDispatch.Execute(context, new RemoveTagCommand(args.Id!));

    /// <summary>
    /// 加一条动作。
    /// </summary>
    /// <remarks>
    /// 动作自己的参数表（<c>ActionDef.Parameters</c>）这一轮表达不了——参数表里没有
    /// 一个能装键值对的位置。建出来的动作参数表是空的，等动作有真正的消费方时再补。
    /// </remarks>
    private static ToolResult AddAction(DiagramToolContext context, EditArguments args)
    {
        if (Missing(args.Id, "id", "动作标识") is { } error)
        {
            return error;
        }

        if (Missing(args.Event, "event", "触发事件名") is { } eventError)
        {
            return eventError;
        }

        if (Missing(args.Kind, "kind", "动作类型名") is { } kindError)
        {
            return kindError;
        }

        var action = new ActionDef
        {
            Id = args.Id!,
            Event = args.Event!,
            Kind = args.Kind!,
            Target = args.TargetId,
        };

        return ActionDispatch.Execute(context, new AddActionCommand(action));
    }

    private static ToolResult RemoveAction(DiagramToolContext context, EditArguments args) =>
        Missing(args.Id, "id", "动作标识") is { } error
            ? error
            : ActionDispatch.Execute(context, new RemoveActionCommand(args.Id!));

    #endregion

    #region 文档

    private static ToolResult SetKind(DiagramToolContext context, EditArguments args)
    {
        if (Missing(args.Value, "value", "图类型") is { } error)
        {
            return error;
        }

        // 数字形式也认，但落在定义之外时不能悄悄当成某个类型——那会让一次参数错误
        // 表现成「类型改了但改错了」。
        if (!Enum.TryParse<DiagramKind>(args.Value, ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
        {
            return ActionDispatch.Reject(
                $"图类型收到的是 {args.Value}，不是它认得的取值",
                "value",
                $"可用取值：{string.Join('、', Enum.GetNames<DiagramKind>())}");
        }

        return ActionDispatch.Execute(context, new SetKindCommand(kind));
    }

    #endregion

    private static ToolResult? Missing(string? value, string parameter, string what) =>
        string.IsNullOrWhiteSpace(value)
            ? ActionDispatch.Missing($"这个动作要给出{what}", parameter)
            : null;
}
