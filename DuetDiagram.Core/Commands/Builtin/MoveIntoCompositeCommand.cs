using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 把一个节点或组合搬进另一个组合，或者搬到顶层。
/// </summary>
/// <remarks>
/// <para>
/// **这是"归属"这件事的唯一入口。** 节点与组合各有一个父级字段，但那个字段是冗余的那一份，
/// 真正说了算的是容器的成员列表。直接改父级字段（`set-node-field` 带 `parent`）会留下一份
/// 自相矛盾的文档——校验器报"成员列表与父级互相矛盾"，而报错的位置离那次改动很远。
/// </para>
/// <para>
/// **成环必须在写之前挡住。** 把一个组合搬进它自己的后代里，"向上找容器"这条链就永远走不到头：
/// 布局会无限递归，而栈溢出的现场离这条命令很远。判定含"搬进它自己"——
/// 那是最短的一个环。
/// </para>
/// <para>
/// **深度按整棵子树算。** 跟着被搬的组合一起沉下去的还有它里面套着的所有东西，
/// 只看被搬的那一个，就会出现"搬完才知道超了"的状态。
/// </para>
/// </remarks>
public sealed class MoveIntoCompositeCommand : DiagramCommandBase
{
    public const string Id = "move-into-composite";

    private readonly string _memberId;
    private readonly string? _targetId;

    /// <param name="memberId">要搬的节点或组合。</param>
    /// <param name="targetCompositeId">搬到哪个组合里。传空表示搬到顶层。</param>
    public MoveIntoCompositeCommand(string memberId, string? targetCompositeId)
        : base(Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberId);

        _memberId = memberId;
        _targetId = targetCompositeId;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!document.HasEndpoint(_memberId))
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.GroupMemberMissing, _memberId));
        }

        if (_targetId is not null && document.FindComposite(_targetId) is null)
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.CompositeMissing, _targetId));
        }

        if (_targetId is not null && document.FindComposite(_memberId) is { } moving)
        {
            // 含自身：把一个组合搬进它自己，是最短的一个环。
            if (CompositeMembership.IsAncestorOf(document, _memberId, _targetId))
            {
                return ValidationResult.Invalid(CommandError.Of(
                    ErrorCodes.GroupCycle,
                    $"{_memberId} 是 {_targetId} 的祖先，搬进去会成环"));
            }

            var depth = CompositeMembership.DepthOf(document, _targetId)
                        + CompositeMembership.SubtreeDepth(document, moving.Id);

            if (depth > CompositeLimits.MaxDepth)
            {
                return ValidationResult.Invalid(CommandError.Of(
                    ErrorCodes.CompositeTooDeep,
                    $"搬到 {_targetId} 之后 {_memberId} 的子树会到第 {depth} 层，"
                    + $"上限是 {CompositeLimits.MaxDepth} 层"));
            }
        }

        return ValidationResult.Valid;
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var previous = CompositeMembership.ParentOf(document, _memberId);

        if (string.Equals(previous, _targetId, StringComparison.Ordinal))
        {
            return CommandResult.NoOp("它已经在这个容器里了");
        }

        if (previous is not null)
        {
            CompositeMembership.Detach(document, previous, _memberId);
        }

        if (_targetId is not null)
        {
            CompositeMembership.Attach(document, _targetId, _memberId);
        }

        CompositeMembership.SetParent(document, _memberId, _targetId);

        return CommandResult.Ok(
            affected: [_memberId],
            changes:
            [
                new FieldChange
                {
                    ElementId = _memberId,
                    Field = FieldNames.Parent,
                    OldValue = previous,
                    NewValue = _targetId,
                    Kind = _targetId is null ? ChangeKind.Removed : ChangeKind.Modified,
                },
            ],
            structural: true,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) =>
        new MoveIntoCompositeCommand(_memberId, _targetId);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new CompositeMemento
    {
        PreviousComposites = [.. document.Composites],
        PreviousParents = [new MemberPlacement(_memberId, CompositeMembership.ParentOf(document, _memberId))],
        AffectedIds = [_memberId],
        InverseChanges = [],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        var typed = (CompositeMemento)memento;

        CompositeMembership.RestoreAll(document, typed.PreviousComposites, typed.PreviousParents);
    }
}
