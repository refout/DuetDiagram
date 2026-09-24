using DuetDiagram.Core.Model;

namespace DuetDiagram.Render;

/// <summary>
/// 把富文本内容排成一块：分段、折行、行内样式混排，最后交给 <see cref="TextLayout.Place"/> 定位。
/// </summary>
/// <remarks>
/// <para>
/// **定位那一步与纯文本共用一份实现。** 这里只负责"哪几个字落在哪一行"，
/// 块的宽高与对齐交给 <see cref="TextLayout"/>——各写一份的话，两边的边距与对齐迟早对不上，
/// 而那种偏差看起来像对齐算错了。
/// </para>
/// <para>
/// **确定性。** 同一个内容配同一份字体度量，每次排出逐字相同的结果：没有缓存、
/// 不依赖集合的迭代顺序、不用当前区域设置。用哈希做缓存键的地方全靠这一条。
/// </para>
/// <para>
/// **不做竖排、不做双向文字、不做连字。** 三样都是各自独立的排版工程，
/// 而这一轮要的只是"富文本正确"。
/// </para>
/// </remarks>
public static class RichTextLayout
{
    /// <summary>
    /// 排一块富文本。
    /// </summary>
    /// <param name="measurer">文本度量。量一行，不含换行符。</param>
    /// <param name="content">富文本内容。</param>
    /// <param name="baseline">节点这一级的文字外观，行内片段没设的项从它继承。</param>
    /// <param name="resolve">把一段行内样式解析成完整外观。为空表示沿用节点这一级。</param>
    /// <param name="maxWidth">
    /// 折行宽度。为空表示不折行——一段就是一行，与纯文本同一口径。
    /// 节点尺寸是量出来的，量之前没有宽度可用，所以按节点排版时传空。
    /// </param>
    public static TextBody Layout(
        ITextMeasurer measurer,
        RichTextContent content,
        TextAppearance baseline,
        Func<RichRunStyle?, TextAppearance> resolve,
        double? maxWidth = null)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(resolve);

        var raw = new List<RawLine>(content.Paragraphs.Count);

        foreach (var paragraph in content.Paragraphs)
        {
            // 段落自己的对齐优先；没写时跟着节点这一级。
            var align = paragraph.Align ?? baseline.Align;
            var atoms = Atoms(measurer, paragraph, baseline, resolve);

            raw.AddRange(Wrap(measurer, atoms, align, baseline, maxWidth));
        }

        return TextLayout.Place(raw);
    }

    #region 切原子

    /// <summary>
    /// 把一个段落切成"最小不可分单元"。
    /// </summary>
    /// <remarks>
    /// 原子是折行的最小单位：一个拉丁词、一个表意文字、一个空格各是一个原子。
    /// 表意文字（中日韩）逐字成原子，所以中文按字断行；拉丁词整体成原子，
    /// 所以英文单词不会被从中间切开——切错的表现正是"英文单词中间断行"。
    /// </remarks>
    private static List<RawSegment> Atoms(
        ITextMeasurer measurer,
        RichParagraph paragraph,
        TextAppearance baseline,
        Func<RichRunStyle?, TextAppearance> resolve)
    {
        var atoms = new List<RawSegment>();

        foreach (var run in paragraph.Runs)
        {
            if (run.Text.Length == 0)
            {
                continue;
            }

            var appearance = resolve(run.Style);
            var height = TextLayout.LineHeight(appearance);

            foreach (var piece in SplitAtoms(run.Text))
            {
                atoms.Add(new RawSegment(piece, appearance, Measure(measurer, piece, appearance), height));
            }
        }

        return atoms;
    }

    /// <summary>把一段文字切成原子。</summary>
    private static IEnumerable<string> SplitAtoms(string text)
    {
        var start = 0;
        var kind = Kind(text[0]);

        for (var index = 1; index < text.Length; index++)
        {
            var next = Kind(text[index]);

            // 词字符连着还是同一个原子；其余每个字符各自成原子。
            if (next == AtomKind.Word && kind == AtomKind.Word)
            {
                continue;
            }

            yield return text[start..index];
            start = index;
            kind = next;
        }

        yield return text[start..];
    }

    private enum AtomKind
    {
        /// <summary>词的一部分：拉丁字母、数字、标点。整段连着不断。</summary>
        Word,

        /// <summary>空白。可以断在它前面，断行时丢掉。</summary>
        Space,

        /// <summary>自成一体：表意文字。可以断在它前后。</summary>
        Single,
    }

    private static AtomKind Kind(char value) =>
        char.IsWhiteSpace(value) ? AtomKind.Space
        : IsIdeographic(value) ? AtomKind.Single
        : AtomKind.Word;

    /// <summary>
    /// 是不是表意文字（中日韩）。
    /// </summary>
    /// <remarks>
    /// 只列真正逐字断行的那些区段。韩文谚文按音节成块，不在这里——它是音节文字，
    /// 与拉丁字母一样按词断，硬拆成单字反而不对。
    /// </remarks>
    private static bool IsIdeographic(char value) =>
        value is >= '\u3040' and <= '\u30ff'      // 平假名与片假名
            or >= '\u3400' and <= '\u4dbf'        // 扩展 A
            or >= '\u4e00' and <= '\u9fff'        // 基本区
            or >= '\uf900' and <= '\ufaff'        // 兼容表意文字
            or >= '\uff00' and <= '\uffef';       // 全角与半角形式

    private static double Measure(ITextMeasurer measurer, string text, TextAppearance appearance) =>
        measurer.Measure(text, appearance.FontFamily, appearance.FontSize, appearance.Weight, appearance.Italic).Width;

    #endregion

    #region 折行

    /// <summary>
    /// 把一个段落的原子排成若干行。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 贪心填行：装得下就放，装不下就换行。**换行处丢掉空格**——留在行尾会撑宽整块，
    /// 留在行首会看起来像缩进。
    /// </para>
    /// <para>
    /// **一个原子比整行还宽时才硬切。** 硬切按字符来，且保证每段至少一个字，
    /// 所以哪怕一行只放得下半个字也不会死循环。
    /// </para>
    /// </remarks>
    private static List<RawLine> Wrap(
        ITextMeasurer measurer,
        IReadOnlyList<RawSegment> atoms,
        TextAlign align,
        TextAppearance baseline,
        double? maxWidth)
    {
        if (maxWidth is not { } limit || limit <= 0)
        {
            return [Line(atoms, align, baseline, trim: false)];
        }

        var pieces = new List<RawSegment>(atoms.Count);

        foreach (var atom in atoms)
        {
            // 比整行还宽的原子在任何一行上都放不下，先切成装得下的片段。
            if (atom.Width <= limit)
            {
                pieces.Add(atom);
            }
            else
            {
                pieces.AddRange(HardCut(measurer, atom, limit));
            }
        }

        var lines = new List<RawLine>();
        var current = new List<RawSegment>();
        var width = 0.0;

        foreach (var piece in pieces)
        {
            var space = IsSpace(piece.Text);

            // 行首的空格不占位：换行之后紧跟的空格要丢掉。
            if (space && current.Count == 0)
            {
                continue;
            }

            if (width + piece.Width <= limit)
            {
                current.Add(piece);
                width += piece.Width;
                continue;
            }

            if (current.Count > 0)
            {
                lines.Add(Line(current, align, baseline, trim: true));
                current.Clear();
                width = 0;
            }

            // 断行处的空格丢掉；其余片段此时一定装得下（已经硬切过）。
            if (!space)
            {
                current.Add(piece);
                width += piece.Width;
            }
        }

        if (current.Count > 0)
        {
            lines.Add(Line(current, align, baseline, trim: true));
        }

        // 空段落照样占一行：跳过它会让两个段落之间的空行消失。
        return lines.Count == 0 ? [Line([], align, baseline, trim: false)] : lines;
    }

    /// <summary>把一个原子按字符切成装得下的片段。</summary>
    private static IEnumerable<RawSegment> HardCut(ITextMeasurer measurer, RawSegment atom, double limit)
    {
        var start = 0;

        while (start < atom.Text.Length)
        {
            var length = 1;

            while (start + length < atom.Text.Length
                && Measure(measurer, atom.Text.Substring(start, length + 1), atom.Appearance) <= limit)
            {
                length++;
            }

            var piece = atom.Text.Substring(start, length);

            yield return atom with { Text = piece, Width = Measure(measurer, piece, atom.Appearance) };
            start += length;
        }
    }

    /// <summary>
    /// 攒出一行。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 行高取行内最高的那个片段，不是取平均也不是取第一个：混排大字号的那一行
    /// 要按大的那一个撑开，否则它会与下一行叠在一起。
    /// </para>
    /// <para>
    /// **相邻的同样式片段合成一段。** 一个 run 在折行时会被切成好几个原子，
    /// 落在同一行上的那些合回去，于是"一段"就是"一个 run 在这一行上的那一截"，
    /// 而不是"折行算法当时手里拿的那一块"——后者的边界取决于行宽，会随宽度漂移。
    /// </para>
    /// <para>
    /// 片段列表**复制一份**再返回：调用方会清空并复用那个列表接着攒下一行，
    /// 直接存引用的话，上一行会跟着一起变，两行变成一样的内容。
    /// </para>
    /// </remarks>
    private static RawLine Line(IReadOnlyList<RawSegment> segments, TextAlign align, TextAppearance baseline, bool trim)
    {
        var kept = new List<RawSegment>(segments);

        if (trim)
        {
            while (kept.Count > 0 && IsSpace(kept[^1].Text))
            {
                kept.RemoveAt(kept.Count - 1);
            }
        }

        var merged = new List<RawSegment>(kept.Count);

        foreach (var segment in kept)
        {
            var last = merged.Count > 0 ? merged[^1] : null;

            if (last is not null && Equals(last.Appearance, segment.Appearance))
            {
                merged[^1] = last with { Text = last.Text + segment.Text, Width = last.Width + segment.Width };
                continue;
            }

            merged.Add(segment);
        }

        var width = 0.0;
        var height = 0.0;

        foreach (var segment in merged)
        {
            width += segment.Width;
            height = Math.Max(height, segment.Height);
        }

        return new RawLine(merged, width, height == 0 ? TextLayout.LineHeight(baseline) : height, align);
    }

    private static bool IsSpace(string text) => text.All(char.IsWhiteSpace);

    #endregion
}
