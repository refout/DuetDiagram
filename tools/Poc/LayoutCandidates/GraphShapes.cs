namespace DuetDiagram.Poc.LayoutCandidates;

/// <summary>
/// 标准图形集合。
/// </summary>
/// <remarks>
/// <para>
/// 所有引擎在同一天平上比较，靠的就是这一组固定图形。图形覆盖四类压力：
/// 交叉密度、层数、同层宽度、分组嵌套。
/// </para>
/// <para>
/// 其中"带交叉的多层图"是本次验证的关键用例：它在一款候选引擎上暴露出横向坐标失控，
/// 而输入本身完全合法。判断另一款候选是否可用，主要就看它在这些图形上的坐标是否有界。
/// </para>
/// </remarks>
internal static class GraphShapes
{
    /// <summary>菱形：最基础的分叉再汇聚，任何引擎都必须处理正确。</summary>
    public static CandidateGraph Diamond() => new(
        "菱形",
        [
            new CandidateNode("a", 80, 40),
            new CandidateNode("b", 80, 40),
            new CandidateNode("c", 80, 40),
            new CandidateNode("d", 80, 40),
        ],
        [
            new CandidateEdge("a", "b"),
            new CandidateEdge("a", "c"),
            new CandidateEdge("b", "d"),
            new CandidateEdge("c", "d"),
        ],
        []);

    /// <summary>长链：层数等于节点数，用来压分层以及跨层边的展开。</summary>
    public static CandidateGraph Chain(int count)
    {
        var nodes = Enumerable.Range(0, count).Select(i => new CandidateNode($"n{i}", 80, 40)).ToArray();
        var edges = Enumerable.Range(0, count - 1).Select(i => new CandidateEdge($"n{i}", $"n{i + 1}")).ToArray();

        return new CandidateGraph($"长链 {count}", nodes, edges, []);
    }

    /// <summary>
    /// 多层图：每层若干并列节点，层间连接同序号与相邻序号。
    /// </summary>
    /// <param name="withCrossings">
    /// 是否加入错位连接。关掉之后图形完全相同只是没有交叉，
    /// 两者的横向宽度应当接近，差距过大说明交叉处理有问题。
    /// </param>
    public static CandidateGraph Layered(int depth, int breadth, bool withCrossings)
    {
        var nodes = new List<CandidateNode>(depth * breadth);

        for (var layer = 0; layer < depth; layer++)
        {
            for (var index = 0; index < breadth; index++)
            {
                nodes.Add(new CandidateNode($"n{layer}-{index}", 80, 40));
            }
        }

        var edges = new List<CandidateEdge>();

        for (var layer = 0; layer < depth - 1; layer++)
        {
            for (var index = 0; index < breadth; index++)
            {
                edges.Add(new CandidateEdge($"n{layer}-{index}", $"n{layer + 1}-{index}"));

                if (withCrossings)
                {
                    edges.Add(new CandidateEdge($"n{layer}-{index}", $"n{layer + 1}-{(index + 1) % breadth}"));
                }
            }
        }

        return new CandidateGraph(
            $"多层 {depth}×{breadth}{(withCrossings ? " 带交叉" : " 无交叉")}",
            [.. nodes],
            [.. edges],
            []);
    }

    /// <summary>
    /// 完全二分串联：每层的每个节点连到下一层的每个节点。
    /// </summary>
    /// <remarks>
    /// 这是"一个判断分出两条路，两条路各自再分出两条"的抽象，交叉最密集而规模最小，
    /// 用来判断引擎在极小输入上是否就会失控。两列十层只有 20 个节点。
    /// </remarks>
    public static CandidateGraph Bipartite(int depth, int breadth)
    {
        var nodes = new List<CandidateNode>((depth + 1) * breadth);

        for (var layer = 0; layer <= depth; layer++)
        {
            for (var index = 0; index < breadth; index++)
            {
                nodes.Add(new CandidateNode($"n{layer}-{index}", 80, 40));
            }
        }

        var edges = new List<CandidateEdge>();

        for (var layer = 0; layer < depth; layer++)
        {
            for (var from = 0; from < breadth; from++)
            {
                for (var to = 0; to < breadth; to++)
                {
                    edges.Add(new CandidateEdge($"n{layer}-{from}", $"n{layer + 1}-{to}"));
                }
            }
        }

        return new CandidateGraph($"完全二分 {depth + 1}×{breadth}", [.. nodes], [.. edges], []);
    }

    /// <summary>复合图：两层嵌套分组，用来验证分组框与子节点的包含关系。</summary>
    public static CandidateGraph Compound() => new(
        "复合分组",
        [
            new CandidateNode("web", 80, 40),
            new CandidateNode("api", 80, 40),
            new CandidateNode("db", 80, 40),
            new CandidateNode("job", 80, 40),
        ],
        [
            new CandidateEdge("web", "api"),
            new CandidateEdge("api", "db"),
            new CandidateEdge("db", "job"),
        ],
        [
            new CandidateGroup("outer", "Outer", [], [
                new CandidateGroup("backend", "Backend", ["api", "db"], []),
            ]),
            new CandidateGroup("frontend", "Frontend", ["web"], []),
        ]);
}
