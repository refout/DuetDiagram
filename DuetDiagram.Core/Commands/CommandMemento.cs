using System.Text.Json.Serialization;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands;

/// <summary>
/// 命令的逆变更快照：命令执行前把"怎么改回去"需要的信息存下来，撤销时照着还原。
/// </summary>
/// <remarks>
/// <para>
/// 为什么用"抽象基类 + 密封派生记录"而不是一个万能的 object 字段：
/// 序列化必须在 AOT 下工作，AOT 不允许运行时反射发现类型。
/// 每个派生类型都显式标注多态标签，反序列化时才能按标签精确还原成正确的类型。
/// </para>
/// <para>
/// **新增派生记录时必须同时补一个标注多态标签的特性**，否则编译能过、
/// 单机运行也能过，但 AOT 发布后一旦反序列化这种 memento 就会失败——
/// 这是最典型的"测试环境发现不了、上生产才炸"的问题，所以有一条专门的测试
/// 用反射枚举程序集里所有具体 memento，跟已标注的集合做全等比较，漏一个就红。
/// </para>
/// <para>
/// 每个命令的 memento 必须携带足够还原的信息。最容易出错的地方是**位置**：
/// 撤销一个删除操作时，元素要插回原来的索引，否则顺序变了，
/// 后续依赖顺序的逻辑（例如层内次序）就会跟删除前不一致。
/// </para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$memento")]
[JsonDerivedType(typeof(AddNodeMemento), "add-node")]
[JsonDerivedType(typeof(RemoveNodeMemento), "remove-node")]
[JsonDerivedType(typeof(ConnectEdgeMemento), "connect-edge")]
public abstract record CommandMemento
{
    /// <summary>本次变更波及的元素标识，用于增量同步时告诉对端"重取这些元素"。</summary>
    public string[] AffectedIds { get; init; } = [];

    /// <summary>
    /// 逆变更，方向与命令本身相反：新增命令存的是"移除"，删除命令存的是"新增"。
    /// 撤销时把它写进版本日志，对端就能看到一次逻辑上正确的反向变更。
    /// </summary>
    public FieldChange[] InverseChanges { get; init; } = [];
}

/// <summary>
/// 一条边在被删除前所处的位置。
/// </summary>
/// <remarks>
/// 撤销删除节点时要连同它关联的边一起还原。只存边本身不够——
/// 边的集合顺序有意义，必须记住它原来在集合里的第几位，
/// 还原时按索引升序插回，才能得到与删除前逐项一致的顺序。
/// </remarks>
public sealed record EdgePlacement(int Index, EdgeDef Edge);

/// <summary>新增节点的逆变更：知道删掉哪个节点即可，索引只用于还原层内次序。</summary>
public sealed record AddNodeMemento : CommandMemento
{
    public required NodeDef Node { get; init; }

    /// <summary>节点被插入的位置。</summary>
    public required int Index { get; init; }
}

/// <summary>
/// 删除节点的逆变更。
/// </summary>
/// <remarks>
/// 删除节点会连带删除所有挂在它身上的边，所以 memento 里除了节点本身，
/// 还要存下这些边以及它们各自原来的位置。
/// </remarks>
public sealed record RemoveNodeMemento : CommandMemento
{
    public required NodeDef Node { get; init; }

    public required int Index { get; init; }

    /// <summary>被连带删除的边及其原索引。</summary>
    public EdgePlacement[] RemovedEdges { get; init; } = [];
}

/// <summary>
/// 新增边的逆变更。
/// </summary>
/// <remarks>
/// 这里只记录边本身和它被追加到的位置。撤销新增边只需按标识把边移除，
/// 不必依赖索引，但保留索引让后续可能的"顺序敏感"场景仍有依据。
/// </remarks>
public sealed record ConnectEdgeMemento : CommandMemento
{
    public required EdgeDef Edge { get; init; }

    public required int Index { get; init; }
}
