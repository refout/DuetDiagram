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

        // 组合只传结构与成员：包围盒要等求解之后由成员的最终坐标算。
        // 成员表是权威的那一份，不从节点的父级反推。
        var groups = document.Composites
            .Select(composite => new LayoutGroup(composite.Id, composite.Members))
            .ToArray();

        return new LayoutJob(nodes, edges, document.Direction, document.Layout) { Groups = groups };
    }

    private static IReadOnlyList<LayoutPort>? Ports(NodeDef node) =>
        node.Ports.Count == 0
            ? null
            : [.. node.Ports.Select(p => new LayoutPort(p.Name, p.Side, p.Offset))];
}
