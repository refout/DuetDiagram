using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Commands;
using DuetDiagram.Render;

namespace DuetDiagram.App.Interaction;

/// <summary>
/// 把命令总线广播出来的变更通知，攒成一份"哪些元素被标记了、怎么标"的状态。
/// </summary>
/// <remarks>
/// <para>
/// **它只听变更通知，不自己猜。** 界面若靠比较前后两份文档来推断"哪个字段变了"，
/// LLM 改的东西就不会亮——那条路径根本不经过界面。通知里带着来源与受影响元素，
/// 是唯一一处同时覆盖人工与自动变更的说法。
/// </para>
/// <para>
/// 撤销与重做单独处理：它们的来源是 <see cref="ChangeSource.Undo"/> / <see cref="ChangeSource.Redo"/>，
/// 但用户想看的还是"谁改的"。所以这里记住每个元素上一次**真实**变更的来源，
/// 撤销重做时继承它，另加一个 ↶ / ↷ 符号。不记的话，撤销之后所有标记都会变成
/// 一个与来源无关的颜色，来源信息就丢了。
/// </para>
/// <para>
/// **标记有寿命。** 一条标记活 <see cref="Theme.HighlightMarkSeconds"/> 秒，过了整条作废。
/// 脉冲（更短的那一截）只是"刚才这一下"，角标与虚线轮廓是"刚才改过这几个"；
/// 两者都要停，否则改过的元素会永久带着一圈虚线与一个角标，改得越多画布越花——
/// 而那时用户已经看不出"哪些是刚改的"了，那正是标记要回答的问题。
/// </para>
/// <para>
/// 通知在后台投递线程上到达，画布在界面线程上读。两份状态之间用一把锁隔开：
/// 无锁的并发字典读起来快，但"读一遍得到一份自洽的快照"这件事它保证不了，
/// 而快照不自洽的表现是某一帧里同一个元素既是撤销又是脉冲。
/// </para>
/// </remarks>
public sealed class HighlightTracker : IDisposable
{
    private readonly Func<DateTimeOffset> _clock;
    private readonly Theme _theme;
    private readonly IDisposable _subscription;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _signaled = new(false);

    private readonly Dictionary<string, ElementHighlight> _highlights = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChangeSource> _lastRealSource = new(StringComparer.Ordinal);

    /// <summary>
    /// 每个元素那一条标记是什么时候开始的。
    /// </summary>
    /// <remarks>
    /// 撤销重做的标记也记在这里：它们没有脉冲，但同样有寿命——
    /// 寿命是整条标记的性质，而脉冲只是标记里可以没有的那一截。
    /// </remarks>
    private readonly Dictionary<string, DateTimeOffset> _markStart = new(StringComparer.Ordinal);

    public HighlightTracker(IChangeBroadcaster broadcaster, Theme theme, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(broadcaster);
        ArgumentNullException.ThrowIfNull(theme);

        _theme = theme;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _subscription = broadcaster.Subscribe(OnNotification);
    }

    /// <summary>
    /// 标记变了。
    /// </summary>
    /// <remarks>
    /// 在后台投递线程上触发。订阅方要自己回到界面线程——画布只能在那条线程上被作废。
    /// 没有这个信号的话，一次变更之后画布只在"文档重载"那一帧刷新过，
    /// 而通知通常比那一帧晚到，高亮就会一直不出现，直到用户下一次碰画布。
    /// </remarks>
    public event Action? Changed;

    /// <summary>有没有还活着的标记。过了寿命的不算。</summary>
    public bool HasHighlights
    {
        get
        {
            lock (_gate)
            {
                var now = _clock();

                foreach (var id in _highlights.Keys)
                {
                    if (!Expired(id, now))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    /// <summary>
    /// 当前是否还有脉冲在跑。
    /// </summary>
    /// <remarks>
    /// 画布按它决定要不要**按帧**重画。脉冲是动画，得逐帧出；而"标记过期"不是动画，
    /// 隔一会儿重画一次就够——两者合成一档的话，标记活着的那几秒里画布要白转三十帧每秒。
    /// </remarks>
    public bool HasActivePulse
    {
        get
        {
            lock (_gate)
            {
                var now = _clock();

                foreach (var (id, highlight) in _highlights)
                {
                    if (highlight.Kinds.Contains(HighlightKind.Pulse) && !PulseExpired(id, now))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    /// <summary>
    /// 脉冲当前相位，取值 0 到 1；没有脉冲在跑时返回负数。
    /// </summary>
    /// <remarks>
    /// 取**最近一次**变更的相位。多个元素同时被标记时它们共用一条时间线——
    /// 各算各的相位会让同一批变更的元素在不同时刻亮起，看起来像出了两回事。
    /// </remarks>
    public double PulsePhase
    {
        get
        {
            lock (_gate)
            {
                var now = _clock();
                var latest = DateTimeOffset.MinValue;

                foreach (var (id, highlight) in _highlights)
                {
                    if (!highlight.Kinds.Contains(HighlightKind.Pulse) || Expired(id, now))
                    {
                        continue;
                    }

                    if (_markStart.TryGetValue(id, out var start) && start > latest)
                    {
                        latest = start;
                    }
                }

                if (latest == DateTimeOffset.MinValue)
                {
                    return -1;
                }

                var elapsed = (now - latest).TotalSeconds;
                var phase = elapsed / _theme.HighlightPulseSeconds;

                return phase - Math.Floor(phase);
            }
        }
    }

    /// <summary>
    /// 当前标记的一份快照。过了寿命的整条不再出现，脉冲过期则只去掉脉冲那一截。
    /// </summary>
    /// <remarks>
    /// 顺手把过期的从表里清掉。不清的话，改过的元素会一直留在表里被逐帧遍历一遍——
    /// 一张大图上改得越多，每帧白跑的圈数越多，而它们早就不画了。
    /// </remarks>
    public IReadOnlyList<ElementHighlight> Snapshot()
    {
        lock (_gate)
        {
            if (_highlights.Count == 0)
            {
                return [];
            }

            var now = _clock();

            Prune(now);

            var list = new List<ElementHighlight>(_highlights.Count);

            foreach (var (id, highlight) in _highlights)
            {
                list.Add(highlight.Kinds.Contains(HighlightKind.Pulse) && PulseExpired(id, now)
                    ? highlight with { Kinds = WithoutPulse(highlight.Kinds) }
                    : highlight);
            }

            return list;
        }
    }

    /// <summary>
    /// 等最近一批通知被处理完。
    /// </summary>
    /// <remarks>
    /// 通知在后台投递线程上到达，测试要在断言之前确认它已经到了。真实界面不调它——
    /// 那一侧本来就按帧刷新，早一帧晚一帧没有关系。
    /// </remarks>
    public bool WaitForNotifications(TimeSpan timeout)
    {
        var arrived = _signaled.Wait(timeout);

        // 等过一次就复位，否则下一次等会立刻返回，测试就会在通知到达之前断言。
        _signaled.Reset();

        return arrived;
    }

    private void OnNotification(ChangeNotification notification)
    {
        lock (_gate)
        {
            Apply(notification);
        }

        _signaled.Set();
        Changed?.Invoke();
    }

    private void Apply(ChangeNotification notification)
    {
        var now = _clock();
        var undoOrRedo = notification.Source is ChangeSource.Undo or ChangeSource.Redo;

        Prune(now);

        foreach (var id in notification.AffectedIds)
        {
            var source = notification.Source;

            if (undoOrRedo)
            {
                // 继承原命令的来源。找不到原来源时退回默认色，而不是把撤销本身当来源。
                if (_lastRealSource.TryGetValue(id, out var original))
                {
                    source = original;
                }
            }
            else
            {
                _lastRealSource[id] = notification.Source;
            }

            var kinds = undoOrRedo
                ? UndoKinds
                : ChangeKinds;

            _highlights[id] = new ElementHighlight(
                id,
                source,
                kinds,
                IsUndo: notification.Source == ChangeSource.Undo,
                IsRedo: notification.Source == ChangeSource.Redo);

            _markStart[id] = now;
        }
    }

    /// <summary>把过了寿命的标记整条清掉。调用方要已经拿着锁。</summary>
    private void Prune(DateTimeOffset now)
    {
        if (_highlights.Count == 0)
        {
            return;
        }

        List<string>? dead = null;

        foreach (var id in _highlights.Keys)
        {
            if (Expired(id, now))
            {
                (dead ??= []).Add(id);
            }
        }

        if (dead is null)
        {
            return;
        }

        foreach (var id in dead)
        {
            _highlights.Remove(id);
            _markStart.Remove(id);
        }
    }

    private static readonly IReadOnlySet<HighlightKind> ChangeKinds =
        new HashSet<HighlightKind> { HighlightKind.Pulse, HighlightKind.Badge, HighlightKind.Outline };

    private static readonly IReadOnlySet<HighlightKind> UndoKinds =
        new HashSet<HighlightKind> { HighlightKind.Badge, HighlightKind.Outline };

    private static IReadOnlySet<HighlightKind> WithoutPulse(IReadOnlySet<HighlightKind> kinds)
    {
        var next = new HashSet<HighlightKind>(kinds);

        next.Remove(HighlightKind.Pulse);

        return next;
    }

    /// <summary>这一条标记过了寿命没有。没记开始时刻的也当成过了——它画不出来。</summary>
    private bool Expired(string id, DateTimeOffset now) =>
        !_markStart.TryGetValue(id, out var start)
        || now >= start + TimeSpan.FromSeconds(_theme.HighlightMarkSeconds);

    /// <summary>这一条标记的脉冲跑完了没有。</summary>
    private bool PulseExpired(string id, DateTimeOffset now) =>
        !_markStart.TryGetValue(id, out var start)
        || now >= start + TimeSpan.FromSeconds(_theme.HighlightPulseSeconds);

    public void Dispose()
    {
        _subscription.Dispose();
        _signaled.Dispose();
    }
}
