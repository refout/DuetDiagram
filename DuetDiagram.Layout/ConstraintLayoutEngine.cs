using System.Diagnostics;
using DuetDiagram.Layout.Internal;

namespace DuetDiagram.Layout;

/// <summary>
/// 带约束的布局引擎。
/// </summary>
/// <remarks>
/// <para>
/// 引擎自身不支持的两项约束全部在这一层补齐，顺序是有讲究的：
/// </para>
/// <list type="number">
/// <item><b>收缩</b>：把同层组换成超节点。必须在引擎之前，因为约束要在布局前表达出来，
/// 布局完成后再调整就变成了打补丁。</item>
/// <item><b>引擎</b>：只看到合法的分层图，不知道同层组的存在。</item>
/// <item><b>展开</b>：把超节点占的空间填上。此时所需空间已经被预留，是纯局部操作。</item>
/// <item><b>回填固定坐标</b>：把用户声明的坐标放回去。</item>
/// <item><b>行内让位</b>：把被固定节点占位的自由节点在同一层内重新排开。</item>
/// <item><b>折线重算</b>：按最终坐标重新路由每条边。</item>
/// </list>
/// <para>
/// 第 4 步与第 5 步分开，是为了让偏差检查可以独立进行：
/// 只要回填之后偏差不为零，就说明回填本身写错了，与让位逻辑无关。
/// </para>
/// <para>
/// 全程只有一条硬保证——**固定坐标一个像素都不偏**。无重叠是这套算法的必然结果而不是收敛目标：
/// 让位在每一层内一次扫描完成，而层与层之间本来就不同位置，所以不会留下"差一点"的状态。
/// 唯一真正无解的情形是两个固定节点互相压住——那是输入本身矛盾，只能如实报出来。
/// </para>
/// </remarks>
public sealed class ConstraintLayoutEngine
{
    /// <summary>求解一次布局。</summary>
    public LayoutResult Layout(LayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Layout(request.Nodes, request.Edges, request.Options);
    }

    /// <summary>
    /// 求解一次布局。
    /// </summary>
    /// <remarks>
    /// 输入为空时返回空结果而不是抛异常：空图是一个合法状态（用户刚新建文档），
    /// 让调用方为此写一个分支没有意义。
    /// </remarks>
    public LayoutResult Layout(
        IReadOnlyList<LayoutNode> nodes,
        IReadOnlyList<LayoutEdge> edges,
        LayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        var effective = options ?? new LayoutOptions();
        var nodeArray = nodes as LayoutNode[] ?? [.. nodes];
        var edgeArray = edges as LayoutEdge[] ?? [.. edges];

        if (nodeArray.Length == 0)
        {
            return new LayoutResult(
                [],
                [],
                0,
                0,
                new LayoutDiagnostics(0, 0, 0, 0, 0, 0, 0, 0, default, default, default, default, default));
        }

        var contractionWatch = Stopwatch.StartNew();
        var contraction = SameRankContraction.Contract(
            nodeArray,
            edgeArray,
            effective.SameRankGroups ?? [],
            effective.NodeSpacing);
        contractionWatch.Stop();

        var engineWatch = Stopwatch.StartNew();
        var placed = EngineAdapter.Compute(contraction.Graph, effective);
        engineWatch.Stop();

        var restorationWatch = Stopwatch.StartNew();
        var expanded = SameRankContraction.Expand(placed, contraction.Expansions, nodeArray);
        var restored = AnchorRestorer.Apply(expanded, nodeArray);
        restorationWatch.Stop();

        var anchored = nodeArray
            .Where(n => n.Pinned is not null)
            .Select(n => n.Id)
            .ToHashSet(StringComparer.Ordinal);

        var reflowWatch = Stopwatch.StartNew();
        var reflow = RowReflow.Apply(restored, anchored, effective.RanksAreVertical, effective.NodeSpacing);
        reflowWatch.Stop();

        // 折线必须跟着重算。引擎给的是按旧坐标画的线，节点被移动之后那些线就指向了旧位置。
        var routingWatch = Stopwatch.StartNew();
        var routed = EdgeRouter.Route(
            reflow.Nodes,
            edgeArray,
            CollectPorts(nodeArray),
            effective.RanksAreVertical,
            out var endpointFailures,
            out var crossingEdges);
        routingWatch.Stop();

        // 重叠统计放在计时之外：它是验证手段而不是算法的一部分，
        // 而且是两两比较的平方复杂度，算进去会让让位看起来比实际慢两个数量级。
        var residualOverlaps = RowReflow.CountOverlaps(reflow.Nodes);
        var overlappingAnchors = anchored.Count > 1
            ? RowReflow.CountOverlaps([.. reflow.Nodes.Where(n => anchored.Contains(n.Id))])
            : 0;

        var diagnostics = new LayoutDiagnostics(
            anchored.Count,
            AnchorRestorer.MaxDeviation(reflow.Nodes, nodeArray),
            residualOverlaps,
            reflow.ReflowedCount,
            overlappingAnchors,
            routed.Length,
            endpointFailures,
            crossingEdges,
            contractionWatch.Elapsed,
            engineWatch.Elapsed,
            restorationWatch.Elapsed,
            reflowWatch.Elapsed,
            routingWatch.Elapsed);

        return new LayoutResult(
            reflow.Nodes,
            routed,
            reflow.Nodes.Max(n => n.Right),
            reflow.Nodes.Max(n => n.Bottom),
            diagnostics);
    }

    private static Dictionary<string, IReadOnlyList<LayoutPort>> CollectPorts(LayoutNode[] nodes)
    {
        var ports = new Dictionary<string, IReadOnlyList<LayoutPort>>(StringComparer.Ordinal);

        foreach (var node in nodes.Where(n => n.Ports is { Count: > 0 }))
        {
            ports[node.Id] = node.Ports!;
        }

        return ports;
    }
}
