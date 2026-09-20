using BenchmarkDotNet.Attributes;
using DuetDiagram.Core.Serialization;
using Mostlylucid.Dagre;
using Mostlylucid.Dagre.Indexed;

namespace DuetDiagram.Benchmarks;

/// <summary>
/// 布局引擎的耗时基线。
/// </summary>
/// <remarks>
/// <para>
/// 只测引擎本身，不含我们自己的约束补齐逻辑。原因是这样的：
/// Phase 1 的验收目标（100 节点不超过 50 毫秒、1000 节点不超过 1000 毫秒）
/// 是拿这次基线乘一点二倍来算的。如果把我们的补齐逻辑算进去，
/// 那个目标就会把当前的实现细节一起固化下来，将来改进补齐逻辑反而变成"没达标"。
/// </para>
/// <para>
/// 补齐逻辑的开销单独记录，见报告，两者相加才是用户感知到的布局耗时。
/// </para>
/// </remarks>
[MemoryDiagnoser]
[ShortRunJob]
public class LayoutBenchmarks
{
    private (string[] Nodes, (string From, string To)[] Edges) _small;
    private (string[] Nodes, (string From, string To)[] Edges) _large;

    [GlobalSetup]
    public void Setup()
    {
        _small = TestGraphs.Layered(depth: 10, breadth: 10);
        _large = TestGraphs.Layered(depth: 20, breadth: 50);
    }

    /// <summary>100 节点：构造输入加求解布局。</summary>
    [Benchmark]
    public double Layout_100_Nodes()
    {
        var graph = Build(_small);
        IndexedDagreLayout.RunLayout(graph);

        return graph.Graph().Width;
    }

    /// <summary>1000 节点：构造输入加求解布局。</summary>
    [Benchmark]
    public double Layout_1000_Nodes()
    {
        var graph = Build(_large);
        IndexedDagreLayout.RunLayout(graph);

        return graph.Graph().Width;
    }

    /// <summary>
    /// 只构造输入不求解。
    /// </summary>
    /// <remarks>
    /// 用它把"构造输入"这部分开销从上面的数字里分出来。
    /// 如果构造占了大部分，那说明瓶颈在我们的适配层而不是引擎，优化方向完全不同。
    /// </remarks>
    [Benchmark]
    public int Build_1000_Nodes()
    {
        var graph = Build(_large);

        return graph.Nodes().Length;
    }

    private static DagreGraph Build((string[] Nodes, (string From, string To)[] Edges) source)
    {
        // 复合模式必须打开：该引擎在非复合模式下连最简单的图形都跑不完。
        var graph = new DagreGraph(compound: true);

        graph.SetGraph(new GraphLabel { RankDir = "TB", NodeSep = 36, RankSep = 72 });

        foreach (var node in source.Nodes)
        {
            graph.SetNode(node, new NodeLabel { Width = (float)TestGraphs.NodeWidth, Height = (float)TestGraphs.NodeHeight });
        }

        foreach (var (from, to) in source.Edges)
        {
            graph.SetEdge(from, to, new EdgeLabel { Minlen = 1, Weight = 1 });
        }

        return graph;
    }
}

/// <summary>
/// 序列化与哈希的耗时基线。
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class SerializationBenchmarks
{
    private readonly DuetDiagram.Core.Model.DiagramDocument _document1000 = TestGraphs.Document(depth: 20, breadth: 50);
    private readonly DuetDiagram.Core.Model.DiagramDocument _document100 = TestGraphs.Document(depth: 10, breadth: 10);

    /// <summary>千节点全量序列化。Phase 1 的目标是不超过 20 毫秒。</summary>
    [Benchmark]
    public int SerializeFull_1000_Nodes() => DiagramSerializer.SerializeFull(_document1000).Length;

    [Benchmark]
    public int SerializeFull_100_Nodes() => DiagramSerializer.SerializeFull(_document100).Length;

    /// <summary>千节点全量结构哈希。Phase 1 的目标是不超过 5 毫秒。</summary>
    [Benchmark]
    public int StructuralHash_1000_Nodes() => DiagramHashing.ComputeStructuralHash(_document1000).Length;

    [Benchmark]
    public int VisualHash_1000_Nodes() => DiagramHashing.ComputeVisualHash(_document1000).Length;

    /// <summary>千节点反序列化。与序列化配对，用来判断往返成本是否对称。</summary>
    [Benchmark]
    public int DeserializeFull_1000_Nodes()
    {
        var json = DiagramSerializer.SerializeFull(_document1000);

        return DiagramSerializer.DeserializeFull(json).Nodes.Count;
    }
}
