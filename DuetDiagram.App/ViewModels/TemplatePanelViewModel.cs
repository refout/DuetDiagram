using System.ComponentModel;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Templates;

namespace DuetDiagram.App.ViewModels;

/// <summary>
/// 模板面板的状态：目录里有哪些模板，点一个把它拼进当前文档。
/// </summary>
/// <remarks>
/// <para>
/// **面板只做两件事：列出、放入。** 「把选中的东西存成模板」是另一件事，
/// 它不进文档、不进撤销栈，所以走的是另一条路（见 <see cref="SaveSelection"/>），
/// 由菜单上那一条触发。两件事混在一个按钮上，用户点下去之前不知道会发生哪一种。
/// </para>
/// <para>
/// **清单来自目录，不来自文档。** 模板是文件，与当前打开的是哪份文档无关；
/// 所以这个面板不订阅文档变更——文档改了模板清单不会变，跟着重扫只是白读一遍磁盘。
/// </para>
/// <para>
/// **放入是一次命令。** 模板里有十条元素也只有一条历史，撤销按一次整份退回——
/// 用户在界面上做的是同一次「放了一个模板」。
/// </para>
/// </remarks>
public sealed class TemplatePanelViewModel : INotifyPropertyChanged
{
    private readonly DiagramSession _session;
    private readonly TemplateCatalog _catalog;

    private IReadOnlyList<TemplateRowViewModel> _rows = [];
    private string? _error;
    private string? _note;

    /// <summary>
    /// 建一个面板状态。
    /// </summary>
    /// <param name="session">当前文档。</param>
    /// <param name="catalog">从哪儿找模板。</param>
    public TemplatePanelViewModel(DiagramSession session, TemplateCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(catalog);

        _session = session;
        _catalog = catalog;

        Refresh();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>模板行，按文件名。</summary>
    public IReadOnlyList<TemplateRowViewModel> Rows => _rows;

    /// <summary>读不出来的那几个文件，各自带一句原因。</summary>
    public IReadOnlyList<TemplateLoadFailure> Failures => _catalog.Failures;

    /// <summary>有没有读不出来的文件要摆出来。</summary>
    public bool HasFailures => Failures.Count > 0;

    /// <summary>一条模板都没有。空清单要配一句话，否则看起来像面板坏了。</summary>
    public bool HasNoTemplates => _rows.Count == 0;

    /// <summary>最近一次失败的一句话。成功一次就清掉——留着它，用户会以为上一次还没成。</summary>
    public string? Error
    {
        get => _error;

        private set
        {
            if (!string.Equals(_error, value, StringComparison.Ordinal))
            {
                _error = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    /// <summary>有没有要显示的错误。</summary>
    public bool HasError => Error is not null;

    /// <summary>最近一次成功的一句话（例如存成了哪个文件）。下次动手就清掉。</summary>
    public string? Note
    {
        get => _note;

        private set
        {
            if (!string.Equals(_note, value, StringComparison.Ordinal))
            {
                _note = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasNote));
            }
        }
    }

    /// <summary>有没有要显示的说明。</summary>
    public bool HasNote => Note is not null;

    /// <summary>只读的原因。可写时为空。</summary>
    public string? ReadOnlyNote => _session.ReadOnlyReason;

    /// <summary>有没有只读提示要显示。</summary>
    public bool HasReadOnlyNote => ReadOnlyNote is not null;

    /// <summary>
    /// 把一份模板拼进当前文档。
    /// </summary>
    /// <remarks>
    /// 标识冲突、片段自身不合法这些都在命令层判，面板只把结果摆出来——
    /// 判据写在两处的话，两处迟早会对同一份模板给出不同的答案。
    /// </remarks>
    public void Apply(TemplateDocument template)
    {
        ArgumentNullException.ThrowIfNull(template);

        Error = null;
        Note = null;

        var result = _session.InsertTemplate(template);

        if (!result.IsEffectiveSuccess)
        {
            Error = Describe(result);
        }
    }

    /// <summary>
    /// 把当前选中的元素存成一份新模板，放进模板目录。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **名字取文档标识。** 模板需要一个名字，而这里没有一个能问名字的地方；
    /// 文档标识是唯一一个用户自己写下过的、能指代"这一批东西"的词。
    /// 同名不覆盖，另起一个带序号的——见 <see cref="TemplateCatalog.Save"/>。
    /// </para>
    /// <para>
    /// **它不改文档，所以不进撤销栈。** 存出去的是一份新文件，
    /// 按撤销退不回"那个文件没被写出来"，所以这里不报成功变更。
    /// </para>
    /// </remarks>
    public void SaveSelection()
    {
        Error = null;
        Note = null;

        if (_session.IsReadOnly)
        {
            Error = ReadOnlyNote;
            return;
        }

        if (_session.SelectedIds.Count == 0)
        {
            Error = "先在画布上选中要存成模板的元素";
            return;
        }

        try
        {
            var template = TemplateDocument.FromDocument(
                _session.Document.Id,
                _session.Document,
                _session.SelectedIds);

            Note = $"已存为 {System.IO.Path.GetFileName(_catalog.Save(template))}";
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            Error = $"存不进去：{exception.Message}";
        }

        Refresh();
    }

    /// <summary>按目录里现在的内容重铺一遍清单。</summary>
    public void Refresh()
    {
        _rows = [.. _catalog.Entries.Select(template => new TemplateRowViewModel(template, () => Apply(template)))];

        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Failures));
        OnPropertyChanged(nameof(HasFailures));
        OnPropertyChanged(nameof(HasNoTemplates));
    }

    private static string Describe(CommandResult result)
    {
        if (result.Errors.Length == 0)
        {
            return result.Message ?? "这一次改动没有写进去";
        }

        var error = result.Errors[0];

        return string.IsNullOrWhiteSpace(error.Payload) ? error.Code : error.Payload;
    }

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
}

/// <summary>
/// 模板清单里的一行。
/// </summary>
/// <remarks>
/// 与形状、调色板那几行的口径一致：行只带"画什么"与"点了做什么"，
/// 不含任何"这份模板是什么"的判断——那份判断在核心层。
/// </remarks>
public sealed class TemplateRowViewModel
{
    public TemplateRowViewModel(TemplateDocument template, Action apply)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(apply);

        Name = template.Name;
        Summary = $"{template.Nodes.Count} 节点 · {template.Edges.Count} 边 · {template.Composites.Count} 组合";
        Apply = apply;
    }

    /// <summary>模板名。取自文件里的文档标识。</summary>
    public string Name { get; }

    /// <summary>里面有几样东西。放之前要能看出来这一份是不是自己要的。</summary>
    public string Summary { get; }

    /// <summary>点这一行做什么。</summary>
    public Action Apply { get; }
}
