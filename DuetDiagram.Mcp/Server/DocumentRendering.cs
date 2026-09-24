using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using DuetDiagram.Render;

namespace DuetDiagram.Mcp.Server;

/// <summary>
/// 把一份文档排出来再交给导出器，供工具层调用。
/// </summary>
/// <remarks>
/// <para>
/// **接线在这一侧，不在工具层。** 工具层只做"把文档翻译成工具调用"，它不引渲染层：
/// 引了的话，这一层连同它的每个宿主都要带上原生绘图库。所以它留一个口子，
/// 由宿主把渲染这一步喂进去——与层投影、固定标识同一口径，
/// 那两样也不是文档的函数。
/// </para>
/// <para>
/// **两个导出器共用一次"排出来"的过程。** 文档到绘制列表那条链路在渲染层里只有一份
/// （<see cref="SceneComposer"/>），这里只是按要的格式再走最后一步。
/// 各写一份的话，两条路的布局参数迟早会分叉，而导出结果仍然像模像样。
/// </para>
/// <para>
/// **每次导出新建一个度量器并当场释放。** 它按字体家族缓存字体对象，而字体对象持有
/// 原生资源；会话级共用一份的话，多个请求会同时用同一份缓存，而缓存本身不是线程安全的。
/// 导出不是热路径，一次重建的代价换的是"不用去管并发"。
/// </para>
/// </remarks>
internal static class DocumentRendering
{
    /// <summary>
    /// 导出 SVG。排不出结果时返回空，由调用方如实报出来。
    /// </summary>
    /// <param name="document">文档。</param>
    /// <param name="pageId">只导这一页。为空表示整份文档。</param>
    public static SvgExport? Svg(DiagramDocument document, string? pageId)
    {
        var scene = Compose(document, pageId);

        return scene is null ? null : SvgExporter.Export(scene.DrawList);
    }

    /// <summary>
    /// 导出 PNG。排不出结果时返回空，由调用方如实报出来。
    /// </summary>
    /// <remarks>
    /// 选项取默认：不透明底、按内容外接框裁、一倍。工具那一条路还没有让调用方选
    /// 缩放与范围的地方，而缺省的那一组是"贴进文档里不会变成黑底"的那一组。
    /// </remarks>
    /// <param name="document">文档。</param>
    /// <param name="pageId">只导这一页。为空表示整份文档。</param>
    public static BitmapExport? Bitmap(DiagramDocument document, string? pageId)
    {
        var scene = Compose(document, pageId);

        return scene is null ? null : BitmapExporter.Export(scene.DrawList);
    }

    /// <summary>
    /// 排一次，给出可画的东西。排不出来时返回空。
    /// </summary>
    /// <remarks>
    /// 布局彻底失败时没有绘制列表可导。报一个空结果而不是抛出去：
    /// 这一层看得见布局引擎的异常类型，工具层看不见，所以分得清"排不出来"
    /// 与"程序坏了"的只有这里。
    /// </remarks>
    private static ComposedScene? Compose(DiagramDocument document, string? pageId)
    {
        try
        {
            using var measurer = new SkiaTextMeasurer();

            return SceneComposer.Compose(document, Theme.Default, measurer, pageId: pageId);
        }
        catch (LayoutFailedException)
        {
            return null;
        }
    }
}
