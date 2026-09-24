using System.Text.Json;
using System.Text.Json.Serialization;
using DuetDiagram.Core.Serialization;

namespace DuetDiagram.Core.Model;

/// <summary>
/// 行内样式的成员名。
/// </summary>
/// <remarks>
/// <para>
/// 名字写成常量集中在一处，与字段表同一套做法。散在各处写裸字符串的代价已经验证过一次：
/// 错误码曾经在两处各写一份，名字不一致而没有任何东西会报错。
/// </para>
/// <para>
/// **认不出的名字要拒绝，不能放行。** 放行的话，渲染层拿到一个不认识的名字只会按默认画，
/// 而写的人以为自己把样式设上了。这与样式成员的白名单是同一条口径。
/// </para>
/// </remarks>
public static class RichStyleFields
{
    public const string Bold = "bold";
    public const string Italic = "italic";
    public const string Underline = "underline";
    public const string Strikethrough = "strikethrough";
    public const string FontSize = "fontSize";
    public const string Color = "color";

    /// <summary>全部成员，按界面上样式按钮的先后次序。</summary>
    public static IReadOnlyList<string> All { get; } =
        [Bold, Italic, Underline, Strikethrough, FontSize, Color];

    /// <summary>
    /// 这个名字认不认得。
    /// </summary>
    /// <remarks>
    /// 大小写敏感：这些名字是写进文件里的键，与序列化出来的键逐字对应。
    /// 不敏感的话，<c>Bold</c> 会被当成认得，而序列化时它又变回 <c>bold</c>——
    /// 于是同一份内容在往返之后换了一个写法，哈希跟着变。
    /// </remarks>
    public static bool IsKnown(string? name) =>
        name is not null && All.Contains(name, StringComparer.Ordinal);
}

/// <summary>
/// 一段行内文字的样式。
/// </summary>
/// <remarks>
/// <para>
/// **只有这六项。** 它们是界面上按选区套样式时真有的那六个动作。段落级的对齐与列表
/// 不在这里——那属于段落，不属于行内；两者混在一起会让"这一段整体居中"与
/// "这几个字加粗"落到同一个记录上，而它们的作用范围本来就不一样。
/// </para>
/// <para>
/// 每一项都可空，与节点的文本样式同一个理由：样式逐层继承，做成必填会迫使每一段都写全，
/// 而改一处就要改遍所有段落。
/// </para>
/// <para>
/// **认不出的成员会被序列化层直接拒绝**，不是静默忽略。忽略的话，一份写错了键名的文件
/// 会被照常读进来，而那些样式一个都没生效，读的人却以为设上了。
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RichRunStyle
{
    public bool? Bold { get; init; }

    public bool? Italic { get; init; }

    public bool? Underline { get; init; }

    public bool? Strikethrough { get; init; }

    /// <summary>字号。与节点文本样式里的那一项同一口径，单位是排版用的那一种。</summary>
    public double? FontSize { get; init; }

    /// <summary>文字颜色。取调色板令牌名，也可以写颜色值。</summary>
    public string? Color { get; init; }

    /// <summary>六项都没设。折空样式时用它，避免留下一份"成员全空"的记录。</summary>
    /// <remarks>
    /// 算出来的成员一律标上"不进序列化"。不标的话它会被写进文件，
    /// 而读回来时那个键没有任何成员接得住——一份自己写出去的内容自己读不回来。
    /// </remarks>
    [JsonIgnore]
    public bool IsEmpty =>
        Bold is null
        && Italic is null
        && Underline is null
        && Strikethrough is null
        && FontSize is null
        && Color is null;
}

/// <summary>
/// 一段行内文字。
/// </summary>
/// <remarks>
/// 段落里的文字被切成若干段，每段一套样式。**层级只有两层：段落与行内。**
/// 列表、表格、嵌套这些一旦进来，排版与导出都要跟着长，而判据只要求富文本正确。
/// </remarks>
public sealed record RichRun
{
    /// <summary>这一段文字。</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>这一段的样式。为空表示沿用所在段落的文本样式。</summary>
    public RichRunStyle? Style { get; init; }
}

/// <summary>
/// 一个段落。
/// </summary>
/// <remarks>
/// 段落的边界就是换行：段落之间在画面上各占一行，段内按宽度折行。
/// 这与节点文本样式里的对齐项配套——对齐是按段落生效的，行内样式管不到它。
/// </remarks>
public sealed record RichParagraph
{
    /// <summary>段落里的行内片段，按先后次序。</summary>
    public IReadOnlyList<RichRun> Runs { get; init; } = [];

    /// <summary>这一段的对齐。为空表示沿用节点的文本样式。</summary>
    public TextAlign? Align { get; init; }

    /// <summary>这一段的纯文本，把片段按次序接起来。</summary>
    [JsonIgnore]
    public string PlainText => string.Concat(Runs.Select(run => run.Text));

    /// <summary>结构化相等。必须重写：<see cref="Runs"/> 是集合。</summary>
    public bool Equals(RichParagraph? other) =>
        other is not null
        && CollectionEquality.List(Runs, other.Runs)
        && Align == other.Align;

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(CollectionEquality.ListHash(Runs));
        hash.Add(Align);

        return hash.ToHashCode();
    }
}

/// <summary>
/// 富文本内容。
/// </summary>
/// <remarks>
/// <para>
/// 它挂在节点上，与纯文本标签的关系见 <see cref="RichLabelRules"/>：
/// 内容在的时候内容权威，标签是它的纯文本投影，两者必须逐字相同。
/// </para>
/// <para>
/// **它进视觉哈希，不进结构哈希。** 改分段与行内样式只改变画出来的样子，
/// 不改节点的坐标。但它与文本样式一样会改变文字的实际宽度——那一点由布局层
/// 对文本变化的既有处置负责，不在这里替它做决定。
/// </para>
/// </remarks>
public sealed record RichTextContent
{
    /// <summary>段落，按先后次序。</summary>
    public IReadOnlyList<RichParagraph> Paragraphs { get; init; } = [];

    /// <summary>整份内容的纯文本：段落之间用换行连接。</summary>
    /// <remarks>
    /// 换行写死 <c>\n</c>，不用运行平台的换行符：这份投影要与标签逐字比较、
    /// 还要进摘要与哈希，换个平台就变的东西没法用。
    /// </remarks>
    [JsonIgnore]
    public string PlainText => string.Join('\n', Paragraphs.Select(paragraph => paragraph.PlainText));

    /// <summary>一个字都没有。折空内容时用它。</summary>
    [JsonIgnore]
    public bool IsEmpty => PlainText.Length == 0;

    /// <summary>
    /// 折成规范形态：丢掉空片段、合并相邻的同样式片段、把成员全空的样式折掉。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 不折的话，同一段文字写成"一段"与写成"三段"会序列化成两份不同的内容，
    /// 而两者画出来一模一样——视觉哈希会把一次没有视觉效果的改动报成"要重绘"。
    /// </para>
    /// <para>
    /// 只合并不改动：片段的先后次序与段落的边界原样保留。
    /// </para>
    /// </remarks>
    public RichTextContent Normalize()
    {
        var paragraphs = new List<RichParagraph>(Paragraphs.Count);

        foreach (var paragraph in Paragraphs)
        {
            var runs = new List<RichRun>(paragraph.Runs.Count);

            foreach (var run in paragraph.Runs)
            {
                // 空片段不承载任何东西：留着只会让"同一段文字"多出几种写法。
                if (run.Text.Length == 0)
                {
                    continue;
                }

                var style = run.Style is { IsEmpty: true } ? null : run.Style;
                var last = runs.Count > 0 ? runs[^1] : null;

                if (last is not null && Equals(last.Style, style))
                {
                    runs[^1] = last with { Text = last.Text + run.Text };
                    continue;
                }

                runs.Add(run with { Style = style });
            }

            paragraphs.Add(paragraph with { Runs = runs });
        }

        return this with { Paragraphs = paragraphs };
    }

    /// <summary>
    /// 把一段文本解析成富文本内容。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 空文本读成"没有内容"，与别的复合字段同一口径：空串表达的是"这个字段没有值"，
    /// 而不是"一份空内容"。两者在渲染上没差别，在哈希与序列化上不一样。
    /// </para>
    /// <para>
    /// 行内样式里认不出的名字在这一步就被挡掉，且**在反序列化之前**挡：
    /// 反序列化只在错误里说一句"有个成员映射不上"，而写的人需要知道是哪一个名字。
    /// 序列化层另有一道同样的拒绝，挡住的是直接读文件那条路。
    /// </para>
    /// </remarks>
    /// <param name="json">内容的 JSON 文本。</param>
    /// <param name="content">解析出来的内容。空文本时为空。</param>
    /// <param name="error">失败原因。成功时为空。</param>
    public static bool TryParse(string? json, out RichTextContent? content, out string? error)
    {
        content = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            using var parsed = JsonDocument.Parse(json);

            if (UnknownStyleField(parsed.RootElement) is { } unknown)
            {
                error = $"行内样式里没有 {unknown} 这一项，认得的只有 {string.Join(" / ", RichStyleFields.All)}";
                return false;
            }

            content = JsonSerializer.Deserialize(json, DiagramJsonContext.Default.RichTextContent);
        }
        catch (JsonException exception)
        {
            error = $"富文本内容的写法不对：{exception.Message}";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 走一遍原始 JSON，找出第一个认不出的行内样式成员名。
    /// </summary>
    /// <remarks>
    /// 形状对不上的地方一律跳过，交给反序列化去报：这一趟只回答"有没有认不出的样式名"，
    /// 顺手把结构也判一遍的话，两处报出来的错误会互相盖住。
    /// </remarks>
    private static string? UnknownStyleField(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("paragraphs", out var paragraphs)
            || paragraphs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var paragraph in paragraphs.EnumerateArray())
        {
            if (paragraph.ValueKind != JsonValueKind.Object
                || !paragraph.TryGetProperty("runs", out var runs)
                || runs.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var run in runs.EnumerateArray())
            {
                if (run.ValueKind != JsonValueKind.Object
                    || !run.TryGetProperty("style", out var style)
                    || style.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var member in style.EnumerateObject())
                {
                    if (!RichStyleFields.IsKnown(member.Name))
                    {
                        return member.Name;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>结构化相等。必须重写：<see cref="Paragraphs"/> 是集合。</summary>
    public bool Equals(RichTextContent? other) =>
        other is not null && CollectionEquality.List(Paragraphs, other.Paragraphs);

    public override int GetHashCode() => CollectionEquality.ListHash(Paragraphs);
}

/// <summary>
/// 纯文本标签与富文本内容之间的关系。
/// </summary>
/// <remarks>
/// <para>
/// **同一段文字只有一份是权威的。** 这里定的是：内容在的时候内容权威，
/// <see cref="NodeDef.Label"/> 是它的纯文本投影；内容不在的时候标签自己就是全部。
/// 两份都存而不定主次的话，渲染读内容、导出与摘要读标签，两边各按一份走，
/// 症状是"画布上是新的、导出去是旧的"，而两边都不会报错。
/// </para>
/// <para>
/// 写入的口径集中在这里，是因为有多条通路要写同一份内容：改节点字段那条命令、
/// 编辑界面提交的那条命令，以及将来可能的导入。各处自己拼一遍的话，
/// 总有一处会忘了同步标签，而那种文档只有整体校验器会报。
/// </para>
/// <para>
/// **投影永远是标签，不是反过来。** 内容里那段文字是切片的依据，
/// 而标签是给人看、给导出用、给搜索用的那一份；两者不一致时以内容为准，
/// 因为内容是渲染真正读的那一份。
/// </para>
/// </remarks>
public static class RichLabelRules
{
    /// <summary>
    /// 取一个节点给人看的纯文本。
    /// </summary>
    /// <remarks>
    /// 内容在的时候从内容投影，不在的时候用标签本身。摘要与导出都走这一条，
    /// 而不是各自去读标签——各读一次的话，一份标签过期的文档会在两个出口
    /// 给出两种文字，而两种都自洽。
    /// </remarks>
    public static string Project(NodeDef node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.RichLabel?.PlainText ?? node.Label;
    }

    /// <summary>
    /// 写富文本内容。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 内容为空时只把内容清掉，**标签与富文本开关都不动**：清掉分段样式不等于
    /// 改掉那段文字。这与"把内容写成空"是同一件事的两面，界面上的表现都是
    /// "这个节点没有分段样式了，文字还在"。
    /// </para>
    /// <para>
    /// 内容非空时标签跟着内容走，并把富文本开关打开：写内容这个动作本身
    /// 就声明了"这个标签按富文本渲染"。
    /// </para>
    /// </remarks>
    public static NodeDef WithContent(NodeDef node, RichTextContent? content)
    {
        ArgumentNullException.ThrowIfNull(node);

        var normalized = content?.Normalize();

        if (normalized is null || normalized.IsEmpty)
        {
            return node.RichLabel is null ? node : node with { RichLabel = null };
        }

        return node with
        {
            RichLabel = normalized,
            Label = normalized.PlainText,
            RichText = true,
        };
    }

    /// <summary>
    /// 写纯文本标签。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 标签变了而内容还在的话，内容的投影就对不上了——分段样式是按位置切出来的，
    /// 文字一换就没有依据。这时候把内容清掉，而不是留一份自相矛盾的文档：
    /// 留着的话只有整体校验器会报，而画布上看到的是一份谁也没写过的文字。
    /// </para>
    /// <para>
    /// 标签没变时内容原样留着，所以"读出来再写回去"是真正的空操作。
    /// </para>
    /// <para>
    /// 富文本开关**不动**：它说的是"要不要按富文本渲染"，与"有没有分段样式"是两件事。
    /// 一次纯文本的编辑不该顺手把用户打开的开关关掉。
    /// </para>
    /// </remarks>
    public static NodeDef WithPlainLabel(NodeDef node, string label)
    {
        ArgumentNullException.ThrowIfNull(node);

        var updated = node with { Label = label };

        return node.RichLabel is not null
            && !string.Equals(node.RichLabel.PlainText, label, StringComparison.Ordinal)
                ? updated with { RichLabel = null }
                : updated;
    }

    /// <summary>
    /// 改富文本开关。
    /// </summary>
    /// <remarks>
    /// 关掉开关等于退回纯文本：分段样式不再有意义，一并清掉。留着的话会得到
    /// "开关关着却带着内容"这一份自相矛盾的文档，而那种文档只有整体校验器会报。
    /// 打开开关则什么都不用动——内容在不在都合法，没有内容时标签按纯文本渲染。
    /// </remarks>
    public static NodeDef WithRichText(NodeDef node, bool richText)
    {
        ArgumentNullException.ThrowIfNull(node);

        return !richText && node.RichLabel is not null
            ? node with { RichText = false, RichLabel = null }
            : node with { RichText = richText };
    }
}
