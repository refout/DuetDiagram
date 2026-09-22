using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using DuetDiagram.App.ViewModels;

namespace DuetDiagram.App.Controls;

/// <summary>
/// 状态栏：光标位置、最近一次失败的提示、当前档位与缩放。
/// </summary>
/// <remarks>
/// <para>
/// 提示的颜色由 <see cref="StatusSeverity"/> 决定：报错红、无操作灰、其余常规。
/// 无操作的失败（例如无可撤销）灰显而不弹窗——弹窗打断的是整条操作链，
/// 而这类失败本来就没有需要用户决策的事。
/// </para>
/// <para>
/// 档位那一段直接读画布的档位对象。拷一份名字过来的话，档位切换时这里不会跟着变，
/// 而状态栏显示的是一个早已过期的档位名。
/// </para>
/// </remarks>
public sealed partial class StatusBar : UserControl
{
    private static readonly IBrush ErrorBrush = Brush.Parse("#c4314b");
    private static readonly IBrush MutedBrush = Brush.Parse("#8a94a3");
    private static readonly IBrush NormalBrush = Brush.Parse("#5b6675");

    private StatusBarViewModel? _status;

    public StatusBar() => InitializeComponent();

    /// <summary>状态栏的数据来源。换一份就换一整行的内容。</summary>
    public StatusBarViewModel? Status
    {
        get => _status;
        set
        {
            if (ReferenceEquals(_status, value))
            {
                return;
            }

            if (_status is not null)
            {
                _status.PropertyChanged -= OnStatusChanged;
                _status.Mode.PropertyChanged -= OnStatusChanged;
            }

            _status = value;

            if (_status is not null)
            {
                _status.PropertyChanged += OnStatusChanged;

                // 档位名由档位对象自己通知，不从状态栏视图模型转一道——
                // 转一道就多一个会忘记转的地方。
                _status.Mode.PropertyChanged += OnStatusChanged;
            }

            Refresh();
        }
    }

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (_status is null)
        {
            return;
        }

        Pointer.Text = _status.PointerText;
        Zoom.Text = _status.ZoomText;
        Mode.Text = _status.Mode.Text;

        Message.Text = _status.Message;
        Message.IsVisible = _status.HasMessage;
        Message.Foreground = _status.Severity switch
        {
            StatusSeverity.Error => ErrorBrush,
            StatusSeverity.Muted => MutedBrush,
            _ => NormalBrush,
        };

        ReadOnly.Text = _status.ReadOnlyNote ?? string.Empty;
        ReadOnly.IsVisible = _status.HasReadOnlyNote;
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

        Pointer = this.FindControl<TextBlock>(nameof(Pointer))
            ?? throw new InvalidOperationException("状态栏的界面标记里没有名为 Pointer 的文本");
        Message = this.FindControl<TextBlock>(nameof(Message))
            ?? throw new InvalidOperationException("状态栏的界面标记里没有名为 Message 的文本");
        Mode = this.FindControl<TextBlock>(nameof(Mode))
            ?? throw new InvalidOperationException("状态栏的界面标记里没有名为 Mode 的文本");
        Zoom = this.FindControl<TextBlock>(nameof(Zoom))
            ?? throw new InvalidOperationException("状态栏的界面标记里没有名为 Zoom 的文本");
        ReadOnly = this.FindControl<TextBlock>(nameof(ReadOnly))
            ?? throw new InvalidOperationException("状态栏的界面标记里没有名为 ReadOnly 的文本");
    }
}
