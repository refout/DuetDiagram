using DuetDiagram.Layout;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 造布局结果的辅助方法。
/// </summary>
/// <remarks>
/// <para>
/// **坐标一律手写，不跑布局引擎。** 这一层测的是"拿到坐标之后画成什么"，
/// 而布局引擎的输出会随引擎版本变化。接上引擎之后，一次引擎升级会让十几个快照一起变红，
/// 而那时分不清是绘制错了还是布局变了。
/// </para>
/// <para>
/// 端到端那一层会真的跑引擎，那是另一个测试工程的事。
/// </para>
/// </remarks>
internal static class Layouts
{
    /// <summary>一份全零的诊断。绘制列表不看它。</summary>
    public static LayoutDiagnostics NoDiagnostics { get; } = new(
        AnchorCount: 0,
        MaxAnchorDeviation: 0,
        ResidualOverlaps: 0,
        ReflowedNodes: 0,
        OverlappingAnchors: 0,
        EdgeCount: 0,
        EndpointFailures: 0,
        UnresolvedEndpoints: 0,
        EdgesCrossingNodes: 0,
        ContractionTime: TimeSpan.Zero,
        EngineTime: TimeSpan.Zero,
        RestorationTime: TimeSpan.Zero,
        ReflowTime: TimeSpan.Zero,
        RoutingTime: TimeSpan.Zero);

    public static EngineLayoutResult Result(
        PlacedNode[] nodes,
        RoutedEdge[] edges,
        double width,
        double height) =>
        new(nodes, edges, width, height, NoDiagnostics);

    /// <summary>造一个节点。</summary>
    public static PlacedNode Node(string id, double x, double y, double width = 120, double height = 44) =>
        new(id, x, y, width, height);

    /// <summary>造一条正交折线。</summary>
    public static RoutedEdge Edge(string id, params (double X, double Y)[] points) =>
        new(id, [.. points.Select(p => new LayoutPoint(p.X, p.Y))]);

    /// <summary>
    /// 一条水平的正交折线，从左边元素右缘到右边元素左缘。
    /// </summary>
    /// <remarks>
    /// 折线本身不是这一层要验的东西，但它的形状会决定标签落在哪，
    /// 所以取一个最简单的、能一眼算出长度的形状。
    /// 两端高度相同时退化成一段直线——中间那两个点会重合，重合点在快照里只是噪声。
    /// </remarks>
    public static RoutedEdge Horizontal(string id, PlacedNode from, PlacedNode to)
    {
        if (Math.Abs(from.CenterY - to.CenterY) < Tolerance)
        {
            return Edge(id, (from.Right, from.CenterY), (to.X, to.CenterY));
        }

        var middle = (from.Right + to.X) / 2;

        return Edge(
            id,
            (from.Right, from.CenterY),
            (middle, from.CenterY),
            (middle, to.CenterY),
            (to.X, to.CenterY));
    }

    /// <summary>一条竖直的正交折线，从上面元素下缘到下面元素上缘。</summary>
    public static RoutedEdge Vertical(string id, PlacedNode from, PlacedNode to)
    {
        if (Math.Abs(from.CenterX - to.CenterX) < Tolerance)
        {
            return Edge(id, (from.CenterX, from.Bottom), (to.CenterX, to.Y));
        }

        var middle = (from.Bottom + to.Y) / 2;

        return Edge(
            id,
            (from.CenterX, from.Bottom),
            (from.CenterX, middle),
            (to.CenterX, middle),
            (to.CenterX, to.Y));
    }

    private const double Tolerance = 0.001;
}
