using DuetDiagram.Core.Commands;

namespace DuetDiagram.Core.Logging;

public enum AuditKind
{
    Executed,
    Failed,
    Rejected,
    Cancelled,
    Undone,
    Redone,
}

/// <summary>
/// 审计条目。
/// </summary>
/// <remarks>
/// AGENTS.md 约定 9：<see cref="CommandError.Payload"/> 不携带异常类型名；
/// 类型名只写应用日志（<c>IDiagnosticsSink</c>），不写 AuditLog。
/// </remarks>
public sealed record AuditEntry
{
    public required string CommandId { get; init; }

    public required ChangeSource Source { get; init; }

    public string? ActorId { get; init; }

    public string? SessionId { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public string? Reason { get; init; }

    public required AuditKind Kind { get; init; }

    public CommandError[]? Errors { get; init; }
}

/// <summary>
/// 审计日志。容量 1000，链表存储，<see cref="Recent"/> 从尾部回溯（方案 §4.6）。
/// </summary>
public sealed class AuditLog
{
    public const int Capacity = 1000;

    private readonly LinkedList<AuditEntry> _entries = new();

    public int Count => _entries.Count;

    public void Record(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _entries.AddLast(entry);

        while (_entries.Count > Capacity)
        {
            _entries.RemoveFirst();
        }
    }

    public IReadOnlyList<AuditEntry> Recent(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var result = new List<AuditEntry>(Math.Min(count, _entries.Count));

        for (var node = _entries.Last; node is not null && result.Count < count; node = node.Previous)
        {
            result.Add(node.Value);
        }

        result.Reverse();
        return result;
    }

    public IReadOnlyList<AuditEntry> All() => [.. _entries];
}
