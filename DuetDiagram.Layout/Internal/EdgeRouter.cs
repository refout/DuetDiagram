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
/// </remarks>
internal static class EdgeRouter
{
    private const double Epsilon = 0.01;

    /// <summary>从指定端口出来时先往外走这么远，避免线贴着节点边框。</summary>
    private const double PortStub = 12;

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
    /// <param name="ranksAreVertical">层是不是沿纵向排列。</param>
    /// <param name="endpointFailures">端点没落在边界上的边数。</param>
    /// <param name="unresolvedEndpoints">端点解析不出来的边数。</param>
    /// <param name="crossingEdges">折线穿过其它节点的边数。</param>
    public static RoutedEdge[] Route(
        PlacedNode[] nodes,
        LayoutEdge[] edges,
        IReadOnlyDictionary<string, IReadOnlyList<LayoutPort>> ports,
        IReadOnlyDictionary<string, PlacedNode> compositeBoxes,
        bool ranksAreVertical,
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

            // 端点必须落在两端节点的边界上。落在内部说明线是从节点身子里钻出来的，
            // 落在外部说明线没有接到节点上——两种都是渲染时一眼能看出来的错误。
            if (!source.IsOnBoundary(points[0]) || !target.IsOnBoundary(points[^1]))
            {
                failures++;
            }

            if (CrossesAnyNode(edge.From, edge.To, points, grid, segmentBuffer))
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
    /// 只检查横向与纵向的线段，与两端节点本身的相交不算——线是从那两个节点上出来的。
    /// 这里用线段与矩形的相交判定，不是只看端点：只看端点会漏掉"线段横穿节点"这种最常见的情况。
    /// </remarks>
    private static bool CrossesAnyNode(
        string from,
        string to,
        LayoutPoint[] points,
        NodeGrid grid,
        List<PlacedNode> buffer)
    {
        for (var i = 0; i < points.Length - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];

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
