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
}

internal sealed record PlacedNode(string Id, double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    /// <summary>两个矩形是否真的重叠。边框相接不算重叠。</summary>
    public bool Overlaps(PlacedNode other) =>
        X < other.Right - Epsilon
        && Right > other.X + Epsilon
        && Y < other.Bottom - Epsilon
        && Bottom > other.Y + Epsilon;

    /// <summary>在横向上推开多少才能分离；已经分离时为 0。</summary>
    public double HorizontalSeparation(PlacedNode other) =>
        Math.Min(Right - other.X, other.Right - X);

    /// <summary>在纵向上推开多少才能分离；已经分离时为 0。</summary>
    public double VerticalSeparation(PlacedNode other) =>
        Math.Min(Bottom - other.Y, other.Bottom - Y);

    public PlacedNode MoveBy(double dx, double dy) => this with { X = X + dx, Y = Y + dy };

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
    TimeSpan ContractionTime,
    TimeSpan EngineTime,
    TimeSpan RestorationTime,
    TimeSpan ReflowTime);

/// <summary>布局结果。诊断与结果一起返回，便于调用方决定是否接受这次布局。</summary>
internal sealed record LayoutOutcome(PlacedNode[] Nodes, double Width, double Height, LayoutDiagnostics Diagnostics)
{
    public PlacedNode? Find(string id) => Nodes.FirstOrDefault(n => n.Id == id);
}
