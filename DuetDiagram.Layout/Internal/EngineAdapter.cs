using DuetDiagram.Core.Model;
using Mostlylucid.Dagre;
using Mostlylucid.Dagre.Indexed;

namespace DuetDiagram.Layout.Internal;

/// <summary>
/// 调用布局引擎。
/// </summary>
/// <remarks>
/// <para>
/// 这里刻意只做一件事：把归一化输入翻译成引擎输入，再把结果翻译回来。
/// 所有约束补齐的逻辑都在调用方的两侧，引擎适配层保持无状态、可替换。
/// 换引擎时只需要改这个文件，约束补齐的实现不受影响。
/// </para>
/// <para>
/// 必须用索引式入口。默认入口的内部索引用字符串，在千节点量级上要慢好几倍——
/// 实测同一张一千节点的图，默认入口要一千二百毫秒，索引式只要十几毫秒。
/// </para>
/// </remarks>
internal static class EngineAdapter
{
    public static Dictionary<string, PlacedNode> Compute(ContractedGraph graph, LayoutOptions options)
    {
        // 复合模式必须打开：该引擎的布局主流程里有一段处理跨层长边的逻辑会建立父子关系，
        // 而在非复合模式下建立父子关系会直接抛异常，连最简单的图形都跑不完。
        var dagre = new DagreGraph(compound: true);

        // 多图模式也必须打开：断开有向环时，引擎会把环上某条边反过来并给它起一个名字，
        // 而带名字的边在非多图模式下会被直接拒绝——图里只要有一个环就整张排不出来。
        // 打开它只放行这一步：我们自己加的边都不带名字，键的算法不看这个开关，
        // 所以同一对节点之间的重复边仍然和以前一样落到同一个键上。
        dagre._isMultigraph = true;

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

        IndexedDagreLayout.RunLayout(dagre);

        var placed = new Dictionary<string, PlacedNode>(StringComparer.Ordinal);

        foreach (var node in graph.Nodes)
        {
            var label = dagre.Node(node.Id);
            placed[node.Id] = new PlacedNode(node.Id, label.X, label.Y, label.Width, label.Height);
        }

        return placed;
    }

    /// <summary>
    /// 主方向到引擎的方向名。
    /// </summary>
    /// <remarks>
    /// 只有自上而下需要改名：我们叫 TB，引擎叫 TB，但内部的枚举写作 TopToBottom 时
    /// 引擎接受的是这一串缩写。其余三个方向两边同名。
    /// </remarks>
    private static string ToRankDir(Direction direction) => direction switch
    {
        Direction.TB => "TB",
        Direction.BT => "BT",
        Direction.LR => "LR",
        Direction.RL => "RL",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "未知方向"),
    };
}
