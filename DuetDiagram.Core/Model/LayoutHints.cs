namespace DuetDiagram.Core.Model;

/// <summary>相对位置关系。</summary>
public enum PlaceRelation
{
    RightOf,
    LeftOf,
    Above,
    Below,
}

/// <summary>
/// 一条约束，带归属方与创建时间。
/// </summary>
/// <remarks>
/// 归属方决定这条约束在冲突时听谁的：人工设定的不该被自动重排冲掉，
/// 而引擎推导的在用户拖动之后应当让位。没有这个字段，两类约束混在一起，
/// 只能靠"谁后加的算谁的"来决定，而那是不可靠的。
/// </remarks>
public sealed record Constraint<T>(T Value, ConstraintOwner Owner, DateTimeOffset CreatedAt) where T : notnull;

/// <summary>同层约束：这些节点必须落在同一层。</summary>
public sealed record SameRankConstraint(IReadOnlyList<string> Nodes)
{
    public bool Equals(SameRankConstraint? other) =>
        other is not null && CollectionEquality.List(Nodes, other.Nodes);

    public override int GetHashCode() => CollectionEquality.ListHash(Nodes);
}

/// <summary>层内顺序约束：某个节点的出边按给定次序排列。次序影响连线的交叉数量。</summary>
public sealed record OrderConstraint(string NodeId, IReadOnlyList<string> Order)
{
    public bool Equals(OrderConstraint? other) =>
        other is not null
        && string.Equals(NodeId, other.NodeId, StringComparison.Ordinal)
        && CollectionEquality.List(Order, other.Order);

    public override int GetHashCode() => HashCode.Combine(NodeId, CollectionEquality.ListHash(Order));
}

/// <summary>对齐约束：这些节点在垂直于分层方向的那个轴上对齐。</summary>
public sealed record AlignConstraint(IReadOnlyList<string> Nodes)
{
    public bool Equals(AlignConstraint? other) =>
        other is not null && CollectionEquality.List(Nodes, other.Nodes);

    public override int GetHashCode() => CollectionEquality.ListHash(Nodes);
}

/// <summary>相对位置约束。</summary>
public sealed record PlaceConstraint(string NodeId, string RelativeTo, PlaceRelation Relation);

/// <summary>
/// 布局提示。
/// </summary>
/// <remarks>
/// <para>
/// 这些内容决定布局结果，但不属于"图形长什么样"。放进结构哈希是因为哈希要回答的问题
/// 其实是"要不要重新求解布局"，而改了间距就必须重排。
/// </para>
/// <para>
/// 四个约束列表都按归属方可以过滤。这是冲突处置的依据：人工拖动产生的固定位置
/// 优先于引擎推导的同层约束，反过来的话用户每次微调都会被下一次重排抹掉。
/// </para>
/// </remarks>
public sealed record LayoutHints
{
    /// <summary>同层节点之间的间距。</summary>
    public double NodeSpacing { get; init; } = LayoutHintsDefaults.NodeSpacing;

    /// <summary>层与层之间的间距。</summary>
    public double LayerSpacing { get; init; } = LayoutHintsDefaults.LayerSpacing;

    public IReadOnlyList<Constraint<SameRankConstraint>> SameRank { get; init; } = [];

    public IReadOnlyList<Constraint<OrderConstraint>> Order { get; init; } = [];

    public IReadOnlyList<Constraint<AlignConstraint>> Align { get; init; } = [];

    public IReadOnlyList<Constraint<PlaceConstraint>> Place { get; init; } = [];

    /// <summary>取指定归属方的同层约束。传空表示不过滤。</summary>
    public IEnumerable<SameRankConstraint> GetSameRank(ConstraintOwner? owner = null) =>
        Filter(SameRank, owner).Select(c => c.Value);

    /// <inheritdoc cref="GetSameRank"/>
    public IEnumerable<OrderConstraint> GetOrder(ConstraintOwner? owner = null) =>
        Filter(Order, owner).Select(c => c.Value);

    /// <inheritdoc cref="GetSameRank"/>
    public IEnumerable<AlignConstraint> GetAlign(ConstraintOwner? owner = null) =>
        Filter(Align, owner).Select(c => c.Value);

    /// <inheritdoc cref="GetSameRank"/>
    public IEnumerable<PlaceConstraint> GetPlace(ConstraintOwner? owner = null) =>
        Filter(Place, owner).Select(c => c.Value);

    /// <summary>是否存在某一归属方的约束。降级路径用它判断"这次布局有没有约束要处理"。</summary>
    public bool HasAny(ConstraintOwner owner) =>
        SameRank.Any(c => c.Owner == owner)
        || Order.Any(c => c.Owner == owner)
        || Align.Any(c => c.Owner == owner)
        || Place.Any(c => c.Owner == owner);

    /// <summary>
    /// 排除某一归属方的约束，返回新实例。
    /// </summary>
    /// <remarks>
    /// 返回新实例而不是就地修改：约束集合可能被多个版本共享，
    /// 就地改会让"撤销之后约束回不到原样"。记录类型在这里帮了忙——
    /// 这份实现里没有任何一处能绕过 <c>init</c> 去改已有实例。
    /// </remarks>
    public LayoutHints Without(ConstraintOwner owner) => new()
    {
        NodeSpacing = NodeSpacing,
        LayerSpacing = LayerSpacing,
        SameRank = [.. SameRank.Where(c => c.Owner != owner)],
        Order = [.. Order.Where(c => c.Owner != owner)],
        Align = [.. Align.Where(c => c.Owner != owner)],
        Place = [.. Place.Where(c => c.Owner != owner)],
    };

    private static IEnumerable<Constraint<T>> Filter<T>(
        IReadOnlyList<Constraint<T>> source,
        ConstraintOwner? owner)
        where T : notnull =>
        owner is null ? source : source.Where(c => c.Owner == owner);

    /// <summary>结构化相等。必须重写：四个约束列表都是集合。</summary>
    public bool Equals(LayoutHints? other) =>
        other is not null
        && NodeSpacing.Equals(other.NodeSpacing)
        && LayerSpacing.Equals(other.LayerSpacing)
        && CollectionEquality.List(SameRank, other.SameRank)
        && CollectionEquality.List(Order, other.Order)
        && CollectionEquality.List(Align, other.Align)
        && CollectionEquality.List(Place, other.Place);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(NodeSpacing);
        hash.Add(LayerSpacing);
        hash.Add(CollectionEquality.ListHash(SameRank));
        hash.Add(CollectionEquality.ListHash(Order));
        hash.Add(CollectionEquality.ListHash(Align));
        hash.Add(CollectionEquality.ListHash(Place));

        return hash.ToHashCode();
    }
}

/// <summary>
/// 布局提示的缺省值。
/// </summary>
/// <remarks>
/// <para>
/// **只提供返回新实例的方法，不提供可变单例。**
/// </para>
/// <para>
/// 共享一个可变默认实例是这类"默认值"最经典的坑：某一处改了它，
/// 从此以后所有新文档的默认间距都变了，而且改动点与受害点隔得很远，查起来极其困难。
/// 用只读常量加构造方法，这个坑在结构上就不存在。
/// </para>
/// </remarks>
public static class LayoutHintsDefaults
{
    /// <summary>同层间距缺省值。</summary>
    public const double NodeSpacing = 40;

    /// <summary>层间距缺省值。</summary>
    public const double LayerSpacing = 70;

    /// <summary>造一份缺省布局提示。每次都返回新实例。</summary>
    public static LayoutHints Create() => new();
}
