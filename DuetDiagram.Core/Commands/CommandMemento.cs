using System.Text.Json.Serialization;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands;

/// <summary>
/// 命令的逆变更快照。
/// </summary>
/// <remarks>
/// AGENTS.md 约定 5：抽象基类 + 派生密封记录，禁止 <c>object</c>。
/// AGENTS.md 约定 5 续：新增派生记录必须同时加 <see cref="JsonDerivedTypeAttribute"/>，
/// 否则 AOT 下多态反序列化失败。MementoRegistration 测试会枚举程序集并强制一致。
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$memento")]
[JsonDerivedType(typeof(AddNodeMemento), "add-node")]
[JsonDerivedType(typeof(RemoveNodeMemento), "remove-node")]
[JsonDerivedType(typeof(ConnectEdgeMemento), "connect-edge")]
public abstract record CommandMemento
{
    /// <summary>受影响的元素 id。</summary>
    public string[] AffectedIds { get; init; } = [];

    /// <summary>逆变更，用于版本日志与变更高亮。</summary>
    public FieldChange[] InverseChanges { get; init; } = [];
}

/// <summary>边在容器中的位置。撤销删除节点时按原索引插回，保证确定性。</summary>
public sealed record EdgePlacement(int Index, EdgeDef Edge);

/// <summary>新增节点的逆变更。</summary>
public sealed record AddNodeMemento : CommandMemento
{
    public required NodeDef Node { get; init; }

    /// <summary>插入位置。</summary>
    public required int Index { get; init; }
}

/// <summary>删除节点及其连带删除的边的逆变更。</summary>
public sealed record RemoveNodeMemento : CommandMemento
{
    public required NodeDef Node { get; init; }

    public required int Index { get; init; }

    public EdgePlacement[] RemovedEdges { get; init; } = [];
}

/// <summary>新增边的逆变更。</summary>
public sealed record ConnectEdgeMemento : CommandMemento
{
    public required EdgeDef Edge { get; init; }

    public required int Index { get; init; }
}
