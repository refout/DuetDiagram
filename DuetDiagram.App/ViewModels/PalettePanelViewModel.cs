using System.ComponentModel;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;

namespace DuetDiagram.App.ViewModels;

/// <summary>
/// 调色板面板的状态：条目清单、选中条目的编辑器，以及新建与删除。
/// </summary>
/// <remarks>
/// <para>
/// **面板改的是条目，不是元素。** 「把选中的元素改成某个令牌」是属性面板里那个
/// 样式令牌字段的事——混进这里的话，一次点击会发两条命令，而撤销要按两次。
/// </para>
/// <para>
/// **预览走主题解析，不自己画色块。** 每行的色块由
/// <see cref="DiagramSession.RenderTheme"/> 按一个只带令牌名的样例节点解析出来，
/// 与画布用同一条代码路径——边线粗细、缺省兜底这些在预览上看不出来，
/// 用户会以为它们没生效。
/// </para>
/// </remarks>
public sealed class PalettePanelViewModel : INotifyPropertyChanged
{
    private readonly DiagramSession _session;
    private readonly List<PaletteRowViewModel> _rows = [];

    private string? _selectedName;
    private string _newName = string.Empty;
    private string? _error;

    public PalettePanelViewModel(DiagramSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;

        // 与图层面板同一套接线：不接 DocumentChanged（它在后台线程上到），
        // 宿主重算完发的 SceneChanged 才在界面线程上。
        _session.SceneChanged += Refresh;

        Refresh();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>条目行，按令牌名的字典序。条目表是字典，声明次序没有语义。</summary>
    public IReadOnlyList<PaletteRowViewModel> Rows => _rows;

    /// <summary>选中的令牌名。没有选中时为空。</summary>
    public string? SelectedName
    {
        get => _selectedName;

        private set
        {
            if (string.Equals(_selectedName, value, StringComparison.Ordinal))
            {
                return;
            }

            _selectedName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(EditorVisible));
        }
    }

    /// <summary>有没有选中的条目。编辑器一节的显隐跟着它走。</summary>
    public bool HasSelection => _selectedName is not null;

    /// <summary>编辑器一节是否可见。没有选中时整节收起来，不摆一排灰着的框。</summary>
    public bool EditorVisible => HasSelection;

    /// <summary>新建框里的名字。绑定写进来，按下「新建」时用。</summary>
    public string NewName
    {
        get => _newName;

        set
        {
            if (!string.Equals(_newName, value, StringComparison.Ordinal))
            {
                _newName = value;
                OnPropertyChanged();
            }
        }
    }

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

    /// <summary>只读的原因。可写时为空。</summary>
    public string? ReadOnlyNote => _session.ReadOnlyReason;

    /// <summary>有没有只读提示要显示。</summary>
    public bool HasReadOnlyNote => ReadOnlyNote is not null;

    /// <summary>选中条目的四个可改成员，按界面上从上到下的次序。</summary>
    public IReadOnlyList<PaletteFieldViewModel> EditorFields { get; private set; } = [];

    /// <summary>按令牌名选一行。界面上点行时调，测试也这样进来。</summary>
    public void Select(string? name)
    {
        SelectedName = _rows.Any(row => string.Equals(row.Name, name, StringComparison.Ordinal))
            ? name
            : null;
        RebuildEditor();
    }

    /// <summary>定义一个新条目。名为空、重名都由命令层挡，错误原样摆到面板上。</summary>
    public void Define()
    {
        Error = null;

        var name = NewName.Trim();
        var result = _session.DefinePaletteEntry(new PaletteEntry
        {
            Name = name,
            Fill = _session.RenderTheme.NodeFill,
            Stroke = _session.RenderTheme.NodeStroke,
        });

        if (result.IsEffectiveSuccess)
        {
            NewName = string.Empty;
            Refresh();
            Select(name);

            return;
        }

        Error = Describe(result);
        Refresh();
    }

    /// <summary>删掉选中的条目。</summary>
    /// <remarks>
    /// 按下时先查一遍引用：还在被引用的话**不发命令**，把名单摆出来——
    /// 等命令的错误回来再展示，那串名字挤在一句话里，而「谁在用」值得整块地方。
    /// 名单与命令校验读的是同一个方法，两处不会对出两份不一样的答案。
    /// </remarks>
    public void RemoveSelected()
    {
        Error = null;

        if (SelectedName is not { } name)
        {
            return;
        }

        var referrers = _session.PaletteReferrers(name);

        if (referrers.Count > 0)
        {
            Error = $"还有 {referrers.Count} 个元素在用 {name}：{string.Join("、", referrers)}";

            return;
        }

        var result = _session.RemovePaletteEntry(name);

        if (result.IsEffectiveSuccess)
        {
            Select(null);
        }
        else
        {
            Error = Describe(result);
        }

        Refresh();
    }

    /// <summary>按当前文档重读一遍清单。</summary>
    public void Refresh()
    {
        _rows.Clear();

        foreach (var name in _session.Document.Palette.Entries.Keys.OrderBy(
            name => name, StringComparer.Ordinal))
        {
            _rows.Add(BuildRow(name));
        }

        // 选中的条目被别的窗口删掉时，选中跟着落空——留着它会让编辑器
        // 对着一个已经不存在的条目写字。
        if (_selectedName is { } selected
            && _rows.All(row => !string.Equals(row.Name, selected, StringComparison.Ordinal)))
        {
            Select(null);
        }

        OnPropertyChanged(nameof(Rows));
    }

    private PaletteRowViewModel BuildRow(string name)
    {
        // 预览与画布同一条解析路径：一个只带令牌名的样例节点，缺的成员落到主题缺省值上。
        var appearance = _session.RenderTheme.Node(new NodeDef { Id = name, StyleToken = name });
        var referrers = _session.PaletteReferrers(name);

        return new PaletteRowViewModel(
            name,
            appearance.Fill,
            appearance.Stroke,
            appearance.Weight,
            referrers.Count);
    }

    private void RebuildEditor()
    {
        if (SelectedName is not { } name
            || _session.Document.Palette.Find(name) is not { } entry)
        {
            EditorFields = [];

            OnPropertyChanged(nameof(EditorFields));

            return;
        }

        // 提交直接对准条目的那个成员：一次一条命令，与命令层「一次只改一个字段」同形状。
        PaletteFieldViewModel Field(string field, string label, string? value) =>
            new(field, label, value, value =>
            {
                Error = null;

                var result = _session.UpdatePaletteEntry(name, field, value);

                if (!result.IsEffectiveSuccess)
                {
                    Error = Describe(result);
                }
            });

        EditorFields =
        [
            Field(FieldNames.PaletteFill, "填充", entry.Fill),
            Field(FieldNames.PaletteStroke, "描边", entry.Stroke),
            Field(FieldNames.PaletteText, "文字", entry.Text),
            Field(FieldNames.PaletteWeight, "描边粗细",
                entry.Weight?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)),
        ];

        OnPropertyChanged(nameof(EditorFields));
    }

    private static string Describe(CommandResult result)
    {
        if (result.Errors.Length == 0)
        {
            return result.Message ?? "这一次改动没有写进去";
        }

        // 引用者名单在第一个错误的载荷里——删条目被挡下时，那就是「谁在用」。
        var error = result.Errors[0];

        return string.IsNullOrWhiteSpace(error.Payload) ? error.Code : error.Payload;
    }

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
}

/// <summary>
/// 调色板清单里的一行。
/// </summary>
/// <remarks>
/// 颜色存的是主题解析完的字符串（与绘制指令里的同一格式），控件只负责把它
/// 变成一块看得见的色块——行自己不做任何「令牌是什么意思」的判断。
/// </remarks>
public sealed class PaletteRowViewModel(
    string name,
    string fill,
    string stroke,
    double weight,
    int referrerCount)
{
    /// <summary>令牌名。</summary>
    public string Name { get; } = name;

    /// <summary>解析完的填充色。</summary>
    public string Fill { get; } = fill;

    /// <summary>解析完的描边色。</summary>
    public string Stroke { get; } = stroke;

    /// <summary>解析完的描边粗细。预览的边框用这个粗细画，粗细在预览上才看得出变化。</summary>
    public double Weight { get; } = weight;

    /// <summary>还在用这个令牌的元素个数。零个才删得掉。</summary>
    public int ReferrerCount { get; } = referrerCount;

    /// <summary>有几个元素在用。零时显示空，不写一个"0"出来添乱。</summary>
    public string ReferrersNote => ReferrerCount == 0 ? string.Empty : $"{ReferrerCount} 个在用";
}

/// <summary>
/// 编辑器里的一个成员输入框。
/// </summary>
/// <remarks>
/// 与属性面板的字段视图同一条约定：界面只收文本、把提交交回来，
/// 写不写得进文档由会话回答，错误由面板显示。
/// </remarks>
public sealed class PaletteFieldViewModel(
    string field,
    string label,
    string? value,
    Action<string?> commit)
{
    /// <summary>条目的成员名，与 <see cref="FieldNames"/> 里的调色板字段同名。</summary>
    public string Field { get; } = field;

    /// <summary>界面上显示的名字。</summary>
    public string Label { get; } = label;

    /// <summary>当前值。可能为空——条目允许只有一半成员。</summary>
    public string? Value { get; } = value;

    /// <summary>用户提交了一个新值。</summary>
    public Action<string?> Commit { get; } = commit;
}
