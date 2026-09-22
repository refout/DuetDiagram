using System.ComponentModel;

namespace DuetDiagram.App.ViewModels;

/// <summary>
/// 面板上的一节。
/// </summary>
/// <remarks>
/// <para>
/// 一节里要么是可编辑的字段，要么是一段只读的文字，不会两者都有。
/// 只读的那两节（端口、动作与链接）现在只能看，理由写在
/// <see cref="PropertyPanelViewModel"/> 的说明里。布局约束那一节两样都不是：
/// 它是一个可增删的列表，由 <see cref="ConstraintEditorViewModel"/> 给出内容，
/// 面板按标题认出它来另搭控件。
/// </para>
/// <para>
/// **只读内容做成一段文字而不是一串行控件。** 行的条数随选中的元素变，
/// 做成控件列表就得在每次换选中时增删控件，而那正是"切换选中不重建树"要避免的事。
/// 一段文字换个字符串就够了。
/// </para>
/// </remarks>
public sealed class PropertySectionViewModel : INotifyPropertyChanged
{
    private string? _text;

    public PropertySectionViewModel(string title, IReadOnlyList<PropertyFieldViewModel> fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(fields);

        Title = title;
        Fields = fields;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>这一节的标题。</summary>
    public string Title { get; }

    /// <summary>这一节里的可编辑字段。只读的那几节是空的。</summary>
    public IReadOnlyList<PropertyFieldViewModel> Fields { get; }

    /// <summary>这一节的只读内容。有可编辑字段时为空。</summary>
    public string? Text
    {
        get => _text;
        private set
        {
            if (string.Equals(_text, value, StringComparison.Ordinal))
            {
                return;
            }

            _text = value;
            Raise(nameof(Text));
            Raise(nameof(HasText));
        }
    }

    /// <summary>这一节有没有只读内容。</summary>
    public bool HasText => !string.IsNullOrEmpty(_text);

    /// <summary>这一节是不是只读的。</summary>
    public bool IsReadOnly => Fields.Count == 0;

    /// <summary>换掉这一节的只读内容。</summary>
    public void SetText(string? text) => Text = text;

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
