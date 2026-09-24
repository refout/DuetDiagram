using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands;

/// <summary>
/// 文本预设字段的读写：把一个字段的值读成文本，或者把一段文本写回字段。
/// </summary>
/// <remarks>
/// <para>
/// 与调色板条目同构：值的载体是字符串，白名单取自字段表里登记在预设名下的那几个
/// <c>preset.*</c> 子字段。拆成子字段而不是只留一份整样式，理由与样式那一组相同——
/// 界面上一个成员一个控件，冲突判定也要能认出"一边改字号、一边改字重"可以共存。
/// </para>
/// <para>
/// **白名单之外的成员一律拒绝，并把可用字段列在错误里。** 放行未知字段的话，
/// 渲染层拿到一个不认识的名字只会静默按默认画，调用方还以为改成了。
/// 预设的样式记录（<see cref="TextStyle"/>）成员比这里的白名单多：白名单只收
/// 节点属性面板上已经有编辑控件的那八个成员——能在一个成员上写到节点的东西，
/// 才轮得到预设替它记住。
/// </para>
/// <para>
/// 值的语法与节点文本字段（<see cref="NodeFieldValue"/>）完全一致，解析就是借它的手：
/// 数值按不变文化、枚举只认名字、开关只认 true 与 false，空文本表示"没有设置"。
/// 两处各写一套解析的话，同一段文本会在节点上能写、在预设上不能写（或反之）。
/// </para>
/// <para>
/// 预设的标识与名字不在这里。它们是预设的身份而不是它的样式，改身份由删除加新增表达。
/// </para>
/// </remarks>
public static class TextPresetFieldValue
{
    /// <summary>字段表里登记在预设名下、可以通过命令写入的字段。</summary>
    public static IReadOnlyList<string> Writable { get; } = BuildWritable();

    /// <summary>这个字段能不能写。元素级名字（带 <c>@</c> 前缀的）不算。</summary>
    public static bool IsWritable(string? field) =>
        field is not null && Writable.Contains(field, StringComparer.Ordinal);

    /// <summary>读一个字段的当前值，读成文本。</summary>
    public static string? Read(TextStylePreset preset, string field)
    {
        ArgumentNullException.ThrowIfNull(preset);

        return field switch
        {
            FieldNames.PresetFontFamily => preset.Style.FontFamily,
            FieldNames.PresetFontSize => Number(preset.Style.FontSize),
            FieldNames.PresetFontWeight => preset.Style.FontWeight?.ToString(),
            FieldNames.PresetItalic => Flag(preset.Style.Italic),
            FieldNames.PresetUnderline => Flag(preset.Style.Underline),
            FieldNames.PresetStrikethrough => Flag(preset.Style.Strikethrough),
            FieldNames.PresetFontColor => preset.Style.FontColor,
            FieldNames.PresetAlign => preset.Style.Align?.ToString(),
            _ => null,
        };
    }

    /// <summary>
    /// 把一段文本解析成字段的新值，写进一份新的预设。失败时是原预设，不改动。
    /// </summary>
    public static bool TryWrite(
        TextStylePreset preset,
        string field,
        string? value,
        out TextStylePreset updated,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(preset);

        updated = preset;
        error = null;

        if (!IsWritable(field))
        {
            error = $"字段 {field} 不在文本预设可写的字段表里，可用的有 {string.Join("、", Writable)}";
            return false;
        }

        // 空文本与全空白都当作"没有值"：写进去一个空串会让样式多出一个成员，
        // 而它表达的意思与"没设置"完全相同。
        var text = string.IsNullOrWhiteSpace(value) ? null : value;

        switch (field)
        {
            case FieldNames.PresetFontFamily:
                updated = preset with { Style = preset.Style with { FontFamily = text } };
                return true;

            case FieldNames.PresetFontColor:
                updated = preset with { Style = preset.Style with { FontColor = text } };
                return true;

            case FieldNames.PresetFontSize:
                if (!NodeFieldValue.TryNumber(text, out var fontSize, out error))
                {
                    return false;
                }

                if (fontSize is <= 0)
                {
                    error = $"字号要大于零，收到的是 {text}";
                    return false;
                }

                updated = preset with { Style = preset.Style with { FontSize = fontSize } };
                return true;

            case FieldNames.PresetFontWeight:
                if (!NodeFieldValue.TryEnum<FontWeight>(text, out var fontWeight, out error))
                {
                    return false;
                }

                updated = preset with { Style = preset.Style with { FontWeight = fontWeight } };
                return true;

            case FieldNames.PresetItalic:
                if (!NodeFieldValue.TryFlag(text, out var italic, out error))
                {
                    return false;
                }

                updated = preset with { Style = preset.Style with { Italic = italic } };
                return true;

            case FieldNames.PresetUnderline:
                if (!NodeFieldValue.TryFlag(text, out var underline, out error))
                {
                    return false;
                }

                updated = preset with { Style = preset.Style with { Underline = underline } };
                return true;

            case FieldNames.PresetStrikethrough:
                if (!NodeFieldValue.TryFlag(text, out var strikethrough, out error))
                {
                    return false;
                }

                updated = preset with { Style = preset.Style with { Strikethrough = strikethrough } };
                return true;

            case FieldNames.PresetAlign:
                if (!NodeFieldValue.TryEnum<TextAlign>(text, out var align, out error))
                {
                    return false;
                }

                updated = preset with { Style = preset.Style with { Align = align } };
                return true;

            default:
                // 走到这里说明字段表加了一个字段而这里没跟上。报"认不出"而不是静默通过。
                error = $"字段 {field} 已登记但还没有对应的读写实现";
                return false;
        }
    }

    private static IReadOnlyList<string> BuildWritable() =>
        [.. FieldRegistry.All
            .Where(f => string.Equals(f.Owner, "预设", StringComparison.Ordinal))
            .Select(f => f.Name)
            .Distinct(StringComparer.Ordinal)];

    private static string? Number(double? value) =>
        value?.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string? Flag(bool? value) =>
        value is null ? null : value.Value ? "true" : "false";
}
