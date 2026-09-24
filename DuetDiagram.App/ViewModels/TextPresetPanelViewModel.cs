using System.ComponentModel;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;

namespace DuetDiagram.App.ViewModels;

/// <summary>
/// 文本预设面板的状态：预设清单、选中预设的成员编辑器，以及新建、删除与应用到选中。
/// </summary>
/// <remarks>
/// <para>
/// 与调色板面板同一套接线：清单随 SceneChanged 重读（不接 DocumentChanged，
/// 它在后台线程上到），行在预设增删时重建，成员编辑器只在换选中时重建——
/// 文档一变就重建的话，用户正在输入的那个框会失焦。
/// </para>
/// <para>
/// **「应用到选中」是这里唯一改元素的入口。** 面板其余部分改的都是预设自己；
/// 把选中的元素改成某个预设是一次批量应用，一条命令进一次历史，
/// 撤销按一次就把这一批全部还原。
/// </para>
/// </remarks>
public sealed class TextPresetPanelViewModel : INotifyPropertyChanged
{
    private readonly DiagramSession _session;
    private readonly List<TextPresetRowViewModel> _rows = [];

    private string? _selectedId;
    private string _newName = string.Empty;
    private string? _error;

    public TextPresetPanelViewModel(DiagramSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;

        _session.SceneChanged += Refresh;

        Refresh();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>预设行，按标识的字典序。与视觉哈希同一条排序口径。</summary>
    public IReadOnlyList<TextPresetRowViewModel> Rows => _rows;

    /// <summary>选中预设的标识。没有选中时为空。</summary>
    public string? SelectedId
    {
        get => _selectedId;

        private set
        {
            if (string.Equals(_selectedId, value, StringComparison.Ordinal))
            {
                return;
            }

            _selectedId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(EditorVisible));
        }
    }

    /// <summary>有没有选中的预设。编辑器与应用按钮的可用性跟着它走。</summary>
    public bool HasSelection => _selectedId is not null;

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

    /// <summary>选中预设的可改成员，按界面上从上到下的次序。</summary>
    public IReadOnlyList<PaletteFieldViewModel> EditorFields { get; private set; } = [];

    /// <summary>当前画布上有没有选中的节点。没有的话「应用到选中」点了也白点。</summary>
    public bool HasNodeSelection => _session.SelectedNodes.Count > 0;

    /// <summary>按标识选一行。界面上点行时调，测试也这样进来。</summary>
    public void Select(string? id)
    {
        SelectedId = _rows.Any(row => string.Equals(row.Id, id, StringComparison.Ordinal))
            ? id
            : null;
        RebuildEditor();
    }

    /// <summary>定义一个新预设，样式从空开始。名为空、标识撞车都由命令层挡，错误原样摆到面板上。</summary>
    public void Define()
    {
        Error = null;

        var result = _session.DefineTextPreset(NewName.Trim());

        if (result.IsEffectiveSuccess)
        {
            NewName = string.Empty;
            Refresh();

            if (result.AffectedIds.Length > 0)
            {
                Select(result.AffectedIds[0]);
            }

            return;
        }

        Error = Describe(result);
        Refresh();
    }

    /// <summary>删掉选中的预设。</summary>
    /// <remarks>
    /// 应用是按值把成员抄到节点上的，删除不需要查引用——与调色板删条目的处境不同。
    /// 命令层只查"存不存在"，失败原因原样摆出来。
    /// </remarks>
    public void RemoveSelected()
    {
        Error = null;

        if (SelectedId is not { } id)
        {
            return;
        }

        var result = _session.RemoveTextPreset(id);

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

    /// <summary>把选中的预设应用到画布上选中的节点。批量是一条命令，撤销按一次全还原。</summary>
    public void ApplyToSelection()
    {
        Error = null;

        if (SelectedId is not { } id)
        {
            return;
        }

        var result = _session.ApplyTextPreset(id);

        if (!result.IsEffectiveSuccess && !result.IsNoOp)
        {
            Error = Describe(result);
        }
        else if (result.IsNoOp)
        {
            Error = result.Message;
        }
    }

    /// <summary>按当前文档重读一遍清单。</summary>
    public void Refresh()
    {
        _rows.Clear();

        foreach (var preset in _session.Document.TextPresets.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            _rows.Add(new TextPresetRowViewModel(preset.Id, preset.Name, Summarize(preset)));
        }

        // 选中的预设被别的窗口删掉时，选中跟着落空——留着它会让编辑器
        // 对着一个已经不存在的预设写字。
        if (_selectedId is { } selected
            && _rows.All(row => !string.Equals(row.Id, selected, StringComparison.Ordinal)))
        {
            Select(null);
        }

        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(HasNodeSelection));
    }

    private static string Summarize(TextStylePreset preset)
    {
        var parts = new List<string>(3);

        if (preset.Style.FontFamily is { } family)
        {
            parts.Add(family);
        }

        if (preset.Style.FontSize is { } size)
        {
            parts.Add(size.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
        }

        if (preset.Style.FontWeight is { } weight)
        {
            parts.Add(weight.ToString());
        }

        return parts.Count == 0 ? "空样式" : string.Join(" · ", parts);
    }

    private void RebuildEditor()
    {
        if (SelectedId is not { } id
            || _session.Document.TextPresets.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal))
                is not { } preset)
        {
            EditorFields = [];

            OnPropertyChanged(nameof(EditorFields));

            return;
        }

        // 提交直接对准预设的那个成员：一次一条命令，与命令层「一次只改一个字段」同形状。
        // 输入框这一格与调色板编辑器共用同一个视图模型——它只是"标签 + 当前值 + 提交"，
        // 不含任何调色板特有的判断。
        PaletteFieldViewModel Field(string field, string label, string? value) =>
            new(field, label, value, value =>
            {
                Error = null;

                var result = _session.UpdateTextPreset(id, field, value);

                if (!result.IsEffectiveSuccess)
                {
                    Error = Describe(result);
                }
            });

        EditorFields =
        [
            Field(FieldNames.PresetFontFamily, "字体", TextPresetFieldValue.Read(preset, FieldNames.PresetFontFamily)),
            Field(FieldNames.PresetFontSize, "字号", TextPresetFieldValue.Read(preset, FieldNames.PresetFontSize)),
            Field(FieldNames.PresetFontWeight, "字重", TextPresetFieldValue.Read(preset, FieldNames.PresetFontWeight)),
            Field(FieldNames.PresetItalic, "斜体", TextPresetFieldValue.Read(preset, FieldNames.PresetItalic)),
            Field(FieldNames.PresetUnderline, "下划线", TextPresetFieldValue.Read(preset, FieldNames.PresetUnderline)),
            Field(FieldNames.PresetStrikethrough, "删除线", TextPresetFieldValue.Read(preset, FieldNames.PresetStrikethrough)),
            Field(FieldNames.PresetFontColor, "文字颜色", TextPresetFieldValue.Read(preset, FieldNames.PresetFontColor)),
            Field(FieldNames.PresetAlign, "水平对齐", TextPresetFieldValue.Read(preset, FieldNames.PresetAlign)),
        ];

        OnPropertyChanged(nameof(EditorFields));
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
/// 文本预设清单里的一行。
/// </summary>
/// <remarks>
/// 摘要只列声明了的那几个成员（字体、字号、字重），没有的写"空样式"——
/// 一个刚建好还没填成员的预设与填好的看起来是两种东西。
/// </remarks>
public sealed class TextPresetRowViewModel(string id, string name, string summary)
{
    /// <summary>预设标识。行按它被选中、被应用。</summary>
    public string Id { get; } = id;

    /// <summary>显示名。</summary>
    public string Name { get; } = name;

    /// <summary>样式的摘要。</summary>
    public string Summary { get; } = summary;
}
