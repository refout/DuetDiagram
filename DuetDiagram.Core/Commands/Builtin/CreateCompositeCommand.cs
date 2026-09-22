using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 新建一个组合（分组、泳道、子流程或组合框）。
/// </summary>
/// <remarks>
/// <para>
/// **四种组合合成一条命令。** 四个派生记录的字段形状完全一样，差别只在类型名。
/// 四类各立一条的话，校验、原子性与撤销三套逻辑要各写四遍，而抄漏的那一遍不会报错，
/// 只会让某一种组合在某个入口下改不动。种类由传进来的记录类型表达——
/// 再引入一个种类枚举的话，同一个意思会有两种写法，两者还可能对不上。
/// </para>
/// <para>
/// **成员会被"搬"进新组合，而不是只被记下。** 一个节点只能属于一个组合，
/// 所以把一个已经在别的分组里的节点放进新分组，必须同时把它从旧分组的成员列表里摘掉。
/// 只往新组合里加而不摘旧的，文档就成了一份自相矛盾的东西——
/// 校验器会报"成员列表与父级互相矛盾"，而那条报错离这次建组合已经很远了。
/// </para>
/// <para>
/// **标识占用要查全部九个集合，不是只查组合那一张表。** 九个集合共用一个命名空间，
/// 只查自己那一张的话，一个组合会拿到与某个节点相同的标识，
/// 而所有按标识定位的操作从那一刻起就有了歧义。
/// </para>
/// </remarks>
public sealed class CreateCompositeCommand : DiagramCommandBase
{
    public const string Id = "create-composite";

    private readonly CompositeDef _composite;
    private readonly int? _index;

    public CreateCompositeCommand(CompositeDef composite, int? index = null)
        : base(Id)
    {
        ArgumentNullException.ThrowIfNull(composite);
        _composite = composite;
        _index = index;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(_composite.Id))
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.InvalidId, "组合标识为空"));
        }

        if (document.IsIdTaken(_composite.Id))
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.DuplicateId, _composite.Id));
        }

        if (_composite.Parent is { } parent && document.FindComposite(parent) is null)
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.ParentMissing, parent));
        }

        var parentDepth = CompositeMembership.DepthOf(document, _composite.Parent);

        if (parentDepth + 1 > CompositeLimits.MaxDepth)
        {
            return ValidationResult.Invalid(TooDeep(_composite.Id, parentDepth + 1));
        }

        foreach (var member in _composite.Members)
        {
            // 把自己放进自己的成员列表里，是最短的一个环。
            if (string.Equals(member, _composite.Id, StringComparison.Ordinal))
            {
                return ValidationResult.Invalid(CommandError.Of(
                    ErrorCodes.GroupCycle,
                    $"组合 {_composite.Id} 的成员列表里有它自己"));
            }

            if (!document.HasEndpoint(member))
            {
                return ValidationResult.Invalid(CommandError.Of(ErrorCodes.GroupMemberMissing, member));
            }

            if (document.FindComposite(member) is null)
            {
                continue;
            }

            // 成员是组合时才有成环与深度两件事要操心：节点没有后代，也不占层数。
            if (CompositeMembership.IsAncestorOf(document, member, _composite.Parent ?? string.Empty))
            {
                return ValidationResult.Invalid(CommandError.Of(
                    ErrorCodes.GroupCycle,
                    $"成员 {member} 是外层 {_composite.Parent} 的祖先，套进去会成环"));
            }

            // 深度要按整棵子树算：跟着这个成员一起沉下去的还有它里面套着的所有东西。
            var deepest = parentDepth + 1 + CompositeMembership.SubtreeDepth(document, member);

            if (deepest > CompositeLimits.MaxDepth)
            {
                return ValidationResult.Invalid(TooDeep(_composite.Id, deepest));
            }
        }

        return ValidationResult.Valid;
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.MutableComposites.Insert(
            Math.Clamp(_index ?? document.Composites.Count, 0, document.Composites.Count),
            _composite);

        // 归属也是两处表达：新组合记着外层是谁，外层也要把新组合列进自己的成员列表。
        // 只写一处的话，外层那条"成员列表"就永远看不到这个子组合，
        // 而解散外层时它也就不会被放出来。
        if (_composite.Parent is { } parentId)
        {
            CompositeMembership.Attach(document, parentId, _composite.Id);
        }

        foreach (var member in _composite.Members)
        {
            // 先摘旧的再挂新的：一个节点只能属于一个组合。
            if (CompositeMembership.ParentOf(document, member) is { } previous)
            {
                CompositeMembership.Detach(document, previous, member);
            }

            CompositeMembership.SetParent(document, member, _composite.Id);
        }

        var changes = new List<FieldChange>(_composite.Members.Count + 2)
        {
            new()
            {
                ElementId = _composite.Id,
                Field = FieldNames.CompositeElement,
                OldValue = null,
                NewValue = _composite.Label,
                Kind = ChangeKind.Added,
            },
        };

        if (_composite.Parent is { } parent)
        {
            changes.Add(new FieldChange
            {
                ElementId = _composite.Id,
                Field = FieldNames.Parent,
                OldValue = null,
                NewValue = parent,
                Kind = ChangeKind.Modified,
            });
        }

        changes.AddRange(_composite.Members.Select(member => new FieldChange
        {
            ElementId = member,
            Field = FieldNames.Parent,
            OldValue = null,
            NewValue = _composite.Id,
            Kind = ChangeKind.Modified,
        }));

        return CommandResult.Ok(
            affected: [_composite.Id, .. _composite.Members],
            changes: [.. changes],
            structural: true,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new CreateCompositeCommand(_composite, _index);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new CompositeMemento
    {
        PreviousComposites = [.. document.Composites],
        PreviousParents =
        [
            .. _composite.Members.Select(member => new MemberPlacement(
                member,
                CompositeMembership.ParentOf(document, member))),
        ],
        AffectedIds = [_composite.Id, .. _composite.Members],

        // 逆变更与命令本身方向相反：新增的逆操作是移除。
        InverseChanges =
        [
            new FieldChange
            {
                ElementId = _composite.Id,
                Field = FieldNames.CompositeElement,
                OldValue = _composite.Label,
                NewValue = null,
                Kind = ChangeKind.Removed,
            },
        ],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        var typed = (CompositeMemento)memento;

        CompositeMembership.RestoreAll(document, typed.PreviousComposites, typed.PreviousParents);
    }

    private static CommandError TooDeep(string compositeId, int depth) => CommandError.Of(
        ErrorCodes.CompositeTooDeep,
        $"组合 {compositeId} 会嵌到第 {depth} 层，上限是 {CompositeLimits.MaxDepth} 层");
}
