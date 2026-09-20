using System.Text.Json.Serialization;
using DuetDiagram.Core.Commands;

namespace DuetDiagram.Core.Logging;

/// <summary>
/// 版本日志的容量与降级阈值。
/// </summary>
/// <remarks>
/// 这几个数字共同决定"内存占用有上限"和"增量同步不会退化成搬运全量数据"两件事。
/// 调大容量能让更落后的对端拿到增量，代价是常驻内存线性增长；
/// 调小阈值会让对端更早拿到全量快照，代价是网络流量变大。
/// 当前取值在两者之间取了偏保守的位置。
/// </remarks>
public static class VersionLogLimits
{
    /// <summary>最多保留多少个版本。超出后最旧的被丢弃，落在区间外的对端只能拿全量快照。</summary>
    public const int MaxEntries = 100;

    /// <summary>单条记录的变更明细超过这个数量就不值得逐条下发，整条降级为批量变更。</summary>
    public const int BulkChangeThreshold = 100;

    /// <summary>单条记录波及的元素超过这个数量同样降级为批量变更。</summary>
    public const int MaxAffectedPerEntry = 200;

    /// <summary>一次差异计算里累计变更数超过这个值，直接给全量快照比给增量更划算。</summary>
    public const int MaxTotalChangesForDiff = 500;
}

/// <summary>
/// 一条版本记录，描述"从上一个版本到这一版发生了什么"。
/// </summary>
/// <remarks>
/// <see cref="IsBulkChange"/> 为真时 <see cref="Changes"/> 与 <see cref="AffectedIds"/> 一定是空的，
/// 只留下 <see cref="OriginalChangeCount"/> 说明原始规模。
/// 这样做的目的是给日志占用封顶：一次批量导入可能产生几万条变更，
/// 如果原样存下来，日志本身就会成为内存黑洞，而这类变更本来也不适合逐条下发。
/// </remarks>
public sealed record VersionEntry
{
    /// <summary>这一版对应的文档版本号。</summary>
    public required int Version { get; init; }

    public required string CommandId { get; init; }

    public required ChangeSource Source { get; init; }

    public string? ActorId { get; init; }

    public string? SessionId { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>波及的元素标识。批量变更时为空。</summary>
    public string[] AffectedIds { get; init; } = [];

    /// <summary>字段级变更明细。批量变更时为空。</summary>
    public FieldChange[] Changes { get; init; } = [];

    /// <summary>是否因为规模过大而被裁剪过。</summary>
    public bool IsBulkChange { get; init; }

    /// <summary>裁剪前的变更条数。非批量变更时为 0。</summary>
    public int OriginalChangeCount { get; init; }
}
