using System.Text.Json.Serialization;

namespace DuetDiagram.Core.Logging;

/// <summary>
/// 版本差异结果，多态序列化（方案 §4.5）。
/// </summary>
/// <remarks>
/// 派生类型命名为 <c>XxxDiff</c> 而非方案原文的 <c>Empty</c> / <c>Invalid</c> / <c>Entries</c>：
/// <c>DiffResult.Empty</c> 这类名字会与静态成员、其它类型名冲突，且不易检索。语义一一对应。
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$diff")]
[JsonDerivedType(typeof(EmptyDiff), "empty")]
[JsonDerivedType(typeof(InvalidDiff), "invalid")]
[JsonDerivedType(typeof(FullSnapshotDiff), "full-snapshot")]
[JsonDerivedType(typeof(ReferenceDiff), "reference")]
[JsonDerivedType(typeof(EntriesDiff), "entries")]
public abstract record DiffResult;

/// <summary>两侧版本相同，无需同步。</summary>
public sealed record EmptyDiff : DiffResult;

/// <summary>请求范围非法（from &gt; to）。</summary>
public sealed record InvalidDiff : DiffResult;

/// <summary>无法用增量表达，调用方必须整体替换本地状态。</summary>
public sealed record FullSnapshotDiff : DiffResult
{
    public required int Version { get; init; }

    public required string FullJson { get; init; }
}

/// <summary>结构哈希一致，调用方只需按 <see cref="AffectedIds"/> 重取这些元素。</summary>
public sealed record ReferenceDiff : DiffResult
{
    public required int Version { get; init; }

    public required int BaseVersion { get; init; }

    public required string StructuralHash { get; init; }

    public string[] AffectedIds { get; init; } = [];
}

/// <summary>增量条目。</summary>
public sealed record EntriesDiff : DiffResult
{
    public required VersionEntry[] Entries { get; init; }
}
