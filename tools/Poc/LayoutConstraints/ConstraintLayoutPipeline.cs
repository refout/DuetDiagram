using System.Diagnostics;

namespace DuetDiagram.Poc.LayoutConstraints;

/// <summary>
/// 带约束的布局流程。
/// </summary>
/// <remarks>
/// <para>
/// 引擎不支持的两项约束全部在这一层补齐，顺序是有讲究的：
/// </para>
/// <list type="number">
/// <item><b>收缩</b>：把同层组换成超节点。必须在引擎之前，因为约束要在布局前表达出来，
/// 布局完成后再调整就变成了打补丁。</item>
/// <item><b>引擎</b>：只看到合法的分层图，不知道同层组的存在。</item>
/// <item><b>展开</b>：把超节点占的空间填上。此时所需空间已经被预留，是纯局部操作。</item>
/// <item><b>回填锚点</b>：把用户声明的坐标放回去。</item>
/// <item><b>行内让位</b>：把被锚点占位的自由节点在同一层内重新排开。</item>
/// </list>
/// <para>
/// 第 5 步之所以不可省：锚点是用户自由放的，它可能正好落在引擎已经安排了节点的地方。
/// 没有这一步，用户每次微调都会把自己的节点和邻居叠在一起。
/// </para>
/// <para>
/// 全程只有一条硬保证——<b>锚点坐标一个像素都不偏</b>。无重叠是这套算法的必然结果而不是收敛目标：
/// 让位在每一层内一次扫描完成，而层与层之间本来就不同高，所以不会留下"差一点"的状态。
/// 唯一真正无解的情形是两个锚点互相压住——那是输入本身矛盾，只能如实报出来。
/// </para>
/// </remarks>
internal static class ConstraintLayoutPipeline
{
    /// <summary>
    /// 行内让位的时间上限。
    /// </summary>
    /// <remarks>
    /// 让位在每一层内是线性扫描，正常量级下应当是毫秒级。
    /// 设一个上限是为了防止层的划分出问题时退化成平方复杂度而拖住整个编辑操作。
    /// 它只约束补齐逻辑自己，不包含引擎耗时——引擎的预算由外层的路径预算单独管。
    /// </remarks>
    public static readonly TimeSpan ReflowBudget = TimeSpan.FromMilliseconds(100);

    public static LayoutOutcome Compute(ConstraintGraph graph, ConstraintOptions options)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(options);

        var contractionWatch = Stopwatch.StartNew();
        var contraction = SameRankContraction.Contract(graph, options);
        contractionWatch.Stop();

        var engineWatch = Stopwatch.StartNew();
        var placed = DagreEngine.Compute(contraction.Graph, options);
        engineWatch.Stop();

        var restorationWatch = Stopwatch.StartNew();
        var expanded = SameRankContraction.Expand(placed, contraction.Expansions, graph);
        var restored = AnchorRestore.Apply(expanded, graph);
        restorationWatch.Stop();

        var anchored = graph.Nodes
            .Where(n => n.Anchor is not null)
            .Select(n => n.Id)
            .ToHashSet(StringComparer.Ordinal);

        var reflowWatch = Stopwatch.StartNew();
        var reflow = RowReflow.Apply(restored, anchored);
        reflowWatch.Stop();

        // 重叠统计放在计时之外：它是验证手段而不是算法的一部分，
        // 而且是两两比较的平方复杂度，算进去会让让位看起来比实际慢两个数量级。
        var residualOverlaps = RowReflow.CountOverlaps(reflow.Nodes);
        var overlappingAnchors = anchored.Count > 1
            ? RowReflow.CountOverlaps([.. reflow.Nodes.Where(n => anchored.Contains(n.Id))])
            : 0;

        var diagnostics = new LayoutDiagnostics(
            anchored.Count,
            AnchorRestore.MaxDeviation(reflow.Nodes, graph),
            residualOverlaps,
            reflow.ReflowedCount,
            overlappingAnchors,
            contractionWatch.Elapsed,
            engineWatch.Elapsed,
            restorationWatch.Elapsed,
            reflowWatch.Elapsed);

        var width = reflow.Nodes.Max(n => n.Right);
        var height = reflow.Nodes.Max(n => n.Bottom);

        return new LayoutOutcome(reflow.Nodes, width, height, diagnostics);
    }
}
