using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 解散一个组合，成员交给它的外层。
/// </summary>
/// <remarks>
/// <para>
/// **成员回到父级，不是回到顶层。** 多层嵌套时回到顶层会把层级结构悄悄压平：
/// 用户拆掉最里面那一层，结果外面几层的归属也跟着没了，而画面上只是"少了个框"。
/// </para>
/// <para>
/// **成员在外层成员列表里的落点是原组合占的那一位。** 泳道里成员的先后就是条带顺序，
/// 把解散出来的成员一律追加到末尾，条带次序会跟着变——而这件事在界面上看不出异常，
/// 只有逐项比较才知道。落回原位还有个好处：连着拆两层，次序仍然是稳的。
/// </para>
/// <para>
/// 解散只动归属关系，不动成员自己的属性。节点与组合本身一个都不删——
/// "拆掉这个框"与"删掉框里的东西"是两件事。
/// </para>
/// </remarks>
public sealed class DissolveCompositeCommand : DiagramCommandBase
{
    public const string Id = "dissolve-composite";

    private readonly string _compositeId;

    public DissolveCompositeCommand(string compositeId)
        : base(Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compositeId);
        _compositeId = compositeId;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.FindComposite(_compositeId) is null
            ? ValidationResult.Invalid(CommandError.Of(ErrorCodes.CompositeMissing, _compositeId))
            : ValidationResult.Valid;
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var composite = document.FindComposite(_compositeId)
            ?? throw new InvalidOperationException($"Cannot dissolve composite: '{_compositeId}' does not exist.");

        var parentId = composite.Parent;

        if (parentId is not null)
        {
            CompositeMembership.ReleaseInto(document, parentId, composite);
        }

        foreach (var member in composite.Members)
        {
            CompositeMembership.SetParent(document, member, parentId);
        }

        document.MutableComposites.RemoveAt(IndexOf(document));

        var changes = new List<FieldChange>(composite.Members.Count + 1)
        {
            new()
            {
                ElementId = _compositeId,
                Field = FieldNames.CompositeElement,
                OldValue = composite.Label,
                NewValue = null,
                Kind = ChangeKind.Removed,
            },
        };

        changes.AddRange(composite.Members.Select(member => new FieldChange
        {
            ElementId = member,
            Field = FieldNames.Parent,
            OldValue = _compositeId,
            NewValue = parentId,
            Kind = ChangeKind.Modified,
        }));

        return CommandResult.Ok(
            affected: [_compositeId, .. composite.Members],
            changes: [.. changes],
            structural: true,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new DissolveCompositeCommand(_compositeId);

    protected override CommandMemento CaptureCore(DiagramDocument document)
    {
        var composite = document.FindComposite(_compositeId)
            ?? throw new InvalidOperationException($"Cannot capture memento: composite '{_compositeId}' does not exist.");

        var affected = new List<string>(composite.Members.Count + 1) { _compositeId };
        affected.AddRange(composite.Members);

        return new CompositeMemento
        {
            PreviousComposites = [.. document.Composites],
            PreviousParents =
            [
                .. composite.Members.Select(member => new MemberPlacement(
                    member,
                    CompositeMembership.ParentOf(document, member))),
            ],
            AffectedIds = [.. affected],

            // 逆变更的方向是"新增"：撤销解散等于把那个组合加回来。
            InverseChanges =
            [
                new FieldChange
                {
                    ElementId = _compositeId,
                    Field = FieldNames.CompositeElement,
                    OldValue = null,
                    NewValue = composite.Label,
                    Kind = ChangeKind.Added,
                },
            ],
        };
    }

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        var typed = (CompositeMemento)memento;

        CompositeMembership.RestoreAll(document, typed.PreviousComposites, typed.PreviousParents);
    }

    private int IndexOf(DiagramDocument document)
    {
        for (var i = 0; i < document.MutableComposites.Count; i++)
        {
            if (string.Equals(document.MutableComposites[i].Id, _compositeId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new InvalidOperationException($"Cannot dissolve composite: '{_compositeId}' does not exist.");
    }
}
