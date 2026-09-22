using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 删掉一个动作。
/// </summary>
/// <remarks>
/// <para>
/// **不需要顺带摘掉任何引用。** 目标引用挂在动作自己身上，被指向的元素上没有回指字段，
/// 所以删一个动作就是删掉那份引用本身，不存在悬空引用。
/// </para>
/// <para>
/// **两个标志都报假。** 动作不进任何哈希，删一条动作不改变坐标也不改变像素——
/// 与新建动作同一口径。
/// </para>
/// <para>
/// 反过来那件事——删掉一个元素之后，某个动作的目标还指着它——
/// 由整体校验器报 <c>ACTION_TARGET_MISSING</c>，命令层留着不管。
/// </para>
/// <para>
/// **要删的动作不存在时报错，不报无操作。** 那通常说明调用方手上那份列表已经过期。
/// </para>
/// </remarks>
public sealed class RemoveActionCommand : DiagramCommandBase
{
    public const string Id = "remove-action";

    private readonly string _actionId;

    public RemoveActionCommand(string actionId)
        : base(Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        _actionId = actionId;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Find(document) is null
            ? ValidationResult.Invalid(CommandError.Of(ErrorCodes.ActionMissing, _actionId))
            : ValidationResult.Valid;
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (Find(document) is not { } action)
        {
            return CommandResult.Fail([CommandError.Of(ErrorCodes.ActionMissing, _actionId)]);
        }

        document.MutableActions.Remove(action);

        return CommandResult.Ok(
            affected: [_actionId],
            changes:
            [
                new FieldChange
                {
                    ElementId = _actionId,
                    Field = FieldNames.ActionElement,
                    OldValue = action.Kind,
                    NewValue = null,
                    Kind = ChangeKind.Removed,
                },
            ],
            structural: false,
            visual: false);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new RemoveActionCommand(_actionId);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new ActionMemento
    {
        PreviousActions = [.. document.Actions],
        AffectedIds = [_actionId],
        InverseChanges =
        [
            new FieldChange
            {
                ElementId = _actionId,
                Field = FieldNames.ActionElement,
                OldValue = null,
                NewValue = Find(document)?.Kind,
                Kind = ChangeKind.Added,
            },
        ],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        var typed = (ActionMemento)memento;

        document.MutableActions.Clear();
        document.MutableActions.AddRange(typed.PreviousActions);
    }

    private ActionDef? Find(DiagramDocument document) =>
        document.Actions.FirstOrDefault(action => string.Equals(action.Id, _actionId, StringComparison.Ordinal));
}
