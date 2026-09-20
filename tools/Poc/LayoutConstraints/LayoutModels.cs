namespace DuetDiagram.Poc.LayoutConstraints;

/// <summary>参与布局的节点。锚点为空表示这个节点由引擎自由摆放。</summary>
internal sealed record ConstraintNode(string Id, double Width, double Height, Anchor? Anchor = null);

/// <summary>
/// 用户显式声明的坐标。
/// </summary>
/// <remarks>
/// 它和引擎算出来的坐标地位完全不同：引擎的结果是建议，这个是不可让的意图。
/// 整个约束补齐流程要守的第一条不变量，就是布局结束后这些坐标一个像素都不能偏。
/// </remarks>
internal sealed record Anchor(double X, double Y);

internal sealed record ConstraintEdge(string From, string To);

/// <summary>要求同处一层的节点集合。</summary>
internal sealed record SameRankGroup(string Id, string[] Members);

internal sealed record ConstraintGraph(
    string Name,
    ConstraintNode[] Nodes,
    ConstraintEdge[] Edges,
    SameRankGroup[] SameRankGroups);

internal sealed record ConstraintOptions(string Direction, double NodeSpacing, double LayerSpacing)
{
    public static ConstraintOptions Default { get; } = new("TD", 36, 72);

    /// <summary>
    /// 层是不是沿纵向排列的。
    /// </summary>
    /// <remarks>
    /// 这个判断决定层内让位往哪个方向推。自上而下与自下而上两种布局里，层是上下叠的，
    /// 层内节点沿横向排开；左右两种布局里层是左右并排的，层内节点沿纵向排开。
    /// 注意这与"层的推进方向"无关：自下而上和自上而下的层内阅读方向都是从左往右，
    /// 所以两者用同一个方向让位。
    /// </remarks>
    public bool RanksAreVertical => Direction is "TD" or "BT";
}

/// <summary>折线上的一个点。</summary>
internal sealed record LayoutPoint(double X, double Y);

/// <summary>一条边的折线。点序为从起点到终点，首尾分别落在两端节点的边界上。</summary>
internal sealed record RoutedEdge(string From, string To, LayoutPoint[] Points);

internal sealed record PlacedNode(string Id, double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + (Width / 2);

    public double CenterY => Y + (Height / 2);

    /// <summary>点是否落在本节点边界上。内部与外部都算否。</summary>
    public bool IsOnBoundary(LayoutPoint point)
    {
        const double Tolerance = 0.01;

        var onVerticalEdge = (Math.Abs(point.X - X) < Tolerance || Math.Abs(point.X - Right) < Tolerance)
            && point.Y >= Y - Tolerance
            && point.Y <= Bottom + Tolerance;

        var onHorizontalEdge = (Math.Abs(point.Y - Y) < Tolerance || Math.Abs(point.Y - Bottom) < Tolerance)
            && point.X >= X - Tolerance
            && point.X <= Right + Tolerance;

        return onVerticalEdge || onHorizontalEdge;
    }

    /// <summary>两个矩形是否真的重叠。边框相接不算重叠。</summary>
    public bool Overlaps(PlacedNode other) =>
        X < other.Right - Epsilon
        && Right > other.X + Epsilon
        && Y < other.Bottom - Epsilon
        && Bottom > other.Y + Epsilon;

    private const double Epsilon = 0.01;
}

/// <summary>
/// 布局的分阶段诊断。
/// </summary>
/// <remarks>
/// 每一段单独计时是刻意的：只记总耗时的话，出问题时无法判断是引擎慢还是我们自己的补齐逻辑慢，
/// 而这两者的处置方式完全不同——前者要换引擎，后者要改我们的算法。
/// </remarks>
internal sealed record LayoutDiagnostics(
    int AnchorCount,
    int MaxAnchorDeviation,
    int ResidualOverlaps,
    int ReflowedNodes,
    int OverlappingAnchors,
    int EdgeCount,
    int EndpointFailures,
    int EdgesCrossingNodes,
    TimeSpan ContractionTime,
    TimeSpan EngineTime,
    TimeSpan RestorationTime,
    TimeSpan ReflowTime,
    TimeSpan RoutingTime);

/// <summary>布局结果。诊断与结果一起返回，便于调用方决定是否接受这次布局。</summary>
internal sealed record LayoutOutcome(
    PlacedNode[] Nodes,
    RoutedEdge[] Edges,
    double Width,
    double Height,
    LayoutDiagnostics Diagnostics)
{
    public PlacedNode? Find(string id) => Nodes.FirstOrDefault(n => n.Id == id);
}
