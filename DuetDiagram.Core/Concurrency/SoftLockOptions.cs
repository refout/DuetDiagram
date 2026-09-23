namespace DuetDiagram.Core.Concurrency;

/// <summary>
/// 软锁的时长参数。
/// </summary>
/// <remarks>
/// 只有一个值，但仍然单独成类型：它是宿主给进来的，而宿主给的这个数与"多久算没人动过"
/// 绑在一起。写死在实现里的话，用例只能靠真的等三十秒来验过期——那种用例跑得慢，
/// 而且时长一改就全红。
/// </remarks>
public sealed record SoftLockOptions
{
    /// <summary>多久没有操作就算这个持有者不要了。</summary>
    /// <remarks>
    /// 这个数要与一次"改一次图"的耗时同一个量级。定得太短的话，一个正在慢慢想下一步的
    /// agent 会在两次操作之间被判成过期，而它的锁被别人拿走——表现是两个 agent 同时以为
    /// 自己在改这份文档。
    /// </remarks>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(30);

    /// <summary>多久没有操作就算过期。</summary>
    public TimeSpan Ttl { get; init; } = DefaultTtl;
}
