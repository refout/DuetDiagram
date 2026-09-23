using DuetDiagram.Core.Commands;

namespace DuetDiagram.Mcp.Server;

/// <summary>
/// 每份凭据每分钟能发多少次请求。
/// </summary>
/// <remarks>
/// <para>
/// 按**凭据**计数，不按来源地址：一个代理可能从多个出口发请求，而一个出口后面可能
/// 挤着好几个代理。按地址算的话，前者会被无辜限制，后者会互相掩护。
/// </para>
/// <para>
/// 窗口是滑动的：只看"这一分钟里已经发了几次"，而不是"当前这一分钟格子里的次数"。
/// 固定格子的写法在格子交界处能放进两倍的量，而超出的那一次看起来完全正常。
/// </para>
/// <para>
/// 状态按凭据名分开，而凭据表是有界的，所以这张表也是有界的。按来源地址分桶就不同了——
/// 那个键由对端决定，能撑到多大由对端说了算。
/// </para>
/// </remarks>
public sealed class RateLimiter
{
    /// <summary>触发限流时用的错误码。</summary>
    public const string RateLimitedCode = ErrorCodes.McpRateLimited;

    /// <summary>窗口长度。约束写的是"每分钟"。</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly int _perWindow;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _windows = new(StringComparer.Ordinal);

    /// <param name="perWindow">一份凭据在一个窗口里最多发几次。</param>
    /// <param name="clock">读时刻的地方。用例传一个固定的，好把窗口推着走。</param>
    public RateLimiter(int perWindow, TimeProvider? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(perWindow, 1);

        _perWindow = perWindow;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// 记一次请求，并说这一次放不放行。
    /// </summary>
    /// <param name="name">凭据的名字。</param>
    /// <param name="retryAfterSeconds">过多久可以再来。放行时为零。</param>
    /// <returns>这一次是否放行。</returns>
    public bool TryAcquire(string name, out int retryAfterSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var now = _clock.GetUtcNow();

        lock (_gate)
        {
            if (!_windows.TryGetValue(name, out var stamps))
            {
                stamps = new Queue<DateTimeOffset>();
                _windows.Add(name, stamps);
            }

            while (stamps.Count > 0 && now - stamps.Peek() >= Window)
            {
                stamps.Dequeue();
            }

            if (stamps.Count < _perWindow)
            {
                stamps.Enqueue(now);
                retryAfterSeconds = 0;

                return true;
            }

            // 最早那一次滑出窗口的时间，就是下一次能发的时刻。向上取整到秒：
            // 报一个比自己实际能等的时间还短的值，调用方会在同一秒里再撞一次。
            var wait = Window - (now - stamps.Peek());
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));

            return false;
        }
    }
}
