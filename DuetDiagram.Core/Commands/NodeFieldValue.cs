using System.Globalization;
using System.Text.Json;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Core.Shapes;

namespace DuetDiagram.Core.Commands;

/// <summary>
/// 节点字段的读写：把一个字段的值读成文本，或者把一段文本写回字段。
/// </summary>
/// <remarks>
/// <para>
/// 值的载体是**字符串**而不是一个联合类型。理由有两条。一是协议层本来就是文本：
/// 外部代理送进来的 JSON 里，字段值就是一段字符串，到了这里再包一层联合类型，
/// 只是把解析挪了个地方。二是 AOT 下联合类型要靠多态注册，而注册漏一个的后果
/// 是发布之后才暴露——这里能避开的注册就不引入。
/// </para>
/// <para>
/// 复合字段分两种处理。样式与文本按成员拆成子字段，因为界面上一个成员一个控件，
/// 而冲突判定也要能认出"一边改填充、一边改描边"可以共存。剩下的复合字段
/// （端口、宿主数据）用 JSON 文本表示——它们没有逐成员的编辑界面，
/// 拆成子字段只是让字段表多出一批没人用的名字。
/// </para>
/// <para>
/// 可写的字段就是字段表里登记在节点名下的那些。**面板与命令读的是同一张表**，
/// 所以在面板上加一个编辑器却忘了登记字段，会在这里被拒掉而不是静默失联。
/// </para>
/// </remarks>
public static class NodeFieldValue
{
    /// <summary>字段表里登记在节点名下、可以通过命令写入的字段。</summary>
    public static IReadOnlyList<string> Writable { get; } = BuildWritable();

    /// <summary>这个字段能不能写。元素级名字（带 <c>@</c> 前缀的）不算。</summary>
    public static bool IsWritable(string? field) =>
        field is not null && Writable.Contains(field, StringComparer.Ordinal);

    /// <summary>
    /// 读一个字段的当前值，读成文本。
    /// </summary>
    /// <remarks>
    /// 复合字段读出来是 JSON 文本，与写入时接受的格式是同一套——
    /// 面板的"改回原值"与命令的往返测试都靠这个对称性。
    /// 字段不可写时返回空，调用方据此当"没有这个字段"处理。
    /// </remarks>
    public static string? Read(NodeDef node, string field)
    {
        ArgumentNullException.ThrowIfNull(node);

        return field switch
        {
            FieldNames.Label => node.Label,
            FieldNames.Shape => node.Shape.ToString(),
            FieldNames.ShapePath => node.ShapePath,
            FieldNames.Parent => node.Parent,
            FieldNames.Layer => node.Layer,
            FieldNames.Page => node.Page,
            FieldNames.StyleToken => node.StyleToken,
            FieldNames.Style => node.Style is null
                ? null
                : JsonSerializer.Serialize(node.Style, DiagramJsonContext.Default.NodeStyle),
            FieldNames.Text => node.Text is null
                ? null
                : JsonSerializer.Serialize(node.Text, DiagramJsonContext.Default.TextStyle),
            FieldNames.Ports => JsonSerializer.Serialize(node.Ports.ToArray(), DiagramJsonContext.Default.PortDefArray),
            FieldNames.RichText => node.RichText ? "true" : "false",
            FieldNames.MathMode => node.MathMode.ToString(),
            FieldNames.Desc => node.Desc,
            FieldNames.Meta => JsonSerializer.Serialize(
                new Dictionary<string, string>(node.Meta, StringComparer.Ordinal),
                DiagramJsonContext.Default.DictionaryStringString),

            FieldNames.StyleFill => node.Style?.Fill,
            FieldNames.StyleStroke => node.Style?.Stroke,
            FieldNames.StyleWeight => Number(node.Style?.Weight),
            FieldNames.StyleBorder => node.Style?.Border?.ToString(),
            FieldNames.StyleRadius => Number(node.Style?.Radius),
            FieldNames.StyleOpacity => Number(node.Style?.Opacity),
            FieldNames.StyleBadge => node.Style?.Badge,

            FieldNames.TextFontFamily => node.Text?.FontFamily,
            FieldNames.TextFontSize => Number(node.Text?.FontSize),
            FieldNames.TextFontWeight => node.Text?.FontWeight?.ToString(),
            FieldNames.TextItalic => Flag(node.Text?.Italic),
            FieldNames.TextUnderline => Flag(node.Text?.Underline),
            FieldNames.TextStrikethrough => Flag(node.Text?.Strikethrough),
            FieldNames.TextFontColor => node.Text?.FontColor,
            FieldNames.TextAlign => node.Text?.Align?.ToString(),

            _ => null,
        };
    }

    /// <summary>
    /// 把一段文本解析成字段的新值，写进一份新的节点定义。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 返回新实例而不是就地改：节点定义是不可变的记录，而"改到一半失败"要能整体回退。
    /// 命令的原子性正是靠这一点——解析失败时调用方手里的那份定义一个字节都没动。
    /// </para>
    /// <para>
    /// 空文本一律读成"这个字段没有值"，而不是空字符串。文本字段里两者在渲染上没差别，
    /// 但在哈希与序列化上不一样：写进去一个空串会让文档多出一个字段，
    /// 而它表达的意思与"没设置"完全相同。
    /// </para>
    /// </remarks>
    /// <param name="node">要改的节点。</param>
    /// <param name="field">字段名，取自字段表。</param>
    /// <param name="value">字段的新值。复合字段是 JSON 文本。</param>
    /// <param name="updated">改好之后的节点。失败时是原节点。</param>
    /// <param name="error">失败原因。成功时为空。</param>
    public static bool TryWrite(
        NodeDef node,
        string field,
        string? value,
        out NodeDef updated,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(node);

        updated = node;
        error = null;

        if (!IsWritable(field))
        {
            error = $"字段 {field} 不在节点可写的字段表里";
            return false;
        }

        // 空文本与全空白都当作"没有值"。空白串在界面上就是用户把内容删光了。
        var text = string.IsNullOrWhiteSpace(value) ? null : value;

        switch (field)
        {
            case FieldNames.Label:
                updated = node with { Label = text ?? string.Empty };
                return true;

            case FieldNames.Parent:
                updated = node with { Parent = text };
                return true;

            case FieldNames.Layer:
                updated = node with { Layer = text };
                return true;

            case FieldNames.Page:
                updated = node with { Page = text };
                return true;

            case FieldNames.StyleToken:
                updated = node with { StyleToken = text };
                return true;

            case FieldNames.Desc:
                updated = node with { Desc = text };
                return true;

            case FieldNames.Shape:
                if (!TryEnum<NodeShape>(text, out var shape, out error) || shape is null)
                {
                    error ??= "形状不能为空";
                    return false;
                }

                updated = node with { Shape = shape.Value };
                return true;

            case FieldNames.ShapePath:
                // 路径在写进来的这一刻就解析一遍。放到渲染时才解析的话，
                // 一份写坏的路径会一路走到画布上才炸，而那时已经分不清是哪一步写进去的。
                if (text is not null && !PathParser.TryParse(text, out _, out var pathError))
                {
                    error = pathError!.Message;
                    return false;
                }

                updated = node with { ShapePath = text };
                return true;

            case FieldNames.MathMode:
                if (!TryEnum<MathMode>(text, out var mathMode, out error) || mathMode is null)
                {
                    error ??= "数学排版模式不能为空";
                    return false;
                }

                updated = node with { MathMode = mathMode.Value };
                return true;

            case FieldNames.RichText:
                // 这是一个必填的开关，没有"未设置"这一档，所以空文本读成"关"。
                var richText = false;

                if (text is not null && !bool.TryParse(text, out richText))
                {
                    error = $"布尔字段要 true 或 false，收到的是 {value}";
                    return false;
                }

                updated = node with { RichText = richText };
                return true;

            case FieldNames.Style:
                if (!TryParseJson(text, DiagramJsonContext.Default.NodeStyle, out NodeStyle? style, out error))
                {
                    return false;
                }

                updated = node with { Style = Trim(style) };
                return true;

            case FieldNames.Text:
                if (!TryParseJson(text, DiagramJsonContext.Default.TextStyle, out TextStyle? textStyle, out error))
                {
                    return false;
                }

                updated = node with { Text = Trim(textStyle) };
                return true;

            case FieldNames.Ports:
                if (!TryParseJson(text, DiagramJsonContext.Default.PortDefArray, out PortDef[]? ports, out error))
                {
                    return false;
                }

                updated = node with { Ports = ports ?? [] };
                return true;

            case FieldNames.Meta:
                if (!TryParseJson(
                        text,
                        DiagramJsonContext.Default.DictionaryStringString,
                        out Dictionary<string, string>? meta,
                        out error))
                {
                    return false;
                }

                updated = node with { Meta = meta ?? new Dictionary<string, string>(StringComparer.Ordinal) };
                return true;

            case FieldNames.StyleFill:
                updated = node with { Style = Trim((node.Style ?? new NodeStyle()) with { Fill = text }) };
                return true;

            case FieldNames.StyleStroke:
                updated = node with { Style = Trim((node.Style ?? new NodeStyle()) with { Stroke = text }) };
                return true;

            case FieldNames.StyleBadge:
                updated = node with { Style = Trim((node.Style ?? new NodeStyle()) with { Badge = text }) };
                return true;

            case FieldNames.StyleWeight:
                if (!TryNumber(text, out var weight, out error))
                {
                    return false;
                }

                updated = node with { Style = Trim((node.Style ?? new NodeStyle()) with { Weight = weight }) };
                return true;

            case FieldNames.StyleRadius:
                if (!TryNumber(text, out var radius, out error))
                {
                    return false;
                }

                updated = node with { Style = Trim((node.Style ?? new NodeStyle()) with { Radius = radius }) };
                return true;

            case FieldNames.StyleOpacity:
                if (!TryNumber(text, out var opacity, out error))
                {
                    return false;
                }

                // 越界的值在这里就挡掉。放过去的话，界面框架在画的时候才报错，
                // 而那已经是另一帧的事，界面上只会看到"图忽然不见了"。
                if (opacity is < 0 or > 1)
                {
                    error = $"不透明度取值在 0 到 1 之间，收到的是 {text}";
                    return false;
                }

                updated = node with { Style = Trim((node.Style ?? new NodeStyle()) with { Opacity = opacity }) };
                return true;

            case FieldNames.StyleBorder:
                if (!TryEnum<LineStyle>(text, out var border, out error))
                {
                    return false;
                }

                updated = node with { Style = Trim((node.Style ?? new NodeStyle()) with { Border = border }) };
                return true;

            case FieldNames.TextFontFamily:
                updated = node with { Text = Trim((node.Text ?? new TextStyle()) with { FontFamily = text }) };
                return true;

            case FieldNames.TextFontColor:
                updated = node with { Text = Trim((node.Text ?? new TextStyle()) with { FontColor = text }) };
                return true;

            case FieldNames.TextFontSize:
                if (!TryNumber(text, out var fontSize, out error))
                {
                    return false;
                }

                if (fontSize is <= 0)
                {
                    error = $"字号要大于零，收到的是 {text}";
                    return false;
                }

                updated = node with { Text = Trim((node.Text ?? new TextStyle()) with { FontSize = fontSize }) };
                return true;

            case FieldNames.TextFontWeight:
                if (!TryEnum<FontWeight>(text, out var fontWeight, out error))
                {
                    return false;
                }

                updated = node with { Text = Trim((node.Text ?? new TextStyle()) with { FontWeight = fontWeight }) };
                return true;

            case FieldNames.TextAlign:
                if (!TryEnum<TextAlign>(text, out var align, out error))
                {
                    return false;
                }

                updated = node with { Text = Trim((node.Text ?? new TextStyle()) with { Align = align }) };
                return true;

            case FieldNames.TextItalic:
                if (!TryFlag(text, out var italic, out error))
                {
                    return false;
                }

                updated = node with { Text = Trim((node.Text ?? new TextStyle()) with { Italic = italic }) };
                return true;

            case FieldNames.TextUnderline:
                if (!TryFlag(text, out var underline, out error))
                {
                    return false;
                }

                updated = node with { Text = Trim((node.Text ?? new TextStyle()) with { Underline = underline }) };
                return true;

            case FieldNames.TextStrikethrough:
                if (!TryFlag(text, out var strikethrough, out error))
                {
                    return false;
                }

                updated = node with { Text = Trim((node.Text ?? new TextStyle()) with { Strikethrough = strikethrough }) };
                return true;

            default:
                // 走到这里说明字段表加了一个字段而这里没跟上。报"认不出"而不是静默通过：
                // 静默通过会让面板上那个编辑器永远改不动文档，而界面上看不出任何异常。
                error = $"字段 {field} 已登记但还没有对应的读写实现";
                return false;
        }
    }

    private static IReadOnlyList<string> BuildWritable() =>
        [.. FieldRegistry.All
            .Where(f => string.Equals(f.Owner, "节点", StringComparison.Ordinal))
            .Select(f => f.Name)
            .Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// 解析枚举字段。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 只认名字，不认数字。枚举的解析器接受数字文本，而数字在协议里是歧义的：
    /// <c>"7"</c> 今天是某个形状，枚举成员一增一减就变成另一个形状，或者干脆越界——
    /// 越界之后渲染时分发不到任何分支，表现是节点忽然什么都不画，看起来像绘制列表坏了。
    /// 名字在成员重排之后仍然指向同一个形状。
    /// </para>
    /// <para>
    /// 大小写不敏感。字段值是人手敲进面板的，为一次大小写差异报错没有意义。
    /// 空文本表示"这个成员没有值"，与文本字段同一口径。
    /// </para>
    /// <para>
    /// 对同程序集开放而不只留给本类：文本预设的字段读写（<see cref="TextPresetFieldValue"/>）
    /// 解析的是同一批 TextStyle 成员，值语法必须与这里逐字一致——各写一份迟早对出两套。
    /// </para>
    /// </remarks>
    internal static bool TryEnum<TEnum>(string? text, out TEnum? parsed, out string? error)
        where TEnum : struct, Enum
    {
        parsed = null;

        if (text is null)
        {
            error = null;
            return true;
        }

        if (!long.TryParse(text, out _) && Enum.TryParse<TEnum>(text, ignoreCase: true, out var value))
        {
            parsed = value;
            error = null;
            return true;
        }

        error = $"{typeof(TEnum).Name} 没有取值 {text}，可选的有 {string.Join(" / ", Enum.GetNames<TEnum>())}";
        return false;
    }

    /// <summary>解析一个可空的开关。空文本表示"没有设置"，与"设成假"是两回事。</summary>
    /// <remarks>开放给 <see cref="TextPresetFieldValue"/>，理由与 <see cref="TryEnum{TEnum}"/> 相同。</remarks>
    internal static bool TryFlag(string? text, out bool? parsed, out string? error)
    {
        parsed = null;

        if (text is null)
        {
            error = null;
            return true;
        }

        if (bool.TryParse(text, out var value))
        {
            parsed = value;
            error = null;
            return true;
        }

        error = $"开关字段要 true 或 false，收到的是 {text}";
        return false;
    }

    /// <summary>
    /// 解析一个可空的数值。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 按不变文化解析。跟随当前区域设置的话，同一段文本在不同机器上会解析成不同的数，
    /// 而文档是会被共享的——"1,5"在一台机器上是 1.5，在另一台上是 15。
    /// </para>
    /// <para>
    /// 开放给 <see cref="TextPresetFieldValue"/>，理由与 <see cref="TryEnum{TEnum}"/> 相同。
    /// </para>
    /// </remarks>
    internal static bool TryNumber(string? text, out double? parsed, out string? error)
    {
        parsed = null;

        if (text is null)
        {
            error = null;
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value))
        {
            parsed = value;
            error = null;
            return true;
        }

        error = $"数值字段要一个数，收到的是 {text}";
        return false;
    }

    private static bool TryParseJson<T>(
        string? text,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        out T? value,
        out string? error)
    {
        if (text is null)
        {
            // 空文本对复合字段表达的是"清空"，与文本字段同一口径。
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

    /// <summary>
    /// 成员全空的样式折成"没有样式"。
    /// </summary>
    /// <remarks>
    /// 不折的话，把最后一个成员清掉会留下一份成员全空的记录，而它与"没有样式"
    /// 序列化出来不一样（一个是 <c>{}</c>，一个是缺这个字段），于是视觉哈希也不同。
    /// 表现是清掉填充色之后图看起来没变、哈希却说变了，增量同步因此白跑一趟。
    /// </remarks>
    private static NodeStyle? Trim(NodeStyle? style) =>
        style is null
        || (style.Fill is null
            && style.Stroke is null
            && style.Text is null
            && style.Border is null
            && style.Weight is null
            && style.Radius is null
            && style.Opacity is null
            && style.Badge is null)
            ? null
            : style;

    /// <inheritdoc cref="Trim(NodeStyle?)"/>
    /// <remarks>对同程序集开放：应用预设把成员抄进节点的文本样式时，要用同一个口径折空样式。</remarks>
    internal static TextStyle? Trim(TextStyle? style) =>
        style is null
        || (style.FontFamily is null
            && style.FontSize is null
            && style.FontWeight is null
            && style.Italic is null
            && style.Underline is null
            && style.Strikethrough is null
            && style.FontColor is null
            && style.TextBackgroundColor is null
            && style.Align is null
            && style.VerticalAlign is null
            && style.WordWrap is null
            && style.LineHeight is null
            && style.TextIndent is null
            && style.Padding is null
            && style.LabelPosition is null
            && style.WritingDirection is null)
            ? null
            : style;

    private static string? Number(double? value) =>
        value?.ToString(CultureInfo.InvariantCulture);

    private static string? Flag(bool? value) =>
        value is null ? null : value.Value ? "true" : "false";
}
