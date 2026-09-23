using System.ComponentModel;
using DuetDiagram.Core.Model;

namespace DuetDiagram.App.ViewModels;

/// <summary>
/// 标签栏上的一个页签。
/// </summary>
/// <remarks>
/// 行按标识认，不按列表位置——删一页之后位置会变，而"我点的是哪一页"不能跟着变。
/// </remarks>
public sealed class PageTabViewModel : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private bool _isCurrent;

    public PageTabViewModel(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Id = id;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>页面标识。</summary>
    public string Id { get; }

    /// <summary>页面上显示的名字。没起名字时为空。</summary>
    public string Name
    {
        get => _name;
        private set => Set(ref _name, value, nameof(Name));
    }

    /// <summary>是不是正在看的那一页。</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        private set => Set(ref _isCurrent, value, nameof(IsCurrent));
    }

    /// <summary>把文档里的那一份推进来。</summary>
    public void Set(PageDef page, bool current)
    {
        ArgumentNullException.ThrowIfNull(page);

        Name = page.Name;
        IsCurrent = current;
    }

    /// <summary>只换"是不是当前页"。</summary>
    /// <remarks>
    /// 改当前页的是会话上的状态、不是文档，所以单独一个方法：走
    /// <see cref="Set"/> 的话调用方要先从文档里把这一页捞出来，而它一个字段都没变。
    /// </remarks>
    public void SetCurrent(bool current) => IsCurrent = current;

    private void Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
