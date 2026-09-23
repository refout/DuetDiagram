using System.ComponentModel;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;

namespace DuetDiagram.App.ViewModels;

/// <summary>
/// 画布上方的标签栏：文档里有哪几页、现在看的是哪一页、新建与删页。
/// </summary>
/// <remarks>
/// <para>
/// **翻页是重算，不只是重画。** 布局按页算，每一页上的元素不同，解出来的坐标也不同。
/// 这件事由会话负责（<see cref="DiagramSession.SwitchPage"/>），
/// 面板只把"我点了哪一页"送过去。
/// </para>
/// <para>
/// **列表顺序取文档里的次序字段。** 面板自己维护一份的话，撤销之后两边会对不上，
/// 而画面上看起来只是"标签的顺序没变回来"。
/// </para>
/// <para>
/// 列表按页签标识认，不按位置：删一页之后位置会变，而"我点的是哪一页"不能跟着变。
/// </para>
/// </remarks>
public sealed class PageTabsViewModel : INotifyPropertyChanged
{
    private readonly DiagramSession _session;
    private readonly List<PageTabViewModel> _tabs = [];

    public PageTabsViewModel(DiagramSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;

        // 只订阅 SceneChanged。文档变了、翻页了都会走到它，而它是在界面线程上的。
        _session.SceneChanged += Refresh;

        Refresh();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>页签，按文档里的次序。</summary>
    public IReadOnlyList<PageTabViewModel> Tabs => _tabs;

    /// <summary>正在看的那一页。文档里一页都没有时为空。</summary>
    public string? CurrentPageId => _session.CurrentPageId;

    /// <summary>当前页的名字。没起名字时回落到标识。</summary>
    public string? CurrentPageName
    {
        get
        {
            var current = _tabs.FirstOrDefault(tab =>
                string.Equals(tab.Id, _session.CurrentPageId, StringComparison.Ordinal));

            if (current is null)
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(current.Name) ? current.Id : current.Name;
        }
    }

    /// <summary>文档里有没有页面。</summary>
    public bool HasPages => _tabs.Count > 0;

    /// <summary>能不能删当前这一页。最后一页删不得。</summary>
    public bool CanDelete => _tabs.Count > 1 && !_session.IsReadOnly;

    /// <summary>文档改不动时顶上那一句。可写时为空。</summary>
    public string? ReadOnlyNote => _session.ReadOnlyReason;

    /// <summary>要不要显示那一句。</summary>
    public bool HasReadOnlyNote => _session.IsReadOnly;

    /// <summary>当前页上有几个元素。翻页时给用户一个"这一页不是空的"的凭据。</summary>
    public int CurrentElementCount
    {
        get
        {
            if (_session.CurrentPageId is not { } pageId)
            {
                return 0;
            }

            var document = _session.Document;

            return document.Nodes.Count(node => PageMembership.Shows(document, node, pageId));
        }
    }

    /// <summary>
    /// 翻到某一页。
    /// </summary>
    /// <remarks>
    /// 会话那边会顺带把选中筛一遍：不在这一页上的元素点不中，
    /// 留着它们会让属性面板显示一个画布上看不见的东西的字段。
    /// </remarks>
    public bool Select(string pageId)
    {
        ArgumentNullException.ThrowIfNull(pageId);

        return _session.SwitchPage(pageId);
    }

    /// <summary>
    /// 新建一页并切过去。
    /// </summary>
    /// <remarks>
    /// 建完就切过去：用户刚建一页，接下来多半要往里放东西。
    /// </remarks>
    public CommandResult Create(string? name = null) => _session.CreatePage(name);

    /// <summary>删掉当前这一页。最后一页删不得。</summary>
    public CommandResult DeleteCurrent() =>
        _session.CurrentPageId is { } pageId
            ? _session.DeletePage(pageId)
            : CommandResult.Fail(CommandError.Of(ErrorCodes.PageMissing, "当前没有可以删的页面"));

    /// <summary>按文档里的页面重读一遍。</summary>
    public void Refresh()
    {
        var pages = PageMembership.Ordered(_session.Document);
        var current = _session.CurrentPageId;

        // 页签集合只在增删页面时重建。翻页只换"哪一个亮着"——重建的话，
        // 每点一次页签整条标签栏会闪一下。
        if (!SameIds(pages))
        {
            _tabs.Clear();

            foreach (var page in pages)
            {
                _tabs.Add(new PageTabViewModel(page.Id));
            }

            Raise(nameof(Tabs));
        }

        for (var index = 0; index < pages.Count && index < _tabs.Count; index++)
        {
            _tabs[index].Set(pages[index], current: string.Equals(pages[index].Id, current, StringComparison.Ordinal));
        }

        Raise(nameof(CurrentPageId));
        Raise(nameof(CurrentPageName));
        Raise(nameof(HasPages));
        Raise(nameof(CanDelete));
        Raise(nameof(CurrentElementCount));
    }

    private bool SameIds(IReadOnlyList<PageDef> pages)
    {
        if (pages.Count != _tabs.Count)
        {
            return false;
        }

        for (var index = 0; index < pages.Count; index++)
        {
            if (!string.Equals(pages[index].Id, _tabs[index].Id, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
