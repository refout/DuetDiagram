namespace DuetDiagram.Core.Commands;

/// <summary>
/// 一次变更的上下文：谁、在哪个会话里、为什么改的。
/// </summary>
/// <remarks>
/// <see cref="Timestamp"/> 与 <see cref="SessionId"/> 的填写时机是刻意分开的：
/// 调用方可以声明自己是谁（<see cref="ActorId"/>、<see cref="SessionId"/>），
/// 但时间戳只能由命令总线在真正执行的瞬间用注入的时钟生成。
/// 这样做的原因是调用方可能提前很久构造好命令对象，如果由它填时间戳，
/// 审计日志记的就是"构造时刻"而不是"生效时刻"，并发场景下没有意义。
/// 同理，调用方声明的 <see cref="SessionId"/> 为空时，总线会用当前会话兜底。
/// </remarks>
public sealed record ChangeContext
{
    public ChangeSource Source { get; init; } = ChangeSource.System;

    /// <summary>操作主体标识，例如用户 id 或代理 id。</summary>
    public string? ActorId { get; init; }

    /// <summary>
    /// 会话标识。格式由 <see cref="SessionIds"/> 的几个工厂方法统一生成，
    /// 不要手写字符串，否则同一种入口很容易出现两种写法。
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>变更原因，用于审计与冲突排查，可为空。</summary>
    public string? Reason { get; init; }

    /// <summary>总线在执行时回填。调用方设置的值会被覆盖。</summary>
    public DateTimeOffset Timestamp { get; init; }

    public static ChangeContext For(
        ChangeSource source,
        string? actorId = null,
        string? sessionId = null,
        string? reason = null) => new()
        {
            Source = source,
            ActorId = actorId,
            SessionId = sessionId,
            Reason = reason,
        };
}
