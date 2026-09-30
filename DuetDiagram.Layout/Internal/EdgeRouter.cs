using DuetDiagram.Core.Model;

namespace DuetDiagram.Layout.Internal;

/// <summary>
/// 边的折线重算。
/// </summary>
/// <remarks>
/// <para>
/// 为什么必须重算：引擎返回的折线是按它自己算出的节点坐标画的。锚点回填与层内让位都会移动节点，
/// 移动之后那些折线就指向了旧位置——看起来是线从节点旁边擦过去，或者悬在半空。
/// </para>
/// <para>
/// 路由方式沿用分层布局的常规做法：从起点朝向终点的那条边出去，
/// 在两层之间的空隙里走一段，再从对面进入终点。
/// **拐点走两层之间的空隙**是关键——那个区域天然是空的，
/// 只要不被固定节点占用，横向段就不会压到任何节点。
/// </para>
/// <para>
/// 固定节点的纵坐标是自由的，可能正好卡在两层之间，把空隙切碎。
/// 所以挑拐点位置时要先把被占用的区间减掉，在剩下的空档里取最宽的一段。
/// 这是精确计算而不是采样试探：采样会漏掉窄但可用的空档，而且结果依赖采样点数量。
/// </para>
/// <para>
/// 若整个空隙都被固定节点占满，就退回中点并如实计入穿过节点的计数，
/// 由上层决定是否提示用户。**不假装绕开**：一条看起来能走通但实际压过节点的折线，
/// 比一条明确报告有冲突的折线更难排查。
/// </para>
/// <para>
/// **主方向那一段压到节点时改走另一条路。** 回边逆着层序，两条竖段必然要跨过中间那些层；
/// 锚点把节点拉到别的层时，连到它的正向边也会跨过中间几层。这两种情况下层间空隙都救不了它——
/// 空隙是横向的，跨过去的那两段是纵向的。这时把纵向段挪进一段**不含任何节点的横向区间**，
/// 见 <see cref="TryChannelRoute"/>。这样的区间不止最外侧那一条，列与列之间的空当同样能用；
/// 同侧有多条边时它们各占一条通道，不再叠在一起。
/// 一段都用不上时仍然退回原折线并如实计入穿越。
/// </para>
/// </remarks>
internal static class EdgeRouter
{
    private const double Epsilon = 0.01;

    /// <summary>从指定端口出来时先往外走这么远，避免线贴着节点边框。</summary>
    private const double PortStub = 12;

    /// <summary>绕行通道离节点列外缘留出的距离。</summary>
    /// <remarks>
    /// 要比 <see cref="PortStub"/> 大：通道要走在端口那截外推线之外，
    /// 否则绕行段会紧贴着端口线走过去。
    /// </remarks>
    private const double LaneGap = 16;

    /// <summary>同一段空隙里相邻两条通道的间距。</summary>
    private const double LanePitch = 8;

    /// <summary>一段空隙里最多排几条通道。空档再宽也要有上限，否则候选数失控。</summary>
    private const int MaxLanesPerSpan = 4;

    /// <summary>单条边最多试几条候选通道。试完还不成，就退回原折线并如实计入穿越。</summary>
    private const int MaxCandidatesPerEdge = 32;

    /// <summary>
    /// 重新算出每条边的折线。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 端点可以是节点，也可以是**组合**——分层架构图里 <c>ODS --&gt; DWD</c> 就是拿分组当端点的。
    /// 组合的盒子由 <paramref name="compositeBoxes"/> 传进来，它**不进坐标网格**：
    /// 组合本来就罩着它的成员，把它当成"要绕开的障碍"会让每条穿过这个分组的边都判成压过节点。
    /// </para>
    /// <para>
    /// 两个失败计数分开报，因为处置完全不同：端点落在节点内部或外部，是路由算法出了问题，
    /// 要去查几何；端点根本解析不出来，是输入里有悬空引用，要去查数据。
    /// 合成一个数之后调用方只能看到"有 N 条边不对"，而不知道该往哪查。
    /// </para>
    /// </remarks>
    /// <param name="nodes">定好坐标的节点。</param>
    /// <param name="edges">要路由的边。</param>
    /// <param name="ports">各节点的端口。</param>
    /// <param name="compositeBoxes">组合的包围盒。没有组合端点时为空。</param>
    /// <param name="direction">主方向。回边绕行要靠它判断哪条边是逆着层序走的。</param>
    /// <param name="endpointFailures">端点没落在边界上的边数。</param>
    /// <param name="unresolvedEndpoints">端点解析不出来的边数。</param>
    /// <param name="crossingEdges">折线穿过其它节点的边数。</param>
    public static RoutedEdge[] Route(
        PlacedNode[] nodes,
        LayoutEdge[] edges,
        IReadOnlyDictionary<string, IReadOnlyList<LayoutPort>> ports,
        IReadOnlyDictionary<string, PlacedNode> compositeBoxes,
        Direction direction,
        out int endpointFailures,
        out int unresolvedEndpoints,
        out int crossingEdges)
    {
        var byId = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

        // 建一次索引给所有边共用。这就是把平方复杂度压下来的关键：
        // 每条边只碰它附近的节点，而不是图里全部节点。
        // 两个缓冲区在整轮路由里复用，避免每条边都新建集合。
        var grid = new NodeGrid(nodes);
        var channelBuffer = new List<PlacedNode>(64);
        var segmentBuffer = new List<PlacedNode>(64);

        var ranksAreVertical = direction is Direction.TB or Direction.BT;

        // 空隙表的搜索范围：从原点起，到最外那个节点之外再让出放得下 MaxLanesPerSpan 条通道的一段。
        // 左边那一侧不用单独量——原点到最左那个节点之间本来就是空的，取补集时自然会出现。
        var farEdge = LaneGap + (MaxLanesPerSpan * LanePitch);

        if (nodes.Length > 0)
        {
            farEdge += ranksAreVertical ? nodes.Max(n => n.Right) : nodes.Max(n => n.Bottom);
        }

        // 空隙表、已占通道表与候选缓冲区在整个路由过程里复用。
        // 空隙表懒算：没有边要绕行时一次都不建，无环的图连这次排序的代价都没有。
        (double Low, double High)[]? spans = null;
        var claimedLanes = new List<double>(4);
        var scratch = new LayoutPoint[4];

        var routed = new List<RoutedEdge>(edges.Length);
        var failures = 0;
        var unresolved = 0;
        var crossings = 0;

        foreach (var edge in edges)
        {
            var source = Resolve(edge.From, byId, compositeBoxes);
            var target = Resolve(edge.To, byId, compositeBoxes);

            if (source is null || target is null)
            {
                // 输入引用了既不是节点也不是组合的东西。布局照常进行，只是这条边画不出来。
                unresolved++;
                continue;
            }

            // 端口只属于节点。端点是组合时不该有端口，真有也找不到——
            // 校验器会先报 EDGE_PORT_ON_COMPOSITE，这里不必重复判。
            var sourcePort = ResolvePort(edge.From, edge.FromPort, ports);
            var targetPort = ResolvePort(edge.To, edge.ToPort, ports);

            var points = ranksAreVertical
                ? RouteVertical(source, target, sourcePort, targetPort, grid, channelBuffer)
                : RouteHorizontal(source, target, sourcePort, targetPort, grid, channelBuffer);

            // 穿越判定先算一次：绝大多数边根本不压节点，到此为止，不再多查一遍。
            var crosses = CrossesAnyNode(
                edge.From,
                edge.To,
                points,
                grid,
                segmentBuffer,
                ranksAreVertical,
                mainAxisOnly: false);

            // 主方向那一段压到节点上时，改走一段不含任何节点的空隙。
            // 只查主方向那一段：绕行走的正是主方向上的空隙，它修不好落在层间那条横段上的穿越——
            // 那是挑不到空档时的如实计数，不该被一条绕到图外的远路顶掉。
            // 指定了端口也不试，那是用户选的出入口。
            //
            // 端点解析成组合时同样不试：组合的成员节点在网格里，而排除的只是组合自己那个标识，
            // 于是这条边与自己的成员相交也会被算成压节点——判据在这里不可信。
            if (crosses
                && sourcePort is null
                && targetPort is null
                && !compositeBoxes.ContainsKey(edge.From)
                && !compositeBoxes.ContainsKey(edge.To)
                && CrossesAnyNode(
                    edge.From,
                    edge.To,
                    points,
                    grid,
                    segmentBuffer,
                    ranksAreVertical,
                    mainAxisOnly: true))
            {
                spans ??= ComputeFreeSpans(nodes, ranksAreVertical, farEdge);
                var detoured = TryChannelRoute(
                    source,
                    target,
                    ranksAreVertical,
                    grid,
                    segmentBuffer,
                    spans,
                    claimedLanes,
                    scratch);

                if (detoured is not null)
                {
                    points = detoured;
                    crosses = CrossesAnyNode(
                        edge.From,
                        edge.To,
                        points,
                        grid,
                        segmentBuffer,
                        ranksAreVertical,
                        mainAxisOnly: false);
                }
            }

            // 端点必须落在两端节点的边界上。落在内部说明线是从节点身子里钻出来的，
            // 落在外部说明线没有接到节点上——两种都是渲染时一眼能看出来的错误。
            if (!source.IsOnBoundary(points[0]) || !target.IsOnBoundary(points[^1]))
            {
                failures++;
            }

            if (crosses)
            {
                crossings++;
            }

            routed.Add(new RoutedEdge(edge.Id, points));
        }

        endpointFailures = failures;
        unresolvedEndpoints = unresolved;
        crossingEdges = crossings;

        return [.. routed];
    }

    /// <summary>端点解析：先按节点找，找不到再按组合的盒子找。</summary>
    /// <remarks>
    /// 顺序与本仓其它地方的端点解析一致（先节点、后组合）：九个集合共用一个标识命名空间，
    /// 所以同一个标识不会既是节点又是组合，先查哪个都不影响结果——顺序统一只是为了让几处读起来是同一件事。
    /// </remarks>
    private static PlacedNode? Resolve(
        string id,
        Dictionary<string, PlacedNode> nodes,
        IReadOnlyDictionary<string, PlacedNode> compositeBoxes) =>
        nodes.TryGetValue(id, out var node) ? node : compositeBoxes.GetValueOrDefault(id);

    /// <summary>层沿纵向排列时的折线：出口在上下边，拐弯在两层之间的空隙里。</summary>
    private static LayoutPoint[] RouteVertical(
        PlacedNode source,
        PlacedNode target,
        LayoutPort? sourcePort,
        LayoutPort? targetPort,
        NodeGrid grid,
        List<PlacedNode> buffer)
    {
        var goingDown = target.CenterY >= source.CenterY;

        var start = sourcePort is null
            ? new LayoutPoint(source.CenterX, goingDown ? source.Bottom : source.Y)
            : source.PortAnchor(sourcePort);

        var end = targetPort is null
            ? new LayoutPoint(target.CenterX, goingDown ? target.Y : target.Bottom)
            : target.PortAnchor(targetPort);

        var startRoute = sourcePort is null ? start : Step(source, sourcePort, start);
        var endRoute = targetPort is null ? end : Step(target, targetPort, end);

        var channelY = PickChannel(
            Math.Min(startRoute.Y, endRoute.Y),
            Math.Max(startRoute.Y, endRoute.Y),
            Math.Min(startRoute.X, endRoute.X),
            Math.Max(startRoute.X, endRoute.X),
            grid,
            buffer);

        return Assemble(
            start,
            startRoute,
            new LayoutPoint(startRoute.X, channelY),
            new LayoutPoint(endRoute.X, channelY),
            endRoute,
            end);
    }

    /// <summary>层沿横向排列时的折线。左右方向下拐点走的是两层之间竖直的空隙。</summary>
    private static LayoutPoint[] RouteHorizontal(
        PlacedNode source,
        PlacedNode target,
        LayoutPort? sourcePort,
        LayoutPort? targetPort,
        NodeGrid grid,
        List<PlacedNode> buffer)
    {
        var goingRight = target.CenterX >= source.CenterX;

        var start = sourcePort is null
            ? new LayoutPoint(goingRight ? source.Right : source.X, source.CenterY)
            : source.PortAnchor(sourcePort);

        var end = targetPort is null
            ? new LayoutPoint(goingRight ? target.X : target.Right, target.CenterY)
            : target.PortAnchor(targetPort);

        var startRoute = sourcePort is null ? start : Step(source, sourcePort, start);
        var endRoute = targetPort is null ? end : Step(target, targetPort, end);

        var channelX = PickChannel(
            Math.Min(startRoute.X, endRoute.X),
            Math.Max(startRoute.X, endRoute.X),
            Math.Min(startRoute.Y, endRoute.Y),
            Math.Max(startRoute.Y, endRoute.Y),
            grid,
            buffer,
            horizontalChannel: true);

        return Assemble(
            start,
            startRoute,
            new LayoutPoint(channelX, startRoute.Y),
            new LayoutPoint(channelX, endRoute.Y),
            endRoute,
            end);
    }

    /// <summary>
    /// 算出坐标轴上一段段不含任何节点的区间。主方向上的通道落在里面就一定碰不到节点。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 把每个节点在垂直于主方向那条轴上的投影区间并起来，再取补集。
    /// 补集天然包含最左边那条带（原点到最左的节点之间）、各列之间的空当、
    /// 以及最右边那条带（最右的节点之外再让出 <paramref name="farEdge"/> 那一段）。
    /// </para>
    /// <para>
    /// 列没对齐时列间的空当会被并掉，剩下的只有最外侧那两条——那时退化成只有外侧通道可用，
    /// 与没有多通道时一样，不会算错。
    /// </para>
    /// </remarks>
    /// <param name="nodes">定好坐标的节点。</param>
    /// <param name="ranksAreVertical">层是不是沿纵向排列。为真时投影到横轴，为假时投影到纵轴。</param>
    /// <param name="farEdge">搜索范围的上界，即最右（最下）那个节点的外缘再往外让出的距离。</param>
    private static (double Low, double High)[] ComputeFreeSpans(
        PlacedNode[] nodes,
        bool ranksAreVertical,
        double farEdge)
    {
        var occupied = new (double Low, double High)[nodes.Length];

        for (var index = 0; index < nodes.Length; index++)
        {
            occupied[index] = ranksAreVertical
                ? (nodes[index].X, nodes[index].Right)
                : (nodes[index].Y, nodes[index].Bottom);
        }

        // 按 (下界, 上界) 排，是全序，所以合并的结果与排序稳不稳定无关。
        Array.Sort(occupied, static (a, b) =>
        {
            var byLow = a.Low.CompareTo(b.Low);
            return byLow != 0 ? byLow : a.High.CompareTo(b.High);
        });

        var spans = new List<(double Low, double High)>(nodes.Length + 1);
        var cursor = 0.0;

        foreach (var interval in occupied)
        {
            if (interval.Low - cursor > Epsilon)
            {
                spans.Add((cursor, interval.Low));
            }

            cursor = Math.Max(cursor, interval.High);
        }

        if (farEdge - cursor > Epsilon)
        {
            spans.Add((cursor, farEdge));
        }

        return [.. spans];
    }

    /// <summary>
    /// 折线的主方向段压到节点上时，改走一段不含节点的空隙。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 按空隙从左到右扫，每段空隙里从贴着节点列的那一侧往外排几条通道。
    /// 已经给别的边用掉的通道跳过——同侧有多条边时它们各占一条，不再叠在一起。
    /// 占用是按整条通道记的：同一条通道上主轴区间不重叠的两条边本可以共用，
    /// 但那要多判一层区间重叠，代价与不确定性都更高，这里不做。
    /// </para>
    /// <para>
    /// 每条候选都要真查一遍：空隙只保证主方向那一段是干净的，两端横着出去的腿不保证——
    /// 同层里挨着的邻居会挡在中间。查不过就试下一条。
    /// </para>
    /// <para>
    /// 候选试完（或试够上限）仍没有可用的就返回空，由调用方保留原折线并如实计入穿越。
    /// </para>
    /// </remarks>
    private static LayoutPoint[]? TryChannelRoute(
        PlacedNode source,
        PlacedNode target,
        bool ranksAreVertical,
        NodeGrid grid,
        List<PlacedNode> buffer,
        (double Low, double High)[] spans,
        List<double> claimed,
        LayoutPoint[] scratch)
    {
        // 拿两端的中点当参照，决定每段空隙该贴哪一边排：贴离两端近的那一边，绕行就短。
        var reference = ranksAreVertical
            ? (source.CenterX + target.CenterX) / 2
            : (source.CenterY + target.CenterY) / 2;

        var evaluated = 0;

        foreach (var span in spans)
        {
            for (var index = 0; index < MaxLanesPerSpan; index++)
            {
                var lane = LaneFor(span, reference, index);

                if (IsClaimed(lane, claimed)
                    || !BuildChannelCandidate(scratch, source, target, ranksAreVertical, lane))
                {
                    continue;
                }

                if (++evaluated > MaxCandidatesPerEdge)
                {
                    return null;
                }

                if (IsUsable(scratch, source.Id, target.Id, grid, buffer, ranksAreVertical))
                {
                    claimed.Add(lane);
                    return [.. scratch];
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 一段空隙里的第几条通道落在哪：第一条贴着离两端最近的那条边缘，之后按
    /// <see cref="LanePitch"/> 朝空隙里面走。
    /// </summary>
    private static double LaneFor((double Low, double High) span, double reference, int index)
    {
        var lane = Math.Abs(reference - span.Low) <= Math.Abs(reference - span.High)
            ? span.Low + LaneGap + (index * LanePitch)
            : span.High - LaneGap - (index * LanePitch);

        return Math.Clamp(lane, span.Low, span.High);
    }

    /// <summary>
    /// 按一条通道拼出候选折线：两端各自从最近的那一侧出去，中间那一段走在通道上。
    /// </summary>
    /// <remarks>
    /// 通道夹在某个端点的横向区间里时这条候选不成立——那样腿要从节点身子里出来。
    /// </remarks>
    private static bool BuildChannelCandidate(
        LayoutPoint[] scratch,
        PlacedNode source,
        PlacedNode target,
        bool ranksAreVertical,
        double lane)
    {
        if (ranksAreVertical)
        {
            if (IsInside(lane, source.X, source.Right) || IsInside(lane, target.X, target.Right))
            {
                return false;
            }

            var sourceExit = lane <= source.X ? source.X : source.Right;
            var targetExit = lane <= target.X ? target.X : target.Right;

            scratch[0] = new LayoutPoint(sourceExit, source.CenterY);
            scratch[1] = new LayoutPoint(lane, source.CenterY);
            scratch[2] = new LayoutPoint(lane, target.CenterY);
            scratch[3] = new LayoutPoint(targetExit, target.CenterY);

            return true;
        }

        if (IsInside(lane, source.Y, source.Bottom) || IsInside(lane, target.Y, target.Bottom))
        {
            return false;
        }

        var sourceExitY = lane <= source.Y ? source.Y : source.Bottom;
        var targetExitY = lane <= target.Y ? target.Y : target.Bottom;

        scratch[0] = new LayoutPoint(source.CenterX, sourceExitY);
        scratch[1] = new LayoutPoint(source.CenterX, lane);
        scratch[2] = new LayoutPoint(target.CenterX, lane);
        scratch[3] = new LayoutPoint(target.CenterX, targetExitY);

        return true;
    }

    private static bool IsInside(double value, double low, double high) =>
        value > low + Epsilon && value < high - Epsilon;

    private static bool IsClaimed(double lane, List<double> claimed)
    {
        foreach (var taken in claimed)
        {
            if (Math.Abs(taken - lane) <= Epsilon)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 一条绕行候选能不能用：不许引出负坐标，也不许把"压节点"从中间挪到外侧的另一处。
    /// </summary>
    /// <remarks>
    /// 负坐标那一条不是洁癖：整张图的范围是以原点为基准的，
    /// 负坐标表示不出来，导出与画布滚动区都会把它裁掉。
    /// </remarks>
    private static bool IsUsable(
        LayoutPoint[] candidate,
        string from,
        string to,
        NodeGrid grid,
        List<PlacedNode> buffer,
        bool ranksAreVertical)
    {
        foreach (var point in candidate)
        {
            if (point.X < 0 || point.Y < 0)
            {
                return false;
            }
        }

        return !CrossesAnyNode(from, to, candidate, grid, buffer, ranksAreVertical, mainAxisOnly: false);
    }

    /// <summary>
    /// 把端点、路由点与拐点拼成折线，去掉相邻的重复点。
    /// </summary>
    /// <remarks>
    /// 指定了端口时，端点与路由点不同（中间多一段向外的短走线），需要两个点都保留；
    /// 没指定时两者相同，去掉重复的那个。重复点在渲染上不出错，但会让"折线有几个拐点"
    /// 这类判断全部要多想一层。
    /// </remarks>
    private static LayoutPoint[] Assemble(
        LayoutPoint start,
        LayoutPoint startRoute,
        LayoutPoint channelStart,
        LayoutPoint channelEnd,
        LayoutPoint endRoute,
        LayoutPoint end)
    {
        var points = new List<LayoutPoint>(6) { start };

        Add(points, startRoute);
        Add(points, channelStart);
        Add(points, channelEnd);
        Add(points, endRoute);
        Add(points, end);

        return [.. points];

        static void Add(List<LayoutPoint> target, LayoutPoint point)
        {
            var last = target[^1];

            if (Math.Abs(last.X - point.X) > Epsilon || Math.Abs(last.Y - point.Y) > Epsilon)
            {
                target.Add(point);
            }
        }
    }

    /// <summary>从端口沿它所在的那条边往外走一小段。</summary>
    private static LayoutPoint Step(PlacedNode node, LayoutPort port, LayoutPoint anchor) => port.Side switch
    {
        PortSide.Left => new LayoutPoint(anchor.X - PortStub, anchor.Y),
        PortSide.Right => new LayoutPoint(anchor.X + PortStub, anchor.Y),
        PortSide.Top => new LayoutPoint(anchor.X, anchor.Y - PortStub),
        _ => new LayoutPoint(anchor.X, anchor.Y + PortStub),
    };

    private static LayoutPort? ResolvePort(
        string nodeId,
        string? portName,
        IReadOnlyDictionary<string, IReadOnlyList<LayoutPort>> ports) =>
        portName is not null
        && ports.TryGetValue(nodeId, out var nodePorts)
            ? nodePorts.FirstOrDefault(p => string.Equals(p.Name, portName, StringComparison.Ordinal))
            : null;

    /// <summary>
    /// 在指定的空隙里挑一个拐点坐标：先把被节点占用的区间减掉，再取最宽的一段的中点。
    /// </summary>
    /// <param name="bandLow">空隙区间的下界。</param>
    /// <param name="bandHigh">空隙区间的上界。</param>
    /// <param name="spanLow">横向段覆盖的范围，用来判断哪些节点真的挡在路上。</param>
    /// <param name="spanHigh">同上。</param>
    /// <param name="grid">节点索引。</param>
    /// <param name="buffer">候选节点缓冲区，由调用方复用。</param>
    /// <param name="horizontalChannel">为真时表示拐点在竖轴上取值，占用区间取节点的横向范围。</param>
    private static double PickChannel(
        double bandLow,
        double bandHigh,
        double spanLow,
        double spanHigh,
        NodeGrid grid,
        List<PlacedNode> buffer,
        bool horizontalChannel = false)
    {
        if (bandHigh - bandLow <= Epsilon)
        {
            return bandLow;
        }

        grid.Query(spanLow, bandLow, spanHigh, bandHigh, buffer);

        // 收集被占用的区间。只关心那些横向段真的会经过的节点：
        // 远处的节点不会挡路，把它们算进来会把可用空档切得七零八落。
        var blockers = new List<(double Low, double High)>(buffer.Count);

        foreach (var node in buffer)
        {
            var nodeSpanLow = horizontalChannel ? node.Y : node.X;
            var nodeSpanHigh = horizontalChannel ? node.Bottom : node.Right;

            if (nodeSpanHigh <= spanLow + Epsilon || nodeSpanLow >= spanHigh - Epsilon)
            {
                continue;
            }

            var low = horizontalChannel ? node.X : node.Y;
            var high = horizontalChannel ? node.Right : node.Bottom;

            if (high > bandLow + Epsilon && low < bandHigh - Epsilon)
            {
                blockers.Add((low, high));
            }
        }

        blockers.Sort((a, b) => a.Low.CompareTo(b.Low));

        // 从空隙的起点往右扫，逐段扣掉被占用的部分，记录剩下最宽的一段。
        var bestLow = double.NaN;
        double bestWidth = 0;
        var cursor = bandLow;

        foreach (var blocker in blockers)
        {
            if (blocker.Low - cursor > bestWidth)
            {
                bestWidth = blocker.Low - cursor;
                bestLow = cursor;
            }

            cursor = Math.Max(cursor, blocker.High);
        }

        if (bandHigh - cursor > bestWidth)
        {
            bestWidth = bandHigh - cursor;
            bestLow = cursor;
        }

        // 整个空隙都被占满时退回中点，并由调用方把它计入穿过节点的计数。
        return bestWidth > 0 ? bestLow + (bestWidth / 2) : (bandLow + bandHigh) / 2;
    }

    /// <summary>
    /// 折线是否穿过了它两端之外的节点。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 只检查横向与纵向的线段，与两端节点本身的相交不算——线是从那两个节点上出来的。
    /// 这里用线段与矩形的相交判定，不是只看端点：只看端点会漏掉"线段横穿节点"这种最常见的情况。
    /// </para>
    /// <para>
    /// <paramref name="mainAxisOnly"/> 为真时只看平行于主方向的那些段。绕行通道走在主方向上的空隙里，
    /// 它修得好的是主方向那一段的穿越；落在层间那条横段上的穿越要靠挑空档，不是靠绕行。
    /// </para>
    /// </remarks>
    private static bool CrossesAnyNode(
        string from,
        string to,
        LayoutPoint[] points,
        NodeGrid grid,
        List<PlacedNode> buffer,
        bool ranksAreVertical,
        bool mainAxisOnly)
    {
        for (var i = 0; i < points.Length - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];

            if (mainAxisOnly
                && (ranksAreVertical ? Math.Abs(a.X - b.X) : Math.Abs(a.Y - b.Y)) > Epsilon)
            {
                continue;
            }

            grid.Query(a.X, a.Y, b.X, b.Y, buffer);

            foreach (var node in buffer)
            {
                // 两端节点本身要排除：线就是从它们身上出来的。
                if (string.Equals(node.Id, from, StringComparison.Ordinal) ||
                    string.Equals(node.Id, to, StringComparison.Ordinal))
                {
                    continue;
                }

                if (SegmentCrossesRect(a, b, node))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentCrossesRect(LayoutPoint a, LayoutPoint b, PlacedNode node)
    {
        // 折线只有轴向段，因此相交判定可以退化成区间重叠判断。
        var segLowX = Math.Min(a.X, b.X);
        var segHighX = Math.Max(a.X, b.X);
        var segLowY = Math.Min(a.Y, b.Y);
        var segHighY = Math.Max(a.Y, b.Y);

        return segHighX > node.X + Epsilon
            && segLowX < node.Right - Epsilon
            && segHighY > node.Y + Epsilon
            && segLowY < node.Bottom - Epsilon;
    }
}
