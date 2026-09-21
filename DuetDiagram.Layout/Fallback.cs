using DuetDiagram.Core.Commands;

namespace DuetDiagram.Layout;

/// <summary>
/// 降级级别。级别越高保留得越少。
/// </summary>
/// <remarks>
/// <para>
/// 从保留最多排到保留最少，顺序本身就是优先级：协调器按顺序试，第一个成功的就用它。
/// 把顺序写进枚举值的排列里，比另写一张优先级表可靠——表会与枚举脱节，排列不会。
/// </para>
/// <para>
/// 每一级丢掉的都是**输入的一部分**，而不是算法的一部分。引擎收到的永远是普通输入，
/// 它不知道自己是第几级。这样"降级"就退化成"少给一点约束再试一次"，
/// 没有额外的代码路径需要验证。
/// </para>
/// </remarks>
public enum LayoutFallbackLevel
{
    /// <summary>全部保留。</summary>
    Full,

    /// <summary>丢掉模型提出的约束。人定的与自动推导的仍然保留。</summary>
    DropLlm,

    /// <summary>丢掉全部约束。固定位置与间距仍然保留。</summary>
    DropAll,

    /// <summary>只按引擎自己的判断排。固定位置也丢掉，间距用引擎缺省值。</summary>
    PureAuto,
}

/// <summary>计划中的一级：降级级别与它分到的时间预算。</summary>
public sealed record LayoutPlanEntry(LayoutFallbackLevel Level, TimeSpan Budget);

/// <summary>
/// 降级计划。
/// </summary>
/// <remarks>
/// <para>
/// **级别不得重复。** 同一个级别出现两次意味着协调器会把同一份输入算两遍：
/// 第一次超时的话第二次同样会超时，白花时间；第一次成功的话第二次根本不会执行，
/// 那份预算形同虚设。两种情况都说明这份计划是写错的，因此在构造时就拒绝，
/// 而不是等运行时表现成"偶尔特别慢"。
/// </para>
/// <para>
/// 预算之和通常大于总预算，这是正常的：它们是每一级**最多**能用多少，
/// 而协调器还要看总共还剩多少。两重限制取较小值。
/// </para>
/// </remarks>
public sealed class LayoutPlan
{
    private readonly LayoutPlanEntry[] _entries;

    public LayoutPlan(params LayoutPlanEntry[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Length == 0)
        {
            throw new ArgumentException("降级计划至少要有一级。", nameof(entries));
        }

        var seen = new HashSet<LayoutFallbackLevel>();

        foreach (var entry in entries)
        {
            if (entry.Budget <= TimeSpan.Zero)
            {
                throw new ArgumentException(
                    $"级别 {entry.Level} 的预算必须为正，实际是 {entry.Budget}。",
                    nameof(entries));
            }

            if (!seen.Add(entry.Level))
            {
                throw new ArgumentException(
                    $"级别 {entry.Level} 在计划里出现了不止一次。"
                    + "重复的级别要么白花时间（都超时），要么预算形同虚设（第一次就成功）。",
                    nameof(entries));
            }
        }

        _entries = entries;
    }

    public IReadOnlyList<LayoutPlanEntry> Entries => _entries;

    /// <summary>缺省计划：四个预算。</summary>
    public static LayoutPlan Default { get; } = new(
        new LayoutPlanEntry(LayoutFallbackLevel.Full, TimeSpan.FromMilliseconds(300)),
        new LayoutPlanEntry(LayoutFallbackLevel.DropLlm, TimeSpan.FromMilliseconds(200)),
        new LayoutPlanEntry(LayoutFallbackLevel.DropAll, TimeSpan.FromMilliseconds(150)),
        new LayoutPlanEntry(LayoutFallbackLevel.PureAuto, TimeSpan.FromMilliseconds(150)));
}

/// <summary>
/// 一次尝试的记录。
/// </summary>
/// <param name="Level">这一级是什么。</param>
/// <param name="TimedOut">是不是因为超时被放弃的。</param>
/// <param name="Elapsed">这一级实际花了多久。</param>
/// <param name="Conflicts">这一级丢掉了哪些东西。没丢则为空。</param>
/// <remarks>
/// 记录的意义是事后能回答"这次布局为什么变慢了"。只有总耗时的话，
/// 看到的就是一个孤零零的数字，无法区分"第一级就慢"与"试了四级才成功"。
/// </remarks>
public sealed record LayoutAttempt(
    LayoutFallbackLevel Level,
    bool TimedOut,
    TimeSpan Elapsed,
    IReadOnlyList<string> Conflicts);

/// <summary>
/// 全部级别都失败时的载荷。
/// </summary>
/// <remarks>
/// <see cref="Attempts"/> 为空就是"一级都没试过"（总预算为零之类），
/// 调用方用它的数量判断即可，不需要额外的布尔字段。
/// </remarks>
public sealed record LayoutFailurePayload(IReadOnlyList<LayoutAttempt> Attempts);

/// <summary>
/// 布局彻底失败。
/// </summary>
/// <remarks>
/// 抛异常而不是返回一个"失败的结果"，是因为调用方拿不到坐标就没法继续——
/// 让它必须显式处理，比让它忘了判空然后画出一堆零坐标要好。
/// </remarks>
public sealed class LayoutFailedException : Exception
{
    public LayoutFailedException(CommandError error, LayoutFailurePayload payload)
        : base(BuildMessage(error, payload))
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(payload);

        Error = error;
        Payload = payload;
    }

    /// <summary>结构化错误，供上层决定怎么呈现。</summary>
    public CommandError Error { get; }

    /// <summary>每一级的尝试记录。</summary>
    public LayoutFailurePayload Payload { get; }

    private static string BuildMessage(CommandError error, LayoutFailurePayload payload) =>
        $"{error.Code}：{payload.Attempts.Count} 级全部失败。";
}

/// <summary>
/// 降级冲突日志。
/// </summary>
/// <remarks>
/// 降级会丢掉一部分用户或模型给出的约束，而那件事**必须留下痕迹**：
/// 用户看到的是"我设的同层约束没生效"，如果没有日志，排查时无从知道
/// 是约束没进布局，还是进了但被降级丢掉了。
/// </remarks>
public interface ILayoutConflictLog
{
    /// <summary>记录一次降级。</summary>
    /// <param name="level">降到了哪一级。</param>
    /// <param name="dropped">丢掉了哪些东西。</param>
    void RecordDowngrade(LayoutFallbackLevel level, IReadOnlyList<string> dropped);
}

/// <summary>不记录任何东西。用于不需要日志的场合。</summary>
public sealed class NullLayoutConflictLog : ILayoutConflictLog
{
    public static NullLayoutConflictLog Instance { get; } = new();

    public void RecordDowngrade(LayoutFallbackLevel level, IReadOnlyList<string> dropped)
    {
    }
}
