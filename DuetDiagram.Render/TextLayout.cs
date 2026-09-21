using DuetDiagram.Core.Model;

namespace DuetDiagram.Render;

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
/// 不做自动折行。折行需要知道容器的宽度，而宽度又取决于折行结果——
/// 节点尺寸是量出来的，量之前没有宽度可用。要支持它得先定一个最小宽度再反复迭代，
/// 那是另一件事。
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
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(text);

        var width = 0.0;

        foreach (var line in lines)
        {
            var measured = measurer.Measure(line, text.FontFamily, text.FontSize, text.Weight);
            width = Math.Max(width, measured.Width);
        }

        return new Size(width, lines.Count * LineHeight(text));
    }
}
