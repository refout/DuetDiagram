namespace DuetDiagram.Layout;

/// <summary>
/// 路径预算：不同交互场景下布局最多能花多久。
/// </summary>
/// <remarks>
/// <para>
/// 分场景给不同预算，是因为"多快算快"取决于用户正在做什么。
/// 拖动过程中每帧都要重布局，超过一帧的时间就是卡顿；
/// 而手动触发的重布局用户已经在等结果了，让他多等一会儿换更好的布局是划算的。
/// </para>
/// <para>
/// <see cref="DragInProgress"/> 是零，表示**这一场景不调用布局**——
/// 拖动过程中的预览由渲染层用已有坐标直接画，松手之后才重新布局。
/// 传零预算给协调器会得到"一级都没试过"的失败，那是刻意的：
/// 与其让它悄悄用一个很短的预算草草算一个结果，不如让调用方显式知道这里不该调。
/// </para>
/// </remarks>
public static class LayoutBudgets
{
    /// <summary>拖动进行中：不调用布局。</summary>
    public static TimeSpan DragInProgress => TimeSpan.Zero;

    /// <summary>拖动松手：用户刚放手，期待立刻看到结果。</summary>
    public static TimeSpan DragReleased => TimeSpan.FromMilliseconds(200);

    /// <summary>结构变更：增删节点或边之后的重排。</summary>
    public static TimeSpan StructuralChange => TimeSpan.FromMilliseconds(800);

    /// <summary>手动重布局：用户主动点了按钮，愿意多等。</summary>
    public static TimeSpan ManualRelayout => TimeSpan.FromSeconds(2);
}

/// <summary>
/// 协调器的返回。
/// </summary>
/// <param name="Layout">引擎给出的坐标与折线。</param>
/// <param name="AppliedLevel">最终用的是哪一级。</param>
/// <param name="Attempts">每一级的尝试记录，含最终成功的那一级。</param>
/// <remarks>
/// 与引擎返回的类型分开：引擎不该知道降级这件事，因此它的结果里没有级别与尝试记录。
/// 级别信息由协调器在这一层补上。合成一个类型的话，引擎要么得返回一个自己都不知道含义的字段，
/// 要么得把级别一路透传下去，两种都会让界线消失。
/// </remarks>
public sealed record LayoutResult(
    EngineLayoutResult Layout,
    LayoutFallbackLevel AppliedLevel,
    IReadOnlyList<LayoutAttempt> Attempts)
{
    public PlacedNode[] Nodes => Layout.Nodes;

    public RoutedEdge[] Edges => Layout.Edges;

    public double Width => Layout.Width;

    public double Height => Layout.Height;

    public LayoutDiagnostics Diagnostics => Layout.Diagnostics;

    /// <summary>这次布局是否发生过降级。</summary>
    public bool WasDowngraded => AppliedLevel != LayoutFallbackLevel.Full;

    public PlacedNode? Find(string id) => Layout.Find(id);
}
