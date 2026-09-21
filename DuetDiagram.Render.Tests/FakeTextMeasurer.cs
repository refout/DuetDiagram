using DuetDiagram.Core.Model;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 按字符个数量文本的度量器。
/// </summary>
/// <remarks>
/// <para>
/// 快照要逐字节可比，而真实字体的度量随机器变化，所以测试一律用它。
/// 它也是"度量必须可注入"这条约束的活证据：把它换成别的实现，
/// 绘制列表就该跟着变，而构建那一步一行都不用改。
/// </para>
/// <para>
/// 宽度的算法故意简单到一眼能算：每个字符占字号乘以系数。
/// 复杂的假实现会让人在核对快照时算不出期望值，只能盲信输出。
/// </para>
/// </remarks>
internal sealed class FakeTextMeasurer : ITextMeasurer
{
    /// <summary>每个字符占字号的几分之几。</summary>
    public const double CharacterWidth = 0.6;

    /// <summary>行高相对字号的倍数。</summary>
    public const double LineFactor = 1.35;

    /// <summary>字重为粗体时额外加宽的比例。</summary>
    public const double BoldFactor = 0.08;

    public Size Measure(string text, string fontFamily, double fontSize, FontWeight weight)
    {
        var width = text.Length * fontSize * CharacterWidth;

        if (weight == FontWeight.Bold)
        {
            width *= 1 + BoldFactor;
        }

        return new Size(width, fontSize * LineFactor);
    }
}
