using DuetDiagram.Core.Model;

namespace DuetDiagram.Render;

/// <summary>
/// 排好版的一个行内片段：文字、它在块内的位置、它自己的尺寸，以及解析后的外观。
/// </summary>
/// <remarks>
/// 位置相对整块的左上角，不是画布坐标——加画布偏移是绘制那一步的事。
/// 一行里每个片段各自带外观，是因为富文本的一段里可以混排不同字号与颜色。
/// </remarks>
public sealed record TextSegment(
    string Text,
    double X,
    double Y,
    double Width,
    double Height,
    TextAppearance Appearance);

/// <summary>排好版的一行。一行里有一个或多个片段。</summary>
public sealed record TextLine(IReadOnlyList<TextSegment> Segments, double Width, double Height);

/// <summary>排好版的整块。折行与对齐都已经算完，画的时候只要加一个原点。</summary>
public sealed record TextBody(IReadOnlyList<TextLine> Lines, double Width, double Height);

/// <summary>
/// 把一段标签排成若干行，并算出整块的尺寸。
/// </summary>
/// <remarks>
/// <para>
/// 只认真正的换行符，不认任何标记语言里的换行写法。把标记语言的换行规则放在这里，
/// 会让渲染层开始认识各种输入格式的方言；那些规则属于各自的导入器，
/// 应当在造出文档之前就把标签归一化。
/// </para>
/// <para>
/// **纯文本与富文本共用这里的定位**：<see cref="Layout(ITextMeasurer, IReadOnlyList{string}, TextAppearance)"/>
/// 把每一行当成只有一个片段的特例，富文本走 <see cref="RichTextLayout"/>，
/// 两者最后都汇到 <see cref="Place"/>。各写一份的话，两边的边距与对齐迟早对不上，
/// 而那种偏差看起来像对齐算错了。
/// </para>
/// <para>
/// 自动折行由富文本那条路带进来（见 <see cref="RichTextLayout"/>）：折行要知道容器宽度，
/// 而宽度又取决于折行结果，纯文本这条路的宽度是量出来的、没有上限可用。
/// </para>
/// </remarks>
public static class TextLayout
{
    private static readonly string[] LineBreaks = ["\r\n", "\n", "\r"];

    /// <summary>按换行符切分。末尾的空行保留，中间的空行也保留。</summary>
    public static IReadOnlyList<string> SplitLines(string? label) =>
        string.IsNullOrEmpty(label) ? [] : label.Split(LineBreaks, StringSplitOptions.None);

    /// <summary>一行的高度。行高倍数乘字号。</summary>
    public static double LineHeight(TextAppearance text) => text.FontSize * text.LineHeight;

    /// <summary>
    /// 量一整块。宽度取最长的一行，高度按行数乘行高。
    /// </summary>
    /// <remarks>
    /// 空行也占一行的高度。跳过它会让"两行之间夹一个空行"的标签在渲染后比预期矮一截，
    /// 而写标签的人以为空行是有效的。
    /// </remarks>
    public static Size MeasureBlock(ITextMeasurer measurer, IReadOnlyList<string> lines, TextAppearance text)
    {
        var block = Layout(measurer, lines, text);

        return new Size(block.Width, block.Height);
    }

    /// <summary>
    /// 把纯文本的若干行排成一块。
    /// </summary>
    /// <remarks>
    /// 每一行是一个片段，外观全部取 <paramref name="text"/>。这是富文本排版的特例，
    /// 两条路汇到同一个 <see cref="Place"/>，所以边距、行高与对齐只有一份实现。
    /// </remarks>
    public static TextBody Layout(ITextMeasurer measurer, IReadOnlyList<string> lines, TextAppearance text)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(text);

        var raw = new List<RawLine>(lines.Count);

        foreach (var line in lines)
        {
            raw.Add(PlainLine(measurer, line, text));
        }

        return Place(raw);
    }

    /// <summary>一行纯文本排成的原始行。</summary>
    internal static RawLine PlainLine(ITextMeasurer measurer, string text, TextAppearance appearance)
    {
        var height = LineHeight(appearance);

        if (text.Length == 0)
        {
            return new RawLine([], 0, height, appearance.Align);
        }

        var width = measurer.Measure(text, appearance.FontFamily, appearance.FontSize, appearance.Weight, appearance.Italic).Width;

        return new RawLine([new RawSegment(text, appearance, width, height)], width, height, appearance.Align);
    }

    /// <summary>
    /// 把若干原始行定位到块里。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 两段定位：先按对齐方式把整块放进容器，再按同一个对齐方式把每一行放进块里。
    /// 看似重复，其实是两件事——块宽取最长的那一行，短行要靠行内对齐才能跟长行对齐。
    /// 只做一段的话，居中的标签会变成左边对齐。
    /// </para>
    /// <para>
    /// 片段的高度一律取**行高**而不是片段自己的高度：一行里混排大小字号时，
    /// 各片段要落在同一条中心线上，取各自的高度会让小字号的那一段往下掉。
    /// </para>
    /// </remarks>
    internal static TextBody Place(IReadOnlyList<RawLine> lines)
    {
        if (lines.Count == 0)
        {
            return new TextBody([], 0, 0);
        }

        var blockWidth = lines.Max(line => line.Width);
        var placed = new TextLine[lines.Count];
        var top = 0.0;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var left = line.Align switch
            {
                TextAlign.Start => 0.0,
                TextAlign.End => blockWidth - line.Width,
                _ => (blockWidth - line.Width) / 2,
            };

            var segments = new TextSegment[line.Segments.Count];
            var x = left;

            for (var slot = 0; slot < line.Segments.Count; slot++)
            {
                var segment = line.Segments[slot];

                segments[slot] = new TextSegment(segment.Text, x, top, segment.Width, line.Height, segment.Appearance);
                x += segment.Width;
            }

            placed[index] = new TextLine(segments, line.Width, line.Height);
            top += line.Height;
        }

        return new TextBody(placed, blockWidth, top);
    }
}

/// <summary>排好版之前的一个片段。宽度与高度是量出来的。</summary>
internal sealed record RawSegment(string Text, TextAppearance Appearance, double Width, double Height);

/// <summary>排好版之前的一行。</summary>
internal sealed record RawLine(IReadOnlyList<RawSegment> Segments, double Width, double Height, TextAlign Align);
