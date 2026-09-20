using DuetDiagram.Core.Commands;

namespace DuetDiagram.Core.Logging;

/// <summary>
/// 一次命令调用的最终结局。
/// </summary>
/// <remarks>
/// <see cref="Rejected"/> 与 <see cref="Failed"/> 的区别是"谁的问题"：
/// 前者是前置校验没过，命令根本没被执行，属于调用方的输入问题；
/// 后者是执行过程中出了岔子（返回失败或抛异常），文档已经被回滚，
/// 属于命令实现或环境的问题。排查时这两类的关注点完全不同。
/// </remarks>
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
/// 审计条目：谁、什么时候、用什么命令、做成了什么、失败了为什么。
/// </summary>
public sealed record AuditEntry
{
    public required string CommandId { get; init; }

    public required ChangeSource Source { get; init; }

    public string? ActorId { get; init; }

    public string? SessionId { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>失败原因或补充说明。成功时通常是命令自带的消息，可为空。</summary>
    public string? Reason { get; init; }

    public required AuditKind Kind { get; init; }

    /// <summary>
    /// 结构化错误。只放错误码和标识类载荷，**不放异常类型名**——
    /// 审计日志会长期保留并可能被外部读到，内部类型名会暴露实现结构。
    /// </summary>
    public CommandError[]? Errors { get; init; }
}

/// <summary>
/// 审计日志。容量固定，写满后挤掉最旧的记录。
/// </summary>
/// <remarks>
/// 用链表而不是队列，是因为"最近若干条"这个查询需要从尾部往回走。
/// 队列只能从头部出队，要取尾部就得整体复制一遍，而审计查询是随时可能发生的操作。
/// </remarks>
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

    /// <summary>
    /// 取最近若干条，返回顺序为从旧到新。
    /// </summary>
    /// <remarks>
    /// 内部要从尾部往前回溯，所以收集结果是倒序的，返回前反转一次。
    /// 调用方（例如界面的操作历史面板）按时间正序展示更自然，
    /// 让每个调用方各自反转很容易漏掉。
    /// </remarks>
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
