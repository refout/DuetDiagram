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
    LayoutOptions Options)
{
    /// <summary>
    /// 组合与它们的成员。
    /// </summary>
    /// <remarks>
    /// 端点在组合上的边靠它算包围盒。默认值是空列表而不是空引用：
    /// "没有组合"与"有组合但没人引用"在路由那里是两回事，而空引用会让两者混在一起。
    /// </remarks>
    public IReadOnlyList<LayoutGroup> Groups { get; init; } = [];
}

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
/// <para>
/// 产出的是**任务**而不是请求：任务里带着带归属方的约束，协调器要靠归属方决定降级时丢什么。
/// 直接调引擎时用 <see cref="LayoutJob.ToRequest"/> 转成请求。
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
    public static LayoutJob FromDocument(
        DiagramDocument document,
        Func<NodeDef, Size> measure,
        IReadOnlyDictionary<string, LayoutPoint>? pinnedNodes = null,
        string? pageId = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(measure);

        var pinned = pinnedNodes ?? new Dictionary<string, LayoutPoint>(StringComparer.Ordinal);

        // 只把这一页上的东西交给引擎。不过滤的话，别的页面上的节点照样占位置，
        // 翻页之后当前页的坐标会跟着别页的内容变——用户看到的是"我翻一页，图就重排了"。
        var nodes = document.Nodes
            .Where(node => PageMembership.Shows(document, node, pageId))
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
            .Where(edge => PageMembership.Shows(document, edge, pageId))
            .Select(edge => new LayoutEdge(edge.Id, edge.From, edge.To, edge.FromPort, edge.ToPort))
            .ToArray();

        // 组合只传结构与成员：包围盒要等求解之后由成员的最终坐标算。
        // 成员表是权威的那一份，不从节点的父级反推。
        // 成员按页裁一遍：把不在这一页上的成员交给引擎，它会去要一个没被传进去的节点。
        var groups = document.Composites
            .Select(composite => new LayoutGroup(composite.Id, Members(document, composite, pageId)))
            .Where(group => group.Members.Count > 0)
            .ToArray();

        return new LayoutJob(nodes, edges, document.Direction, document.Layout) { Groups = groups };
    }

    /// <summary>一个组合在这一页上还剩哪些成员。</summary>
    /// <remarks>
    /// 节点看它在不在这一页；嵌套的组合没有自己的归属，先留着——它在这一页上还有没有
    /// 东西由下一层自己的成员决定。取不到的标识直接去掉（悬空引用整体校验器会报）。
    /// 不过滤页时原样返回，那条路上一次都不该多走。
    /// </remarks>
    private static IReadOnlyList<string> Members(
        DiagramDocument document,
        CompositeDef composite,
        string? pageId)
    {
        if (pageId is null)
        {
            return composite.Members;
        }

        return
        [
            .. composite.Members.Where(member =>
                document.Nodes.FirstOrDefault(node => string.Equals(node.Id, member, StringComparison.Ordinal))
                    is { } node
                    ? PageMembership.Shows(document, node, pageId)
                    : document.Composites.Any(item => string.Equals(item.Id, member, StringComparison.Ordinal))),
        ];
    }

    private static IReadOnlyList<LayoutPort>? Ports(NodeDef node) =>
        node.Ports.Count == 0
            ? null
            : [.. node.Ports.Select(p => new LayoutPort(p.Name, p.Side, p.Offset))];
}
