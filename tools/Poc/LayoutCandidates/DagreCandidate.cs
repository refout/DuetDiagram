using System.Reflection;
using Mostlylucid.Dagre;
using Mostlylucid.Dagre.Indexed;

namespace DuetDiagram.Poc.LayoutCandidates;

/// <summary>
/// 备选引擎的适配器。
/// </summary>
/// <remarks>
/// 这个库的输入模型比主选丰富：节点带坐标字段、边带最小层距、图级带对齐方式。
/// 但"字段存在"不等于"字段是输入"——坐标字段在分层布局里通常是算出来的输出，
/// 写进去也会被覆盖。所以除了反射列出成员，还要实际跑一次把坐标填进去看能不能保住。
/// </remarks>
internal sealed class DagreCandidate : ILayoutCandidate
{
    private readonly bool _useIndexedLayout;

    public DagreCandidate(bool useIndexedLayout = false)
    {
        _useIndexedLayout = useIndexedLayout;
    }

    public string Name => _useIndexedLayout
        ? "Mostlylucid.Dagre 2.0.1（索引式布局）"
        : "Mostlylucid.Dagre 2.0.1（默认布局）";

    public LayoutInputFacts InputFacts { get; } = ProbeInputFacts();

    public CandidateResult Compute(CandidateGraph graph, LayoutKnobs knobs)
    {
        var (dagre, handle) = Build(graph, knobs);

        // 两条入口的算法相同，区别在于内部用整数下标还是字符串标识做索引。
        // 大图上这个差别可能很大，所以两条都留着，由外面决定跑哪条。
        if (_useIndexedLayout)
        {
            IndexedDagreLayout.RunLayout(dagre);
        }
        else
        {
            DagreLayout.RunLayout(dagre);
        }

        var boxes = graph.Nodes
            .Select(n =>
            {
                // 查询时必须用建立时拿到的句柄，而不是节点的标识字符串：
                // 复合图内部会插入边界节点并改写标识，用原始标识查不到。
                var label = dagre.Node(handle[n.Id]);

                return new CandidateBox(n.Id, label.X, label.Y, label.Width, label.Height);
            })
            .ToArray();

        var groups = graph.Groups
            .Where(g => handle.ContainsKey(g.Id))
            .Select(g =>
            {
                var label = dagre.Node(handle[g.Id]);
                return new CandidateBox(g.Id, label.X, label.Y, label.Width, label.Height);
            })
            .ToArray();

        var graphLabel = dagre.Graph();

        return new CandidateResult(boxes, groups, graphLabel.Width, graphLabel.Height);
    }

    /// <summary>
    /// 把两个节点的坐标预先写进输入，跑完布局看它们是否被保留。
    /// </summary>
    /// <remarks>
    /// 这是判断"能不能固定位置"的唯一可靠方式。分层布局的节点坐标通常是输出，
    /// 预先写入会被算法覆盖；如果覆盖了，说明这个库也只能自动布局，
    /// 人工拖动的位置同样带不进下一次布局。
    /// </remarks>
    public PinProbeResult ProbePinned(CandidateGraph graph, LayoutKnobs knobs)
    {
        var (dagre, handle) = Build(graph, knobs);

        var pinned = new Dictionary<string, (float X, float Y)>();
        var ids = graph.Nodes.Select(n => n.Id).Take(2).ToArray();

        for (var i = 0; i < ids.Length; i++)
        {
            // 故意取一个远离任何自然布局结果的坐标，避免"碰巧算成一样"造成误判。
            var x = 1000f + (i * 500);
            var y = 1000f + (i * 500);

            pinned[ids[i]] = (x, y);

            var label = dagre.Node(handle[ids[i]]);
            label.X = x;
            label.Y = y;
        }

        DagreLayout.RunLayout(dagre);

        var honored = pinned.All(kv =>        {
            var label = dagre.Node(handle[kv.Key]);
            return Math.Abs(label.X - kv.Value.X) < 0.5 && Math.Abs(label.Y - kv.Value.Y) < 0.5;
        });

        var evidence = string.Join("，", pinned.Select(kv =>
        {
            var label = dagre.Node(handle[kv.Key]);
            return $"{kv.Key} 写入 ({kv.Value.X:0},{kv.Value.Y:0}) 得到 ({label.X:0},{label.Y:0})";
        }));

        return new PinProbeResult(true, honored, evidence);
    }

    /// <summary>
    /// 用一条最小层距为 0 的边把两个节点拉到同一层。
    /// </summary>
    /// <remarks>
    /// 这是分层布局里的通行做法：边的层距表示"两端至少相差几层"，取 0 就表示允许同层。
    /// 之所以用这种方式而不是某个专门的"同层"字段，是因为输入模型里没有这样的字段。
    /// </remarks>
    public SameRankProbeResult ProbeSameRank(CandidateGraph graph, LayoutKnobs knobs)
    {
        // 构造 a → b → c 再加 a → d：不加约束时 c 在第三层、d 在第二层。
        CandidateGraph probeGraph = new(
            "同层探测",
            [
                new CandidateNode("a", 80, 40),
                new CandidateNode("b", 80, 40),
                new CandidateNode("c", 80, 40),
                new CandidateNode("d", 80, 40),
            ],
            [
                new CandidateEdge("a", "b"),
                new CandidateEdge("b", "c"),
                new CandidateEdge("a", "d"),
                new CandidateEdge("c", "d"),
            ],
            []);

        var plainEdges = probeGraph with { Edges = [.. probeGraph.Edges.Take(3)] };

        var plain = Compute(plainEdges, knobs);
        var constrained = ComputeWithZeroMinlen(probeGraph, knobs, out var failure);

        if (constrained is null)
        {
            // 第一条路径不通。改用"强制层号"这条：它需要在布局前给出目标层号，
            // 所以要先跑一遍拿到自然层号，再把两个节点都锁在较大的那一层。
            var ranked = ComputeWithFixedRank(plainEdges, knobs, "c", "d", out var rankFailure);

            if (ranked is null)
            {
                return new SameRankProbeResult(
                    true,
                    false,
                    $"层距为 0 的边不可用（{failure}）；强制层号也不可用（{rankFailure}）");
            }

            var plainGap = Math.Abs(plain.Find("c")!.Y - plain.Find("d")!.Y);
            var rankedGap = Math.Abs(ranked.Find("c")!.Y - ranked.Find("d")!.Y);
            var rankedHonored = rankedGap < 0.5 && plainGap > 0.5;

            return new SameRankProbeResult(
                true,
                rankedHonored,
                $"层距为 0 的边不可用（{failure}）；改用强制层号：未加约束时纵坐标相差 {plainGap:0}，加之后相差 {rankedGap:0}");
        }

        var plainDelta = Math.Abs(plain.Find("c")!.Y - plain.Find("d")!.Y);
        var constrainedDelta = Math.Abs(constrained.Find("c")!.Y - constrained.Find("d")!.Y);

        var honored = constrainedDelta < 0.5 && plainDelta > 0.5;

        return new SameRankProbeResult(
            true,
            honored,
            $"未加约束时 c 与 d 纵坐标相差 {plainDelta:0}，加最小层距为 0 的边之后相差 {constrainedDelta:0}");
    }

    private CandidateResult? ComputeWithZeroMinlen(CandidateGraph graph, LayoutKnobs knobs, out string failure)
    {
        try
        {
            var dagre = new DagreGraph(compound: true);

            dagre.SetGraph(new GraphLabel
            {
                RankDir = ToRankDir(knobs.Direction),
                NodeSep = (int)Math.Round(knobs.NodeSpacing),
                RankSep = (int)Math.Round(knobs.LayerSpacing),
            });

            foreach (var node in graph.Nodes)
            {
                dagre.SetNode(node.Id, new NodeLabel { Width = (float)node.Width, Height = (float)node.Height });
            }

            for (var i = 0; i < graph.Edges.Length; i++)
            {
                var edge = graph.Edges[i];

                // 最后一条边用 0 层距表达"允许同层"。
                dagre.SetEdge(edge.From, edge.To, new EdgeLabel { Minlen = i == graph.Edges.Length - 1 ? 0 : 1, Weight = 1 });
            }

            IndexedDagreLayout.RunLayout(dagre);

            var result = new CandidateResult(
                [.. graph.Nodes.Select(n =>
                {
                    var label = dagre.Node(n.Id);
                    return new CandidateBox(n.Id, label.X, label.Y, label.Width, label.Height);
                })],
                [],
                dagre.Graph().Width,
                dagre.Graph().Height);

            failure = string.Empty;
            return result;
        }
        catch (Exception ex)
        {
            failure = $"{ex.GetType().Name}：{ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// 用两遍布局把两个节点压到同一层。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 第一遍按正常方式算，读出两个节点各自落在第几层；第二遍把两个节点的最小层号和最大层号
    /// 都设成二者中较大的那个，也就是强制它们落在同一层。
    /// </para>
    /// <para>
    /// 之所以要两遍，是因为层号取决于图形本身，事先算不出来；
    /// 而"强制落在第几层"这种输入必须在布局开始前就给出去。
    /// </para>
    /// </remarks>
    private CandidateResult? ComputeWithFixedRank(CandidateGraph graph, LayoutKnobs knobs, string first, string second, out string failure)
    {
        try
        {
            // 第一遍：拿到两个节点的自然层号。
            var probe = Build(graph, knobs);
            IndexedDagreLayout.RunLayout(probe.Graph);

            var targetRank = Math.Max(
                probe.Graph.Node(probe.Handle[first]).Rank,
                probe.Graph.Node(probe.Handle[second]).Rank);

            // 第二遍：把两个节点都锁在这一层。
            var (dagre, handle) = Build(graph, knobs);

            foreach (var id in new[] { first, second })
            {
                var label = dagre.Node(handle[id]);
                label.MinRank = targetRank;
                label.MaxRank = targetRank;
            }

            IndexedDagreLayout.RunLayout(dagre);

            var result = new CandidateResult(
                [.. graph.Nodes.Select(n =>
                {
                    var label = dagre.Node(handle[n.Id]);
                    return new CandidateBox(n.Id, label.X, label.Y, label.Width, label.Height);
                })],
                [],
                dagre.Graph().Width,
                dagre.Graph().Height);

            failure = string.Empty;
            return result;
        }
        catch (Exception ex)
        {
            failure = $"{ex.GetType().Name}：{ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// 建立图并记录每个业务标识对应的内部句柄。
    /// </summary>
    /// <remarks>
    /// 复合图模式下，分组会被当作一种特殊节点插进图里，并且引擎可能给它们改标识。
    /// 直接拿业务标识去查询会失败，所以这里在建立时就把对应关系留好。
    /// </remarks>
    private static (DagreGraph Graph, Dictionary<string, string> Handle) Build(CandidateGraph graph, LayoutKnobs knobs)
    {
        // 复合模式必须无条件打开。该库的布局主流程里有一段处理跨层长边的逻辑会调用建立父子关系，
        // 而建立父子关系在非复合模式下会直接抛异常。也就是说传 false 时连最简单的菱形图都跑不完，
        // 与"有没有分组"无关。这里统一传 true，分组仍然只在确实存在时才建立。
        var dagre = new DagreGraph(compound: true);

        dagre.SetGraph(new GraphLabel
        {
            RankDir = ToRankDir(knobs.Direction),
            NodeSep = (int)Math.Round(knobs.NodeSpacing),
            RankSep = (int)Math.Round(knobs.LayerSpacing),
        });

        var handle = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var node in graph.Nodes)
        {
            dagre.SetNode(node.Id, new NodeLabel { Width = (float)node.Width, Height = (float)node.Height });
            handle[node.Id] = node.Id;
        }

        foreach (var edge in graph.Edges)
        {
            // 最小层距为 1 表示两端至少相差一层。默认值就是这个含义，
            // 显式写出来是为了避免以后默认值变化时行为悄悄改变。
            dagre.SetEdge(edge.From, edge.To, new EdgeLabel { Minlen = 1, Weight = 1 });
        }

        if (graph.Groups.Length > 0)
        {
            foreach (var group in graph.Groups)
            {
                AddGroup(dagre, handle, group, parentId: null);
            }
        }

        return (dagre, handle);
    }

    private static void AddGroup(DagreGraph dagre, Dictionary<string, string> handle, CandidateGroup group, string? parentId)
    {
        // 分组本身在图里也是一个节点，只是被标记为组并且下面挂成员。
        dagre.SetNode(group.Id, new NodeLabel { IsGroup = true });
        handle[group.Id] = group.Id;

        if (parentId is not null)
        {
            dagre.SetParent(group.Id, parentId);
        }

        foreach (var member in group.Members)
        {
            dagre.SetParent(member, group.Id);
        }

        foreach (var child in group.Children)
        {
            AddGroup(dagre, handle, child, group.Id);
        }
    }

    private static string ToRankDir(string direction) => direction switch
    {
        "TD" => "TB",
        "BT" => "BT",
        "LR" => "LR",
        "RL" => "RL",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "未知方向"),
    };

    private static LayoutInputFacts ProbeInputFacts()
    {
        var nodeMembers = typeof(NodeLabel).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToArray();
        var layoutMembers = typeof(GraphLabel).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name)
            .Concat(typeof(EdgeLabel).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name))
            .ToArray();

        return new LayoutInputFacts(
            HasPinnedField: ContainsAny(nodeMembers, "X", "Y", "Position", "Pin", "Fixed"),
            HasOrderOrAlignOrPlaceField: ContainsAny(nodeMembers.Concat(layoutMembers), "Order", "Align", "Place", "Nudge"),
            NodeMembers: string.Join(", ", nodeMembers),
            LayoutMembers: string.Join(", ", layoutMembers));
    }

    private static bool ContainsAny(IEnumerable<string> members, params string[] names) =>
        members.Any(member => names.Any(name => member.Contains(name, StringComparison.OrdinalIgnoreCase)));
}
