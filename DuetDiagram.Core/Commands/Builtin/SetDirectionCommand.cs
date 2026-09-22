using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 改整份文档的布局主方向。
/// </summary>
/// <remarks>
/// <para>
/// 方向不是某个元素的字段，而是文档自己的属性：它决定层往哪个方向推进，是布局的输入。
/// 所以它不在元素字段表里，要单立一条命令。判断标准是"被改的值挂在元素上还是挂在文档上"。
/// </para>
/// <para>
/// **它是结构变更。** 方向进的是结构哈希，而那个哈希要回答的正是"要不要重新求解布局"。
/// 报成纯外观的话，宿主会只重绘不重排，而画面上的层还是按旧方向排的。
/// </para>
/// <para>
/// 改成与当前相同的方向是 NoOp。批量下发时重复给同一个方向很常见，
/// 每次都推进版本、每次都重排一遍布局，是白付的代价。
/// </para>
/// <para>
/// 变更明细的归属元素写的是**文档自己的标识**：这一次改动没有落在任何元素上，
/// 硬指一个节点的话，界面会把高亮打在无关的节点上。写文档标识之后，
/// 读日志的人一眼能看出"改的是这份文档"。
/// </para>
/// </remarks>
public sealed class SetDirectionCommand : DiagramCommandBase
{
    public const string Id = "set-direction";

    private readonly Direction _direction;

    public SetDirectionCommand(Direction direction)
        : base(Id)
    {
        _direction = direction;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // 枚举值可能来自反序列化或外部协议。落在定义之外时不能悄悄当成某个方向——
        // 那会让一次参数错误表现成"方向改了但改错了"。
        return Enum.IsDefined(_direction)
            ? ValidationResult.Valid
            : ValidationResult.Invalid(CommandError.Of(
                ErrorCodes.FieldValueInvalid,
                $"不是已定义的布局方向：{(int)_direction}"));
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var previous = document.Direction;

        if (previous == _direction)
        {
            return CommandResult.NoOp("方向没变");
        }

        document.Direction = _direction;

        return CommandResult.Ok(
            affected: [],
            changes:
            [
                new FieldChange
                {
                    ElementId = document.Id,
                    Field = FieldNames.Direction,
                    OldValue = previous.ToString(),
                    NewValue = _direction.ToString(),
                    Kind = ChangeKind.Modified,
                },
            ],
            structural: true,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new SetDirectionCommand(_direction);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new SetDirectionMemento
    {
        Previous = document.Direction,

        // 方向是文档级的，没有哪个元素"被改到了"。
        AffectedIds = [],
        InverseChanges =
        [
            new FieldChange
            {
                ElementId = document.Id,
                Field = FieldNames.Direction,
                OldValue = _direction.ToString(),
                NewValue = document.Direction.ToString(),
                Kind = ChangeKind.Modified,
            },
        ],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento) =>
        document.Direction = ((SetDirectionMemento)memento).Previous;
}
