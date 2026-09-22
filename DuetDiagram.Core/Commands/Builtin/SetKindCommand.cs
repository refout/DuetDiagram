using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 改整份文档的图类型（流程图 / 框图 / 时序图）。
/// </summary>
/// <remarks>
/// <para>
/// 图类型不是某个元素的字段，而是文档自己的属性，所以进不了元素字段表，要单立一条命令。
/// 判断标准是"被改的值挂在元素上还是挂在文档上"。
/// </para>
/// <para>
/// **它是结构变更。** 图类型进的是结构哈希，而那个哈希要回答的正是"要不要重新求解布局"：
/// 换了类型之后分层的语义与可用的形状都可能变，已算出的坐标不一定还有效。
/// 报成纯外观的话，宿主会只重绘不重排，而画面还是按旧类型排的。
/// </para>
/// <para>
/// 改成与当前相同的类型是 NoOp。批量下发时重复给同一个类型很常见，
/// 每次都推进版本、每次都重排一遍布局，是白付的代价。
/// </para>
/// <para>
/// 变更明细的归属元素写的是**文档自己的标识**：这一次改动没有落在任何元素上，
/// 硬指一个节点的话，界面会把高亮打在无关的节点上。
/// </para>
/// </remarks>
public sealed class SetKindCommand : DiagramCommandBase
{
    public const string Id = "set-kind";

    private readonly DiagramKind _kind;

    public SetKindCommand(DiagramKind kind)
        : base(Id)
    {
        _kind = kind;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // 枚举值可能来自反序列化或外部协议。落在定义之外时不能悄悄当成某个类型——
        // 那会让一次参数错误表现成"类型改了但改错了"。
        return Enum.IsDefined(_kind)
            ? ValidationResult.Valid
            : ValidationResult.Invalid(CommandError.Of(
                ErrorCodes.FieldValueInvalid,
                $"不是已定义的图类型：{(int)_kind}"));
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var previous = document.Kind;

        if (previous == _kind)
        {
            return CommandResult.NoOp("图类型没变");
        }

        document.Kind = _kind;

        return CommandResult.Ok(
            affected: [],
            changes:
            [
                new FieldChange
                {
                    ElementId = document.Id,
                    Field = FieldNames.Kind,
                    OldValue = previous.ToString(),
                    NewValue = _kind.ToString(),
                    Kind = ChangeKind.Modified,
                },
            ],
            structural: true,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new SetKindCommand(_kind);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new SetKindMemento
    {
        Previous = document.Kind,

        // 图类型是文档级的，没有哪个元素"被改到了"。
        AffectedIds = [],
        InverseChanges =
        [
            new FieldChange
            {
                ElementId = document.Id,
                Field = FieldNames.Kind,
                OldValue = _kind.ToString(),
                NewValue = document.Kind.ToString(),
                Kind = ChangeKind.Modified,
            },
        ],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento) =>
        document.Kind = ((SetKindMemento)memento).Previous;
}
