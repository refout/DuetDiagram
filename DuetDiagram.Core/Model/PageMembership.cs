namespace DuetDiagram.Core.Model;

/// <summary>
/// 元素归在哪一页，以及一页上看得见谁。
/// </summary>
/// <remarks>
/// <para>
/// **这条规则只有这一份。** 布局、渲染、导出与摘要四处都要回答"这一页上有谁"，
/// 各判一次的话四处迟早会不一致，而表现是"翻页之后有几个元素赖着不走"——
/// 那种不一致在每一处单独看都是自洽的。
/// </para>
/// <para>
/// **缺省页是次序最小的那一页**，次序相同用标识断并列。没有声明归属的元素、
/// 以及归属指向一个已经不存在的页面的元素，都归它。与图层的缺省层同一形状。
/// </para>
/// <para>
/// **边要两端都在这一页上。** 跨页的边哪一页都不画：挑一页画出来的话，
/// 用户在那一页上会看到一条通向空处的线，而它在另一页上才接得上。
/// </para>
/// <para>
/// **文档一页都没有时，<see cref="EffectivePageId"/> 给空。** 那时传空进来表示"不过滤"，
/// 也就是单页文档的旧行为——一份没有页面的文档本来就只有一个隐含的页面。
/// </para>
/// </remarks>
public static class PageMembership
{
    /// <summary>文档里的页面，按次序排好。</summary>
    public static IReadOnlyList<PageDef> Ordered(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return
        [
            .. document.Pages
                .OrderBy(page => page.Order)
                .ThenBy(page => page.Id, StringComparer.Ordinal),
        ];
    }

    /// <summary>缺省页的标识。一个页面都没有时为空。</summary>
    public static string? DefaultPageId(DiagramDocument document)
    {
        var pages = Ordered(document);

        return pages.Count == 0 ? null : pages[0].Id;
    }

    /// <summary>
    /// 一个元素实际归在哪一页。
    /// </summary>
    /// <param name="document">文档。</param>
    /// <param name="declared">元素上声明的页面标识，可以为空。</param>
    /// <remarks>
    /// 声明指向一个不存在的页面时按缺省页处理，**不报错**：校验器不查这条引用，
    /// 而渲染与布局的立场是"合法的文档里不会有，只需要别崩"。
    /// </remarks>
    public static string? EffectivePageId(DiagramDocument document, string? declared)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (declared is not null
            && document.Pages.Any(page => string.Equals(page.Id, declared, StringComparison.Ordinal)))
        {
            return declared;
        }

        return DefaultPageId(document);
    }

    /// <summary>
    /// 这一页上画不画这个节点。
    /// </summary>
    /// <param name="pageId">要看的页。传空表示不过滤，即每一页都算。</param>
    public static bool Shows(DiagramDocument document, NodeDef node, string? pageId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);

        return pageId is null
            || string.Equals(EffectivePageId(document, node.Page), pageId, StringComparison.Ordinal);
    }

    /// <summary>
    /// 这一页上画不画这条边。
    /// </summary>
    /// <param name="pageId">要看的页。传空表示不过滤。</param>
    /// <remarks>
    /// <para>
    /// 三个条件都要成立：**两端都在这一页上**、边自己的归属也在这一页。
    /// </para>
    /// <para>
    /// **边没声明归属时跟着两端走。** 缺省页是"次序最小的那一页"这条规则用在边上会出岔子：
    /// 两端都在第二页、而边没声明归属时，它会归到第一页，于是两页都不画它——
    /// 一条两端都在、却哪儿都看不见的边。跟着两端走就没有这个洞。
    /// </para>
    /// <para>
    /// **两端不在同一页时哪一页都不画。** 挑一页画出来的话，用户在那一页上会看到
    /// 一条通向空处的线，而它在另一页上才接得上。
    /// </para>
    /// </remarks>
    public static bool Shows(DiagramDocument document, EdgeDef edge, string? pageId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(edge);

        if (pageId is null)
        {
            return true;
        }

        // 端点取不到就不画。整体校验器会报悬空引用，而这里只需要别崩。
        if (document.FindNode(edge.From) is not { } from || !Shows(document, from, pageId))
        {
            return false;
        }

        if (document.FindNode(edge.To) is not { } to || !Shows(document, to, pageId))
        {
            return false;
        }

        // 到这一步两端都在这一页上，所以"跟着两端走"那一档必然就是它。
        return edge.Page is not { } declared
            || string.Equals(declared, pageId, StringComparison.Ordinal);
    }

    /// <summary>
    /// 把一份文档投影成"只有这一页上的东西"。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 给导出与摘要用：它们要的是一份快照，而不是一个"当前页"的概念，
    /// 让它们各自按页过滤的话，同一个判据会散成好几份。
    /// </para>
    /// <para>
    /// **布局与渲染不走这条路。** 它们每一帧都要问一次，投影会多构造一份文档、
    /// 多算两遍哈希；那两处把页标识传下去，用上面那几个判据过滤。判据是同一份。
    /// </para>
    /// <para>
    /// 组合没有自己的归属，所以按"成员里有几个在这一页上"处理：一个都不在的不留，
    /// 剩下的把成员表裁到这一页上的那些。
    /// </para>
    /// </remarks>
    /// <param name="document">原文档。</param>
    /// <param name="pageId">要留下的那一页。传空表示原样返回，不投影。</param>
    public static DiagramDocument Project(DiagramDocument document, string? pageId)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (pageId is null)
        {
            return document;
        }

        var nodes = document.Nodes.Where(node => Shows(document, node, pageId)).ToArray();
        var edges = document.Edges.Where(edge => Shows(document, edge, pageId)).ToArray();
        var onPage = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);

        var composites = document.Composites
            .Select(composite => composite with
            {
                Members = [.. composite.Members.Where(onPage.Contains)],
            })
            .Where(composite => composite.Members.Count > 0)
            .ToArray();

        var projected = DiagramDocument.CreateFromContent(
            document.Id,
            document.Kind,
            document.Direction,
            document.Pages,
            document.Layers,
            nodes,
            edges,
            composites,
            document.Tags,
            document.Actions,
            document.Fonts,
            document.TextPresets,
            document.Palette,
            document.Layout,
            document.Canvas);

        // 版本号要留原值：投影是一份视图，不是一次变更。丢掉的话，
        // 摘要里报的版本会比文档上的小——而调用方正是按它判断自己手上的副本新不新。
        projected.Version = document.Version;

        return projected;
    }
}
