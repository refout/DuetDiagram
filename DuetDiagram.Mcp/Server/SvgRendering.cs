using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using DuetDiagram.Render;

namespace DuetDiagram.Mcp.Server;

/// <summary>
/// 把一份文档排出来再导成 SVG，供工具层调用。
/// </summary>
/// <remarks>
/// <para>
/// **接线在这一侧，不在工具层。** 工具层只做"把文档翻译成工具调用"，它不引渲染层：
/// 引了的话，这一层连同它的每个宿主都要带上原生绘图库。所以它留一个口子，
/// 由宿主把渲染这一步喂进去——与层投影、固定标识同一口径，
/// 那两样也不是文档的函数。
/// </para>
/// <para>
/// **每次导出新建一个度量器并当场释放。** 它按字体家族缓存字体对象，而字体对象持有
/// 原生资源；会话级共用一份的话，多个请求会同时用同一份缓存，而缓存本身不是线程安全的。
/// 导出不是热路径，一次重建的代价换的是"不用去管并发"。
/// </para>
/// </remarks>
internal static class SvgRendering
{
    /// <summary>
    /// 导出。排不出结果时返回空，由调用方如实报出来。
    /// </summary>
    /// <param name="document">文档。</param>
    /// <param name="pageId">只导这一页。为空表示整份文档。</param>
    public static SvgExport? Export(DiagramDocument document, string? pageId)
    {
        try
        {
            using var measurer = new SkiaTextMeasurer();

            var scene = SceneComposer.Compose(document, Theme.Default, measurer, pageId: pageId);

            return SvgExporter.Export(scene.DrawList);
        }
        catch (LayoutFailedException)
        {
            // 布局彻底失败时没有绘制列表可导。报一个空结果而不是抛出去：
            // 这一层看得见布局引擎的异常类型，工具层看不见，所以分得清"排不出来"
            // 与"程序坏了"的只有这里。
            return null;
        }
    }
}
