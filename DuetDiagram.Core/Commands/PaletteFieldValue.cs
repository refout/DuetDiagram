using System.Globalization;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands;

/// <summary>
/// 调色板条目字段的读写：把一个字段的值读成文本，或者把一段文本写回字段。
/// </summary>
/// <remarks>
/// <para>
/// 与节点、边两个同构：值的载体是字符串，面板与命令读的是同一张字段表，
/// 所以在面板上加一个编辑器却忘了登记字段，会在这里被拒掉而不是静默失联。
/// </para>
/// <para>
/// 拆成子字段而不是只留一个整体条目，理由与样式那一组相同：界面上一个成员一个控件，
/// 而冲突判定要能认出"一边改填充、一边改描边"这两件事可以共存。
/// 只留整体条目的话，两次互不相干的修改会被判成冲突。
/// </para>
/// <para>
/// 令牌名（<see cref="PaletteEntry.Name"/>）不在这里。它是条目的标识而不是它的属性，
/// 改它等于换一个条目，由删除加新增表达。
/// </para>
/// </remarks>
public static class PaletteFieldValue
{
    /// <summary>字段表里登记在调色板名下、可以通过命令写入的字段。</summary>
    public static IReadOnlyList<string> Writable { get; } = BuildWritable();

    /// <summary>这个字段能不能写。元素级名字（带 <c>@</c> 前缀的）不算。</summary>
    public static bool IsWritable(string? field) =>
        field is not null && Writable.Contains(field, StringComparer.Ordinal);

    /// <summary>读一个字段的当前值，读成文本。</summary>
    public static string? Read(PaletteEntry entry, string field)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return field switch
        {
            FieldNames.PaletteFill => entry.Fill,
            FieldNames.PaletteStroke => entry.Stroke,
            FieldNames.PaletteText => entry.Text,

            // 数字用不变文化格式化。跟着当前区域走的话，同一次改动在不同机器上
            // 会写出不同的小数分隔符，而变更明细要能逐字节比较。
            FieldNames.PaletteWeight => entry.Weight?.ToString("0.###", CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    /// <summary>
    /// 把一段文本解析成字段的新值，写进一份新的条目。失败时是原条目，不改动。
    /// </summary>
    public static bool TryWrite(
        PaletteEntry entry,
        string field,
        string? value,
        out PaletteEntry updated,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(entry);

        updated = entry;
        error = null;

        if (!IsWritable(field))
        {
            error = $"字段 {field} 不在调色板可写的字段表里";
            return false;
        }

        // 空文本与全空白都当作"没有值"：写进去一个空串会让条目多出一个成员，
        // 而它表达的意思与"没设置"完全相同。
        var text = string.IsNullOrWhiteSpace(value) ? null : value;

        switch (field)
        {
            case FieldNames.PaletteFill:
                updated = entry with { Fill = text };
                return true;

            case FieldNames.PaletteStroke:
                updated = entry with { Stroke = text };
                return true;

            case FieldNames.PaletteText:
                updated = entry with { Text = text };
                return true;

            case FieldNames.PaletteWeight:
                if (text is null)
                {
                    updated = entry with { Weight = null };
                    return true;
                }

                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight)
                    || !double.IsFinite(weight)
                    || weight <= 0)
                {
                    error = $"描边粗细 {text}：必须是大于零的有限数";
                    return false;
                }

                updated = entry with { Weight = weight };
                return true;

            default:
                error = $"字段 {field} 已登记但还没有对应的读写实现";
                return false;
        }
    }

    private static IReadOnlyList<string> BuildWritable() =>
        [.. FieldRegistry.All
            .Where(f => string.Equals(f.Owner, "调色板", StringComparison.Ordinal))
            .Select(f => f.Name)
            .Distinct(StringComparer.Ordinal)];
}
