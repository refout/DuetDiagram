using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DuetDiagram.App.Controls;

/// <summary>
/// 人工产物损坏时的恢复提示：从备份恢复 / 放弃人工调整。
/// </summary>
/// <remarks>
/// <para>
/// 它只在人工产物（固定位置与折点）读不出来时出现。这两种处置对应两种取舍：
/// 恢复备份是"尽量保住用户摆过的位置"，放弃是从此按自动布局走。
/// 这里没有第三个"取消"——那份文件已经坏了，不选也得选一个。
/// </para>
/// <para>
/// 与布局失败提示同理，它只负责问，不负责办：恢复与放弃都由宿主去执行。
/// </para>
/// </remarks>
public sealed partial class SidecarRecoveryDialog : UserControl
{
    public SidecarRecoveryDialog()
    {
        InitializeComponent();

        RestoreButton.Click += (_, _) => RestoreRequested?.Invoke();
        DiscardButton.Click += (_, _) => DiscardRequested?.Invoke();
    }

    /// <summary>用户选了"从备份恢复"。</summary>
    public event Action? RestoreRequested;

    /// <summary>用户选了"放弃人工调整"。</summary>
    public event Action? DiscardRequested;

    /// <summary>提示里那句补充说明。</summary>
    public void SetDetail(string detail) => DetailText.Text = detail;

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

        DetailText = this.FindControl<TextBlock>(nameof(DetailText))
            ?? throw new InvalidOperationException("恢复提示里没有名为 DetailText 的文本");
        RestoreButton = this.FindControl<Button>(nameof(RestoreButton))
            ?? throw new InvalidOperationException("恢复提示里没有名为 RestoreButton 的按钮");
        DiscardButton = this.FindControl<Button>(nameof(DiscardButton))
            ?? throw new InvalidOperationException("恢复提示里没有名为 DiscardButton 的按钮");
    }
}
