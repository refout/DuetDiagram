using System.Text.Json.Serialization;
using DuetDiagram.Core.Commands;

namespace DuetDiagram.Core.Logging;

/// <summary>版本日志容量与阈值（方案 §4.5）。</summary>
public static class VersionLogLimits
{
    public const int MaxEntries = 100;

    public const int BulkChangeThreshold = 100;

    public const int MaxAffectedPerEntry = 200;

    public const int MaxTotalChangesForDiff = 500;
}

/// <summary>一条版本记录。仅在非批量变更时携带 <see cref="Changes"/> / <see cref="AffectedIds"/>。</summary>
public sealed record VersionEntry
{
    public required int Version { get; init; }

    public required string CommandId { get; init; }

    public required ChangeSource Source { get; init; }

    public string? ActorId { get; init; }

    public string? SessionId { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public string[] AffectedIds { get; init; } = [];

    public FieldChange[] Changes { get; init; } = [];

    public bool IsBulkChange { get; init; }

    /// <summary>裁剪前的变更条数。批量变更时供调用方判断规模。</summary>
    public int OriginalChangeCount { get; init; }
}
