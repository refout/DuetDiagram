using DuetDiagram.Core.Model;

namespace DuetDiagram.Render;

/// <summary>
/// 文本度量。
/// </summary>
/// <remarks>
/// <para>
/// **它必须是注入的。** 节点占多大由标签量出来的宽高决定，而量出来的结果取决于字体、
/// 字号与渲染后端。写死成某个绘图库的调用之后，同一份文档在两台机器上会量出不同的尺寸，
/// 于是绘制列表不同、快照对不上，而那种红看起来像画错了。
/// </para>
/// <para>
/// 量的是**一行**。分行由 <see cref="TextLayout"/> 做，因为它需要知道度量结果才能算块尺寸，
/// 而反过来让度量器自己处理换行，等于把换行规则也塞进后端实现里，
/// 于是换一台机器连分行都变了。
/// </para>
/// </remarks>
public interface ITextMeasurer
{
    /// <summary>
    /// 量一行文本。
    /// </summary>
    /// <remarks>
    /// **对同样的输入必须给出同样的结果。** 同一份输入两次量出不同的宽度，
    /// 图会自己变形，而那种变形很难与其它原因区分开。
    /// </remarks>
    /// <param name="text">文本。不含换行符。</param>
    /// <param name="fontFamily">字体名。</param>
    /// <param name="fontSize">字号。</param>
    /// <param name="weight">字重。</param>
    Size Measure(string text, string fontFamily, double fontSize, FontWeight weight);
}
