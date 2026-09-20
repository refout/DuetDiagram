using DuetDiagram.Core.Model;

namespace DuetDiagram.Benchmarks;

/// <summary>
/// 基准测试用的固定图形。
/// </summary>
/// <remarks>
/// 图形形状固定下来，基线才有可比性。换一个形状，即使节点数与边数相同，
/// 耗时也可能差几倍，那样的数字没法用来判断后续有没有退化。
/// </remarks>
internal static class TestGraphs
{
    /// <summary>基准测试用的节点尺寸，取一个常见的流程图节点大小。</summary>
    public const double NodeWidth = 80;

    public const double NodeHeight = 40;

    /// <summary>
    /// 多层图：每层若干并列节点，层间连接同序号节点。
    /// </summary>
    /// <remarks>
    /// 这是真实流程图与架构图最常见的形态，也是布局算法压力最大的形态之一：
    /// 层数决定分层阶段的工作量，每层宽度决定层内排序与坐标分配的工作量。
    /// </remarks>
    public static (string[] Nodes, (string From, string To)[] Edges) Layered(int depth, int breadth)
    {
        var nodes = new List<string>(depth * breadth);
        var edges = new List<(string, string)>();

        for (var layer = 0; layer < depth; layer++)
        {
            for (var index = 0; index < breadth; index++)
            {
                nodes.Add($"n{layer}-{index}");
            }
        }

        for (var layer = 0; layer < depth - 1; layer++)
        {
            for (var index = 0; index < breadth; index++)
            {
                edges.Add(($"n{layer}-{index}", $"n{layer + 1}-{index}"));
            }
        }

        return ([.. nodes], [.. edges]);
    }

    /// <summary>
    /// 构造一份用于序列化与哈希测量的文档。
    /// </summary>
    /// <remarks>
    /// 直接用记录构造，不走命令层。原因是命令层每执行一条都会重算全量哈希，
    /// 用它组装千节点文档是平方复杂度，光组装就要几分钟，基准测试的时间会全花在准备上。
    /// 这里只需要一份内容正确的文档，不需要它满足命令层的全部不变量。
    /// </remarks>
    public static DiagramDocument Document(int depth, int breadth)
    {
        var (nodeIds, edgeIds) = Layered(depth, breadth);

        var nodes = nodeIds
            .Select((id, index) => new NodeDef
            {
                Id = id,
                Label = $"节点 {index}",
                Shape = index % 7 == 0 ? NodeShape.Diamond : NodeShape.Rect,
                StyleToken = index % 5 == 0 ? "primary" : null,
            })
            .ToList();

        var edges = edgeIds
            .Select(e => new EdgeDef { Id = $"{e.From}->{e.To}", From = e.From, To = e.To })
            .ToList();

        return new DiagramDocument(
            $"bench-{depth}x{breadth}",
            DiagramKind.Flowchart,
            Direction.TB,
            version: edges.Count,
            structuralHash: "bench-structural",
            visualHash: "bench-visual",
            nodes,
            edges);
    }
}
