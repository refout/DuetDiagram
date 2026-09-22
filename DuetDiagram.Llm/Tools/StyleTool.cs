using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Llm.Tools;

/// <summary>
/// <c>diagram_style</c> 收到的参数。
/// </summary>
/// <param name="Action">要做的事。</param>
/// <param name="Id">要改的元素标识。</param>
/// <param name="Token">样式令牌名。</param>
/// <param name="Field">要改的样式成员名。</param>
/// <param name="Value">成员要写成的值。</param>
internal sealed record StyleArguments(
    string Action,
    string? Id = null,
    string? Token = null,
    string? Field = null,
    string? Value = null);

/// <summary>
/// 改外观的那一组动作。
/// </summary>
/// <remarks>
/// <para>
/// 这一组都是**节点级或文档级**的，所以不需要在节点与边之间猜：改边的样式走
/// <c>diagram_edit</c> 的 <c>set-edge-field</c>。
/// </para>
/// <para>
/// 三个成员动作（形状、样式、文本）最终都落到同一条按字段名分发的命令上，
/// 区别只在前缀。**前缀检查不是装饰**：不查的话，把 <c>text.fontSize</c> 传给
/// <c>set-style</c> 也会成功，而这两个动作各自的说明就成了一句空话。
/// </para>
/// </remarks>
internal static class StyleTool
{
    public static ToolResult Run(DiagramToolContext context, StyleArguments args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (!ActionTable.For(DiagramToolset.Style).Any(entry => string.Equals(entry.Name, args.Action, StringComparison.Ordinal)))
        {
            return ActionDispatch.UnknownAction(DiagramToolset.Style, args.Action);
        }

        return args.Action switch
        {
            "set-shape" => SetShape(context, args),
            "set-style" => SetStyle(context, args),
            "set-text" => SetText(context, args),
            "set-canvas" => SetCanvas(context, args),
            "define-palette-entry" => DefinePaletteEntry(context, args),
            "update-palette-entry" => UpdatePaletteEntry(context, args),
            "remove-palette-entry" => RemovePaletteEntry(context, args),
            _ => DeclaredButNotWired(args.Action),
        };
    }

    private static ToolResult DeclaredButNotWired(string action) =>
        ToolResult.NotSupported(DiagramToolset.Style, action, "动作表里有它，但执行体还没接上");

    private static ToolResult SetShape(DiagramToolContext context, StyleArguments args)
    {
        if (Missing(args.Id, "id", "节点标识") is { } error)
        {
            return error;
        }

        return Missing(args.Value, "value", "形状名") is { } valueError
            ? valueError
            : ActionDispatch.Execute(
                context,
                new SetNodeFieldCommand(args.Id!, FieldNames.Shape, args.Value));
    }

    /// <summary>
    /// 改样式。
    /// </summary>
    /// <remarks>
    /// 给了 <c>token</c> 就写样式令牌，否则按 <c>field</c> 写某个成员。
    /// 令牌要过白名单：放行未知令牌的话，渲染层拿到不认识的名字会静默按默认样式画，
    /// 而调用方以为样式生效了。
    /// </remarks>
    private static ToolResult SetStyle(DiagramToolContext context, StyleArguments args)
    {
        if (Missing(args.Id, "id", "节点标识") is { } error)
        {
            return error;
        }

        if (!string.IsNullOrWhiteSpace(args.Token))
        {
            return StyleResolver.CheckToken(context.Document, args.Token!) is { } tokenError
                ? tokenError
                : ActionDispatch.Execute(
                    context,
                    new SetNodeFieldCommand(args.Id!, FieldNames.StyleToken, args.Token));
        }

        if (!StyleResolver.HasPrefix(args.Field, StyleResolver.StylePrefix))
        {
            return ActionDispatch.Reject(
                string.IsNullOrWhiteSpace(args.Field)
                    ? "set-style 要给出 token，或者给出一个 style. 开头的成员名"
                    : $"{args.Field} 不是样式成员",
                "field",
                $"成员名要以 {StyleResolver.StylePrefix} 开头，例如 style.fill");
        }

        return ActionDispatch.Execute(context, new SetNodeFieldCommand(args.Id!, args.Field!, args.Value));
    }

    private static ToolResult SetText(DiagramToolContext context, StyleArguments args)
    {
        if (Missing(args.Id, "id", "节点标识") is { } error)
        {
            return error;
        }

        if (!StyleResolver.HasPrefix(args.Field, StyleResolver.TextPrefix))
        {
            return ActionDispatch.Reject(
                string.IsNullOrWhiteSpace(args.Field)
                    ? "set-text 要给出一个成员名"
                    : $"{args.Field} 不是文本成员",
                "field",
                $"成员名要以 {StyleResolver.TextPrefix} 开头，例如 text.fontSize");
        }

        return ActionDispatch.Execute(context, new SetNodeFieldCommand(args.Id!, args.Field!, args.Value));
    }

    private static ToolResult SetCanvas(DiagramToolContext context, StyleArguments args)
    {
        var (command, error) = StyleResolver.Canvas(args.Field, args.Value);

        return error ?? ActionDispatch.Execute(context, command!);
    }

    private static ToolResult DefinePaletteEntry(DiagramToolContext context, StyleArguments args)
    {
        if (Missing(args.Token, "token", "令牌名") is { } error)
        {
            return error;
        }

        var (entry, memberError) = StyleResolver.PaletteEntry(args.Token!, args.Field, args.Value);

        return memberError ?? ActionDispatch.Execute(context, new DefinePaletteEntryCommand(entry!));
    }

    private static ToolResult UpdatePaletteEntry(DiagramToolContext context, StyleArguments args)
    {
        if (Missing(args.Token, "token", "令牌名") is { } error)
        {
            return error;
        }

        return Missing(args.Field, "field", "成员名") is { } fieldError
            ? fieldError
            : ActionDispatch.Execute(
                context,
                new UpdatePaletteEntryCommand(args.Token!, args.Field!, args.Value));
    }

    private static ToolResult RemovePaletteEntry(DiagramToolContext context, StyleArguments args) =>
        Missing(args.Token, "token", "令牌名") is { } error
            ? error
            : ActionDispatch.Execute(context, new RemovePaletteEntryCommand(args.Token!));

    private static ToolResult? Missing(string? value, string parameter, string what) =>
        string.IsNullOrWhiteSpace(value)
            ? ActionDispatch.Missing($"这个动作要给出{what}", parameter)
            : null;
}
