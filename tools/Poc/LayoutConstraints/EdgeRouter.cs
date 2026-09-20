namespace DuetDiagram.Poc.LayoutConstraints;

/// <summary>
/// 节点的均匀网格索引。
/// </summary>
/// <remarks>
/// <para>
/// 路由一开始写成对每条边遍历全部节点，这在千节点上是平方复杂度：950 条边乘 1000 个节点，
/// 实测 48 毫秒，和布局引擎本身一个量级——补齐逻辑本来应该可以忽略不计。
/// </para>
/// <para>
/// 用网格而不是按层分桶，是因为固定节点的纵坐标是自由的，层结构在它那里是断的，
/// 按层分桶会把锚点归到错误的桶里，查询就会漏。网格只依赖坐标，与层结构无关。
/// </para>
/// <para>
/// 查询结果写进调用方给的缓冲区，去重靠一个代次数组而不是每次新建集合。
/// 这两件事合起来把每条边的四次查询变成零分配——每条边都新建一个哈希集合的话，
/// 千条边就是几千次分配，光垃圾回收就比索引本身省下的时间还多。
/// </para>
/// <para>
/// 格子边长取节点尺寸量级的两倍左右：太小会让每个查询碰上很多空格子，
/// 太大就退化成遍历全部节点。
/// </para>
/// </remarks>
internal sealed class NodeGrid
{
    private const double CellSize = 160;

    private readonly PlacedNode[] _nodes;
    private readonly Dictionary<(int Column, int Row), List<int>> _cells = [];
    private readonly int[] _visited;
    private int _generation;

    public NodeGrid(PlacedNode[] nodes)
    {
        _nodes = nodes;
        _visited = new int[nodes.Length];

        for (var i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i];

            ForEachCell(node.X, node.Y, node.Right, node.Bottom, (column, row) =>
            {
                if (!_cells.TryGetValue((column, row), out var bucket))
                {
                    bucket = [];
                    _cells[(column, row)] = bucket;
                }

                bucket.Add(i);
            });
        }
    }

    /// <summary>
    /// 取与给定矩形相交的候选节点，写进 <paramref name="buffer"/>，返回个数。
    /// 结果是候选集，调用方仍需做精确的相交判定。
    /// </summary>
    /// <remarks>
    /// 格子遍历写成嵌套循环而不是带状态机的迭代器：迭代器每调用一次都会分配一个状态机对象，
    /// 而这个方法每条边要调用四次，千条边就是近四千次分配，光回收就抵掉了索引省下的时间。
    /// </remarks>
    public int Query(double x0, double y0, double x1, double y1, List<PlacedNode> buffer)
    {
        buffer.Clear();
        _generation++;

        var minColumn = (int)Math.Floor(Math.Min(x0, x1) / CellSize);
        var maxColumn = (int)Math.Floor(Math.Max(x0, x1) / CellSize);
        var minRow = (int)Math.Floor(Math.Min(y0, y1) / CellSize);
        var maxRow = (int)Math.Floor(Math.Max(y0, y1) / CellSize);

        for (var column = minColumn; column <= maxColumn; column++)
        {
            for (var row = minRow; row <= maxRow; row++)
            {
                if (!_cells.TryGetValue((column, row), out var bucket))
                {
                    continue;
                }

                foreach (var index in bucket)
                {
                    // 一个节点可能横跨多个格子，用代次标记去重，避免重复判定。
                    if (_visited[index] == _generation)
                    {
                        continue;
                    }

                    _visited[index] = _generation;
                    buffer.Add(_nodes[index]);
                }
            }
        }

        return buffer.Count;
    }

    private static void ForEachCell(double x0, double y0, double x1, double y1, Action<int, int> visit)
    {
        var minColumn = (int)Math.Floor(x0 / CellSize);
        var maxColumn = (int)Math.Floor(x1 / CellSize);
        var minRow = (int)Math.Floor(y0 / CellSize);
        var maxRow = (int)Math.Floor(y1 / CellSize);

        for (var column = minColumn; column <= maxColumn; column++)
        {
            for (var row = minRow; row <= maxRow; row++)
            {
                visit(column, row);
            }
        }
    }
}

/// <summary>
/// 边的折线重算。
/// </summary>
/// <remarks>
/// <para>
/// 为什么必须重算：引擎返回的折线是按它自己算出的节点坐标画的。锚点回填与层内让位都会移动节点，
/// 移动之后那些折线就指向了旧位置——看起来是线从节点旁边擦过去，或者悬在半空。
/// 只有把折线一起重算，结果才是自洽的。
/// </para>
/// <para>
/// 路由方式沿用分层布局的常规做法：从起点朝向终点的那条边出去，
/// 在两层之间的空隙里横向走一段，再从对面进入终点。
/// **拐点走两层之间的空隙**是关键——那个区域天然是空的，
/// 只要不被固定节点占用，横向段就不会压到任何节点。
/// </para>
/// <para>
/// 固定节点的纵坐标是自由的，可能正好卡在两层之间，把空隙切碎。
/// 所以挑拐点位置时要先把被占用的区间减掉，在剩下的空档里取最宽的一段。
/// 这是精确计算而不是采样试探：采样会漏掉窄但可用的空档，而且结果依赖采样点数量。
/// </para>
/// <para>
/// 若整个空隙都被固定节点占满，就退回中点（可能是最近的可用位置）并如实计入穿过节点的计数，
/// 由上层决定是否提示用户。**不假装绕开**：一条看起来能走通但实际压过节点的折线，
/// 比一条明确报告有冲突的折线更难排查。
/// </para>
/// </remarks>
internal static class EdgeRouter
{
    private const double Epsilon = 0.01;

    /// <summary>折线从节点边界往外让出的距离，避免与边框重叠。</summary>


    public static RoutedEdge[] Route(
        PlacedNode[] nodes,
        ConstraintEdge[] edges,
        bool ranksAreVertical,
        out int endpointFailures,
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
        var crossings = 0;

        foreach (var edge in edges)
        {
            if (!byId.TryGetValue(edge.From, out var source) || !byId.TryGetValue(edge.To, out var target))
            {
                // 收缩阶段被移除的组内边不会走到这里；真走到这里说明边引用了不存在的节点。
                failures++;
                continue;
            }

            var points = ranksAreVertical
                ? RouteVertical(source, target, grid, channelBuffer)
                : RouteHorizontal(source, target, grid, channelBuffer);

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

            routed.Add(new RoutedEdge(edge.From, edge.To, points));
        }

        endpointFailures = failures;
        crossingEdges = crossings;

        return [.. routed];
    }

    /// <summary>层沿纵向排列时的折线：出口在上下边，拐弯在两层之间的空隙里。</summary>
    private static LayoutPoint[] RouteVertical(PlacedNode source, PlacedNode target, NodeGrid grid, List<PlacedNode> buffer)
    {
        var goingDown = target.CenterY >= source.CenterY;

        var start = new LayoutPoint(source.CenterX, goingDown ? source.Bottom : source.Y);
        var end = new LayoutPoint(target.CenterX, goingDown ? target.Y : target.Bottom);

        var channelY = PickChannel(
            Math.Min(start.Y, end.Y),
            Math.Max(start.Y, end.Y),
            Math.Min(start.X, end.X),
            Math.Max(start.X, end.X),
            grid,
            buffer);

        return
        [
            start,
            new LayoutPoint(start.X, channelY),
            new LayoutPoint(end.X, channelY),
            end,
        ];
    }

    /// <summary>层沿横向排列时的折线。左右方向下拐点走的是两层之间竖直的空隙。</summary>
    private static LayoutPoint[] RouteHorizontal(PlacedNode source, PlacedNode target, NodeGrid grid, List<PlacedNode> buffer)
    {
        var goingRight = target.CenterX >= source.CenterX;

        var start = new LayoutPoint(goingRight ? source.Right : source.X, source.CenterY);
        var end = new LayoutPoint(goingRight ? target.X : target.Right, target.CenterY);

        var channelX = PickChannel(
            Math.Min(start.X, end.X),
            Math.Max(start.X, end.X),
            Math.Min(start.Y, end.Y),
            Math.Max(start.Y, end.Y),
            grid,
            buffer,
            horizontalChannel: true);

        return
        [
            start,
            new LayoutPoint(channelX, start.Y),
            new LayoutPoint(channelX, end.Y),
            end,
        ];
    }

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
