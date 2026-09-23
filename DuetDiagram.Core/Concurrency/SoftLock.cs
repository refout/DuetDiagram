using DuetDiagram.Core.Time;

namespace DuetDiagram.Core.Concurrency;

/// <summary>
/// 文档级软锁：一个主体说"这份文档我在改"，别人在这段时间里拿不到。
/// </summary>
/// <remarks>
/// <para>
/// **它与跨进程那把文档锁是两件事，不要合并。** 那一把管的是"这个进程在编辑这份文件"，
/// 靠独占句柄，粒度是进程与文件，抢不到就退成只读；这一把管的是"这个 agent 正在改这份文档"，
/// 靠时刻，粒度是文档，到期自动放。合并的话，一个卡住的 agent 会让整个进程连只读都退不进去，
/// 而用户看到的是一份再也改不动的文档。
/// </para>
/// <para>
/// **到期的判据是"最后一次操作"，不是"拿到锁的时刻"。** 只看拿锁时刻的话，
/// 一个每二十九秒动一次的 agent 会被判成过期，而它其实正在写。所以持有者的每一次操作
/// 都要把那个时刻往前推：宿主在命令成功之后调 <see cref="Renew"/>，或者再调一次
/// <see cref="TryAcquire"/>（同一个人再拿一次算成功）。
/// </para>
/// <para>
/// **释放是懒的，没有后台定时器。** 到期的锁在下一个来拿的人看来就是空的，
/// 于是自然被拿走——不另起一条清理线程，省下一处在进程退出时可能卡住的定时任务。
/// </para>
/// <para>
/// 这个类型只回答"现在谁拿着、还能不能拿"。它不碰文档、不发命令，也不决定拿不到锁时该做什么：
/// 那是宿主的事，因为它才知道该报错还是该排队。
/// </para>
/// </remarks>
public sealed class SoftLock
{
    private readonly Lock _gate = new();
    private readonly SoftLockOptions _options;
    private readonly ITimeProvider _clock;

    private string? _holder;
    private DateTimeOffset _lastSeen;

    /// <param name="options">时长参数。不传时用 <see cref="SoftLockOptions.DefaultTtl"/>。</param>
    /// <param name="clock">读时刻的地方。不传时读系统时间。</param>
    public SoftLock(SoftLockOptions? options = null, ITimeProvider? clock = null)
    {
        _options = options ?? new SoftLockOptions();
        _clock = clock ?? SystemTimeProvider.Instance;
    }

    /// <summary>多久没有操作就算过期。</summary>
    public TimeSpan Ttl => _options.Ttl;

    /// <summary>
    /// 这一刻还算数的持有者。
    /// </summary>
    /// <remarks>
    /// 没人拿着、或者拿着的那一位已经过了期，都是空。过期的锁在这里被就地清掉，
    /// 免得 <see cref="IsFree"/> 说空而内部那个字段还写着一个人名。
    /// </remarks>
    public string? Holder
    {
        get
        {
            lock (_gate)
            {
                return LiveHolder();
            }
        }
    }

    /// <summary>这一刻是不是没人拿着。</summary>
    public bool IsFree => Holder is null;

    /// <summary>
    /// 拿锁。
    /// </summary>
    /// <param name="owner">要拿锁的主体，通常是凭据名或会话标识。</param>
    /// <param name="heldBy">拿不到时，锁在谁手里。拿得到时为空。</param>
    /// <remarks>
    /// 同一个人再拿一次算成功，并顺带把到期时刻往后推：宿主重连之后会重新拿一次，
    /// 而它并没有失去这个锁。判成失败的话，那个 agent 会以为有人跟它抢，实际上没有。
    /// </remarks>
    public bool TryAcquire(string owner, out string? heldBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        lock (_gate)
        {
            var current = LiveHolder();

            if (current is null || string.Equals(current, owner, StringComparison.Ordinal))
            {
                _holder = owner;
                _lastSeen = _clock.UtcNow;
                heldBy = null;

                return true;
            }

            heldBy = current;

            return false;
        }
    }

    /// <summary>
    /// 持有者报一次活，把到期时刻往后推。
    /// </summary>
    /// <remarks>
    /// **已经过期就续不上了。** 续成功的话，两个主体会同时以为自己在改这份文档——
    /// 而先来的那一个已经在改了。它要重新走一次拿锁，那时拿得到就拿得到、拿不到就知道有人接手了。
    /// </remarks>
    public bool Renew(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        lock (_gate)
        {
            if (!string.Equals(LiveHolder(), owner, StringComparison.Ordinal))
            {
                return false;
            }

            _lastSeen = _clock.UtcNow;

            return true;
        }
    }

    /// <summary>
    /// 持有者主动放开。
    /// </summary>
    /// <remarks>
    /// 不是持有者时什么都不做，也返回假。让非持有者放得掉的话，一个拼错的主体名会把
    /// 别人的锁解掉，而那位正在写。
    /// </remarks>
    public bool Release(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        lock (_gate)
        {
            if (!string.Equals(LiveHolder(), owner, StringComparison.Ordinal))
            {
                return false;
            }

            _holder = null;

            return true;
        }
    }

    /// <summary>
    /// 这一刻真正还算数的那一份持有者。过期时顺手清掉字段。
    /// </summary>
    /// <remarks>
    /// 调用方必须已经持有门锁。过期判据用"最后一次操作"而不是"拿到锁的时刻"——
    /// 见类型说明里那一条。
    /// </remarks>
    private string? LiveHolder()
    {
        if (_holder is null)
        {
            return null;
        }

        if (_clock.UtcNow - _lastSeen > _options.Ttl)
        {
            _holder = null;

            return null;
        }

        return _holder;
    }
}
