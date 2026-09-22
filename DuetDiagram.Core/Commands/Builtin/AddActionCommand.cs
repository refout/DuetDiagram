using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 新建一个动作。
/// </summary>
/// <remarks>
/// <para>
/// **它是唯一一条两个标志都报假的命令。** 动作不进任何哈希：它不影响坐标，也不影响像素，
/// 只影响交互。所以报成视觉变更会让宿主白白重绘一次，而画面上一个像素都不会变。
/// </para>
/// <para>
/// 两个标志都假**不等于无操作**：文档内容确实变了（序列化里有这条动作），
/// 所以版本照推、历史照进、广播照发。变的只是宿主那一侧的判断——不必重排，也不必重绘。
/// </para>
/// <para>
/// **目标在写入前要真的存在。** 目标指向的元素上没有回指字段，所以一个不存在的目标
/// 不会被任何别的地方发现。整体校验器报的是同一个码，两处含义相同。
/// 目标允许为空——那表示这个动作不作用于具体元素，整体校验器也不查这一档。
/// </para>
/// <para>
/// **标识占用要查全部九个集合。** 九个集合共用一个命名空间。
/// </para>
/// </remarks>
public sealed class AddActionCommand : DiagramCommandBase
{
    public const string Id = "add-action";

    private readonly ActionDef _action;

    public AddActionCommand(ActionDef action)
        : base(Id)
    {
        ArgumentNullException.ThrowIfNull(action);
        _action = action;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(_action.Id))
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.InvalidId, "动作标识为空"));
        }

        if (document.IsIdTaken(_action.Id))
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.DuplicateId, _action.Id));
        }

        return _action.Target is { } target && !document.IsIdTaken(target, except: null)
            ? ValidationResult.Invalid(CommandError.Of(ErrorCodes.ActionTargetMissing, target))
            : ValidationResult.Valid;
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.MutableActions.Add(_action);

        return CommandResult.Ok(
            affected: [_action.Id],
            changes:
            [
                new FieldChange
                {
                    ElementId = _action.Id,
                    Field = FieldNames.ActionElement,
                    OldValue = null,
                    NewValue = _action.Kind,
                    Kind = ChangeKind.Added,
                },
            ],

            // 动作两个哈希都不进：加一条动作既不改变坐标，也不改变像素。
            structural: false,
            visual: false);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new AddActionCommand(_action);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new ActionMemento
    {
        PreviousActions = [.. document.Actions],
        AffectedIds = [_action.Id],
        InverseChanges =
        [
            new FieldChange
            {
                ElementId = _action.Id,
                Field = FieldNames.ActionElement,
                OldValue = _action.Kind,
                NewValue = null,
                Kind = ChangeKind.Removed,
            },
        ],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        var typed = (ActionMemento)memento;

        document.MutableActions.Clear();
        document.MutableActions.AddRange(typed.PreviousActions);
    }
}
