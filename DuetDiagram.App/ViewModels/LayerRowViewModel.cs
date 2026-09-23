using System.ComponentModel;
using DuetDiagram.Core.Model;

namespace DuetDiagram.App.ViewModels;

/// <summary>
/// 图层面板上的一行。
/// </summary>
/// <remarks>
/// <para>
/// **值只从文档来。** 面板上显示的名字、开关状态与元素个数都是文档里的那一份，
/// 用户点一下之后先去命令层，再由文档那一侧回流过来。控件自己记一份"刚点成了什么样"的话，
/// 命令被拒时界面会停在用户期望的样子，而实际没改成——两边都得再看一遍才知道谁对。
/// </para>
/// <para>
/// 一个属性一个属性地推给控件（见 <c>FieldEditBinder.Watch</c>），整行不重建：
/// 重建会让用户正在输入的那个框失焦，而挪动次序时正需要它保持焦点。
/// </para>
/// </remarks>
public sealed class LayerRowViewModel : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private bool _visible = true;
    private bool _locked;
    private bool _isCurrent;
    private int _elementCount;
    private bool _canMoveUp;
    private bool _canMoveDown;

    public LayerRowViewModel(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Id = id;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>图层标识。行是按它认的，不按列表位置——切换选中时位置会变。</summary>
    public string Id { get; }

    public string Name
    {
        get => _name;
        private set => Set(ref _name, value, nameof(Name));
    }

    /// <summary>这一层画不画。</summary>
    public bool Visible
    {
        get => _visible;
        private set => Set(ref _visible, value, nameof(Visible));
    }

    /// <summary>这一层能不能改。锁着的那一层照常画出来，但点不中、改不了。</summary>
    public bool Locked
    {
        get => _locked;
        private set => Set(ref _locked, value, nameof(Locked));
    }

    /// <summary>这一行是不是"当前图层"：新建的元素与「移入」都朝它走。</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        private set => Set(ref _isCurrent, value, nameof(IsCurrent));
    }

    /// <summary>这一层上有几个元素。纯展示，不进任何哈希。</summary>
    public int ElementCount
    {
        get => _elementCount;
        private set
        {
            if (Set(ref _elementCount, value, nameof(ElementCount)))
            {
                Raise(nameof(ElementCountText));
            }
        }
    }

    /// <summary>元素个数写成一句话。</summary>
    public string ElementCountText => ElementCount == 0 ? "空" : $"{ElementCount} 个元素";

    /// <summary>能不能往上挪。已经在最上面时不能。</summary>
    public bool CanMoveUp
    {
        get => _canMoveUp;
        private set => Set(ref _canMoveUp, value, nameof(CanMoveUp));
    }

    /// <summary>能不能往下挪。已经在最下面时不能。</summary>
    public bool CanMoveDown
    {
        get => _canMoveDown;
        private set => Set(ref _canMoveDown, value, nameof(CanMoveDown));
    }

    /// <summary>把文档里的那一份推进来。</summary>
    /// <param name="layer">文档里的这个图层。</param>
    /// <param name="current">它是不是当前图层。</param>
    /// <param name="elements">这一层上有几个元素。</param>
    /// <param name="canMoveUp">它上面还有没有别的图层。</param>
    /// <param name="canMoveDown">它下面还有没有别的图层。</param>
    public void Set(LayerDef layer, bool current, int elements, bool canMoveUp, bool canMoveDown)
    {
        ArgumentNullException.ThrowIfNull(layer);

        Name = layer.Name;
        Visible = layer.Visible;
        Locked = layer.Locked;
        IsCurrent = current;
        ElementCount = elements;
        CanMoveUp = canMoveUp;
        CanMoveDown = canMoveDown;
    }

    /// <summary>
    /// 换一下"是不是当前图层"。
    /// </summary>
    /// <remarks>
    /// 单独一个方法，因为改当前图层的是面板自己的状态、不是文档。
    /// 走 <see cref="Set"/> 的话，调用方要先从文档里把这一行的那一份捞出来，
    /// 而它本来一个字段都没变。
    /// </remarks>
    public void SetCurrent(bool current) => IsCurrent = current;

    private bool Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);

        return true;
    }

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
