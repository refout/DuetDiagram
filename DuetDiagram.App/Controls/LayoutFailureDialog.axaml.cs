using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DuetDiagram.App.Controls;

/// <summary>
/// 布局彻底失败时的提示：重试 / 手动布局 / 简化图。
/// </summary>
/// <remarks>
/// <para>
/// 它只是一个"问用户走哪条路"的面板，不认识会话也不认识布局——按了哪个按钮由宿主去办。
/// 面板自己去调布局的话，那件事就有了两个入口，而两条路径迟早会对同一次失败给出不同的处置。
/// </para>
/// <para>
/// 画面不清空：布局失败时保留上一次成功的结果，用户看到的还是那张图，只是多了一个提示。
/// 清空会让用户以为图丢了。
/// </para>
/// </remarks>
public sealed partial class LayoutFailureDialog : UserControl
{
    public LayoutFailureDialog()
    {
        InitializeComponent();

        RetryButton.Click += (_, _) => RetryRequested?.Invoke();
        ManualButton.Click += (_, _) => ManualLayoutRequested?.Invoke();
        SimplifyButton.Click += (_, _) => SimplifyRequested?.Invoke();
    }

    /// <summary>用户选了"重试"。</summary>
    public event Action? RetryRequested;

    /// <summary>用户选了"手动布局"。</summary>
    public event Action? ManualLayoutRequested;

    /// <summary>用户选了"简化图"。</summary>
    public event Action? SimplifyRequested;

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
            ?? throw new InvalidOperationException("布局失败提示里没有名为 DetailText 的文本");
        RetryButton = this.FindControl<Button>(nameof(RetryButton))
            ?? throw new InvalidOperationException("布局失败提示里没有名为 RetryButton 的按钮");
        ManualButton = this.FindControl<Button>(nameof(ManualButton))
            ?? throw new InvalidOperationException("布局失败提示里没有名为 ManualButton 的按钮");
        SimplifyButton = this.FindControl<Button>(nameof(SimplifyButton))
            ?? throw new InvalidOperationException("布局失败提示里没有名为 SimplifyButton 的按钮");
    }
}
