using DuetDiagram.Core.Model;

namespace DuetDiagram.Layout;

/// <summary>
/// 一次布局的完整输入。
/// </summary>
/// <remarks>
/// 把三样东西打成一包，是为了让调用方一次拿全、一次传全。
/// 分开传的时候，漏传某一项不会报错，只会让结果看起来"莫名其妙地不对"。
/// </remarks>
public sealed record LayoutRequest(
    LayoutNode[] Nodes,
    LayoutEdge[] Edges,
    LayoutOptions Options);

/// <summary>
/// 从文档造布局输入。
/// </summary>
/// <remarks>
/// <para>
/// 这一层负责把 IR 的语义翻译成布局的几何：语义里没有尺寸，几何里必须有。
/// 尺寸由调用方测量后提供——它取决于字体、字号与文本长度，那些要到渲染时才确定。
/// </para>
/// <para>
/// 固定坐标来自人工产物的那份映射，不从 IR 读：坐标属于渲染结果，
/// 存进 IR 会让同一份语义在不同机器上产生不同的文档内容。
/// </para>
/// </remarks>
public static class LayoutRequestFactory
{
    /// <summary>
    /// 从文档造输入。
    /// </summary>
    /// <param name="document">文档。</param>
    /// <param name="measure">节点尺寸的测量方式。</param>
    /// <param name="pinnedNodes">固定坐标。来自人工产物。</param>
    /// <remarks>
    /// **测量委托必须对同一个节点给出稳定的结果。**
    /// 同一个节点在这次调用里量出 80 宽、下次量出 96 宽，布局结果会随之跳动，
    /// 而表现是"图会自己变形"，很难与其它原因区分开。
    /// </remarks>
    public static LayoutRequest FromDocument(
        DiagramDocument document,
        Func<NodeDef, Size> measure,
        IReadOnlyDictionary<string, LayoutPoint>? pinnedNodes = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(measure);

        var pinned = pinnedNodes ?? new Dictionary<string, LayoutPoint>(StringComparer.Ordinal);

        var nodes = document.Nodes
            .Select(node =>
            {
                var size = measure(node);

                return new LayoutNode(
                    node.Id,
                    size.Width,
                    size.Height,
                    pinned.TryGetValue(node.Id, out var anchor) ? anchor : null,
                    Ports(node),
                    node.Parent);
            })
            .ToArray();

        var edges = document.Edges
            .Select(edge => new LayoutEdge(edge.Id, edge.From, edge.To, edge.FromPort, edge.ToPort))
            .ToArray();

        var hints = document.Layout;

        return new LayoutRequest(
            nodes,
            edges,
            new LayoutOptions(
                document.Direction,
                hints.NodeSpacing,
                hints.LayerSpacing,
                SameRankGroups(hints)));
    }

    /// <summary>
    /// 收集同层组。
    /// </summary>
    /// <remarks>
    /// 不带归属方过滤：归属方决定的是冲突时听谁的，而冲突已经在进布局之前处理掉了。
    /// 到了这一层，留下的每一条约束都应当被执行。
    /// </remarks>
    private static IReadOnlyList<IReadOnlyList<string>> SameRankGroups(LayoutHints hints) =>
    [
        .. hints.SameRank.Select(c => c.Value.Nodes),
    ];

    private static IReadOnlyList<LayoutPort>? Ports(NodeDef node) =>
        node.Ports.Count == 0
            ? null
            : [.. node.Ports.Select(p => new LayoutPort(p.Name, p.Side, p.Offset))];
}
