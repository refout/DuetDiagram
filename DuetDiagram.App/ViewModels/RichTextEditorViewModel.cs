using System.ComponentModel;
using System.Globalization;
using DuetDiagram.App.Interaction;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;

namespace DuetDiagram.App.ViewModels;

/// <summary>
/// 标签编辑器的状态：草稿的纯文本、当前选区、六项行内样式按钮，以及进出编辑的那两步。
/// </summary>
/// <remarks>
/// <para>
/// **它改的是会话里的草稿，不是文档。** 提交那一步才走命令层，所以只读门与版本检查
/// 都在那一次过——编辑控件本身没有写文档的能力。
/// </para>
/// <para>
/// **选区由控件报进来。** 编辑控件每次选区变化都把起点与长度写进
/// <see cref="SelectionStart"/> / <see cref="SelectionLength"/>，样式按钮读这两个数。
/// 让样式按钮自己去问控件的话，它得先拿到控件，而状态对象不该认识控件。
/// </para>
/// <para>
/// **没有选区时按钮不做任何事。** "作用于随后输入的文字"由编辑控件自己那套输入法管，
/// 它落在草稿上的表现就是新敲的字继承光标左边那一份样式（见 <see cref="TextEditSession.SetText"/>）。
/// </para>
/// </remarks>
public sealed class RichTextEditorViewModel : INotifyPropertyChanged
{
    private readonly DiagramSession _session;

    // 上一次看到的草稿。换了一次编辑（或者退出编辑）就与当前的对不上，
    // 那一刻才把选区归零。
    private TextEditSession? _sessionSeen;

    private string _text = string.Empty;
    private int _selectionStart;
    private int _selectionLength;
    private string? _error;
    private double _left;
    private double _top;
    private double _width;
    private double _height;

    public RichTextEditorViewModel(DiagramSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _session.TextEditChanged += Refresh;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>编辑器开着没有。</summary>
    public bool IsOpen => _session.TextEdit is not null;

    /// <summary>正在编辑的节点标识。没开时为空。</summary>
    public string? NodeId => _session.TextEdit?.NodeId;

    /// <summary>草稿的纯文本。编辑控件双向绑它。</summary>
    public string Text
    {
        get => _text;

        set
        {
            if (string.Equals(_text, value, StringComparison.Ordinal))
            {
                return;
            }

            _text = value;
            _session.SetTextEditText(value);
            Changed(nameof(Text));
        }
    }

    /// <summary>当前选区的起点。由编辑控件报进来。</summary>
    public int SelectionStart
    {
        get => _selectionStart;

        set
        {
            if (_selectionStart == value)
            {
                return;
            }

            _selectionStart = value;
            Changed(nameof(SelectionStart));
        }
    }

    /// <summary>当前选区的长度。由编辑控件报进来。</summary>
    public int SelectionLength
    {
        get => _selectionLength;

        set
        {
            if (_selectionLength == value)
            {
                return;
            }

            _selectionLength = value;
            Changed(nameof(SelectionLength));
        }
    }

    /// <summary>最近一次失败的一句话。成功提交之后清空。</summary>
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
            Changed(nameof(Error));
            Changed(nameof(HasError));
        }
    }

    /// <summary>有没有错误要显示。</summary>
    public bool HasError => !string.IsNullOrEmpty(_error);

    /// <summary>编辑器左上角在画布上的横坐标。</summary>
    public double Left => _left;

    /// <summary>编辑器左上角在画布上的纵坐标。</summary>
    public double Top => _top;

    /// <summary>编辑器宽度。取排版结果那一块。</summary>
    public double Width => _width;

    /// <summary>编辑器高度。取排版结果那一块。</summary>
    public double Height => _height;

    #region 样式

    /// <summary>加粗。有选区时按选区套，没有选区时什么都不做。</summary>
    public void Bold() => Style(RichStyleFields.Bold, Toggle(RichStyleFields.Bold));

    /// <summary>斜体。</summary>
    public void Italic() => Style(RichStyleFields.Italic, Toggle(RichStyleFields.Italic));

    /// <summary>下划线。</summary>
    public void Underline() => Style(RichStyleFields.Underline, Toggle(RichStyleFields.Underline));

    /// <summary>删除线。</summary>
    public void Strikethrough() => Style(RichStyleFields.Strikethrough, Toggle(RichStyleFields.Strikethrough));

    /// <summary>字号。传空或非数表示去掉这一项。</summary>
    public void FontSize(string? value) => Style(RichStyleFields.FontSize, value);

    /// <summary>文字颜色。传空表示去掉这一项。</summary>
    public void Color(string? value) => Style(RichStyleFields.Color, value);

    /// <summary>
    /// 这一项在选区上是不是已经全开着。
    /// </summary>
    /// <remarks>
    /// 判据是"选区里每一个字都带着它"，而不是只看第一个字：只看第一个字的话，
    /// 一段半粗半不粗的文字上按一下按钮，得到的是"把剩下那半也加粗"还是"全取消"
    /// 取决于第一个字，而用户看不出为什么。
    /// </remarks>
    private bool IsOn(string field)
    {
        var session = _session.TextEdit;

        if (session is null || _selectionLength <= 0)
        {
            return false;
        }

        for (var index = _selectionStart; index < _selectionStart + _selectionLength; index++)
        {
            if (!On(session.StyleAt(index), field))
            {
                return false;
            }
        }

        return true;
    }

    private static bool On(RichRunStyle? style, string field) => field switch
    {
        RichStyleFields.Bold => style?.Bold == true,
        RichStyleFields.Italic => style?.Italic == true,
        RichStyleFields.Underline => style?.Underline == true,
        RichStyleFields.Strikethrough => style?.Strikethrough == true,
        _ => false,
    };

    /// <summary>按钮要写的值：已经全开着就关掉，否则打开。</summary>
    private string Toggle(string field) => IsOn(field) ? "false" : "true";

    private void Style(string field, string? value)
    {
        _session.ApplyTextStyle(_selectionStart, _selectionLength, field, value);
        Error = null;

        // 样式改的是草稿，通知一遍让按钮的高亮跟着换。
        Changed(nameof(Text));
    }

    #endregion

    #region 进出编辑

    /// <summary>
    /// 打开编辑器。
    /// </summary>
    /// <param name="left">编辑器左上角在画布上的横坐标。</param>
    /// <param name="top">编辑器左上角在画布上的纵坐标。</param>
    /// <param name="width">宽度。</param>
    /// <param name="height">高度。</param>
    public void Open(double left, double top, double width, double height)
    {
        _left = left;
        _top = top;
        _width = width;
        _height = height;
        Error = null;

        Changed(nameof(Left));
        Changed(nameof(Top));
        Changed(nameof(Width));
        Changed(nameof(Height));
    }

    /// <summary>取消这次编辑，草稿丢掉，文档不动。</summary>
    public void Cancel() => _session.CancelTextEdit();

    /// <summary>提交这次编辑。整次编辑算一条命令。</summary>
    public void Commit()
    {
        var result = _session.CommitTextEdit();

        if (!result.IsSuccess)
        {
            // 提交失败（只读、锁着、节点没了）时编辑器**不关**：
            // 关掉的话用户刚敲的那一段就没了，而它其实还没写进去。
            Error = Describe(result);
        }
    }

    private static string Describe(CommandResult result) =>
        result.Errors.Length > 0
            ? string.Join("；", result.Errors.Select(error => error.Payload ?? error.Code))
            : "这次编辑没有写进去";

    #endregion

    private void Refresh()
    {
        var session = _session.TextEdit;

        // 选区只在换了一次编辑时归零。每敲一个字都归零的话，控件刚报上来的选区
        // 会被抹掉，而样式按钮读的正是它——加粗一个词会变成什么都没做。
        if (!ReferenceEquals(_sessionSeen, session))
        {
            _sessionSeen = session;
            _selectionStart = 0;
            _selectionLength = 0;
            Changed(nameof(SelectionStart));
            Changed(nameof(SelectionLength));
        }

        _text = session?.Text ?? string.Empty;

        Changed(nameof(IsOpen));
        Changed(nameof(NodeId));
        Changed(nameof(Text));
    }

    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>字号那一栏的初值。按当前选区取，取不到就用节点的字号。</summary>
    public string FontSizeText
    {
        get
        {
            var session = _session.TextEdit;

            if (session is null || _selectionLength <= 0)
            {
                return string.Empty;
            }

            var size = session.StyleAt(_selectionStart)?.FontSize;

            return size is null ? string.Empty : size.Value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
