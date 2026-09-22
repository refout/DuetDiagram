using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 删掉一个标签。
/// </summary>
/// <remarks>
/// <para>
/// **不需要顺带摘掉任何引用。** 标签与元素的关系是单向的：成员列表挂在标签自己身上，
/// 被点名的元素上没有回指字段。删一个标签就是删掉那份成员列表本身，
/// 不存在"元素上还挂着它"这种状态，也就没有悬空引用要清理。
/// </para>
/// <para>
/// 这与删调色板条目的处置**不同**，而判据是同一条「这件事有没有第二个地方能看见」：
/// 删掉一个还在被引用的样式令牌，渲染层只是静默退回元素自己的样式、没有任何东西会报，
/// 所以那一条要挡住；而删标签不会让任何别处的数据变得没有意义。
/// </para>
/// <para>
/// 反过来那件事——删掉一个元素之后，某个标签的成员列表里还写着它——
/// 由整体校验器报 <c>TAG_MEMBER_MISSING</c>，命令层留着不管。
/// 与删边留下的悬空次序同一口径：有第二个地方能看见，就让它去报。
/// </para>
/// <para>
/// **要删的标签不存在时报错，不报无操作。** 那通常说明调用方手上那份列表已经过期，
/// 报成功会让它继续拿一份错的列表往下走。
/// </para>
/// </remarks>
public sealed class RemoveTagCommand : DiagramCommandBase
{
    public const string Id = "remove-tag";

    private readonly string _tagId;

    public RemoveTagCommand(string tagId)
        : base(Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagId);
        _tagId = tagId;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Find(document) is null
            ? ValidationResult.Invalid(CommandError.Of(ErrorCodes.TagMissing, _tagId))
            : ValidationResult.Valid;
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (Find(document) is not { } tag)
        {
            return CommandResult.Fail([CommandError.Of(ErrorCodes.TagMissing, _tagId)]);
        }

        document.MutableTags.Remove(tag);

        return CommandResult.Ok(
            affected: [_tagId],
            changes:
            [
                new FieldChange
                {
                    ElementId = _tagId,
                    Field = FieldNames.TagElement,
                    OldValue = tag.Label,
                    NewValue = null,
                    Kind = ChangeKind.Removed,
                },
            ],
            structural: false,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new RemoveTagCommand(_tagId);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new TagMemento
    {
        PreviousTags = [.. document.Tags],
        AffectedIds = [_tagId],
        InverseChanges =
        [
            new FieldChange
            {
                ElementId = _tagId,
                Field = FieldNames.TagElement,
                OldValue = null,
                NewValue = Find(document)?.Label,
                Kind = ChangeKind.Added,
            },
        ],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        var typed = (TagMemento)memento;

        document.MutableTags.Clear();
        document.MutableTags.AddRange(typed.PreviousTags);
    }

    private TagDef? Find(DiagramDocument document) =>
        document.Tags.FirstOrDefault(tag => string.Equals(tag.Id, _tagId, StringComparison.Ordinal));
}
