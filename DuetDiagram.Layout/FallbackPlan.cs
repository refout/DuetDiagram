using DuetDiagram.Core.Model;

namespace DuetDiagram.Layout;

/// <summary>
/// 一次布局任务的完整输入。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="LayoutRequest"/> 的区别在于**约束带归属方**。
/// 协调器要靠归属方决定哪一级该丢什么，而引擎不需要知道归属方——
/// 它拿到的 <see cref="LayoutRequest"/> 里约束已经没有归属之分了。
/// </para>
/// <para>
/// 这一层区分让"降级"退化成一次纯函数式的输入变换：把任务降一级，
/// 得到一份新的请求，再调一次引擎。引擎全程不知道发生过降级。
/// </para>
/// </remarks>
public sealed record LayoutJob(
    LayoutNode[] Nodes,
    LayoutEdge[] Edges,
    Direction Direction,
    LayoutHints Hints)
{
    /// <summary>
    /// 按最高级别（全部保留）转成引擎输入。
    /// </summary>
    /// <remarks>
    /// 最高级别不丢任何东西，因此这个转换是无损的，可以公开放出来给直接调引擎的场合用。
    /// 其余级别不提供公开转换——它们会丢东西，调用方应当显式走协调器，
    /// 那样"这次布局降过级"这个事实才会被记录下来。
    /// </remarks>
    public LayoutRequest ToRequest() =>
        FallbackPlan.Apply(LayoutFallbackLevel.Full, this).Request;
}

/// <summary>降级后的请求，以及这一级丢掉了什么。</summary>
/// <param name="Request">交给引擎的输入。</param>
/// <param name="Dropped">丢掉的东西。给日志与排查用。</param>
public sealed record FallbackOutcome(LayoutRequest Request, IReadOnlyList<string> Dropped);

/// <summary>
/// 保留项矩阵：每一级保留什么、丢掉什么。
/// </summary>
/// <remarks>
/// <para>
/// 逐级丢掉的是**约束的归属方**，而不是约束的种类。这个切法来自一个判断：
/// 人定的比模型定的更值得保留——人看到图变样会立刻发觉，而模型只是少了一条提示。
/// 自动推导的排最后，因为它本来就是引擎自己的判断，丢掉只是回到引擎的默认行为。
/// </para>
/// <para>
/// **固定位置一直保留到最后一级。** 它是唯一由人直接给出的坐标，
/// 也是唯一"丢了用户立刻会发觉"的东西。只有连固定位置都保不住的场合才降到最后一级——
/// 那时说明这批输入已经让引擎完全跑不动了。
/// </para>
/// </remarks>
internal static class FallbackPlan
{
    public static FallbackOutcome Apply(LayoutFallbackLevel level, LayoutJob job)
    {
        var dropped = new List<string>();

        var keepPins = level != LayoutFallbackLevel.PureAuto;
        var keepConstraints = level is LayoutFallbackLevel.Full or LayoutFallbackLevel.DropLlm;
        var keepSpacing = level != LayoutFallbackLevel.PureAuto;

        var nodes = keepPins
            ? job.Nodes
            : [.. job.Nodes.Select(n => n with { Pinned = null })];

        if (!keepPins && job.Nodes.Any(n => n.Pinned is not null))
        {
            dropped.Add($"固定位置 {job.Nodes.Count(n => n.Pinned is not null)} 个");
        }

        var groups = keepConstraints ? SameRankGroups(job.Hints, level, dropped) : [];

        if (!keepConstraints)
        {
            ReportAllConstraints(job.Hints, dropped);
        }

        var hints = job.Hints;

        return new FallbackOutcome(
            new LayoutRequest(
                nodes,
                job.Edges,
                new LayoutOptions(
                    job.Direction,
                    keepSpacing ? hints.NodeSpacing : new LayoutOptions().NodeSpacing,
                    keepSpacing ? hints.LayerSpacing : new LayoutOptions().LayerSpacing,
                    groups)),
            dropped);
    }

    /// <summary>
    /// 收集这一级保留的同层约束。
    /// </summary>
    /// <remarks>
    /// 到 <see cref="LayoutFallbackLevel.DropLlm"/> 为止只丢模型提的，
    /// 人定的与自动推导的都留下。
    /// </remarks>
    private static IReadOnlyList<IReadOnlyList<string>> SameRankGroups(
        LayoutHints hints,
        LayoutFallbackLevel level,
        List<string> dropped)
    {
        var kept = new List<IReadOnlyList<string>>();
        var droppedLlm = 0;

        foreach (var constraint in hints.SameRank)
        {
            if (level == LayoutFallbackLevel.DropLlm && constraint.Owner == ConstraintOwner.Llm)
            {
                droppedLlm++;
                continue;
            }

            kept.Add(constraint.Value.Nodes);
        }

        if (droppedLlm > 0)
        {
            dropped.Add($"模型提出的同层约束 {droppedLlm} 条");
        }

        return kept;
    }

    /// <summary>把文档里所有约束按归属方报出来，用于"整类丢弃"那一级。</summary>
    private static void ReportAllConstraints(LayoutHints hints, List<string> dropped)
    {
        Report("同层", hints.SameRank.Count);
        Report("层内次序", hints.Order.Count);
        Report("对齐", hints.Align.Count);
        Report("相对位置", hints.Place.Count);

        void Report(string name, int count)
        {
            if (count > 0)
            {
                dropped.Add($"{name}约束 {count} 条");
            }
        }
    }
}
