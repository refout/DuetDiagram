using DuetDiagram.Core.Commands;

namespace DuetDiagram.Core.Broadcasting;

/// <summary>
/// 变更通知。
/// </summary>
/// <remarks>
/// AGENTS.md 约定 8：<see cref="Timestamp"/> 仅用于日志和审计，
/// 排序一律用单调递增的 <see cref="Version"/>（多进程场景时钟可能不同步）。
/// </remarks>
public sealed record ChangeNotification
{
    public required string DocumentId { get; init; }

    public required int Version { get; init; }

    public string[] AffectedIds { get; init; } = [];

    public required ChangeSource Source { get; init; }

    public required DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// 变更广播器。<see cref="Enqueue"/> 必须非阻塞。
/// </summary>
public interface IChangeBroadcaster : IAsyncDisposable
{
    void Enqueue(ChangeNotification notification);

    /// <summary>订阅。释放返回的句柄即取消订阅。</summary>
    IDisposable Subscribe(Action<ChangeNotification> handler);
}
