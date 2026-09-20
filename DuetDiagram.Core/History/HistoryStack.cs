using DuetDiagram.Core.Commands;

namespace DuetDiagram.Core.History;

/// <summary>一次可撤销的操作。</summary>
public sealed record HistoryEntry
{
    public required IDiagramCommand Command { get; init; }

    public required CommandMemento Memento { get; init; }

    public required CommandResult Result { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public string? SessionId { get; init; }
}

/// <summary>
/// 撤销 / 重做双栈（方案 §4.7）。人与 LLM 共用同一条历史。
/// </summary>
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

    /// <summary>新命令执行时调用。</summary>
    public void ClearRedo() => _redo.Clear();

    /// <summary>
    /// 清空两个栈。
    /// </summary>
    /// <remarks>
    /// AGENTS.md 约定 11：仅用于同一文档重新加载，不重置 Version。
    /// 打开新文档必须创建新的 <c>DiagramDocument</c> + <c>DiagramCommandBus</c>。
    /// </remarks>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
