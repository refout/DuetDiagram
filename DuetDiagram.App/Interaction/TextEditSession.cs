using System.Globalization;
using DuetDiagram.Core.Model;

namespace DuetDiagram.App.Interaction;

/// <summary>
/// 一次标签编辑的草稿。
/// </summary>
/// <remarks>
/// <para>
/// **它只改草稿，不碰文档。** 编辑期间文档一个字节都不动，退出时才由宿主发一条命令。
/// 每敲一个字发一条命令的话，撤销栈会被一次编辑灌满，而用户眼里那是一次编辑。
/// 顺带还有两个好处：画布不会因为打字而重排（文档没变），只读门与版本检查
/// 也只在提交那一次过一遍——绕过它们是不可能的，因为草稿根本进不了文档。
/// </para>
/// <para>
/// **草稿按"一个字符一份样式"存。** 编辑控件给的是纯文本加一个选区，而样式要按选区套；
/// 把两份东西对齐最省事的做法就是逐字符记样式，套样式时把区间里的每一份改掉。
/// 标签只有几十个字，这点开销无所谓，换来的是选区与样式之间不需要任何位置换算。
/// </para>
/// <para>
/// 段落由换行符切出来，与投影那条口径一致（<see cref="RichTextContent.PlainText"/>）。
/// 这一轮不做段落级对齐，所以段落只带片段。
/// </para>
/// </remarks>
public sealed class TextEditSession
{
    private string _text;
    private RichRunStyle?[] _styles;

    /// <summary>开一次编辑。</summary>
    /// <param name="nodeId">正在编辑的节点。</param>
    /// <param name="label">节点当前的纯文本标签。内容不在时它就是全部。</param>
    /// <param name="content">节点当前的富文本内容。为空表示只有标签。</param>
    public TextEditSession(string nodeId, string label, RichTextContent? content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        NodeId = nodeId;
        _text = content?.PlainText ?? label;
        _styles = Styles(content, _text.Length);
    }

    /// <summary>正在编辑的节点标识。</summary>
    public string NodeId { get; }

    /// <summary>草稿的纯文本。编辑控件显示的就是它。</summary>
    public string Text => _text;

    /// <summary>有没有任何一个字符带着样式。</summary>
    public bool HasStyles => Array.Exists(_styles, style => style is not null);

    /// <summary>
    /// 换掉整段文字。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 样式按"公共前缀 + 公共后缀"对齐：两端没动的那部分保留原来的样式，
    /// 中间被替换掉的那一段继承改动点左边那一份（没有左边就取右边，都没有就取空）。
    /// 这是编辑控件能给的最接近意图的推断——光标插在哪儿，新敲的字就长成什么样。
    /// </para>
    /// <para>
    /// 不这么做的话，只能整段样式清零，于是"改一个错别字"会把加粗全弄丢。
    /// </para>
    /// </remarks>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.Equals(_text, text, StringComparison.Ordinal))
        {
            return;
        }

        var prefix = 0;

        while (prefix < _text.Length && prefix < text.Length && _text[prefix] == text[prefix])
        {
            prefix++;
        }

        var suffix = 0;

        while (suffix < _text.Length - prefix
            && suffix < text.Length - prefix
            && _text[_text.Length - 1 - suffix] == text[text.Length - 1 - suffix])
        {
            suffix++;
        }

        // 改动点左边那一份样式；没有左边就取右边；都没有就是没有样式。
        var inherited = prefix > 0
            ? _styles[prefix - 1]
            : prefix < _text.Length ? _styles[prefix] : null;

        var styles = new RichRunStyle?[text.Length];

        Array.Copy(_styles, 0, styles, 0, prefix);

        for (var index = prefix; index < text.Length - suffix; index++)
        {
            styles[index] = inherited;
        }

        Array.Copy(_styles, _text.Length - suffix, styles, text.Length - suffix, suffix);

        _text = text;
        _styles = styles;
    }

    /// <summary>
    /// 给一段选区套上或去掉一项行内样式。
    /// </summary>
    /// <remarks>
    /// 作用对象是**选区**，不是整个标签：没有选区时调用方传长度为 0 的区间，
    /// 这里什么都不做——"随后输入的文字"由编辑控件自己那套输入法管，
    /// 而它落在草稿上的表现就是 <see cref="SetText"/> 继承改动点左边那一份样式。
    /// 作用于整个标签的话，用户想加粗一个词却整段变粗。
    /// </remarks>
    /// <param name="start">选区起点，按字符。</param>
    /// <param name="length">选区长度。</param>
    /// <param name="field">六项行内样式之一。</param>
    /// <param name="value">值。空表示去掉这一项。</param>
    public void ApplyStyle(int start, int length, string field, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        if (!RichStyleFields.IsKnown(field))
        {
            throw new ArgumentException($"没有 {field} 这一项行内样式", nameof(field));
        }

        if (length <= 0)
        {
            return;
        }

        var from = Math.Clamp(start, 0, _text.Length);
        var to = Math.Clamp(start + length, from, _text.Length);

        for (var index = from; index < to; index++)
        {
            _styles[index] = Member(_styles[index], field, value);
        }
    }

    /// <summary>某一个字符上的样式。用例用它核对"选区套上去的是哪几份"。</summary>
    public RichRunStyle? StyleAt(int index) =>
        index >= 0 && index < _styles.Length ? _styles[index] : null;

    /// <summary>
    /// 把草稿折成内容模型。
    /// </summary>
    /// <remarks>
    /// 段落按换行符切，段内把连续的同样式字符合成一段。折出来的形状与
    /// <see cref="RichTextContent.Normalize"/> 的规范形态一致，所以提交之后
    /// 模型里的内容与这里逐段逐段相同。
    /// </remarks>
    public RichTextContent ToContent()
    {
        var paragraphs = new List<RichParagraph>();
        var start = 0;

        while (true)
        {
            var end = _text.IndexOf('\n', start);
            var stop = end < 0 ? _text.Length : end;

            paragraphs.Add(new RichParagraph { Runs = Runs(start, stop) });

            if (end < 0)
            {
                break;
            }

            start = end + 1;
        }

        return new RichTextContent { Paragraphs = paragraphs };
    }

    /// <summary>
    /// 草稿是不是"没有样式的一段纯文字"。
    /// </summary>
    /// <remarks>
    /// 是的话提交走纯文本标签那条路，而不是写成一份富文本内容：用户只是改了个错别字，
    /// 不该顺手把节点从纯文本变成富文本。这也让"进编辑什么都不改就退出"成为真正的空操作。
    /// </remarks>
    public bool IsPlain => !HasStyles;

    private List<RichRun> Runs(int start, int stop)
    {
        var runs = new List<RichRun>();
        var index = start;

        while (index < stop)
        {
            var style = _styles[index];
            var end = index;

            while (end < stop && Equals(_styles[end], style))
            {
                end++;
            }

            runs.Add(new RichRun { Text = _text[index..end], Style = style });
            index = end;
        }

        return runs;
    }

    /// <summary>把内容摊成逐字符的样式。段落之间的换行符没有样式。</summary>
    private static RichRunStyle?[] Styles(RichTextContent? content, int length)
    {
        var styles = new RichRunStyle?[length];

        if (content is null)
        {
            return styles;
        }

        var index = 0;

        foreach (var paragraph in content.Paragraphs)
        {
            foreach (var run in paragraph.Runs)
            {
                for (var offset = 0; offset < run.Text.Length && index < length; offset++)
                {
                    styles[index++] = run.Style;
                }
            }

            // 段落的边界是一个换行符，它本身不带样式。
            index++;
        }

        return styles;
    }

    /// <summary>
    /// 给一份样式设一项成员。
    /// </summary>
    /// <remarks>
    /// 设完之后一项都不剩就折成空：留着一份"成员全空"的记录会让同一段文字多出一种写法，
    /// 而两种写法画出来一模一样——与模型那边折空样式是同一条口径。
    /// </remarks>
    private static RichRunStyle? Member(RichRunStyle? style, string field, string? value)
    {
        var current = style ?? new RichRunStyle();

        var updated = field switch
        {
            RichStyleFields.Bold => current with { Bold = Flag(value) },
            RichStyleFields.Italic => current with { Italic = Flag(value) },
            RichStyleFields.Underline => current with { Underline = Flag(value) },
            RichStyleFields.Strikethrough => current with { Strikethrough = Flag(value) },
            RichStyleFields.FontSize => current with { FontSize = Number(value) },
            RichStyleFields.Color => current with { Color = Blank(value) },
            _ => current,
        };

        return updated.IsEmpty ? null : updated;
    }

    /// <summary>
    /// 开关型成员的值：只有 "true" 算打开。
    /// </summary>
    /// <remarks>
    /// 关掉一项就是把它去掉，而不是写一个 <c>false</c>：模型里这两者画出来一模一样，
    /// 但留着一个 <c>false</c> 会让同一段文字多出一种写法，而它同样会让
    /// <see cref="HasStyles"/> 变成真——用户只是取消了一次加粗，标签却因此
    /// 从纯文本变成了富文本。
    /// </remarks>
    private static bool? Flag(string? value) => string.Equals(value, "true", StringComparison.Ordinal) ? true : null;

    private static double? Number(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
