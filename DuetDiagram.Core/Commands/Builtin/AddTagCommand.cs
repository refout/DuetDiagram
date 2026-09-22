using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 新建一个标签。
/// </summary>
/// <remarks>
/// <para>
/// **成员在写入前要真的存在。** 标签与元素的关系是单向的——成员列表挂在标签自己身上，
/// 被点名的元素上没有回指字段。所以一个不存在的成员不会被任何别的地方发现，
/// 只会让这个标签从建起来那一刻就指着一个空位。整体校验器报的是同一个码，
/// 两处含义相同，不另起名字。
/// </para>
/// <para>
/// **标识占用要查全部九个集合。** 九个集合共用一个命名空间，标签的成员列表里
/// 不区分成员是节点还是组合，所以一个标签拿到与某个节点相同的标识之后，
/// 所有按标识定位的操作都会变得有歧义。
/// </para>
/// <para>
/// **它只计外观，不触发重排。** 标签进的是视觉哈希：打标签不改变任何坐标。
/// 报成结构变更的话，给一批节点打标签就要把整张图重排一遍。
/// </para>
/// <para>
/// 标签色取的是调色板令牌名，但这里**不检查令牌是否存在**：与节点的样式令牌同一个口径，
/// 认不出的令牌由渲染层退回兜底外观。在这里挡住的话，先打标签、后定义令牌这种顺序就做不到了。
/// </para>
/// </remarks>
public sealed class AddTagCommand : DiagramCommandBase
{
    public const string Id = "add-tag";

    private readonly TagDef _tag;

    public AddTagCommand(TagDef tag)
        : base(Id)
    {
        ArgumentNullException.ThrowIfNull(tag);
        _tag = tag;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(_tag.Id))
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.InvalidId, "标签标识为空"));
        }

        if (document.IsIdTaken(_tag.Id))
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.DuplicateId, _tag.Id));
        }

        foreach (var member in _tag.Members)
        {
            if (!document.IsIdTaken(member, except: null))
            {
                return ValidationResult.Invalid(CommandError.Of(ErrorCodes.TagMemberMissing, member));
            }
        }

        return ValidationResult.Valid;
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.MutableTags.Add(_tag);

        return CommandResult.Ok(
            affected: [_tag.Id],
            changes:
            [
                new FieldChange
                {
                    ElementId = _tag.Id,
                    Field = FieldNames.TagElement,
                    OldValue = null,
                    NewValue = _tag.Label,
                    Kind = ChangeKind.Added,
                },
            ],
            structural: false,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new AddTagCommand(_tag);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new TagMemento
    {
        PreviousTags = [.. document.Tags],
        AffectedIds = [_tag.Id],

        // 逆变更与命令本身方向相反：新增的逆操作是移除。
        InverseChanges =
        [
            new FieldChange
            {
                ElementId = _tag.Id,
                Field = FieldNames.TagElement,
                OldValue = _tag.Label,
                NewValue = null,
                Kind = ChangeKind.Removed,
            },
        ],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        var typed = (TagMemento)memento;

        // 整份换回去。标签与元素的关系是单向的，别处没有要摘的引用，
        // 所以还原就是这一处。
        document.MutableTags.Clear();
        document.MutableTags.AddRange(typed.PreviousTags);
    }
}
