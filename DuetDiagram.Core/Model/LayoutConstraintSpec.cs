namespace DuetDiagram.Core.Model;

/// <summary>
/// 中级布局约束的种类。
/// </summary>
/// <remarks>
/// 三类的形态不同，但增删的动作一样，所以用同一个命令加一个种类字段，
/// 而不是一类一条命令。一类一条命令的话，校验、原子性与撤销三套逻辑要各写三遍，
/// 而抄漏的那一遍不会报错，只会让某一类约束在某个入口下改不动。
/// </remarks>
public enum LayoutConstraintKind
{
    /// <summary>同层：这些节点必须落在同一层。</summary>
    SameRank,

    /// <summary>层内次序：某个节点的出边按给定次序排列。</summary>
    Order,

    /// <summary>对齐：这些节点在垂直于分层方向的那个轴上取齐。</summary>
    Align,
}

/// <summary>
/// 一条约束的规格：要加或要删的是哪一条。
/// </summary>
/// <param name="Kind">哪一类约束。</param>
/// <param name="Subject">
/// 主语节点。只有层内次序有主语——次序讲的是"某个节点的几条出边谁先谁后"，
/// 其余两类是一组平级的节点，没有主语。
/// </param>
/// <param name="Members">
/// 成员。同层与对齐是节点标识；层内次序是**出边标识**，按先后排列。
/// </param>
/// <remarks>
/// <para>
/// 次序存的是出边标识而不是目标节点标识：同一个终点可以有好几条边，
/// 而"这几条谁先谁后"用节点标识表达不出来。这个形状与布局提示里存的一致，
/// 转换只在求解那一侧发生。
/// </para>
/// <para>
/// 相等按"是不是同一条约束"算，不按列表的字面顺序：同层与对齐是集合语义，
/// 成员写在前写在后是同一条约束；层内次序则是顺序语义，次序本身就是它的内容。
/// </para>
/// </remarks>
public sealed record LayoutConstraintSpec(
    LayoutConstraintKind Kind,
    string? Subject,
    IReadOnlyList<string> Members)
{
    /// <summary>一条同层约束。</summary>
    public static LayoutConstraintSpec SameRank(IReadOnlyList<string> nodes) =>
        new(LayoutConstraintKind.SameRank, null, nodes);

    /// <summary>一条对齐约束。</summary>
    public static LayoutConstraintSpec Align(IReadOnlyList<string> nodes) =>
        new(LayoutConstraintKind.Align, null, nodes);

    /// <summary>一条层内次序约束。</summary>
    public static LayoutConstraintSpec Order(string subject, IReadOnlyList<string> edges) =>
        new(LayoutConstraintKind.Order, subject, edges);

    /// <summary>这条规格描述的是不是这一条同层约束。</summary>
    public bool Matches(Constraint<SameRankConstraint> constraint) =>
        Kind == LayoutConstraintKind.SameRank && SameMembers(constraint.Value.Nodes);

    /// <summary>这条规格描述的是不是这一条对齐约束。</summary>
    public bool Matches(Constraint<AlignConstraint> constraint) =>
        Kind == LayoutConstraintKind.Align && SameMembers(constraint.Value.Nodes);

    /// <summary>这条规格描述的是不是这一条层内次序约束。次序是顺序语义，逐位比较。</summary>
    public bool Matches(Constraint<OrderConstraint> constraint) =>
        Kind == LayoutConstraintKind.Order
        && string.Equals(constraint.Value.NodeId, Subject, StringComparison.Ordinal)
        && CollectionEquality.List(constraint.Value.Order, Members);

    /// <summary>
    /// 变更明细里这一条约束的字段名。
    /// </summary>
    /// <remarks>
    /// 用点分的小写名，与其它字段名一样是稳定标识，不是给人读的句子。
    /// 给人读的那一份由界面自己拼——两处混用的话，改一次文案就会让审计日志里的字段名跟着变。
    /// </remarks>
    public string Field => Kind switch
    {
        LayoutConstraintKind.SameRank => "layout.same-rank",
        LayoutConstraintKind.Order => "layout.order",
        _ => "layout.align",
    };

    /// <summary>成员是不是同一批，不看顺序。</summary>
    private bool SameMembers(IReadOnlyList<string> nodes) =>
        nodes.Count == Members.Count
        && Members.All(member => nodes.Contains(member, StringComparer.Ordinal))
        && Members.Distinct(StringComparer.Ordinal).Count() == Members.Count;

    /// <summary>结构化相等。必须重写：成员是集合。</summary>
    public bool Equals(LayoutConstraintSpec? other) =>
        other is not null
        && Kind == other.Kind
        && string.Equals(Subject, other.Subject, StringComparison.Ordinal)
        && CollectionEquality.List(Members, other.Members);

    public override int GetHashCode() =>
        HashCode.Combine(Kind, Subject ?? string.Empty, CollectionEquality.ListHash(Members));
}
