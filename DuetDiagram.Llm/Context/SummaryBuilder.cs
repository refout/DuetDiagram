using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Llm.Context;

/// <summary>
/// 造一份摘要所需要的全部输入。
/// </summary>
/// <remarks>
/// <para>
/// 文档之外的三样都**不是 IR 里的内容**，所以只能由宿主提供：
/// 固定位置在人工产物里，层投影在布局结果里，变更记录在命令总线的版本日志里。
/// </para>
/// <para>
/// 它们都不给也能出一份摘要，只是对应的那一段会空着。**空着比猜一个好**——
/// 猜出来的层数或固定列表与实际画面各按一套算法，对不上时没有任何东西会报错。
/// </para>
/// </remarks>
public sealed record SummaryInput
{
    public required DiagramDocument Document { get; init; }

    /// <summary>最近一次布局里每个节点落在第几层。为空表示还没有排过。</summary>
    public IReadOnlyList<NodeRank> Placement { get; init; } = [];

    /// <summary>被固定的节点标识。来自人工产物。</summary>
    public IReadOnlyList<string> PinnedNodes { get; init; } = [];

    /// <summary>变更记录，通常取命令总线版本日志的快照。</summary>
    public IReadOnlyList<VersionEntry> RecentChanges { get; init; } = [];

    /// <summary>
    /// 只看这一页。传空表示整份文档。
    /// </summary>
    /// <remarks>
    /// **它必须是一个真的存在的页。** 这是调用方点名要的东西，与元素上那个归属字段
    /// 不同：归属指向一个不存在的页面时按缺省页处理（那是文档内部的引用），
    /// 而这里点的是一个不存在的页面时该如实说一句，否则调用方会拿着一张别的页去办事。
    /// 校验在工具那一层做，这里只负责照着过滤。
    /// </remarks>
    public string? PageId { get; init; }
}

/// <summary>
/// 从文档造一份归一化摘要。
/// </summary>
/// <remarks>
/// <para>
/// **归一化在这里，不在渲染那一步。** 集合按标识排序之后才组装，
/// 于是同一份文档无论集合的插入顺序如何，得到的结构对象完全相同；
/// 文本又是从结构对象渲染的，所以也完全相同。
/// 不归一化的话，摘要进不了快照测试，也没法用来判断「这份文档变了吗」——
/// 撤销之后哈希能回到原值，摘要却回不去。
/// </para>
/// <para>
/// 这里不做任何布局计算，也不读任何文件。它只把拿到的输入重排、截断、投影。
/// </para>
/// </remarks>
public static class SummaryBuilder
{
    /// <summary>「最近修改」那一段最多列几条。再多就不是「最近」了。</summary>
    public const int RecentChangeLimit = 5;

    public static DiagramSummary Build(SummaryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // 按页投影一次，后面全都用这一份。投影只留这一页上的节点与边，
        // 而"缺省页是哪一页、跨页的边算谁的"那套口径在 Core 里只有一份。
        var document = PageMembership.Project(input.Document, input.PageId);
        var placement = Placement(input.Placement, document);
        var (layerCount, groups) = ProjectLayers(placement);

        return new DiagramSummary(
            document.Id,
            document.Kind,
            document.Version,
            [.. document.Nodes
                .OrderBy(node => node.Id, StringComparer.Ordinal)
                .Select(node => new SummaryNode(node.Id, node.Label, node.Shape, node.StyleToken))],
            [.. document.Edges
                .OrderBy(edge => edge.Id, StringComparer.Ordinal)
                .Select(edge => new SummaryEdge(edge.Id, edge.From, edge.To, edge.Label, edge.Line))],
            new SummaryLayout(document.Direction, layerCount, groups),

            // 去重之后排序：同一个节点被固定两次只该说一次，而顺序不能随人工产物里的写入次序变。
            [.. input.PinnedNodes.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal)],

            // 令牌实时取自调色板。写死一份的话，用户改了调色板之后模型会照着一个
            // 不存在的令牌去设样式，而命令层只能回一句「没有这个令牌」。
            [.. document.Palette.Entries.Keys.OrderBy(token => token, StringComparer.Ordinal)],
            Recent(input.RecentChanges));
    }

    /// <summary>
    /// 把同层分布裁到投影之后还剩下的那些节点。
    /// </summary>
    /// <remarks>
    /// 层分布是从最近一次布局来的，而布局算的是整份文档。不裁的话，
    /// 按页读出来的摘要里会列出一批不在这一页上的节点，而读的人会去找它们。
    /// </remarks>
    private static IReadOnlyList<NodeRank> Placement(
        IReadOnlyList<NodeRank> placement,
        DiagramDocument document)
    {
        if (placement.Count == 0 || placement.Count == document.Nodes.Count)
        {
            return placement;
        }

        var onPage = document.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);

        return [.. placement.Where(rank => onPage.Contains(rank.Id))];
    }

    /// <summary>
    /// 把层投影压成「层数」与「同层分组」。
    /// </summary>
    /// <remarks>
    /// 只列成员不少于两个的组：一个节点自成一层不是「分组」，
    /// 把它也列出来的话，那一段会被一层一个的条目填满，而真正要看的并列关系被淹掉。
    /// </remarks>
    private static (int? LayerCount, IReadOnlyList<IReadOnlyList<string>> Groups) ProjectLayers(
        IReadOnlyList<NodeRank> placement)
    {
        if (placement.Count == 0)
        {
            return (null, []);
        }

        var byLayer = placement
            .GroupBy(rank => rank.Layer)
            .OrderBy(group => group.Key)
            .Select(group => (IReadOnlyList<string>)[.. group
                .Select(rank => rank.Id)
                .OrderBy(id => id, StringComparer.Ordinal)])
            .ToArray();

        return (byLayer.Length, [.. byLayer.Where(members => members.Count > 1)]);
    }

    /// <summary>
    /// 取最近若干条变更，最新的在前。
    /// </summary>
    /// <remarks>
    /// 先按版本号倒序取前几条，再在每条里把字段明细按「元素标识、字段名」排一遍：
    /// 命令自己的明细次序是稳定的，但摘要要的是**与产生次序无关**的同一份输出。
    /// </remarks>
    private static IReadOnlyList<SummaryChange> Recent(IReadOnlyList<VersionEntry> entries) =>
    [
        .. entries
            .OrderByDescending(entry => entry.Version)
            .ThenBy(entry => entry.CommandId, StringComparer.Ordinal)
            .Take(RecentChangeLimit)
            .Select(entry => new SummaryChange(
                entry.Version,
                entry.CommandId,
                entry.Source,
                entry.ActorId,
                entry.Timestamp,
                [.. entry.Changes
                    .OrderBy(change => change.ElementId, StringComparer.Ordinal)
                    .ThenBy(change => change.Field, StringComparer.Ordinal)
                    .Select(change => new SummaryFieldChange(
                        change.ElementId,
                        change.Field,
                        change.NewValue,
                        change.Kind))],
                entry.IsBulkChange,
                entry.OriginalChangeCount)),
    ];
}
