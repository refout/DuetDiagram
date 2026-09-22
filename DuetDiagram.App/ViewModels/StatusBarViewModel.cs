using System.ComponentModel;
using DuetDiagram.App.Services;
using DuetDiagram.App.ViewModels;

namespace DuetDiagram.App.ViewModels;

/// <summary>
/// 状态栏那一行的状态：左边是光标与缩放，右边是最近一次失败的呈现。
/// </summary>
/// <remarks>
/// <para>
/// 画布的状态（光标、缩放、档位）与失败提示合成一个对象，是因为它们显示在同一行上，
/// 而状态栏只有一个数据上下文。分成两个的话，控件要么挂两个来源，
/// 要么其中一个靠回调去要——两种写法都会让"这一行到底谁说了算"变得含糊。
/// </para>
/// <para>
/// 它**不决定怎么呈现**：呈现方式由错误码查表给出，这里只负责把那一句话与颜色摆上去。
/// 就地判断的话，同一个错误码在两个入口会长出两种样子。
/// </para>
/// </remarks>
public sealed class StatusBarViewModel : INotifyPropertyChanged
{
    private readonly CanvasViewModel _canvas;

    private string _message = string.Empty;
    private StatusSeverity _severity = StatusSeverity.Info;
    private string? _readOnlyNote;

    public StatusBarViewModel(CanvasViewModel canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        _canvas = canvas;
        _canvas.PropertyChanged += OnCanvasChanged;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>当前档位。状态栏直接读它，不另拷一份名字。</summary>
    public RenderModeViewModel Mode => _canvas.Mode;

    /// <summary>光标在文档里的位置。</summary>
    public string PointerText => _canvas.PointerText;

    /// <summary>当前缩放倍数。</summary>
    public string ZoomText => _canvas.ZoomText;

    /// <summary>要显示的一句话。没有失败时为空。</summary>
    public string Message => _message;

    /// <summary>这句话的语气：普通、灰显（无操作）还是报错。</summary>
    public StatusSeverity Severity => _severity;

    /// <summary>有没有一句话要显示。</summary>
    public bool HasMessage => _message.Length > 0;

    /// <summary>
    /// 只读那一句常驻说明。可写时为空。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 它和上面那句话分开，因为两者的寿命完全不同：那句话是一次操作的结果，
    /// 下一次成功或重排就会被清掉；这一句说的是这份文档的状态，从打开到关掉一直成立。
    /// 合成一句话的话，改不动东西时用户会以为是自己刚做错了什么，
    /// 而真正的原因——这份文档是别人的——只在打开那一刻闪现过一次。
    /// </para>
    /// <para>
    /// 界面上的写入口另外会被禁掉。只留一句话而不禁控件的话，用户会一次次去点，
    /// 每次都得到同一句提示，而他不知道自己什么时候才能点。
    /// </para>
    /// </remarks>
    public string? ReadOnlyNote => _readOnlyNote;

    /// <summary>有没有那句常驻说明。</summary>
    public bool HasReadOnlyNote => !string.IsNullOrEmpty(_readOnlyNote);

    /// <summary>摆上（或撤掉）只读那句常驻说明。</summary>
    public void SetReadOnly(string? note)
    {
        if (string.Equals(_readOnlyNote, note, StringComparison.Ordinal))
        {
            return;
        }

        _readOnlyNote = note;

        Raise(nameof(ReadOnlyNote));
        Raise(nameof(HasReadOnlyNote));
    }

    /// <summary>按呈现方式把一句话摆上状态栏。</summary>
    public void Show(ErrorPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        _message = string.IsNullOrEmpty(presentation.Detail)
            ? presentation.Message
            : $"{presentation.Message}（{presentation.Detail}）";
        _severity = presentation.Kind switch
        {
            ErrorPresentationKind.StatusBarMuted => StatusSeverity.Muted,
            ErrorPresentationKind.StatusBar or ErrorPresentationKind.LayoutFailureDialog => StatusSeverity.Error,
            _ => StatusSeverity.Info,
        };

        Raise(nameof(Message));
        Raise(nameof(Severity));
        Raise(nameof(HasMessage));
    }

    /// <summary>清掉状态栏上的那句话。</summary>
    public void Clear()
    {
        if (_message.Length == 0)
        {
            return;
        }

        _message = string.Empty;

        Raise(nameof(Message));
        Raise(nameof(HasMessage));
    }

    private void OnCanvasChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CanvasViewModel.PointerText))
        {
            Raise(nameof(PointerText));
        }
        else if (e.PropertyName is nameof(CanvasViewModel.ZoomText))
        {
            Raise(nameof(ZoomText));
        }
    }

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>状态栏那句话的语气。</summary>
public enum StatusSeverity
{
    /// <summary>普通提示。</summary>
    Info,

    /// <summary>灰显。无操作的失败走这一档，不弹窗。</summary>
    Muted,

    /// <summary>报错。红色。</summary>
    Error,
}
