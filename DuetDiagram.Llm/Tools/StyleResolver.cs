using System.Globalization;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Llm.Tools;

/// <summary>
/// 把外观类动作的参数翻译成命令层要的东西。
/// </summary>
/// <remarks>
/// <para>
/// 翻译只做两件事：**查白名单**与**把文本解析成类型**。字段该不该写、值合不合法，
/// 到了命令层还会再判一次——那一次才是权威的。这里判一遍是为了让错误落在
/// 「你这次调用给错了参数」上，而不是等命令层回一句与调用方无关的话。
/// </para>
/// <para>
/// 调色板那一组走 <see cref="PaletteFieldValue"/>，不自己再写一份字段表：
/// 两处各写一份的话，加一个成员时只改一处，而漏改的那一处表现是「这个成员改不动」。
/// </para>
/// </remarks>
public static class StyleResolver
{
    /// <summary>样式成员的字段名前缀。</summary>
    public const string StylePrefix = "style.";

    /// <summary>文本成员的字段名前缀。</summary>
    public const string TextPrefix = "text.";

    /// <summary>画布设置里能改的成员，用来填「不认得这个成员」那条错误。</summary>
    public const string CanvasMembers =
        "canvas.grid、canvas.gridSize、canvas.pageSize、canvas.orientation、canvas.background、canvas.infinite";

    /// <summary>
    /// 这个样式令牌在文档的调色板里有没有。
    /// </summary>
    /// <remarks>
    /// 白名单是**运行时**判据：令牌表是文档里的数据，用户随时可以加一个，
    /// 所以它进不了 schema 里那份写死的约束。不在表里时把可用的列出来——
    /// 只回一句「没有这个令牌」的话，模型只能猜着重试，而每次重试都是一轮往返。
    /// </remarks>
    public static ToolResult? CheckToken(DiagramDocument document, string token)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        if (document.Palette.Find(token) is not null)
        {
            return null;
        }

        var available = document.Palette.Entries.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        return available.Length == 0
            ? ActionDispatch.Reject(
                $"调色板里还没有 {token} 这个令牌，而它现在是空的",
                "token",
                "先用 define-palette-entry 定义一个令牌")
            : ActionDispatch.Reject(
                $"调色板里没有 {token} 这个令牌",
                "token",
                $"可用令牌：{string.Join('、', available)}");
    }

    /// <summary>
    /// 把画布设置的一个成员解析成一条命令。
    /// </summary>
    /// <remarks>
    /// 一次只改一个成员：那条命令自己就是按成员发变更明细的，一次传多项会让
    /// 「哪一项改了」这件事在冲突判定里退化成一条粗粒度的记录。
    /// </remarks>
    public static (SetCanvasSettingsCommand? Command, ToolResult? Error) Canvas(string? field, string? value)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return (null, ActionDispatch.Missing("set-canvas 要给出要改的成员名", "field"));
        }

        return field switch
        {
            FieldNames.CanvasGrid => ParseEnum<GridStyle>(
                field, value, grid => new SetCanvasSettingsCommand(grid: grid)),

            FieldNames.CanvasOrientation => ParseEnum<CanvasOrientation>(
                field, value, orientation => new SetCanvasSettingsCommand(orientation: orientation)),

            FieldNames.CanvasGridSize => ParseNumber(
                field, value, size => new SetCanvasSettingsCommand(gridSize: size)),

            FieldNames.CanvasInfinite => ParseFlag(
                field, value, infinite => new SetCanvasSettingsCommand(infinite: infinite)),

            // 背景色用空串表示清除，与那条命令自己的口径一致：空引用占的是"这一项不动"。
            FieldNames.CanvasBackground => value is null
                ? (null, ActionDispatch.Missing($"{field} 要给出值", "value"))
                : (new SetCanvasSettingsCommand(background: value), null),

            FieldNames.CanvasPageSize => ParsePageSize(value),

            _ => (null, ActionDispatch.Reject(
                $"{field} 不是画布设置里能改的成员",
                "field",
                $"可用成员：{CanvasMembers}")),
        };
    }

    /// <summary>
    /// 造一个调色板条目。
    /// </summary>
    /// <remarks>
    /// 条目名是必需的；成员可以顺带给一个。不在这里查重名——那是命令层的判据，
    /// 它报 <c>DUPLICATE_ID</c> 而不是覆盖，理由是不覆盖一个可能正被几百个节点引用的令牌。
    /// </remarks>
    public static (PaletteEntry? Entry, ToolResult? Error) PaletteEntry(string token, string? field, string? value)
    {
        var entry = new PaletteEntry { Name = token };

        if (string.IsNullOrWhiteSpace(field))
        {
            return (entry, null);
        }

        if (!PaletteFieldValue.IsWritable(field))
        {
            return (null, ActionDispatch.Reject(
                $"{field} 不是调色板能写的成员",
                "field",
                $"可用成员：{string.Join('、', PaletteFieldValue.Writable)}"));
        }

        return PaletteFieldValue.TryWrite(entry, field, value, out var updated, out var error)
            ? (updated, null)
            : (null, ActionDispatch.Reject(error ?? $"{field} 的值不合法", "value"));
    }

    /// <summary>字段名前缀对不对。两个动作各自唯一的那点语义就靠它。</summary>
    public static bool HasPrefix(string? field, string prefix) =>
        field is not null && field.StartsWith(prefix, StringComparison.Ordinal);

    /// <summary>
    /// 枚举值。
    /// </summary>
    /// <remarks>
    /// 数字形式也认，但落在定义之外时不能悄悄当成某个取值——那会让一次参数错误表现成
    /// 「改了但改错了」。所以解析之后再查一次是否在定义内。
    /// </remarks>
    private static (SetCanvasSettingsCommand?, ToolResult?) ParseEnum<T>(
        string field,
        string? value,
        Func<T, SetCanvasSettingsCommand> build)
        where T : struct, Enum
    {
        if (value is null)
        {
            return (null, ActionDispatch.Missing($"{field} 要给出值", "value"));
        }

        return Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? (build(parsed), null)
            : (null, ActionDispatch.Reject(
                $"{field} 收到的是 {value}，不是它认得的取值",
                "value",
                $"可用取值：{string.Join('、', Enum.GetNames<T>())}"));
    }

    private static (SetCanvasSettingsCommand?, ToolResult?) ParseNumber(
        string field,
        string? value,
        Func<double, SetCanvasSettingsCommand> build)
    {
        if (value is null)
        {
            return (null, ActionDispatch.Missing($"{field} 要给出值", "value"));
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number)
            ? (build(number), null)
            : (null, ActionDispatch.Reject($"{field} 要是一个有限的数，收到的是 {value}", "value"));
    }

    private static (SetCanvasSettingsCommand?, ToolResult?) ParseFlag(
        string field,
        string? value,
        Func<bool, SetCanvasSettingsCommand> build)
    {
        if (value is null)
        {
            return (null, ActionDispatch.Missing($"{field} 要给出值", "value"));
        }

        return bool.TryParse(value, out var flag)
            ? (build(flag), null)
            : (null, ActionDispatch.Reject($"{field} 要是 true 或 false，收到的是 {value}", "value"));
    }

    /// <summary>纸张尺寸的写法是「宽x高」，两个都必须是大于零的有限数。</summary>
    private static (SetCanvasSettingsCommand?, ToolResult?) ParsePageSize(string? value)
    {
        var parts = value?.Split('x', StringSplitOptions.TrimEntries) ?? [];

        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var height)
            && double.IsFinite(width) && width > 0
            && double.IsFinite(height) && height > 0)
        {
            return (new SetCanvasSettingsCommand(pageSize: new Size(width, height)), null);
        }

        return (null, ActionDispatch.Reject(
            $"纸张尺寸要写成「宽x高」，收到的是 {value}",
            "value",
            "例如 1123x794"));
    }
}
