using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 删掉一页。
/// </summary>
/// <remarks>
/// <para>
/// **最后一页删不得。** 一份文档至少要有一页：渲染层拿到空的页面集合时该画什么没有定义，
/// 而"没有页面"这件事在界面上也没有对应的样子——用户看到的会是一片空白，
/// 却没有任何东西告诉他这是因为文档没有页。所以这条限制放在命令层挡住，
/// 而不是留给渲染层去兜底。
/// </para>
/// <para>
/// **要删的页面不存在时报错，不报无操作。** 那通常说明调用方手上那份列表已经过期——
/// 它以为还有那一页，实际上已经没了。报成功会让它继续拿一份错的列表往下走。
/// </para>
/// <para>
/// 撤销时把**整份页面集合**换回去，而不是把那一页追加到末尾。页面的集合位置是加入顺序，
/// 追加会让撤销之后的次序与删除前不同；而两个哈希都按标识排序后遍历，
/// 顺序错了照样对得上，这个错在哈希上一点痕迹都没有。
/// </para>
/// </remarks>
public sealed class DeletePageCommand : DiagramCommandBase
{
    public const string Id = "delete-page";

    private readonly string _pageId;

    public DeletePageCommand(string pageId)
        : base(Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        _pageId = pageId;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (PageAccess.Find(document, _pageId) is null)
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.PageMissing, _pageId));
        }

        // 只剩这一页时挡住。页面集合为空之后渲染层无页面可画，
        // 而那不是一次"删掉了一个东西"能解释的状态。
        return document.Pages.Count <= 1
            ? ValidationResult.Invalid(CommandError.Of(
                ErrorCodes.PageRequired,
                $"文档至少要留一页，{_pageId} 是最后一页"))
            : ValidationResult.Valid;
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var index = PageAccess.IndexOf(document, _pageId);

        if (index < 0)
        {
            return CommandResult.Fail([CommandError.Of(ErrorCodes.PageMissing, _pageId)]);
        }

        if (document.Pages.Count <= 1)
        {
            return CommandResult.Fail([CommandError.Of(
                ErrorCodes.PageRequired,
                $"文档至少要留一页，{_pageId} 是最后一页")]);
        }

        var removed = document.Pages[index];

        document.MutablePages.RemoveAt(index);

        return CommandResult.Ok(
            affected: [_pageId],
            changes:
            [
                new FieldChange
                {
                    ElementId = _pageId,
                    Field = FieldNames.PageElement,
                    OldValue = removed.Name,
                    NewValue = null,
                    Kind = ChangeKind.Removed,
                },
            ],
            structural: false,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new DeletePageCommand(_pageId);

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
                OldValue = null,
                NewValue = PageAccess.Find(document, _pageId)?.Name,
                Kind = ChangeKind.Added,
            },
        ],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento) =>
        PageAccess.RestoreAll(document, ((PageMemento)memento).PreviousPages);
}
