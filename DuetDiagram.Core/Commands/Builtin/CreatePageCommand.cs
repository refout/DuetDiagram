using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 新建一页。
/// </summary>
/// <remarks>
/// <para>
/// **次序由命令算出来，不由调用方给。** 调用方手上那份页面列表可能已经过期，
/// 它算出来的"下一个次序"很可能与现有某一页撞上；而两页次序相同之后，
/// 翻页的先后就变成由集合位置决定的偶然结果。这里每次读当前最大次序再加一。
/// </para>
/// <para>
/// **标识占用要查全部九个集合。** 九个集合共用一个命名空间，
/// 只查页面那一张的话，一页会拿到与某个节点相同的标识，
/// 而所有按标识定位的操作从那一刻起就有了歧义。
/// </para>
/// <para>
/// **它只计外观，不触发重排。** 页面进的是视觉哈希：加一页不改变任何坐标。
/// 报成结构变更的话，新建一页就要把整张图重排一遍。
/// </para>
/// </remarks>
public sealed class CreatePageCommand : DiagramCommandBase
{
    public const string Id = "create-page";

    private readonly string _pageId;
    private readonly string _name;

    public CreatePageCommand(string pageId, string name = "")
        : base(Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);

        _pageId = pageId;
        _name = name;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.IsIdTaken(_pageId)
            ? ValidationResult.Invalid(CommandError.Of(ErrorCodes.DuplicateId, _pageId))
            : ValidationResult.Valid;
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.MutablePages.Add(new PageDef
        {
            Id = _pageId,
            Name = _name,
            Order = NextOrder(document),
        });

        return CommandResult.Ok(
            affected: [_pageId],
            changes:
            [
                new FieldChange
                {
                    ElementId = _pageId,
                    Field = FieldNames.PageElement,
                    OldValue = null,
                    NewValue = _name,
                    Kind = ChangeKind.Added,
                },
            ],
            structural: false,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new CreatePageCommand(_pageId, _name);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new PageMemento
    {
        PreviousPages = [.. document.Pages],
        AffectedIds = [_pageId],
        InverseChanges =
        [
            new FieldChange
            {
                ElementId = _pageId,
                Field = FieldNames.PageElement,
                OldValue = _name,
                NewValue = null,
                Kind = ChangeKind.Removed,
            },
        ],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento) =>
        PageAccess.RestoreAll(document, ((PageMemento)memento).PreviousPages);

    /// <summary>当前最大的页面次序加一。没有页面时从零开始。</summary>
    private static int NextOrder(DiagramDocument document) =>
        document.Pages.Count == 0 ? 0 : document.Pages.Max(page => page.Order) + 1;
}
