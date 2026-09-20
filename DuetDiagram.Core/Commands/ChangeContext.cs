namespace DuetDiagram.Core.Commands;

/// <summary>
/// 一次变更的上下文。由调用方声明来源，由命令总线补全 <see cref="Timestamp"/> 与 <see cref="SessionId"/>。
/// </summary>
public sealed record ChangeContext
{
    public ChangeSource Source { get; init; } = ChangeSource.System;

    public string? ActorId { get; init; }

    /// <summary>格式见 <see cref="SessionIds"/>。</summary>
    public string? SessionId { get; init; }

    public string? Reason { get; init; }

    /// <summary>仅命令总线可填充（AGENTS.md 约定 4）。</summary>
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
