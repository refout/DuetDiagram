using DuetDiagram.Render;

namespace DuetDiagram.App.Controls;

/// <summary>
/// 把变更高亮接到画布上。
/// </summary>
/// <remarks>
/// 它只做一件事：拿一份高亮状态、一份基准绘制列表与一个相位，算出这一帧要叠的指令。
/// 真正的构建在渲染层的 <see cref="Highlight.Build"/> 里——那一段是纯计算，
/// 可以在没有界面、没有窗口的情况下逐条断言，所以它属于渲染层而不是画布。
/// 这里只是一层适配，让画布不必知道高亮状态是从哪儿来的。
/// </remarks>
internal static class HighlightOverlay
{
    /// <summary>这一帧要叠的高亮指令。</summary>
    public static IReadOnlyList<DrawCommand> Commands(
        IReadOnlyList<ElementHighlight> highlights,
        DrawList list,
        Theme theme,
        double phase)
    {
        ArgumentNullException.ThrowIfNull(highlights);
        ArgumentNullException.ThrowIfNull(list);

        return highlights.Count == 0
            ? []
            : Highlight.Build(highlights, list.Commands, theme, phase);
    }
}
