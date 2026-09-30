using System.Globalization;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DuetDiagram.App.Resources;
using DuetDiagram.Core.Model;

namespace DuetDiagram.App.Controls;

/// <summary>
/// 导出对话框：选格式、选范围、选倍数，然后交给宿主去写文件。
/// </summary>
/// <remarks>
/// <para>
/// **它只负责收选择，不负责办。** 要导的是哪一页、固定位置有哪些、文件往哪儿写，
/// 都由宿主去办——面板自己去读会话的话，同一件事会有两个地方各判一次，
/// 而两处的判据迟早会不一样。
/// </para>
/// <para>
/// **范围与倍数按格式灰掉。** 给一个格式它办不到的旋钮时，正确的处置是让人选不了，
/// 而不是收下参数再按缺省值出图——后者会让人以为自己的选择生效了。
/// 工具那一条路对同一件事的做法是拒绝并说清哪个格式办得到，两张表是同一张
/// （<see cref="ExportFormats.HonorsRange"/> 与 <see cref="ExportFormats.HonorsScale"/>）。
/// </para>
/// <para>
/// **倍数填不出一个大于零的数时，导出那一个按钮是灰的。** 让人点下去再报错的话，
/// 他要点两次才知道哪里不对；灰着并在下面写一句为什么，一次就说清了。
/// </para>
/// </remarks>
public sealed partial class ExportDialog : UserControl
{
    public ExportDialog()
    {
        InitializeComponent();

        FillChoices();

        ConfirmButton.Click += (_, _) => Confirm();
        CancelButton.Click += (_, _) => DismissRequested?.Invoke();

        // 换格式要重新算哪几个旋钮可用。这一条挂在格式上而不是挂在范围或倍数上：
        // 后两者动不了可用性，它们只动取值。
        FormatBox.SelectionChanged += (_, _) => ApplyApplicability();

        // 倍数那一栏改一次就重算一次。**挂在属性上而不是挂在 TextChanged 上**：
        // 那个事件在这一版里对程序化赋值不发（实测），于是"填一个数、按钮跟着变"
        // 这条链只在使用者一个字一个字敲键盘时才通，而用代码摆一个值进去就断了。
        // 属性变更两条路都发，而且发在值已经换过去之后——判据读的就是那一个新值。
        ScaleBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                RefreshConfirmable();
            }
        };

        // 摆一份缺省的选择，但**不显示**——显不显示是宿主的事。
        // 构造函数里就显示的话，窗口一搭起来它就浮在画布上了。
        Reset();
    }

    /// <summary>用户点了导出。选择由 <see cref="Selection"/> 给。</summary>
    public event Action<ExportRequest>? ExportRequested;

    /// <summary>用户把对话框关掉了。</summary>
    public event Action? DismissRequested;

    /// <summary>此刻选中的那一份请求。页面标识留空，由宿主按"当前页"补。</summary>
    public ExportRequest Selection => new(SelectedFormat, null, SelectedRange, ScaleOf() ?? 1);

    /// <summary>格式那一栏里摆出来的记号。</summary>
    public IReadOnlyList<string> Formats => Tokens(FormatBox);

    /// <summary>范围那一栏里摆出来的记号。</summary>
    public IReadOnlyList<string> Ranges => Tokens(RangeBox);

    /// <summary>此刻选中的格式。</summary>
    public string SelectedFormat =>
        FormatBox.SelectedItem is ComboBoxItem { Content: string token } ? token : ExportFormats.All[0];

    /// <summary>此刻选中的范围。</summary>
    public string SelectedRange =>
        RangeBox.SelectedItem is ComboBoxItem { Tag: string token } ? token : ExportRanges.Content;

    /// <summary>范围那一栏此刻能不能动。</summary>
    public bool RangeEnabled => RangeBox.IsEnabled;

    /// <summary>倍数那一栏此刻能不能动。</summary>
    public bool ScaleEnabled => ScaleBox.IsEnabled;

    /// <summary>倍数那一栏此刻填的是什么。</summary>
    public string ScaleText => ScaleBox.Text ?? string.Empty;

    /// <summary>此刻点得动导出那一个按钮。</summary>
    public bool CanConfirm => ConfirmButton.IsEnabled;

    /// <summary>倍数填得不对时摆出来的那一句。没有话要说时为空。</summary>
    public string? Hint => HintText.IsVisible ? HintText.Text : null;

    /// <summary>把对话框摆出来，并从缺省的那一组选择重新开始。</summary>
    public void Open()
    {
        Reset();
        IsVisible = true;
    }

    /// <summary>把对话框收起来。</summary>
    public void Dismiss()
    {
        IsVisible = false;
        HintText.IsVisible = false;
        HintText.Text = string.Empty;
    }

    /// <summary>改选一个格式。给测试与宿主用。</summary>
    public void SelectFormat(string format)
    {
        var index = ExportFormats.All.ToList().IndexOf(format);

        FormatBox.SelectedIndex = index < 0 ? 0 : index;
    }

    /// <summary>改选一个范围。给测试与宿主用。</summary>
    public void SelectRange(string range)
    {
        var index = ExportRanges.All.ToList().IndexOf(range);

        RangeBox.SelectedIndex = index < 0 ? 0 : index;
    }

    /// <summary>改填一个倍数。给测试与宿主用。</summary>
    public void SetScale(string text) => ScaleBox.Text = text;

    /// <summary>点一次导出。给测试用——它走的是与点按钮完全相同的那一段。</summary>
    public void Confirm()
    {
        if (!CanConfirm)
        {
            return;
        }

        ExportRequested?.Invoke(Selection);
    }

    #region 内部

    /// <summary>摆回缺省那一组选择：SVG、按内容外接框、一倍。</summary>
    /// <remarks>
    /// 缺省是 SVG，因为最常要的是"给同事看一眼"——而它在三种图格式里最通用。
    /// </remarks>
    private void Reset()
    {
        SelectFormat(ExportFormats.Svg);
        SelectRange(ExportRanges.Content);
        ScaleBox.Text = "1";

        ApplyApplicability();
    }

    /// <summary>按所选格式算哪几个旋钮可用，用不了的摆回缺省值。</summary>
    /// <remarks>
    /// 用不了就摆回缺省值，而不是留着上一次选的：灰着的那一栏里留一个用不上的取值，
    /// 看起来像"它其实会生效"。
    /// </remarks>
    private void ApplyApplicability()
    {
        var format = SelectedFormat;

        RangeBox.IsEnabled = ExportFormats.HonorsRange(format);
        ScaleBox.IsEnabled = ExportFormats.HonorsScale(format);

        if (!RangeBox.IsEnabled)
        {
            SelectRange(ExportRanges.Content);
        }

        if (!ScaleBox.IsEnabled)
        {
            ScaleBox.Text = "1";
        }

        RefreshConfirmable();
    }

    /// <summary>按倍数填得对不对决定导出那一个按钮能不能点。</summary>
    /// <remarks>
    /// 判据要从格式与倍数现算，**不能读按钮此刻的状态**：读它的话，这个方法就是
    /// 拿自己的结果当输入，按钮一旦被灰掉就再也亮不回来。
    /// </remarks>
    private void RefreshConfirmable()
    {
        var ok = !ExportFormats.HonorsScale(SelectedFormat) || ScaleOf() is not null;

        ConfirmButton.IsEnabled = ok;
        HintText.IsVisible = !ok;
        HintText.Text = ok ? string.Empty : Strings.ExportDialogScaleInvalid;
    }

    /// <summary>倍数那一栏填的数。填不出一个大于零的数时为空。</summary>
    /// <remarks>
    /// 用不变量文化解析：这一份程序集在不变量全球化下运行，按当前文化解析的写法
    /// 在这里要么拿不到小数点、要么直接抛。
    /// </remarks>
    private double? ScaleOf() =>
        double.TryParse(ScaleBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && value > 0
        && !double.IsInfinity(value)
            ? value
            : null;

    /// <summary>把两个下拉填上。记号取自 Core 那一份词汇，两处不会各写一遍。</summary>
    private void FillChoices()
    {
        foreach (var format in ExportFormats.All)
        {
            FormatBox.Items.Add(new ComboBoxItem { Content = format });
        }

        foreach (var range in ExportRanges.All)
        {
            // 记号进 Tag，显示的是人话：下拉里摆 content / page 两个英文单词，
            // 而这一栏在中文界面下要能看懂。
            RangeBox.Items.Add(new ComboBoxItem { Content = LabelOf(range), Tag = range });
        }
    }

    private static string LabelOf(string range) => range switch
    {
        ExportRanges.Page => Strings.ExportDialogRangePage,
        _ => Strings.ExportDialogRangeContent,
    };

    private static IReadOnlyList<string> Tokens(ComboBox box) =>
        [.. box.Items.OfType<ComboBoxItem>().Select(item => item.Tag as string ?? item.Content as string).OfType<string>()];

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

        TitleText = this.FindControl<TextBlock>(nameof(TitleText))
            ?? throw new InvalidOperationException("导出对话框里没有名为 TitleText 的文本");
        FormatBox = this.FindControl<ComboBox>(nameof(FormatBox))
            ?? throw new InvalidOperationException("导出对话框里没有名为 FormatBox 的下拉");
        RangeBox = this.FindControl<ComboBox>(nameof(RangeBox))
            ?? throw new InvalidOperationException("导出对话框里没有名为 RangeBox 的下拉");
        ScaleBox = this.FindControl<TextBox>(nameof(ScaleBox))
            ?? throw new InvalidOperationException("导出对话框里没有名为 ScaleBox 的输入框");
        HintText = this.FindControl<TextBlock>(nameof(HintText))
            ?? throw new InvalidOperationException("导出对话框里没有名为 HintText 的文本");
        ConfirmButton = this.FindControl<Button>(nameof(ConfirmButton))
            ?? throw new InvalidOperationException("导出对话框里没有名为 ConfirmButton 的按钮");
        CancelButton = this.FindControl<Button>(nameof(CancelButton))
            ?? throw new InvalidOperationException("导出对话框里没有名为 CancelButton 的按钮");
    }

    #endregion
}
