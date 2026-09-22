using System.ComponentModel;

namespace DuetDiagram.App.ViewModels;

/// <summary>
/// 面板上的一个可编辑字段。
/// </summary>
/// <remarks>
/// <para>
/// **它不碰文档。** 用户改完一个值，它只是把值报上去，由宿主走命令层写回去。
/// 它自己写文档的话，"人和 LLM 能力对等"当场不成立——命令层会缺一个入口，
/// 而 LLM 那边没有面板。
/// </para>
/// <para>
/// 值随时可以被外面换掉（换了选中的元素、命令被撤销、编辑被拒），
/// 所以写入前不假设手上的值还是最新的。
/// </para>
/// </remarks>
public sealed class PropertyFieldViewModel : INotifyPropertyChanged
{
    private string? _value;
    private string? _error;
    private bool _isMixed;
    private bool _isReadOnly;

    public PropertyFieldViewModel(PropertyFieldSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        Spec = spec;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>用户改完一个值。</summary>
    /// <remarks>
    /// 参数是字段名与新值。宿主据此构造命令，写不进去时再调 <see cref="Show"/> 把错误挂回来。
    /// </remarks>
    public event Action<PropertyFieldViewModel, string?>? Committed;

    public PropertyFieldSpec Spec { get; }

    /// <summary>字段名，取自字段表。</summary>
    public string Field => Spec.Field;

    /// <summary>界面上显示的名字。</summary>
    public string Label => Spec.Label;

    /// <summary>用哪种控件。</summary>
    public PropertyEditor Editor => Spec.Editor;

    /// <summary>可选取值。选择控件之外的字段为空。</summary>
    public IReadOnlyList<string> Choices => Spec.Choices ?? [];

    /// <summary>选择控件给不给"没有设置"这一档。</summary>
    public bool AllowEmpty => Spec.AllowEmpty;

    /// <summary>当前值，读成文本。没有值时为空。</summary>
    public string? Value
    {
        get => _value;
        private set
        {
            if (string.Equals(_value, value, StringComparison.Ordinal))
            {
                return;
            }

            _value = value;
            Raise(nameof(Value));
        }
    }

    /// <summary>
    /// 选中的多个元素在这个字段上取值不一致。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与"没有值"是两回事，所以单独立一个标志而不是把值填成一句话：
    /// 填成一句话的话，用户改别的字段时那一段字会被当成真值写进文档。
    /// </para>
    /// <para>
    /// 显示成"多个值"而不是随便取其中一个。取一个的话，用户看到的是一个
    /// 只对其中一部分元素成立的值，改别的字段时也看不出这个字段本来就不一致。
    /// </para>
    /// </remarks>
    public bool IsMixed
    {
        get => _isMixed;
        private set
        {
            if (_isMixed == value)
            {
                return;
            }

            _isMixed = value;
            Raise(nameof(IsMixed));
        }
    }

    /// <summary>
    /// 这个字段上一次编辑失败的原因。
    /// </summary>
    /// <remarks>
    /// 挂到字段上而不是面板顶上：一次编辑被拒时，用户要知道的是**哪一个**字段没写进去。
    /// 只给一句全局提示的话，他还得自己猜是刚改的那一个还是别的。
    /// </remarks>
    public string? Error
    {
        get => _error;
        private set
        {
            if (string.Equals(_error, value, StringComparison.Ordinal))
            {
                return;
            }

            _error = value;
            Raise(nameof(Error));
            Raise(nameof(HasError));
        }
    }

    /// <summary>这个字段上有没有没被消掉的错误。</summary>
    public bool HasError => !string.IsNullOrEmpty(_error);

    /// <summary>
    /// 这个字段是不是只读的。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 为真时编辑器被禁用，<see cref="Commit"/> 也不再往外报值。界面禁用与这里再挡一次
    /// 是两件事：禁用控件是为了让人一眼看出改不了，而挡住写入的是 <see cref="Commit"/>——
    /// 编辑器有好几种，失焦、回车、选中项变了各是一条上报路径，
    /// 漏掉哪一种都不会报错，只会让一次改动悄悄落到命令层上。
    /// </para>
    /// <para>
    /// 值照常读进来。只读说的是"改不动"，不是"看不到"——
    /// 字段显示成空的话，用户会以为这份文档里这个字段本来就没值。
    /// </para>
    /// </remarks>
    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            if (_isReadOnly == value)
            {
                return;
            }

            _isReadOnly = value;
            Raise(nameof(IsReadOnly));
        }
    }

    /// <summary>
    /// 换一个值，并清掉旧的错误。
    /// </summary>
    /// <remarks>
    /// 清错误是必要的：这个调用来自"重读一遍文档"，而重读之后上一次的错误
    /// 说的是一个已经不存在的输入。留着它，用户会看到一句与眼前的值对不上的报错。
    /// </remarks>
    public void Set(string? value, bool mixed)
    {
        Value = value;
        IsMixed = mixed;
        Error = null;
    }

    /// <summary>把一条错误挂到这个字段上。</summary>
    public void Show(string? error) => Error = error;

    /// <summary>用户改完了。把值报给宿主。</summary>
    /// <remarks>
    /// 只读时一个值也不往外报。控件那边已经禁用了，但上报路径不止一条（失焦、回车、
    /// 选中项变了），而报上去的那一次会走到命令层再被拒一次——字段上于是留下一句
    /// "这份文档改不了"的提示，与用户在这个字段上敲了什么毫无关系。
    /// </remarks>
    public void Commit(string? value)
    {
        if (IsReadOnly)
        {
            return;
        }

        Committed?.Invoke(this, value);
    }

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
