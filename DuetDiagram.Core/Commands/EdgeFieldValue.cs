using System.Globalization;
using System.Text.Json;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;

namespace DuetDiagram.Core.Commands;

/// <summary>
/// 边字段的读写：把一个字段的值读成文本，或者把一段文本写回字段。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="NodeFieldValue"/> 同构：值的载体是字符串，复合字段（样式）是 JSON 文本。
/// 面板与命令读的是同一张字段表，所以在面板上加一个编辑器却忘了登记字段，
/// 会在这里被拒掉而不是静默失联。
/// </para>
/// <para>
/// 当前可写的边字段是标签与样式。端点（from/to/端口）不在这里——它们由重连命令整体改，
/// 不按字段逐条写，因为四个字段要作为一次操作一起生效，拆成字段改写会破坏原子性。
/// </para>
/// </remarks>
public static class EdgeFieldValue
{
    /// <summary>字段表里登记在边名下、可以通过命令写入的字段。</summary>
    public static IReadOnlyList<string> Writable { get; } = BuildWritable();

    /// <summary>这个字段能不能写。元素级名字（带 <c>@</c> 前缀的）不算。</summary>
    public static bool IsWritable(string? field) =>
        field is not null && Writable.Contains(field, StringComparer.Ordinal);

    /// <summary>读一个字段的当前值，读成文本。</summary>
    public static string? Read(EdgeDef edge, string field)
    {
        ArgumentNullException.ThrowIfNull(edge);

        return field switch
        {
            FieldNames.Label => edge.Label,
            FieldNames.Style => edge.Style is null
                ? null
                : JsonSerializer.Serialize(edge.Style, DiagramJsonContext.Default.EdgeStyle),
            _ => null,
        };
    }

    /// <summary>
    /// 把一段文本解析成字段的新值，写进一份新的边定义。失败时是原边，不改动。
    /// </summary>
    public static bool TryWrite(
        EdgeDef edge,
        string field,
        string? value,
        out EdgeDef updated,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(edge);

        updated = edge;
        error = null;

        if (!IsWritable(field))
        {
            error = $"字段 {field} 不在边可写的字段表里";
            return false;
        }

        // 空文本与全空白都当作"没有值"：写进去一个空串会让文档多出一个字段，
        // 而它表达的意思与"没设置"完全相同。
        var text = string.IsNullOrWhiteSpace(value) ? null : value;

        switch (field)
        {
            case FieldNames.Label:
                updated = edge with { Label = text ?? string.Empty };
                return true;

            case FieldNames.Style:
                if (!TryParseJson(text, DiagramJsonContext.Default.EdgeStyle, out EdgeStyle? style, out error))
                {
                    return false;
                }

                // EdgeDef.Style 不可空：清空表达成"默认样式"，而不是空引用。
                updated = edge with { Style = Trim(style) ?? new EdgeStyle() };
                return true;

            default:
                error = $"字段 {field} 已登记但还没有对应的读写实现";
                return false;
        }
    }

    private static IReadOnlyList<string> BuildWritable() =>
        [.. FieldRegistry.All
            .Where(f => string.Equals(f.Owner, "边", StringComparison.Ordinal))
            .Select(f => f.Name)
            .Distinct(StringComparer.Ordinal)];

    private static bool TryParseJson<T>(
        string? text,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        out T? value,
        out string? error)
    {
        if (text is null)
        {
            value = default;
            error = null;
            return true;
        }

        try
        {
            value = JsonSerializer.Deserialize(text, typeInfo);
        }
        catch (JsonException exception)
        {
            value = default;
            error = $"值不是合法的 JSON：{exception.Message}";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>成员全空的样式折成"没有样式"，避免空记录让视觉哈希无谓地变。</summary>
    private static EdgeStyle? Trim(EdgeStyle? style) =>
        style is null
        || (style.Line is null
            && style.Arrow is null
            && style.StyleToken is null)
            ? null
            : style;
}
