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
    /// 导出 PDF。排不出结果时返回空，由调用方如实报出来。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 选项取默认：按内容外接框裁。工具那一条路还没有让调用方选范围的地方，
    /// 而缺省的那一组是"这张图本身有多大就出多大"的那一组。
    /// </para>
    /// <para>
    /// **整份文档导出来是好几页，一页一张纸。** 页面标识为空时按文档自己声明的页序走，
    /// 每一页各排一次、各占一页；文档一页都没声明时只有一个隐含的页面，
    /// 那正是传空给排布那一层的含义。点了名就只出那一页。
    /// </para>
    /// <para>
    /// 每一页各自排一次而不是排一次再切：布局的输入里带着页面标识，
    /// 所以"只排这一页"与"排整份再筛"本来就不是同一份布局结果。按页导出那两条路
    /// 走的是同一个入口，于是同一个页面在 SVG、PNG 与 PDF 里画出来的是同一份东西。
    /// </para>
    /// </remarks>
    /// <param name="document">文档。</param>
    /// <param name="pageId">只导这一页。为空表示整份文档，一页一张纸。</param>
    public static PdfExport? Pdf(DiagramDocument document, string? pageId)
    {
        var lists = new List<DrawList>();

        foreach (var id in PageIds(document, pageId))
        {
            if (Compose(document, id) is not { } scene)
            {
                return null;
            }

            lists.Add(scene.DrawList);
        }

        return lists.Count == 0 ? null : PdfExporter.Export(lists);
    }

    /// <summary>
    /// 这一次导出要出哪几页。
    /// </summary>
    /// <remarks>
    /// 点了名就只出那一页；没点名按文档自己声明的页序走。文档一页都没声明时给一个空标识，
    /// 表示"不过滤"——一份没有页面的文档本来就只有一个隐含的页面，
    /// 传空进去正好是那个意思。页序取 <see cref="PageMembership.Ordered"/> 那一份，
    /// 不在这里重排：排出来的顺序要与界面上翻页的顺序一致。
    /// </remarks>
    private static IReadOnlyList<string?> PageIds(DiagramDocument document, string? pageId)
    {
        if (pageId is not null)
        {
            return [pageId];
        }

        var pages = PageMembership.Ordered(document);

        return pages.Count == 0 ? [null] : [.. pages.Select(page => page.Id)];
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
