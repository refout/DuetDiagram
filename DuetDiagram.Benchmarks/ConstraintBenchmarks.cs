using BenchmarkDotNet.Attributes;
using DuetDiagram.Core.Model;
using DuetDiagram.Layout;

namespace DuetDiagram.Benchmarks;

/// <summary>
/// 约束补齐逻辑的耗时基线。
/// </summary>
/// <remarks>
/// <para>
/// 类名里带 <c>LayoutBenchmarks</c> 是**有意**的：验收命令按这个名字过滤，
/// 两条基线要在同一次运行里出数。引擎慢要换引擎，补齐逻辑慢要改算法，两件事必须分开看。
/// </para>
/// <para>
/// **补齐开销读的是与"无约束"那一条之差。** 引擎调用在流水线正中间，从外面没法把它摘出去
/// 单独计时；而几条基线的引擎输入完全相同——固定位置与中级约束都不进引擎，
/// 它们是在引擎之外补齐的——所以差值剩下的就是补齐逻辑本身。
/// </para>
/// <para>
/// 差值法的分辨率取决于约束那一边有多重，所以给了两条轻重不同的约束输入：
/// 一条是常见情形（固定节点骑在层边界上），一条是路由的最坏情形（整层被钉到别处）。
/// 实测下来常见情形的差值落在噪声里，最坏情形才读得出来——这本身就是结论，
/// 它说明**补齐逻辑的代价主要由固定节点的位置决定，而不是由约束的条数决定**。
/// </para>
/// </remarks>
[MemoryDiagnoser]
[ShortRunJob]
public class ConstraintLayoutBenchmarks
{
    private ConstraintLayoutEngine _engine = null!;
    private LayoutRequest _free = null!;
    private LayoutRequest _constrained = null!;
    private LayoutRequest _teleported = null!;

    [GlobalSetup]
    public void Setup()
    {
        _engine = new ConstraintLayoutEngine();

        var (nodeIds, edgeIds) = TestGraphs.Layered(depth: 20, breadth: 50);

        var nodes = nodeIds
            .Select(id => new LayoutNode(id, TestGraphs.NodeWidth, TestGraphs.NodeHeight))
            .ToArray();

        var edges = edgeIds
            .Select((e, index) => new LayoutEdge($"e{index}", e.From, e.To))
            .ToArray();

        _free = new LayoutRequest(nodes, edges, new LayoutOptions());

        // 钉的位置从一次真实布局里取，不写死数字：引擎换了坐标约定之后，
        // 写死的数字会让这条基线悄悄变成"钉在空处"，而它照样出数、照样不报错。
        var baseline = _engine.Layout(_free);

        // 每四个钉一个，各自上移半个节点高，正好骑在层的上边界上。这个位置是刻意挑的：
        //   - 钉在层内的原位，让位一次都不会触发；
        //   - 钉到整张图之外，固定节点全在右侧，让位同样不触发；
        //   - 骑在边界上，它压住本层一个自由节点的位置，也占掉两层之间那段空隙的一半，
        //     而折线的拐点正是要在那段空隙里找位置。
        // 真实拖动的位置是自由的，骑在层边界上并不罕见，所以这一种才代表补齐逻辑的真实代价。
        // 同一层里固定节点相隔四个槽位（480），远大于节点宽度；不同层的固定节点相隔一个层距（110），
        // 远大于节点高度，所以固定节点之间不会互相压住。
        var pinned = nodes
            .Select((n, index) =>
            {
                if (index % 4 != 0)
                {
                    return n;
                }

                var placed = baseline.Find(n.Id)!;

                return n with { Pinned = new LayoutPoint(placed.X, placed.Y - (TestGraphs.NodeHeight / 2)) };
            })
            .ToList();

        // 再单独钉一个到整张图的右边界之外，给对齐约束一个够得着的目标。
        // 对齐要求"这几个排成一列"，而在一个每层都排满的等宽网格上，任何跨层的取齐都会撞上
        // 目标层里已有的节点——只有把目标放到整层之外才谈得上取齐。
        var far = baseline.Nodes.Max(n => n.Right) + 100;
        var anchor = Array.IndexOf(nodeIds, "n5-25");
        var moved = baseline.Find(nodeIds[anchor])!;
        pinned[anchor] = nodes[anchor] with { Pinned = new LayoutPoint(far, moved.Y - (TestGraphs.NodeHeight / 2)) };

        _constrained = new LayoutRequest(
            [.. pinned],
            edges,
            new LayoutOptions(
                Direction.TB,
                OrderGroups: [new[] { "n5-5", "n5-3" }],
                AlignGroups: [new[] { "n0-25", "n5-25" }]));

        // 最坏情形：把第十五层的五十个节点整层钉到第一层的条带上，并且放到整张图之外。
        // 于是"第十四层 → 第十五层"那五十条边必须从第十四层一路走到第一层，横穿中间十几层。
        // 折线的拐点要在层间空隙里找位置，而它要跨的层越多、能走的空隙越少，路由就越贵。
        // 这一条是对照，不是常见情形：真实拖动很少会把一整层搬到别处。
        var layerOne = baseline.Find(nodeIds[50])!.Y;

        var teleported = nodes
            .Select((n, index) =>
            {
                if (index / 50 != 15)
                {
                    return n;
                }

                return n with { Pinned = new LayoutPoint(far + (index % 50 * 120), layerOne) };
            })
            .ToArray();

        _teleported = new LayoutRequest(teleported, edges, new LayoutOptions());

        Guard();
    }

    /// <summary>
    /// 核对约束真的落到了这次布局上。
    /// </summary>
    /// <remarks>
    /// 一条静默退化成"没有约束"的基线照样会出数，而那个数字看起来完全正常——
    /// 于是"补齐逻辑有多贵"这个问题的答案会变成"几乎不要钱"，而真实原因是根本没测。
    /// 这里在开跑之前先确认固定位置生效了、约束真的满足了、图也没被压坏，
    /// 任何一条不成立就直接终止整次运行。
    /// </remarks>
    private void Guard()
    {
        Check(_constrained, "常见情形");
        Check(_teleported, "最坏情形");
    }

    private void Check(LayoutRequest request, string name)
    {
        var d = _engine.Layout(request).Diagnostics;

        if (d.AnchorCount == 0)
        {
            throw new InvalidOperationException($"{name}的基线里固定位置没有生效，这次测量没有意义");
        }

        if (d.MaxAnchorDeviation > 0.01 || d.ResidualOverlaps != 0)
        {
            throw new InvalidOperationException(
                $"{name}的基线里图不合法：固定坐标偏差 {d.MaxAnchorDeviation}，残留重叠 {d.ResidualOverlaps}");
        }

        if (d.EndpointFailures != 0 || d.UnresolvedEndpoints != 0)
        {
            throw new InvalidOperationException(
                $"{name}的基线里折线端点有问题：不贴合 {d.EndpointFailures}，解析不出来 {d.UnresolvedEndpoints}");
        }

        if (d.ConstraintConflicts.Count > 0)
        {
            throw new InvalidOperationException(
                $"{name}的基线里约束没有全部生效：{string.Join("；", d.ConstraintConflicts)}");
        }
    }

    /// <summary>千节点、无约束。这一条是补齐开销的对照。</summary>
    [Benchmark(Baseline = true)]
    public int Pipeline_1000_Nodes_No_Constraints() => Run(_free);

    /// <summary>千节点、二百五十一个固定位置（骑在层边界上）、一组层内次序、一组对齐。</summary>
    [Benchmark]
    public int Pipeline_1000_Nodes_With_Constraints() => Run(_constrained);

    /// <summary>千节点、整层被钉到别处的固定位置。这一条是路由的最坏情形。</summary>
    [Benchmark]
    public int Pipeline_1000_Nodes_With_Teleported_Layer() => Run(_teleported);

    private int Run(LayoutRequest request) => _engine.Layout(request).Nodes.Length;
}
