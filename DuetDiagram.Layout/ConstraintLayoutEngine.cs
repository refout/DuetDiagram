using System.Diagnostics;
using DuetDiagram.Layout.Internal;

namespace DuetDiagram.Layout;

/// <summary>
/// 带约束的布局引擎。
/// </summary>
/// <remarks>
/// <para>
/// 引擎自身不支持的四项约束全部在这一层补齐，顺序是有讲究的：
/// </para>
/// <list type="number">
/// <item><b>收缩</b>：把同层组换成超节点。必须在引擎之前，因为约束要在布局前表达出来，
/// 布局完成后再调整就变成了打补丁。</item>
/// <item><b>引擎</b>：只看到合法的分层图，不知道同层组的存在。</item>
/// <item><b>展开</b>：把超节点占的空间填上。此时所需空间已经被预留，是纯局部操作。</item>
/// <item><b>回填固定坐标</b>：把用户声明的坐标放回去。</item>
/// <item><b>逐元素对齐</b>：把一组节点在层内轴上取齐。</item>
/// <item><b>行内让位</b>：把被固定节点占位的自由节点在同一层内重新排开。</item>
/// <item><b>层内次序</b>：按用户要的先后把同一层里的节点换位。</item>
/// <item><b>折线重算</b>：按最终坐标重新路由每条边。</item>
/// </list>
/// <para>
/// 第 4 步与第 6 步分开，是为了让偏差检查可以独立进行：
/// 只要回填之后偏差不为零，就说明回填本身写错了，与让位逻辑无关。
/// </para>
/// <para>
/// 对齐在让位之前、次序在让位之后，两个方向都不是随手定的：取齐可能把节点挪到同层
/// 另一个节点的身上，而让位正是用来把那种情况解开的，所以对齐要先做；
/// 让位保持的是当前次序，而次序约束要改的恰恰是当前次序，所以次序要后做。
/// </para>
/// <para>
/// 全程只有一条硬保证——**固定坐标一个像素都不偏**。无重叠是这套算法的必然结果而不是收敛目标：
/// 让位在每一层内一次扫描完成，而层与层之间本来就不同位置，所以不会留下"差一点"的状态。
/// 唯一真正无解的情形是两个固定节点互相压住——那是输入本身矛盾，只能如实报出来。
/// 中级约束之间也可能互相矛盾，同样如实报出，不静默丢掉其中一条。
/// </para>
/// </remarks>
public sealed class ConstraintLayoutEngine : ILayoutEngine
{
    /// <summary>求解一次布局。</summary>
    public EngineLayoutResult Layout(LayoutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Layout(request.Nodes, request.Edges, request.Options, request.Groups, cancellationToken);
    }

    /// <summary>
    /// 求解一次布局。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 输入为空时返回空结果而不是抛异常：空图是一个合法状态（用户刚新建文档），
    /// 让调用方为此写一个分支没有意义。
    /// </para>
    /// <para>
    /// 取消令牌在**每个阶段之间**检查。阶段内部不再细分检查点：
    /// 每个阶段本身是毫秒级的，而每个节点都检查一次会把令牌检查的开销
    /// 摊进算法本身的耗时里，得不偿失。代价是超时可能比预算多出一个阶段的时长。
    /// </para>
    /// <para>
    /// 引擎调用那一阶段**无法被取消**——它是第三方的同步调用，不看我们的令牌。
    /// 这一阶段的超时只能由调用方从外面兜（见协调器）。
    /// </para>
    /// </remarks>
    /// <param name="nodes">参与布局的节点。</param>
    /// <param name="edges">要连的边。</param>
    /// <param name="options">布局选项。</param>
    /// <param name="groups">组合与成员。端点在组合上的边靠它算包围盒。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public EngineLayoutResult Layout(
        IReadOnlyList<LayoutNode> nodes,
        IReadOnlyList<LayoutEdge> edges,
        LayoutOptions? options = null,
        IReadOnlyList<LayoutGroup>? groups = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        var effective = options ?? new LayoutOptions();
        var groupArray = groups ?? [];
        var nodeArray = nodes as LayoutNode[] ?? [.. nodes];
        var edgeArray = edges as LayoutEdge[] ?? [.. edges];

        if (nodeArray.Length == 0)
        {
            return new EngineLayoutResult(
                [],
                [],
                0,
                0,
                new LayoutDiagnostics(0, 0, 0, 0, 0, 0, 0, 0, 0, default, default, default, default, default));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var contractionWatch = Stopwatch.StartNew();
        var contraction = SameRankContraction.Contract(
            nodeArray,
            edgeArray,
            effective.SameRankGroups ?? [],
            effective.NodeSpacing);
        contractionWatch.Stop();

        cancellationToken.ThrowIfCancellationRequested();

        var engineWatch = Stopwatch.StartNew();
        var placed = EngineAdapter.Compute(contraction.Graph, effective);
        engineWatch.Stop();

        cancellationToken.ThrowIfCancellationRequested();

        var restorationWatch = Stopwatch.StartNew();
        var expanded = SameRankContraction.Expand(placed, contraction.Expansions, nodeArray);
        var restored = AnchorRestorer.Apply(expanded, nodeArray);
        restorationWatch.Stop();

        var anchored = nodeArray
            .Where(n => n.Pinned is not null)
            .Select(n => n.Id)
            .ToHashSet(StringComparer.Ordinal);

        cancellationToken.ThrowIfCancellationRequested();

        // 对齐放在让位**之前**：取齐可能把节点挪到同层另一个节点的身上，
        // 而让位正是用来把那种情况解开的。反过来先让位再对齐，对齐的结果就没有人再检查了。
        var alignWatch = Stopwatch.StartNew();
        var aligned = AlignSolver.Apply(
            restored,
            effective.AlignGroups ?? [],
            anchored,
            effective.RanksAreVertical);
        alignWatch.Stop();

        cancellationToken.ThrowIfCancellationRequested();

        var reflowWatch = Stopwatch.StartNew();
        var reflow = RowReflow.Apply(aligned.Nodes, anchored, effective.RanksAreVertical, effective.NodeSpacing);
        reflowWatch.Stop();

        cancellationToken.ThrowIfCancellationRequested();

        // 次序放在让位**之后**：让位保持的是当前次序，而次序约束要改的恰恰是当前次序。
        // 反过来先做次序，让位会按旧坐标的先后把它排回去。
        var orderWatch = Stopwatch.StartNew();
        var order = OrderSolver.Apply(
            reflow.Nodes,
            effective.OrderGroups ?? [],
            anchored,
            effective.RanksAreVertical,
            effective.NodeSpacing);
        orderWatch.Stop();

        var placedNodes = order.Nodes;

        // 两条中级约束互相干扰时如实报出来。求解阶段各自报过一遍，这里报的是
        // "求解时看着可行、被后面的步骤推开了"那一类——不检查的话它就是一个静默失效。
        // 求解阶段已经判定做不了的那些组要跳过：它们根本没被改动过，
        // 再报一次会给出一个错误的理由（"被让位推开了"，而实际是压根没动）。
        var conflicts = new List<string>();
        conflicts.AddRange(aligned.Conflicts);
        conflicts.AddRange(order.Conflicts);
        conflicts.AddRange(Verify(effective, placedNodes, aligned.FailedGroups, order.FailedGroups));

        cancellationToken.ThrowIfCancellationRequested();

        // 折线必须跟着重算。引擎给的是按旧坐标画的线，节点被移动之后那些线就指向了旧位置。
        // 组合的盒子用**让位之后**的坐标算。用求解后的旧坐标会算出一块偏掉的区域，
        // 而端点要落在那块区域的边界上，于是线会接到一个空处。
        var compositeBoxes = CompositeOutline.Compute(groupArray, placedNodes);

        var routingWatch = Stopwatch.StartNew();
        var routed = EdgeRouter.Route(
            placedNodes,
            edgeArray,
            CollectPorts(nodeArray),
            compositeBoxes,
            effective.RanksAreVertical,
            out var endpointFailures,
            out var unresolvedEndpoints,
            out var crossingEdges);
        routingWatch.Stop();

        // 重叠统计放在计时之外：它是验证手段而不是算法的一部分，
        // 而且是两两比较的平方复杂度，算进去会让让位看起来比实际慢两个数量级。
        var residualOverlaps = RowReflow.CountOverlaps(placedNodes);
        var overlappingAnchors = anchored.Count > 1
            ? RowReflow.CountOverlaps([.. placedNodes.Where(n => anchored.Contains(n.Id))])
            : 0;

        var diagnostics = new LayoutDiagnostics(
            anchored.Count,
            AnchorRestorer.MaxDeviation(placedNodes, nodeArray),
            residualOverlaps,
            reflow.ReflowedCount,
            overlappingAnchors,
            routed.Length,
            endpointFailures,
            unresolvedEndpoints,
            crossingEdges,
            contractionWatch.Elapsed,
            engineWatch.Elapsed,
            restorationWatch.Elapsed,
            reflowWatch.Elapsed,
            routingWatch.Elapsed)
        {
            ConstraintConflicts = conflicts,
            AlignTime = alignWatch.Elapsed,
            OrderTime = orderWatch.Elapsed,
        };

        return new EngineLayoutResult(
            placedNodes,
            routed,
            placedNodes.Max(n => n.Right),
            placedNodes.Max(n => n.Bottom),
            diagnostics);
    }

    /// <summary>
    /// 核对中级约束在最终坐标上是否仍然成立。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 求解阶段报的是"当时算不出来"，这里报的是"算出来了但被后面的步骤推开了"。
    /// 少了这一步，那一类失效是**静默**的：约束看着进了流水线，结果上却看不出来。
    /// </para>
    /// <para>
    /// 层内次序只在同一层之内核对。跨层的情形在求解阶段已经报过，
    /// 在这里再报一次会让同一条约束出现两条记录。
    /// </para>
    /// </remarks>
    /// <param name="options">布局选项。</param>
    /// <param name="nodes">最终坐标。</param>
    /// <param name="failedAlign">求解阶段就判定做不了的对齐组。它们不在最终坐标上核对。</param>
    /// <param name="failedOrder">求解阶段就判定做不了的次序组。它们不在最终坐标上核对。</param>
    private static List<string> Verify(
        LayoutOptions options,
        PlacedNode[] nodes,
        IReadOnlyList<IReadOnlyList<string>> failedAlign,
        IReadOnlyList<IReadOnlyList<string>> failedOrder)
    {
        var conflicts = new List<string>();
        var byId = new Dictionary<string, PlacedNode>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            byId[node.Id] = node;
        }

        foreach (var group in options.AlignGroups ?? [])
        {
            if (AlreadyReported(group, failedAlign))
            {
                continue;
            }

            var members = group.Where(byId.ContainsKey).Select(id => byId[id]).ToArray();

            if (members.Length < 2)
            {
                continue;
            }

            // 层内轴：层沿纵向排列时是横轴，否则是纵轴。
            var axis = options.RanksAreVertical
                ? members.Select(m => m.X).ToArray()
                : members.Select(m => m.Y).ToArray();

            if (axis.Max() - axis.Min() <= 0.01)
            {
                continue;
            }

            conflicts.Add(
                $"对齐约束「{Describe(group)}」在最终坐标上没有取齐：被同层的让位推开了。");
        }

        foreach (var group in options.OrderGroups ?? [])
        {
            if (AlreadyReported(group, failedOrder))
            {
                continue;
            }

            var present = group.Where(byId.ContainsKey).ToArray();

            if (present.Length < 2)
            {
                continue;
            }

            var members = present.Select(id => byId[id]).ToArray();
            var ranks = members
                .Select(m => Math.Round(options.RanksAreVertical ? m.Y : m.X, 3))
                .Distinct()
                .ToArray();

            if (ranks.Length > 1)
            {
                continue;
            }

            var ordered = options.RanksAreVertical
                ? members.OrderBy(m => m.X).Select(m => m.Id)
                : members.OrderBy(m => m.Y).Select(m => m.Id);

            if (ordered.SequenceEqual(present, StringComparer.Ordinal))
            {
                continue;
            }

            conflicts.Add(
                $"层内次序约束「{Describe(group)}」在最终坐标上没有成立：被同层的让位推回了原次序。");
        }

        return conflicts;
    }

    /// <summary>这一组是不是求解阶段已经报过的那一组。</summary>
    private static bool AlreadyReported(IReadOnlyList<string> group, IReadOnlyList<IReadOnlyList<string>> reported)
    {
        foreach (var other in reported)
        {
            if (group.SequenceEqual(other, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string Describe(IReadOnlyList<string> members) => string.Join("、", members);

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
