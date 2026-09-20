using System.Diagnostics;
using System.Runtime.ExceptionServices;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Layout;

/// <summary>
/// 逐级降级的布局协调器。
/// </summary>
/// <remarks>
/// <para>
/// 按计划逐级尝试，第一个在预算内成功的就是结果。每一级丢掉一部分约束，
/// 换取"总能给出一个结果"这个保证——大图或刁钻的约束下，宁可少几条约束，
/// 也不该让用户看到一片空白。
/// </para>
/// <para>
/// **超时用带时限的等待来判定，而不是靠引擎自己看令牌。** 引擎调用那一阶段是第三方的
/// 同步调用，它不会（也不该）为了配合我们的预算而变得可中断。只有从外面卡时限，
/// 预算才是真的预算；靠协作式取消的话，一个跑十分钟的引擎调用会把四级预算一起吞掉。
/// </para>
/// <para>
/// 被放弃的那一次**不会立刻停止**：它在引擎调用那一阶段仍然跑着。
/// 这安全的，因为引擎是纯函数——它不碰共享状态，也不会改文档。
/// 取消令牌会让它在下一个阶段边界停下来，所以残留的时间是有限的。
/// </para>
/// <para>
/// **不注入时间提供者。** 预算约束的是真实流逝的时间，用一个可以手动推进的时钟来算，
/// 会让"超时"变成一个由测试摆布的虚构事件，而真实的超时行为反而没被验证过。
/// 超时路径用"故意很慢的引擎 + 很小的预算"来测，那测的是真东西。
/// </para>
/// </remarks>
public sealed class LayoutCoordinator
{
    /// <summary>
    /// 剩余时间少于这个值就不再开新的一级。
    /// </summary>
    /// <remarks>
    /// 不留余量的话，最后一级会分到几毫秒的预算，几乎必然超时，
    /// 于是白白多一条"试过了但超时"的记录，而那条记录会让人误以为引擎有问题。
    /// 宁可如实报告"没时间试了"。
    /// </remarks>
    private static readonly TimeSpan MinimumUsefulBudget = TimeSpan.FromMilliseconds(5);

    private readonly ILayoutEngine _engine;
    private readonly LayoutPlan _plan;
    private readonly ILayoutConflictLog _conflicts;

    public LayoutCoordinator(
        ILayoutEngine engine,
        LayoutPlan? plan = null,
        ILayoutConflictLog? conflicts = null)
    {
        ArgumentNullException.ThrowIfNull(engine);

        _engine = engine;
        _plan = plan ?? LayoutPlan.Default;
        _conflicts = conflicts ?? NullLayoutConflictLog.Instance;
    }

    /// <summary>
    /// 求解一次布局，必要时逐级降级。
    /// </summary>
    /// <param name="job">输入。</param>
    /// <param name="budget">总预算。为空时用结构变更那一档。</param>
    /// <param name="cancellationToken">外部取消。</param>
    /// <exception cref="LayoutFailedException">全部级别都没能给出结果。</exception>
    public LayoutResult Compute(
        LayoutJob job,
        TimeSpan? budget = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var total = budget ?? LayoutBudgets.StructuralChange;
        var clock = Stopwatch.StartNew();
        var attempts = new List<LayoutAttempt>();

        foreach (var entry in _plan.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = total - clock.Elapsed;

            if (remaining < MinimumUsefulBudget)
            {
                break;
            }

            // 没有模型提出的约束时，这一级的输入与上一级完全相同，试它只是白花时间。
            if (entry.Level == LayoutFallbackLevel.DropLlm && !job.Hints.HasAny(ConstraintOwner.Llm))
            {
                continue;
            }

            var outcome = FallbackPlan.Apply(entry.Level, job);

            if (outcome.Dropped.Count > 0)
            {
                _conflicts.RecordDowngrade(entry.Level, outcome.Dropped);
            }

            var levelBudget = remaining < entry.Budget ? remaining : entry.Budget;
            var watch = Stopwatch.StartNew();

            EngineLayoutResult? layout;

            try
            {
                layout = RunWithBudget(outcome.Request, levelBudget, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 引擎自己出错了。记一条非超时的失败，继续往下一级试——
                // 下一级的输入更简单，有可能就过去了。
                watch.Stop();
                attempts.Add(new LayoutAttempt(entry.Level, TimedOut: false, watch.Elapsed, outcome.Dropped));
                _ = ex;
                continue;
            }

            watch.Stop();

            if (layout is null)
            {
                attempts.Add(new LayoutAttempt(entry.Level, TimedOut: true, watch.Elapsed, outcome.Dropped));
                continue;
            }

            attempts.Add(new LayoutAttempt(entry.Level, TimedOut: false, watch.Elapsed, outcome.Dropped));

            return new LayoutResult(layout, entry.Level, attempts);
        }

        throw new LayoutFailedException(
            CommandError.Of(ErrorCodes.LayoutAllLevelsTimeout, job.Nodes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new LayoutFailurePayload(attempts));
    }

    /// <summary>
    /// 在时限内跑一次引擎。超时返回空。
    /// </summary>
    /// <remarks>
    /// 超时时把令牌取消掉，让被放弃的那一次在下一个阶段边界停下。
    /// 不取消的话它会跑完整轮，白白占着线程。
    /// </remarks>
    private EngineLayoutResult? RunWithBudget(
        LayoutRequest request,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        using var level = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var task = Task.Run(() => _engine.Layout(request, level.Token), level.Token);

        bool completed;

        try
        {
            completed = task.Wait(budget);
        }
        catch (AggregateException ex)
        {
            // 把引擎内部的异常原样抛出，不要包一层 AggregateException——
            // 上层要按异常本身决定处置，包一层会让它们都要多写一次展开。
            ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw();
            throw;
        }

        if (completed)
        {
            return task.Result;
        }

        level.Cancel();
        return null;
    }
}
