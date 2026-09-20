using System.Text.Json.Serialization;

namespace DuetDiagram.Core.Logging;

/// <summary>
/// 差异计算的结果。调用方拿着自己的版本号来问"我落后了多少"，得到的就是这五种之一。
/// </summary>
/// <remarks>
/// <para>
/// 五种结果的取舍逻辑是"能少给就少给，给不了就老实给全量"：
/// 两边版本相同什么都不用给；版本里没记录增量就给全量；
/// 结构没变过就只给一份受影响元素清单让对端自己重取；其余情况给增量条目。
/// </para>
/// <para>
/// 派生类型名都带 Diff 后缀。不加后缀时名字会跟常见的静态成员、其它类型撞名，
/// 调用处一眼看不出是哪个命名空间下的东西。
/// </para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$diff")]
[JsonDerivedType(typeof(EmptyDiff), "empty")]
[JsonDerivedType(typeof(InvalidDiff), "invalid")]
[JsonDerivedType(typeof(FullSnapshotDiff), "full-snapshot")]
[JsonDerivedType(typeof(ReferenceDiff), "reference")]
[JsonDerivedType(typeof(EntriesDiff), "entries")]
public abstract record DiffResult;

/// <summary>两边版本一致，没有任何需要同步的东西。这是最常见的正常返回。</summary>
public sealed record EmptyDiff : DiffResult;

/// <summary>调用方声称的版本比当前版本还新。这不是并发冲突，而是调用方传错了参数。</summary>
public sealed record InvalidDiff : DiffResult;

/// <summary>
/// 只能用全量快照表达差异。对端应当丢弃本地状态、整体替换。
/// </summary>
/// <remarks>
/// 序列化是延迟的：只有真的走到这一步才会去序列化整个文档。
/// 差异计算在同步路径上被高频调用，而绝大多数情况都落在空差异或增量上，
/// 提前序列化会让每次查询都白付一遍全量序列化的代价。
/// </remarks>
public sealed record FullSnapshotDiff : DiffResult
{
    public required int Version { get; init; }

    public required string FullJson { get; init; }
}

/// <summary>
/// 结构没有变化，对端只需要重取清单里列出的元素。
/// </summary>
/// <remarks>
/// 典型的适用场景是纯样式或纯文本改动：连接关系没动，对端本地已有的拓扑仍然有效，
/// 把那些元素的标签、颜色重新取一遍就够了，不必搬运整张图。
/// </remarks>
public sealed record ReferenceDiff : DiffResult
{
    public required int Version { get; init; }

    /// <summary>对端当前所处的版本，也就是这次增量计算的起点。</summary>
    public required int BaseVersion { get; init; }

    public required string StructuralHash { get; init; }

    public string[] AffectedIds { get; init; } = [];
}

/// <summary>逐条的增量变更。对端按顺序回放即可追平。</summary>
public sealed record EntriesDiff : DiffResult
{
    public required VersionEntry[] Entries { get; init; }
}
