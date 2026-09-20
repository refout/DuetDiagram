namespace DuetDiagram.Poc.LayoutConstraints;

/// <summary>交给引擎的节点。只含引擎认识的字段。</summary>
internal sealed record EngineNode(string Id, double Width, double Height);

internal sealed record EngineEdge(string From, string To);

internal sealed record ContractedGraph(EngineNode[] Nodes, EngineEdge[] Edges);

/// <summary>一条被改指到超节点的边，保留原本的端点以便展开后还原。</summary>
internal sealed record RedirectedEdge(string NewFrom, string NewTo, string OriginalFrom, string OriginalTo);

/// <summary>一个同层组展开所需的全部信息。</summary>
internal sealed record SameRankExpansion(
    string GroupId,
    string SuperNodeId,
    string[] Members,
    double Gap,
    RedirectedEdge[] Redirected,
    ConstraintEdge[] InternalEdges);

/// <summary>收缩结果。</summary>
internal sealed record ContractionResult(ContractedGraph Graph, SameRankExpansion[] Expansions);

/// <summary>
/// 同层约束的补齐：把同层组收缩成一个超节点参与布局，布局完成后再展开。
/// </summary>
/// <remarks>
/// <para>
/// 为什么用收缩而不是布局后把节点搬到同一纵坐标：后者会与同层其它节点撞在一起，
/// 而且撞了之后没有干净的补救办法。收缩是在布局**之前**把约束表达成引擎能理解的形式，
/// 引擎自然会把所需空间留出来，展开就只是把预留好的空间填上。
/// </para>
/// <para>
/// 关键细节是超节点的尺寸必须按**展开后的总尺寸**申报，而不是取成员中的最大值。
/// 只申报最大值的话，引擎按一个节点宽度预留空间，展开成三个节点就会横着压到邻居身上。
/// 这正是分层布局引擎内部处理跨层长边的标准手法——插入虚拟节点并给它足够的尺寸，
/// 我们只是把同一手法用在同层组上。
/// </para>
/// </remarks>
internal static class SameRankContraction
{
    private const string SuperNodePrefix = "sr:";

    public static bool TryGetSuperNodeId(string nodeId, out string groupId)
    {
        if (nodeId.StartsWith(SuperNodePrefix, StringComparison.Ordinal))
        {
            groupId = nodeId[SuperNodePrefix.Length..];
            return true;
        }

        groupId = string.Empty;
        return false;
    }

    /// <summary>
    /// 收缩。没有同层组时原样返回，调用方不必为这种情况写分支。
    /// </summary>
    public static ContractionResult Contract(ConstraintGraph graph, ConstraintOptions options)
    {
        if (graph.SameRankGroups.Length == 0)
        {
            return new ContractionResult(
                new ContractedGraph(
                    [.. graph.Nodes.Select(n => new EngineNode(n.Id, n.Width, n.Height))],
                    [.. graph.Edges.Select(e => new EngineEdge(e.From, e.To))]),
                []);
        }

        // 成员到所属组的反查表。一个节点只允许属于一个同层组，属于多个组是矛盾的输入。
        var groupOf = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var group in graph.SameRankGroups)
        {
            foreach (var member in group.Members)
            {
                groupOf[member] = group.Id;
            }
        }

        var nodes = new List<EngineNode>();
        var expansions = new List<SameRankExpansion>();

        foreach (var group in graph.SameRankGroups)
        {
            var members = graph.Nodes.Where(n => group.Members.Contains(n.Id, StringComparer.Ordinal)).ToArray();

            // 按宽度之和加间隙申报，高度取成员最大值。
            var width = members.Sum(m => m.Width) + (options.NodeSpacing * (members.Length - 1));
            var height = members.Length == 0 ? 0 : members.Max(m => m.Height);

            nodes.Add(new EngineNode(SuperNodePrefix + group.Id, width, height));
            expansions.Add(new SameRankExpansion(group.Id, SuperNodePrefix + group.Id, group.Members, options.NodeSpacing, [], []));
        }

        foreach (var node in graph.Nodes.Where(n => !groupOf.ContainsKey(n.Id)))
        {
            nodes.Add(new EngineNode(node.Id, node.Width, node.Height));
        }

        var edges = new List<EngineEdge>();
        var redirected = new List<RedirectedEdge>();
        var internalEdges = new List<ConstraintEdge>();
        var seen = new HashSet<(string From, string To)>();

        foreach (var edge in graph.Edges)
        {
            var fromGroup = groupOf.GetValueOrDefault(edge.From);
            var toGroup = groupOf.GetValueOrDefault(edge.To);

            // 组内部的边在收缩图里必须去掉，否则会变成超节点指向自己。
            if (fromGroup is not null && fromGroup == toGroup)
            {
                internalEdges.Add(edge);
                continue;
            }

            var newFrom = fromGroup is null ? edge.From : SuperNodePrefix + fromGroup;
            var newTo = toGroup is null ? edge.To : SuperNodePrefix + toGroup;

            redirected.Add(new RedirectedEdge(newFrom, newTo, edge.From, edge.To));

            // 多条边改指之后可能变成同一对端点。引擎不区分重复边，这里去重，
            // 而每一条的原始端点都记在重定向表里，展开时按原始端点逐条还原。
            if (seen.Add((newFrom, newTo)))
            {
                edges.Add(new EngineEdge(newFrom, newTo));
            }
        }

        // 把每组自己的重定向边与组内边归位，展开时直接取用。
        var bound = expansions
            .Select(expansion =>
            {
                var members = expansion.Members.ToHashSet(StringComparer.Ordinal);

                return expansion with
                {
                    Redirected =
                    [
                        .. redirected.Where(r =>
                            (r.NewFrom == expansion.SuperNodeId && members.Contains(r.OriginalFrom))
                            || (r.NewTo == expansion.SuperNodeId && members.Contains(r.OriginalTo))),
                    ],
                    InternalEdges =
                    [
                        .. internalEdges.Where(e => members.Contains(e.From) && members.Contains(e.To)),
                    ],
                };
            })
            .ToArray();

        return new ContractionResult(new ContractedGraph([.. nodes], [.. edges]), bound);
    }

    /// <summary>
    /// 展开：把超节点占的矩形切成若干份，成员横向排列，纵向居中。
    /// </summary>
    /// <remarks>
    /// 之所以是纯局部操作：所需的空间在收缩阶段就已经申报给引擎了，
    /// 引擎按那个尺寸排布，超节点框内必然是空的。所以这里不需要检查碰撞，
    /// 也不会挤压任何邻居——如果这里还要做避让，说明收缩阶段的尺寸算错了。
    /// </remarks>
    public static PlacedNode[] Expand(
        IReadOnlyDictionary<string, PlacedNode> placed,
        IReadOnlyList<SameRankExpansion> expansions,
        ConstraintGraph graph)
    {
        var widthOf = graph.Nodes.ToDictionary(n => n.Id, n => (n.Width, n.Height), StringComparer.Ordinal);
        var result = new List<PlacedNode>();

        foreach (var node in placed.Values)
        {
            if (!TryGetSuperNodeId(node.Id, out var groupId))
            {
                result.Add(node);
                continue;
            }

            var expansion = expansions.First(e => string.Equals(e.GroupId, groupId, StringComparison.Ordinal));
            var cursor = node.X;

            foreach (var member in expansion.Members)
            {
                var (width, height) = widthOf[member];

                result.Add(new PlacedNode(
                    member,
                    cursor,
                    node.Y + ((node.Height - height) / 2),
                    width,
                    height));

                cursor += width + expansion.Gap;
            }
        }

        return [.. result];
    }
}
