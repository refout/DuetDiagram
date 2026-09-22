using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 设置或清除一个节点的相对位置约束。
/// </summary>
/// <remarks>
/// <para>
/// **这是第四类布局约束，形状与前三类不同。** 同层与对齐说的是"这一组节点"，
/// 层内次序说的是"这个主语的这几条出边"，而相对位置说的是"这个节点在那个节点的某一侧"——
/// 它是一个**函数式关系**：同一对节点之间只该有一条。所以它单列一条命令做设置与清除，
/// 而不是并进按组增删的那两条。
/// </para>
/// <para>
/// **写入时必须合并，不许整份换掉布局提示。** 换掉的话，加一条相对位置会把之前加的
/// 同层约束悄悄删掉，而用户看到的只是"我加了个相对位置"。
/// </para>
/// <para>
/// **同一个主语与参照之间按归属方各留一条。** 归属方是降级矩阵的输入，
/// 人工定的与模型提的要分开记，否则布局解不出来时不知道该先丢谁。
/// 同一归属方下再设一次是替换而不是追加：追加几次就积下几条互相矛盾的相对位置，
/// 而求解器取的是列表里的第一条，于是生效的是最早那一次。
/// </para>
/// <para>
/// **求解器现在还不消费这一类约束。** 它进结构哈希、进整体校验、进约束面板，
/// 但不改变任何坐标——布局的降级计划只投影同层、层内次序与对齐三类。
/// 也就是说调用方设完之后节点不会动，这一点必须让调用方知道，
/// 否则它会把"命令没生效"当成一次失败。
/// </para>
/// </remarks>
public sealed class SetPlaceCommand : DiagramCommandBase
{
    public const string Id = "set-place";

    private readonly string _nodeId;
    private readonly string _relativeTo;
    private readonly PlaceRelation? _relation;
    private readonly ConstraintOwner _owner;

    /// <param name="nodeId">要摆位的节点。</param>
    /// <param name="relativeTo">参照节点。</param>
    /// <param name="relation">摆在哪一侧。传空表示清除这一条。</param>
    /// <param name="owner">归属方。必须显式给出，不给默认值——它是降级矩阵的输入。</param>
    public SetPlaceCommand(
        string nodeId,
        string relativeTo,
        PlaceRelation? relation,
        ConstraintOwner owner)
        : base(Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeTo);

        _nodeId = nodeId;
        _relativeTo = relativeTo;
        _relation = relation;
        _owner = owner;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.Equals(_nodeId, _relativeTo, StringComparison.Ordinal))
        {
            // "自己摆在自己的右边"描述不出任何位置，而且它会让求解器陷入自指。
            return Invalid("参照节点不能是它自己");
        }

        if (_relation is { } relation && !Enum.IsDefined(relation))
        {
            return ValidationResult.Invalid(CommandError.Of(
                ErrorCodes.FieldValueInvalid,
                $"不是已定义的相对位置：{(int)relation}"));
        }

        var missing = new List<string>(2);

        if (document.FindNode(_nodeId) is null)
        {
            missing.Add(_nodeId);
        }

        if (document.FindNode(_relativeTo) is null)
        {
            missing.Add(_relativeTo);
        }

        return missing.Count == 0
            ? ValidationResult.Valid
            : ValidationResult.Invalid(
                CommandError.Of(ErrorCodes.LayoutNodeMissing, string.Join("、", missing)));
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var previous = document.Layout;

        if (Find(previous) is { } existing && _relation is { } wanted && existing.Value.Relation == wanted)
        {
            return CommandResult.NoOp("这一条相对位置已经有了");
        }

        if (Find(previous) is null && _relation is null)
        {
            return CommandResult.NoOp("本来就没有这一条相对位置");
        }

        var kept = previous.Place.Where(c => !IsThis(c));
        var updated = previous with
        {
            Place = _relation is { } relation
                ? [.. kept, new Constraint<PlaceConstraint>(new PlaceConstraint(_nodeId, _relativeTo, relation), _owner, CreatedAt())]
                : [.. kept],
        };

        document.Layout = updated;

        return CommandResult.Ok(
            affected: [_nodeId, _relativeTo],
            changes:
            [
                new FieldChange
                {
                    ElementId = _nodeId,
                    Field = FieldNames.Place,
                    OldValue = Find(previous) is { } before ? Describe(before.Value.Relation) : null,
                    NewValue = _relation is { } after ? Describe(after) : null,
                    Kind = _relation is null ? ChangeKind.Removed : ChangeKind.Modified,
                },
            ],
            structural: true,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) =>
        new SetPlaceCommand(_nodeId, _relativeTo, _relation, _owner);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new LayoutConstraintMemento
    {
        // 记整份布局提示：还原时整份换回去，不必按"这一条"再拼一次列表。
        // 两处拼接一旦分叉，撤销出来的约束集合与原来那份会有细微差别，而差异只体现在哈希上。
        Previous = document.Layout,
        AffectedIds = [_nodeId, _relativeTo],
        InverseChanges = [],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento) =>
        document.Layout = ((LayoutConstraintMemento)memento).Previous;

    /// <summary>这一对主语与参照上、属于本命令那个归属方的那一条。</summary>
    private Constraint<PlaceConstraint>? Find(LayoutHints layout) => layout.Place.FirstOrDefault(IsThis);

    private bool IsThis(Constraint<PlaceConstraint> constraint) =>
        constraint.Owner == _owner
        && string.Equals(constraint.Value.NodeId, _nodeId, StringComparison.Ordinal)
        && string.Equals(constraint.Value.RelativeTo, _relativeTo, StringComparison.Ordinal);

    /// <summary>
    /// 这一条约束的创建时刻。总线在执行那一刻回填；直接调执行路径时没有执行时刻，
    /// 写一个确定的纪元值而不是 0001 年——那个日期在文件里看起来像坏数据。
    /// 它不参与任何哈希。
    /// </summary>
    private DateTimeOffset CreatedAt() =>
        Context.Timestamp == default ? DateTimeOffset.UnixEpoch : Context.Timestamp;

    /// <summary>给人看的一句话。变更明细里存的是这句，不是枚举名。</summary>
    private static string Describe(PlaceRelation relation) => relation switch
    {
        PlaceRelation.RightOf => "在右侧",
        PlaceRelation.LeftOf => "在左侧",
        PlaceRelation.Above => "在上方",
        _ => "在下方",
    };

    private static ValidationResult Invalid(string detail) =>
        ValidationResult.Invalid(CommandError.Of(ErrorCodes.LayoutConstraintInvalid, detail));
}
