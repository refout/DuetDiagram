namespace DuetDiagram.Core.Logging;

/// <summary>
/// 环形版本日志，用来把落后的对端拉平到当前版本。
/// </summary>
/// <remarks>
/// <para>
/// 结构上是一个有容量上限的先进先出队列：记满之后最旧的记录被挤出去。
/// 上限的存在意味着"对端落后太多"是必然会发生的情况，差异计算必须能识别它并给出全量快照，
/// 而不是返回一段缺失开头的增量。
/// </para>
/// <para>
/// 记录时会检查规模，过大的记录被裁剪成"批量变更"。这是为了让日志的内存占用可预测：
/// 一次批量导入可能产生几万条字段变更，原样保存会让日志本身变成内存热点，
/// 而这类变更逐条下发也没有意义——对端拿到全量快照反而更快。
/// </para>
/// </remarks>
public sealed class VersionLog
{
    private readonly Queue<VersionEntry> _entries = new();

    public int Count => _entries.Count;

    /// <summary>队列中最早的版本号；队列为空时返回 0。</summary>
    public int EarliestVersion => _entries.Count == 0 ? 0 : _entries.Peek().Version;

    /// <summary>队列中最新的版本号；队列为空时返回 0。</summary>
    public int LatestVersion => _entries.Count == 0 ? 0 : _entries.Last().Version;

    public IReadOnlyList<VersionEntry> Snapshot() => [.. _entries];

    /// <summary>
    /// 记录一条版本。规模超限时把明细清空并标记为批量变更，只保留原始条数。
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
    /// 计算调用方需要的差异。
    /// </summary>
    /// <param name="from">调用方当前所处的版本。</param>
    /// <param name="to">当前版本。</param>
    /// <param name="clientStructuralHash">调用方声明的结构哈希，可为空。</param>
    /// <param name="currentStructuralHash">当前文档的结构哈希。</param>
    /// <param name="serializeFull">按需生成全量快照。只在确实要返回全量时才会被调用。</param>
    /// <remarks>
    /// 判断顺序是有讲究的，每一步都排除掉一批"给不出有用增量"的情况，
    /// 顺序颠倒会让某些情况落入代价更高的分支：
    /// <list type="number">
    /// <item>版本相同：无差异。</item>
    /// <item>调用方版本更靠前：这是参数错误，不是并发冲突，要区分开。</item>
    /// <item>日志为空、或调用方的起点早于日志保留的最早版本：增量链断了，只能给全量。</item>
    /// <item>区间内查不到记录：说明这段时间没有产生变更，视为无差异。</item>
    /// <item>区间内累计变更过多、或存在批量变更：增量不划算，给全量。</item>
    /// <item>调用方声明的结构哈希与当前一致：拓扑没变，只给受影响元素清单。</item>
    /// <item>其余情况：给逐条增量。</item>
    /// </list>
    /// 第 3 步与第 4 步的区别值得注意：前者是"记录被挤掉了，无从得知发生了什么"，
    /// 后者是"这段时间确实什么都没发生"，处理方式完全不同。
    /// </remarks>
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

        // 区间是左开右闭的：起点版本的内容调用方已经有了，需要补的是它之后的那些版本。
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
            // 拓扑一致，对端本地的坐标与连线仍然有效，只需要按清单重取这些元素的属性。
            // 用并集去重：同一元素在一段区间里可能被改了多次。
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

        // 到这里才真正付出序列化成本。
        FullJson = serializeFull(),
    };

    /// <summary>
    /// 判断这段增量是否已经不值得下发。
    /// </summary>
    /// <remarks>
    /// 两个条件任一成立就降级：区间里只要有一条批量变更，说明那一次的规模本来就已经被裁掉了，
    /// 增量链本身是不完整的；或者累计条数超过上限，逐条下发的传输量已经超过全量快照。
    /// 累计时优先用 <see cref="VersionEntry.OriginalChangeCount"/>（非批量记录该值为 0），
    /// 这样即便某条记录被裁剪过，也能按它的真实规模计入总量。
    /// </remarks>
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
