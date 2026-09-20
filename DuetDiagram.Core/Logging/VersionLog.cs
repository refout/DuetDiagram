namespace DuetDiagram.Core.Logging;

/// <summary>
/// 环形版本日志。用于把 MCP / GUI 的旧版本拉平到当前版本。
/// </summary>
public sealed class VersionLog
{
    private readonly Queue<VersionEntry> _entries = new();

    public int Count => _entries.Count;

    /// <summary>队列中最早（最小）的版本号；队列为空时返回 0。</summary>
    public int EarliestVersion => _entries.Count == 0 ? 0 : _entries.Peek().Version;

    /// <summary>队列中最新（最大）的版本号；队列为空时返回 0。</summary>
    public int LatestVersion => _entries.Count == 0 ? 0 : _entries.Last().Version;

    public IReadOnlyList<VersionEntry> Snapshot() => [.. _entries];

    /// <summary>
    /// 记录一条版本。超过 BulkChangeThreshold / MaxAffectedPerEntry 时裁剪
    /// <see cref="VersionEntry.Changes"/> 与 <see cref="VersionEntry.AffectedIds"/> 并标记为批量变更（方案 §4.5）。
    /// </summary>
    public void Record(VersionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var isBulk = entry.Changes.Length > VersionLogLimits.BulkChangeThreshold
            || entry.AffectedIds.Length > VersionLogLimits.MaxAffectedPerEntry;

        var stored = isBulk
            ? entry with
            {
                Changes = [],
                AffectedIds = [],
                IsBulkChange = true,
                OriginalChangeCount = entry.Changes.Length,
            }
            : entry;

        _entries.Enqueue(stored);

        while (_entries.Count > VersionLogLimits.MaxEntries)
        {
            _entries.Dequeue();
        }
    }

    /// <summary>
    /// 按方案 §4.5 的固定顺序构建差异。
    /// </summary>
    /// <param name="from">调用方持有的版本。</param>
    /// <param name="to">当前版本。</param>
    /// <param name="clientStructuralHash">调用方声明的结构哈希，可为空。</param>
    /// <param name="currentStructuralHash">当前文档结构哈希。</param>
    /// <param name="serializeFull">延迟序列化。只在确实需要全量快照时调用（避免白付序列化成本）。</param>
    public DiffResult BuildDiff(
        int from,
        int to,
        string? clientStructuralHash,
        string currentStructuralHash,
        Func<string> serializeFull)
    {
        ArgumentNullException.ThrowIfNull(serializeFull);

        if (from == to)
        {
            return new EmptyDiff();
        }

        if (from > to)
        {
            return new InvalidDiff();
        }

        if (_entries.Count == 0 || from < EarliestVersion)
        {
            return FullSnapshot(to, serializeFull);
        }

        var relevant = _entries.Where(e => e.Version > from && e.Version <= to).ToArray();

        if (relevant.Length == 0)
        {
            return new EmptyDiff();
        }

        if (ExceedsThreshold(relevant))
        {
            return FullSnapshot(to, serializeFull);
        }

        if (!string.IsNullOrEmpty(clientStructuralHash) &&
            string.Equals(clientStructuralHash, currentStructuralHash, StringComparison.Ordinal))
        {
            return new ReferenceDiff
            {
                Version = to,
                BaseVersion = from,
                StructuralHash = currentStructuralHash,
                AffectedIds = [.. relevant.SelectMany(e => e.AffectedIds).Distinct(StringComparer.Ordinal)],
            };
        }

        return new EntriesDiff { Entries = relevant };
    }

    private static FullSnapshotDiff FullSnapshot(int to, Func<string> serializeFull) => new()
    {
        Version = to,
        FullJson = serializeFull(),
    };

    /// <summary>累计原始变更数超阈值、或任一条目为批量变更，即认为增量不值得下发。</summary>
    private static bool ExceedsThreshold(IEnumerable<VersionEntry> entries)
    {
        var total = 0;

        foreach (var entry in entries)
        {
            if (entry.IsBulkChange)
            {
                return true;
            }

            total += entry.OriginalChangeCount > 0 ? entry.OriginalChangeCount : entry.Changes.Length;

            if (total > VersionLogLimits.MaxTotalChangesForDiff)
            {
                return true;
            }
        }

        return false;
    }
}
