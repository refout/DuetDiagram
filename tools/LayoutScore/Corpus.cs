using DuetDiagram.Core.Model;

namespace LayoutScore;

/// <summary>
/// 一份种子图。
/// </summary>
/// <param name="Id">标识。报告与人工评分按它对上号。</param>
/// <param name="Note">这是什么形状，一句话。</param>
/// <param name="Document">要排的那份文档。</param>
internal sealed record SeedGraph(string Id, string Note, DiagramDocument Document);

/// <summary>
/// 种子语料。
/// </summary>
/// <remarks>
/// <para>
/// **写死在这里，不从文件读。** 与覆盖率取证装置的分母同一条口径：语料是判据的一部分，
/// 换一批图就会换一个分数，所以它必须是仓库里的一份确定内容，而不是"跑的时候看目录里有什么"。
/// </para>
/// <para>
/// 形状挑的是流程图与架构图最常见的几种：链、多层网格、菱形分支、扇出、树、带回边的环。
/// 前六种是顺向的（每一条边都顺着主方向），最后一种刻意带一条回边——方向一致那一项
/// 在它上面本来就该低，低多少由装置量出来，而不是假装它不存在。
/// </para>
/// <para>
/// 有一份带同层约束，用来走一遍"约束进布局"的路径。约束的归属方写 <c>Llm</c>：
/// 语料是装置造的，不是人手设的。
/// </para>
/// </remarks>
internal static class Corpus
{
    /// <summary>语料里所有图，按报告里的次序。</summary>
    public static IReadOnlyList<SeedGraph> Graphs { get; } =
    [
        new("chain-8", "八节点直线链，自上而下", Chain(8, Direction.TB)),
        new("layered-4x3", "四层、每层三个，自上而下", Layered(4, 3, Direction.TB)),
        new("layered-3x4", "三层、每层四个，自左而右", Layered(3, 4, Direction.LR)),
        new("layered-6x5", "六层、每层五个，自上而下（本批最大）", Layered(6, 5, Direction.TB)),
        new("diamond", "一个起点分两路再汇合", Diamond(Direction.TB)),
        new("fork-6", "一个起点分六路再汇合", Fork(6, Direction.TB)),
        new("tree-3", "三层二叉树", Tree(3, Direction.TB)),
        new("fan-12", "一进十二出再汇合，自左而右", Fork(12, Direction.LR)),
        new("cycle-4", "四节点环，带回边", Cycle(4)),
        new("k33", "三对三全连（9 条边），最密的一种", CompleteBipartite(3)),
        new("same-rank", "带同层约束的菱形", SameRank()),
    ];

    /// <summary>一个节点量出来的尺寸。固定值，好让同一份语料两次跑出同一个数。</summary>
    public static Size Measure(NodeDef node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return new Size(80, 40);
    }

    /// <summary>直线链。</summary>
    private static DiagramDocument Chain(int count, Direction direction)
    {
        var nodes = new List<NodeDef>(count);
        var edges = new List<EdgeDef>(Math.Max(0, count - 1));

        for (var index = 0; index < count; index++)
        {
            nodes.Add(Node($"n{index}"));
        }

        for (var index = 0; index + 1 < count; index++)
        {
            edges.Add(Edge($"n{index}", $"n{index + 1}"));
        }

        return Doc($"chain-{count}", direction, nodes, edges);
    }

    /// <summary>多层网格：每层若干并列节点，层间连同序号。</summary>
    private static DiagramDocument Layered(int depth, int breadth, Direction direction)
    {
        var nodes = new List<NodeDef>(depth * breadth);
        var edges = new List<EdgeDef>();

        for (var layer = 0; layer < depth; layer++)
        {
            for (var index = 0; index < breadth; index++)
            {
                nodes.Add(Node($"n{layer}-{index}"));
            }
        }

        for (var layer = 0; layer + 1 < depth; layer++)
        {
            for (var index = 0; index < breadth; index++)
            {
                edges.Add(Edge($"n{layer}-{index}", $"n{layer + 1}-{index}"));
            }
        }

        return Doc($"layered-{depth}x{breadth}", direction, nodes, edges);
    }

    /// <summary>一个起点分出两条路，再汇到一个终点。</summary>
    private static DiagramDocument Diamond(Direction direction) =>
        Doc(
            "diamond",
            direction,
            [Node("start"), Node("left"), Node("right"), Node("end")],
            [Edge("start", "left"), Edge("start", "right"), Edge("left", "end"), Edge("right", "end")]);

    /// <summary>一个起点分出若干条并列的路，再汇到一个终点。</summary>
    private static DiagramDocument Fork(int branches, Direction direction)
    {
        var nodes = new List<NodeDef>(branches + 2) { Node("p") };
        var edges = new List<EdgeDef>();

        for (var index = 0; index < branches; index++)
        {
            var branch = $"b{index}";
            nodes.Add(Node(branch));
            edges.Add(Edge("p", branch));
            edges.Add(Edge(branch, "q"));
        }

        nodes.Add(Node("q"));

        return Doc($"fork-{branches}", direction, nodes, edges);
    }

    /// <summary>满二叉树。</summary>
    private static DiagramDocument Tree(int depth, Direction direction)
    {
        var nodes = new List<NodeDef>();
        var edges = new List<EdgeDef>();
        var last = (1 << depth) - 1;

        for (var index = 0; index < last; index++)
        {
            nodes.Add(Node($"t{index}"));

            var left = (2 * index) + 1;
            var right = left + 1;

            if (left < last)
            {
                edges.Add(Edge($"t{index}", $"t{left}"));
                edges.Add(Edge($"t{index}", $"t{right}"));
            }
        }

        return Doc($"tree-{depth}", direction, nodes, edges);
    }

    /// <summary>四节点环：最后一条边是回边。</summary>
    private static DiagramDocument Cycle(int count)
    {
        var nodes = new List<NodeDef>(count);
        var edges = new List<EdgeDef>(count);

        for (var index = 0; index < count; index++)
        {
            nodes.Add(Node($"c{index}"));
        }

        for (var index = 0; index < count; index++)
        {
            edges.Add(Edge($"c{index}", $"c{(index + 1) % count}"));
        }

        return Doc($"cycle-{count}", Direction.TB, nodes, edges);
    }

    /// <summary>
    /// 两边各若干个、两两相连。
    /// </summary>
    /// <remarks>
    /// 三对三是最密的一种两层图：九条边挤在同一个层间空隙里。它量的不是"能不能排"，
    /// 而是密集输入下外接框会不会被撑开——九条边里每一条都要一个拐点，
    /// 而拐点只有那一个空隙可用。
    /// </remarks>
    private static DiagramDocument CompleteBipartite(int side)
    {
        var nodes = new List<NodeDef>(side * 2);
        var edges = new List<EdgeDef>(side * side);

        for (var index = 0; index < side; index++)
        {
            nodes.Add(Node($"l{index}"));
        }

        for (var index = 0; index < side; index++)
        {
            nodes.Add(Node($"r{index}"));
        }

        for (var left = 0; left < side; left++)
        {
            for (var right = 0; right < side; right++)
            {
                edges.Add(Edge($"l{left}", $"r{right}"));
            }
        }

        return Doc($"k{side}{side}", Direction.TB, nodes, edges);
    }

    /// <summary>菱形，但两条分支被要求同层。</summary>
    private static DiagramDocument SameRank()
    {
        var layout = new LayoutHints
        {
            SameRank =
            [
                new Constraint<SameRankConstraint>(
                    new SameRankConstraint(["left", "right"]),
                    ConstraintOwner.Llm,
                    DateTimeOffset.UnixEpoch),
            ],
        };

        return Doc(
            "same-rank",
            Direction.TB,
            [Node("start"), Node("left"), Node("right"), Node("end")],
            [Edge("start", "left"), Edge("start", "right"), Edge("left", "end"), Edge("right", "end")],
            layout);
    }

    private static NodeDef Node(string id) => new() { Id = id, Label = id };

    private static EdgeDef Edge(string from, string to) =>
        new() { Id = $"{from}->{to}", From = from, To = to };

    private static DiagramDocument Doc(
        string id,
        Direction direction,
        IReadOnlyList<NodeDef> nodes,
        IReadOnlyList<EdgeDef> edges,
        LayoutHints? layout = null) =>
        new(
            id,
            DiagramKind.Flowchart,
            direction,
            version: 0,
            structuralHash: string.Empty,
            visualHash: string.Empty,
            pages: null,
            layers: null,
            nodes: nodes,
            edges: edges,
            composites: null,
            tags: null,
            actions: null,
            fonts: null,
            textPresets: null,
            palette: null,
            layout: layout,
            canvas: null);
}
