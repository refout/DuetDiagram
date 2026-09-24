using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using DuetDiagram.App.ViewModels;

namespace DuetDiagram.App.Controls;

/// <summary>
/// 就地编辑一个节点的标签：一段多行文字，加上六项行内样式。
/// </summary>
/// <remarks>
/// <para>
/// **它只改会话里的草稿。** 改字、套样式都只落在草稿上，退出时才由状态对象发一条命令。
/// 每敲一个字发一条命令的话，撤销栈会被一次编辑灌满，而用户眼里那是一次编辑。
/// </para>
/// <para>
/// **选区由控件报上去。** 样式按钮读的是状态对象里那两个数，不是控件自己——
/// 状态对象不认识控件，而按钮的动作要能在没有界面的情况下被走到。
/// </para>
/// <para>
/// **退出即提交，除非明确取消。** Esc 取消、点别处提交。点别处那一下由窗口的隧道处理器
/// 判定并转过来，不在这里靠失焦判断：输入法的候选窗口会让编辑框短暂失焦，
/// 靠失焦判断的话，打一半的拼音会被当成"用户走了"而提交出去。
/// </para>
/// </remarks>
public sealed partial class RichTextEditor : UserControl
{
    /// <summary>编辑器最少这么宽。比这更窄的话，那一排样式按钮会折成两行。</summary>
    private const double MinEditorWidth = 220;

    private RichTextEditorViewModel? _model;

    // 正在把状态对象上的值写回控件。这一段里控件报出来的变化都是回声，不能当成用户输入。
    private bool _syncing;

    public RichTextEditor()
    {
        InitializeComponent();

        BoldButton.Click += (_, _) => Apply(model => model.Bold());
        ItalicButton.Click += (_, _) => Apply(model => model.Italic());
        UnderlineButton.Click += (_, _) => Apply(model => model.Underline());
        StrikethroughButton.Click += (_, _) => Apply(model => model.Strikethrough());

        FontSizeBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;

            // 先把用户敲的那个数收下来再报选区：报选区会把这一栏刷成当前值，
            // 之后再读就成了一次没打算做的改动。
            var value = FontSizeBox.Text;

            Apply(model => model.FontSize(value));
        };

        ColorBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;

            var value = ColorBox.Text;

            Apply(model => model.Color(value));
        };

        TextArea.TextChanged += (_, _) =>
        {
            if (!_syncing && _model is { } model)
            {
                model.Text = TextArea.Text ?? string.Empty;
            }
        };

        // 选区变化只由这两个属性报出来，控件没有单独的选区事件。
        TextArea.PropertyChanged += (_, e) =>
        {
            if (!_syncing
                && (e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty))
            {
                Report();
            }
        };

        TextArea.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
            {
                return;
            }

            e.Handled = true;
            CancelRequested?.Invoke();
        };

        CancelButton.Click += (_, _) => CancelRequested?.Invoke();
        CommitButton.Click += (_, _) => RequestCommit();

        AutomationProperties.SetAutomationId(BoldButton, "richtext.bold");
        AutomationProperties.SetAutomationId(ItalicButton, "richtext.italic");
        AutomationProperties.SetAutomationId(UnderlineButton, "richtext.underline");
        AutomationProperties.SetAutomationId(StrikethroughButton, "richtext.strikethrough");
        AutomationProperties.SetAutomationId(FontSizeBox, "richtext.font-size");
        AutomationProperties.SetAutomationId(ColorBox, "richtext.color");
        AutomationProperties.SetAutomationId(TextArea, "richtext.text");
        AutomationProperties.SetAutomationId(ErrorText, "richtext.error");
        AutomationProperties.SetAutomationId(CancelButton, "richtext.cancel");
        AutomationProperties.SetAutomationId(CommitButton, "richtext.commit");
    }

    /// <summary>用户按了取消。草稿丢掉，文档不动。</summary>
    public event Action? CancelRequested;

    /// <summary>用户按了完成。</summary>
    public event Action? CommitRequested;

    /// <summary>编辑器开着没有。</summary>
    public bool IsOpen => _model?.IsOpen == true;

    /// <summary>
    /// 接上一个状态对象。接上之后控件自己跟着它变，宿主不必再推。
    /// </summary>
    /// <remarks>
    /// 换一个对象时把旧的那份摘掉。留着的话，上一次编辑的状态对象会继续把变化推过来，
    /// 而编辑器已经显示另一次编辑的内容了。
    /// </remarks>
    public void Attach(RichTextEditorViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
        }

        _model = model;
        _model.PropertyChanged += OnModelChanged;

        Sync();
    }

    /// <summary>
    /// 提交这次编辑。
    /// </summary>
    /// <remarks>
    /// 提交前先把编辑框里当前的文字收上来。宿主在"点了别处"那条路上也走这里，
    /// 而不是直接叫状态对象提交——直接叫的话，用户最后敲的那几个字还留在编辑框里
    /// 没报上去，提交出去的就是上一次的文字。
    /// </remarks>
    public void RequestCommit()
    {
        Report();
        CommitRequested?.Invoke();
    }

    /// <summary>把焦点交给编辑框，光标落在末尾。</summary>
    /// <remarks>
    /// 先排一帧再要焦点。编辑器刚摆出来时它还没被排布过，而没排布过的控件拿不到焦点——
    /// 焦点管理器要求目标已经接进视觉树，而那一层正是排布时才建起来的。
    /// 不排这一帧的话，用户点进来还得再点一下编辑框才能打字。
    /// </remarks>
    public void FocusText()
    {
        this.GetLayoutManager()?.ExecuteLayoutPass();

        TextArea.Focus();
        TextArea.CaretIndex = TextArea.Text?.Length ?? 0;
    }

    /// <summary>这一点在不在编辑器里面。</summary>
    /// <remarks>
    /// 窗口用它判断"用户是不是点了别处"。按视觉树判：编辑框、样式按钮、
    /// 错误那一行都在这一棵里，而画布与各个面板不在。
    /// </remarks>
    public bool Contains(Visual? visual) =>
        visual is not null && (ReferenceEquals(visual, this) || this.IsVisualAncestorOf(visual));

    /// <summary>
    /// 按一下样式按钮：先把编辑框当下的文字与选区收上来，再动手。
    /// </summary>
    /// <remarks>
    /// 收一次的理由见 <see cref="Report"/>：按钮读的是状态对象里那两个数，
    /// 而那两个数未必已经跟上编辑框里此刻的样子。
    /// </remarks>
    private void Apply(Action<RichTextEditorViewModel> action)
    {
        if (_model is not { } model)
        {
            return;
        }

        Report();
        action(model);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Sync();

    /// <summary>
    /// 把编辑框里当前的文字与选区报上去。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **文字与选区一起报。** 编辑框把文字变化留到下一轮消息里才发出来，所以
    /// "改完字马上按完成"那条路上，状态对象手里还是上一次的文字——报一次，
    /// 读到的才是用户按下按钮那一刻屏幕上写着的东西。
    /// </para>
    /// <para>
    /// **选区那边更要紧。** 编辑框是按单个属性报选区的，而起点与终点是两次变化：
    /// 只等它们各自报完的话，按钮读到的可能是两次变化中间那个半截选区——
    /// 起点已经挪了、终点还没跟上，长度于是为零，而按钮看长度为零就什么都不做。
    /// </para>
    /// </remarks>
    private void Report()
    {
        if (_model is not { IsOpen: true } model)
        {
            return;
        }

        model.Text = TextArea.Text ?? string.Empty;
        model.SelectionStart = TextArea.SelectionStart;
        model.SelectionLength = Math.Max(0, TextArea.SelectionEnd - TextArea.SelectionStart);

        // 字号那一栏跟着选区显示当前值。不跟着换的话，选中一段 20 号字再点别的，
        // 那一栏还留着上一次输入的数字，而它已经不表示任何东西了。
        _syncing = true;

        try
        {
            FontSizeBox.Text = model.FontSizeText;
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// 把状态对象上的值写到控件上。
    /// </summary>
    /// <remarks>
    /// 文字只在真的不同时才写回。无条件写的话，每敲一个字都会把编辑框的内容重设一遍，
    /// 光标会跳到末尾——而用户正在中间改一个错别字。
    /// </remarks>
    private void Sync()
    {
        if (_model is null)
        {
            return;
        }

        _syncing = true;

        try
        {
            IsVisible = _model.IsOpen;

            Canvas.SetLeft(this, _model.Left);
            Canvas.SetTop(this, _model.Top);

            // 尺寸取排版结果那一块，但不小于工具栏放得下的宽度。
            MinWidth = Math.Max(_model.Width, MinEditorWidth);
            MinHeight = _model.Height;

            var text = _model.Text;

            if (!string.Equals(TextArea.Text, text, StringComparison.Ordinal))
            {
                TextArea.Text = text;
                TextArea.CaretIndex = text.Length;
            }

            ErrorText.Text = _model.Error ?? string.Empty;
            ErrorText.IsVisible = _model.HasError;
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// 取一次界面标记里那几个带名字的控件。
    /// </summary>
    /// <remarks>
    /// 名字对应的字段由界面标记生成器声明，而给它赋值的那一份 <c>InitializeComponent</c>
    /// 只在类里没有同名方法时才会生成。这里手写了这一个，所以那一份不再生成——
    /// 少掉的正是赋值那一步，字段会一直是空的，而编译期看不出任何异常。
    /// </remarks>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        BoldButton = Required<Button>(nameof(BoldButton));
        ItalicButton = Required<Button>(nameof(ItalicButton));
        UnderlineButton = Required<Button>(nameof(UnderlineButton));
        StrikethroughButton = Required<Button>(nameof(StrikethroughButton));
        FontSizeBox = Required<TextBox>(nameof(FontSizeBox));
        ColorBox = Required<TextBox>(nameof(ColorBox));
        TextArea = Required<TextBox>(nameof(TextArea));
        ErrorText = Required<TextBlock>(nameof(ErrorText));
        CancelButton = Required<Button>(nameof(CancelButton));
        CommitButton = Required<Button>(nameof(CommitButton));
    }

    private T Required<T>(string name)
        where T : Control =>
        this.FindControl<T>(name)
            ?? throw new InvalidOperationException($"标签编辑器里没有名为 {name} 的控件");
}
