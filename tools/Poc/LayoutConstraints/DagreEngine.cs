using Mostlylucid.Dagre;
using Mostlylucid.Dagre.Indexed;

namespace DuetDiagram.Poc.LayoutConstraints;

/// <summary>
/// 调用布局引擎。
/// </summary>
/// <remarks>
/// 这里刻意只做一件事：把归一化输入翻译成引擎输入，再把结果翻译回来。
/// 所有约束补齐的逻辑都在调用方的两侧，引擎适配层保持无状态、可替换。
/// 换引擎时只需要改这个文件，约束补齐的实现不受影响。
/// </remarks>
internal static class DagreEngine
{
    public static Dictionary<string, PlacedNode> Compute(ContractedGraph graph, ConstraintOptions options)
    {
        // 复合模式必须打开：该引擎的布局主流程里有一段处理跨层长边的逻辑会建立父子关系，
        // 而在非复合模式下建立父子关系会直接抛异常，连最简单的图形都跑不完。
        var dagre = new DagreGraph(compound: true);

        dagre.SetGraph(new GraphLabel
        {
            RankDir = ToRankDir(options.Direction),
            NodeSep = (int)Math.Round(options.NodeSpacing),
            RankSep = (int)Math.Round(options.LayerSpacing),
        });

        foreach (var node in graph.Nodes)
        {
            dagre.SetNode(node.Id, new NodeLabel { Width = (float)node.Width, Height = (float)node.Height });
        }

        foreach (var edge in graph.Edges)
        {
            dagre.SetEdge(edge.From, edge.To, new EdgeLabel { Minlen = 1, Weight = 1 });
        }

        // 用索引式入口。默认入口的内部索引用字符串，在千节点量级上要慢好几倍。
        IndexedDagreLayout.RunLayout(dagre);

        var placed = new Dictionary<string, PlacedNode>(StringComparer.Ordinal);

        foreach (var node in graph.Nodes)
        {
            var label = dagre.Node(node.Id);
            placed[node.Id] = new PlacedNode(node.Id, label.X, label.Y, label.Width, label.Height);
        }

        return placed;
    }

    private static string ToRankDir(string direction) => direction switch
    {
        "TD" => "TB",
        "BT" => "BT",
        "LR" => "LR",
        "RL" => "RL",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "未知方向"),
    };
}
