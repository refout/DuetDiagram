using DuetDiagram.Core.Commands;

namespace DuetDiagram.Core.History;

/// <summary>
/// 一次可撤销的操作：命令本身、执行前的快照、执行结果，以及当时的时间与会话。
/// </summary>
/// <remarks>
/// 命令和执行结果都要留着。撤销时用它拿到逆变更，重做时用它重新执行，
/// 界面展示"最近操作"时用 <see cref="Result"/> 里的消息和受影响元素。
/// </remarks>
public sealed record HistoryEntry
{
    public required IDiagramCommand Command { get; init; }

    public required CommandMemento Memento { get; init; }

    public required CommandResult Result { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public string? SessionId { get; init; }
}

/// <summary>
/// 撤销栈与重做栈。所有来源的操作共用同一条历史，不区分是人还是 AI 发起的。
/// </summary>
/// <remarks>
/// <para>
/// 两个栈都从尾部进出：尾部是最新的一条。撤销时从撤销栈尾部弹出并压进重做栈，
/// 重做时反向操作。这样"撤销最近一次"是常数时间。
/// </para>
/// <para>
/// 两个栈各自有容量上限，超出后从头部（最旧）丢弃。
/// 上限是按栈分别计算的，所以撤销栈满时不会影响重做栈里已有的内容。
/// </para>
/// <para>
/// 执行新命令时必须清空重做栈。原因是重做栈里的记录建立在"接下来会发生什么"的假设上，
/// 一旦有了新的变更，那些假设就不再成立，继续重做会把文档带到一个谁也没预期的状态。
/// </para>
/// </remarks>
public sealed class HistoryStack
{
    public const int Capacity = 500;

    private readonly LinkedList<HistoryEntry> _undo = new();
    private readonly LinkedList<HistoryEntry> _redo = new();

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    public HistoryEntry? PeekUndo() => _undo.Last?.Value;

    public HistoryEntry? PeekRedo() => _redo.Last?.Value;

    public IReadOnlyList<HistoryEntry> UndoEntries() => [.. _undo];

    public IReadOnlyList<HistoryEntry> RedoEntries() => [.. _redo];

    public void PushUndo(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _undo.AddLast(entry);

        while (_undo.Count > Capacity)
        {
            _undo.RemoveFirst();
        }
    }

    public HistoryEntry? TryPopUndo()
    {
        var entry = _undo.Last?.Value;

        if (entry is not null)
        {
            _undo.RemoveLast();
        }

        return entry;
    }

    public void PushRedo(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _redo.AddLast(entry);

        while (_redo.Count > Capacity)
        {
            _redo.RemoveFirst();
        }
    }

    public HistoryEntry? TryPopRedo()
    {
        var entry = _redo.Last?.Value;

        if (entry is not null)
        {
            _redo.RemoveLast();
        }

        return entry;
    }

    /// <summary>新命令成功执行后调用。</summary>
    public void ClearRedo() => _redo.Clear();

    /// <summary>
    /// 清空两个栈。只适用于"同一份文档重新加载"：版本号保持不变，历史归零。
    /// </summary>
    /// <remarks>
    /// 打开另一份文档时不要用它。历史记录里存着命令实例，命令又持有具体的节点和边，
    /// 直接套用到新文档上会把这些元素错误地带过去。
    /// 换文档的正确做法是连同文档对象和命令总线一起新建。
    /// </remarks>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
