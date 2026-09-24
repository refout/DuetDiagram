using DuetDiagram.Core.Model;

namespace DuetDiagram.Render;

/// <summary>
/// 把一段公式原文排成可画的一整块。
/// </summary>
/// <remarks>
/// <para>
/// **它是这一层的入口，也是"认不认得"的唯一判据。** 解析与排版分在两个文件里，
/// 但对外只有这一个口子：调用方拿到的是"排好了"或者"哪一处不认得"，
/// 而不是一棵需要自己去排的语法树。让调用方自己组合这两步的话，
/// 迟早会有一处只解析不排版，或者反过来。
/// </para>
/// <para>
/// **认不出就不画。** 调用方在拿到假之后什么都不画，把理由交给整体校验去说。
/// 原样把原文画出来的话，用户看到的是一串花括号，而他会以为是自己语法写错了；
/// 而抛异常会让一处笔误把整张图变成画不出来。
/// </para>
/// </remarks>
public static class MathTypesetter
{
    /// <summary>这个节点的标签要不要按公式排。</summary>
    public static bool HasMath(NodeDef node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.MathMode != MathMode.None;
    }

    /// <summary>
    /// 排一段公式。
    /// </summary>
    /// <param name="source">公式原文。</param>
    /// <param name="text">外观。字号与颜色取它，斜体由语法树按字决定。</param>
    /// <param name="measurer">文本度量。</param>
    /// <param name="body">排好的整块。认不出时是一块空的。</param>
    /// <param name="error">认不出的地方。排好了时为空。</param>
    /// <returns>认不认得。</returns>
    public static bool TryLayout(
        string? source,
        TextAppearance text,
        ITextMeasurer measurer,
        out MathBody body,
        out MathSyntaxError? error)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measurer);

        if (!MathSyntax.TryParse(source, out var root, out error))
        {
            body = new MathBody(0, 0, 0, [], []);
            return false;
        }

        body = MathLayout.Arrange(root, text, measurer);
        return true;
    }

    /// <summary>
    /// 量一段公式占多大。
    /// </summary>
    /// <remarks>
    /// 认不出时量出来是零。**不退回按纯文本量**：那样节点会突然变成一行字那么高，
    /// 而画面上什么都没有——尺寸与内容对不上的那种不一致最难查。
    /// </remarks>
    public static Size Measure(string? source, TextAppearance text, ITextMeasurer measurer) =>
        TryLayout(source, text, measurer, out var body, out _)
            ? new Size(body.Width, body.Height)
            : new Size(0, 0);

    /// <summary>
    /// 一整块公式摆在容器里的哪儿。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **两档的差别全在这里。** 独立成行的那一档整块居中——它自己占一块，
    /// 没有别的东西要跟它对。
    /// </para>
    /// <para>
    /// 行内那一档按**基线**对齐：公式的基线落在这一行文字的基线上。按框居中排的话，
    /// 带分数的公式比一行字高得多，居中之后它的基线会掉到文字基线下面，
    /// 看着像整行被顶起来了一截。行框与基线都由文本那一套约定推出来，
    /// 不另立一份。
    /// </para>
    /// </remarks>
    /// <param name="body">排好的整块。</param>
    /// <param name="mode">行内还是独立成行。</param>
    /// <param name="text">容器里的文字外观，用来推行框与基线。</param>
    /// <param name="container">容器。节点是节点框，连线标签是标签那一块。</param>
    public static DrawPoint Place(MathBody body, MathMode mode, TextAppearance text, SpatialRect container)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(text);

        var left = container.X + ((container.Width - body.Width) / 2);

        if (mode == MathMode.Block)
        {
            return new DrawPoint(left, container.Y + ((container.Height - body.Height) / 2));
        }

        var lineHeight = TextLayout.LineHeight(text);
        var lineTop = container.Y + ((container.Height - lineHeight) / 2);
        var baseline = lineTop + (lineHeight * MathMetrics.BaselineFraction);

        return new DrawPoint(left, baseline - body.Baseline);
    }
}
