using System.Text.Json.Serialization;

namespace DuetDiagram.Core.Model;

/// <summary>
/// 组合定义。把若干节点或子组合圈在一起，是分组、泳道、子流程、组合框的共同抽象。
/// </summary>
/// <remarks>
/// <para>
/// 用抽象基类加派生密封记录，而不是一个带"类型"字段的万能记录。
/// 后者在读取时到处都要判类型字段，任何一处忘了判就会把泳道当成普通分组处理；
/// 前者让编译器替我们把这件事管住。
/// </para>
/// <para>
/// 派生类型必须同时登记多态标签，否则原生编译下反序列化会失败——
/// 运行时没有反射可用，标签是唯一的线索。
/// </para>
/// <para>
/// **成员关系的两处表达**：本记录的 <see cref="Members"/> 与
/// <see cref="NodeDef.Parent"/> 表达的是同一件事。两者都要满足，
/// 但两份数据天然可能不一致。约定是：**<see cref="Members"/> 为准**，
/// 节点的 <c>Parent</c> 是便于查询的冗余字段，两者必须一致。
/// 校验见 <c>DiagramValidator.ValidateCompositeMembership</c>。
/// </para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$composite")]
[JsonDerivedType(typeof(GroupDef), "group")]
[JsonDerivedType(typeof(LaneDef), "lane")]
[JsonDerivedType(typeof(SubflowDef), "subflow")]
[JsonDerivedType(typeof(ComboDef), "combo")]
public abstract record CompositeDef : IDefinition
{
    /// <summary>组合标识。文档内唯一，与节点、边的标识共用同一个命名空间。</summary>
    public required string Id { get; init; }

    public string Label { get; init; } = string.Empty;

    /// <summary>外层组合的标识。顶层组合为空。</summary>
    public string? Parent { get; init; }

    /// <summary>
    /// 成员标识列表。成员可以是节点，也可以是另一个组合。
    /// 顺序有意义：泳道里成员的先后就是条带顺序。
    /// </summary>
    public IReadOnlyList<string> Members { get; init; } = [];

    /// <summary>组合内部的布局方向。为空表示跟随文档的主方向。</summary>
    public Direction? Direction { get; init; }

    /// <summary>是否折叠。折叠时内部成员不参与布局，只按整体占位。</summary>
    public bool Collapsed { get; init; }

    public NodeStyle? Style { get; init; }

    /// <summary>组合内部的布局提示。它覆盖文档级的设置，用于让子流程内部用更紧的间距。</summary>
    public LayoutHints? LocalLayout { get; init; }

    /// <summary>
    /// 结构化相等。
    /// </summary>
    /// <remarks>
    /// 必须重写：<see cref="Members"/> 是集合，记录自动生成的相等性对它用引用比较。
    /// 派生类型继承这份实现，因此不必各自再写一遍。
    /// </remarks>
    public virtual bool Equals(CompositeDef? other) =>
        other is not null
        && string.Equals(Id, other.Id, StringComparison.Ordinal)
        && string.Equals(Label, other.Label, StringComparison.Ordinal)
        && string.Equals(Parent, other.Parent, StringComparison.Ordinal)
        && CollectionEquality.List(Members, other.Members)
        && Direction == other.Direction
        && Collapsed == other.Collapsed
        && Equals(Style, other.Style)
        && Equals(LocalLayout, other.LocalLayout);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Id, StringComparer.Ordinal);
        hash.Add(Label, StringComparer.Ordinal);
        hash.Add(Parent, StringComparer.Ordinal);
        hash.Add(CollectionEquality.ListHash(Members));
        hash.Add(Direction);
        hash.Add(Collapsed);
        hash.Add(Style);
        hash.Add(LocalLayout);

        return hash.ToHashCode();
    }
}

/// <summary>分组：把若干节点圈在一起，带边框与标题。架构图里的"层"就是它。</summary>
public sealed record GroupDef : CompositeDef;

/// <summary>泳道：按责任方划分的条带。成员的先后就是条带顺序。</summary>
public sealed record LaneDef : CompositeDef;

/// <summary>子流程：可以折叠的嵌套流程。折叠后按一个节点参与布局。</summary>
public sealed record SubflowDef : CompositeDef;

/// <summary>组合框：不参与布局的纯标注性边框，用于在图上圈出一块区域加以说明。</summary>
public sealed record ComboDef : CompositeDef;
