namespace DuetDiagram.Layout;

/// <summary>
/// 布局后的一个节点。
/// </summary>
/// <remarks>
/// 标识与尺寸从输入原样带过来。带上尺寸是为了让调用方不必再回头查输入——
/// 画图、命中测试、路由都要用到它。
/// </remarks>
public sealed record PlacedNode(string Id, double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + (Width / 2);

    public double CenterY => Y + (Height / 2);

    /// <summary>取指定名称的端口在同一坐标系下的位置。</summary>
    /// <remarks>
    /// 端口位置由所在边与偏移算出。自动端口的偏移是按形状均分的，
    /// 自定义端口带自己的偏移，两者在这里走同一条计算。
    /// </remarks>
    public LayoutPoint PortAnchor(LayoutPort port)
    {
        ArgumentNullException.ThrowIfNull(port);

        var t = Math.Clamp(port.Offset, 0, 1);

        return port.Side switch
        {
            Core.Model.PortSide.Left => new LayoutPoint(X, Y + (Height * t)),
            Core.Model.PortSide.Right => new LayoutPoint(Right, Y + (Height * t)),
            Core.Model.PortSide.Top => new LayoutPoint(X + (Width * t), Y),
            _ => new LayoutPoint(X + (Width * t), Bottom),
        };
    }

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
        X < other.Right - Tolerance
        && Right > other.X + Tolerance
        && Y < other.Bottom - Tolerance
        && Bottom > other.Y + Tolerance;

    private const double Tolerance = 0.01;
}

/// <summary>一条边的折线。点序为从起点到终点，首尾分别落在两端元素的边界上。</summary>
public sealed record RoutedEdge(string Id, LayoutPoint[] Points);

/// <summary>
/// 布局诊断。
/// </summary>
/// <remarks>
/// <para>
/// 与结果一起返回，让调用方自己决定这次布局能不能接受。
/// 引擎不替调用方做这个判断：同一个重叠在拖动的过程中无所谓，在导出成图时就不可接受。
/// </para>
/// <para>
/// 分阶段计时而不是只记总耗时。只记总耗时在排查时等于没有线索——
/// 引擎慢要换引擎，补齐逻辑慢要改算法，两种处置完全不同。
/// </para>
/// </remarks>
/// <param name="AnchorCount">固定位置的节点数。</param>
/// <param name="MaxAnchorDeviation">固定坐标的最大偏移。这一项必须为零。</param>
/// <param name="ResidualOverlaps">剩下的重叠对数。正常情况为零。</param>
/// <param name="ReflowedNodes">被让位移动过的节点数。</param>
/// <param name="OverlappingAnchors">互相压住的固定节点对数。这是输入矛盾，不是算法问题。</param>
/// <param name="EdgeCount">边数。</param>
/// <param name="EndpointFailures">端点没落在边界上的边数。这一项必须为零。</param>
/// <param name="EdgesCrossingNodes">折线穿过其它节点的边数。质量指标，见下。</param>
public sealed record LayoutDiagnostics(
    int AnchorCount,
    double MaxAnchorDeviation,
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
    TimeSpan RoutingTime)
{
    /// <summary>这次布局是否满足全部硬保证。</summary>
    /// <remarks>
    /// 只覆盖硬保证：固定坐标不偏、没有残留重叠、端点贴合。
    /// 折线穿越节点**不在**其中——当锚点把节点拉到别的层时，
    /// 连到它的边必然穿过中间的层，那是锚点语义的后果而不是缺陷。
    /// </remarks>
    public bool SatisfiesHardGuarantees =>
        MaxAnchorDeviation <= 0.01 && ResidualOverlaps == 0 && EndpointFailures == 0;
}

/// <summary>布局结果。</summary>
public sealed record EngineLayoutResult(
    PlacedNode[] Nodes,
    RoutedEdge[] Edges,
    double Width,
    double Height,
    LayoutDiagnostics Diagnostics)
{
    public PlacedNode? Find(string id) =>
        Nodes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.Ordinal));
}
