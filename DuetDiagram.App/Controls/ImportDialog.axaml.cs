using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DuetDiagram.App.Controls;

/// <summary>
/// 一次导入之后摆出来的那张单子：导了哪个文件、导进去多少、哪些地方与原文对不上。
/// </summary>
/// <remarks>
/// <para>
/// **它是这份报告唯一的落点。** 导入是宽松模式：认不出的内容不抛异常，
/// 只是不进 IR；被丢掉的节点声明与没映射的样式属性也都是**有意的取舍**。
/// 这些一句话都不说的话，用户拿到的是一张少了几条边、少了一处颜色的图，
/// 而他无从知道是原文里就没有、还是程序没做。
/// </para>
/// <para>
/// **它只负责显示，不负责办。** 读文件、拼进文档、摆到哪儿都由宿主去做；
/// 面板自己去读文件的话，同一份文件会在两个地方被解析一遍，
/// 而两处的判据迟早会不一样。
/// </para>
/// <para>
/// **它只读一次，不做确认。** 导入是一条命令、撤销一次整份退回，
/// 所以"先预览再确认"那一步是多余的；用户导错了按一下撤销就回去了。
/// </para>
/// </remarks>
public sealed partial class ImportDialog : UserControl
{
    public ImportDialog()
    {
        InitializeComponent();

        CloseButton.Click += (_, _) => DismissRequested?.Invoke();
    }

    /// <summary>用户把这张单子关掉了。</summary>
    public event Action? DismissRequested;

    /// <summary>这张单子现在说的是哪一份文件。没有内容时为空。</summary>
    public string? File => string.IsNullOrEmpty(FileText.Text) ? null : FileText.Text;

    /// <summary>此刻摆在面板上的那几句说明。</summary>
    public IReadOnlyList<string> Notes => NotesList.ItemsSource?.Cast<string>().ToArray() ?? [];

    /// <summary>
    /// 摆一次报告。
    /// </summary>
    /// <param name="file">文件名，不含路径。</param>
    /// <param name="headline">一句话说清这次导入的结果，或者为什么导不进来。</param>
    /// <param name="notes">要逐条列出来的那些话。可以是空的。</param>
    public void Show(string file, string headline, IReadOnlyList<string> notes)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(headline);
        ArgumentNullException.ThrowIfNull(notes);

        FileText.Text = file;
        HeadlineText.Text = headline;
        NotesList.ItemsSource = notes;

        // 没有要交代的就整块收起来。留一个空框看起来像"本来有话要说但没显示出来"。
        NotesScroll.IsVisible = notes.Count > 0;

        IsVisible = true;
    }

    /// <summary>把这张单子收起来。</summary>
    public void Dismiss()
    {
        IsVisible = false;
        FileText.Text = string.Empty;
        HeadlineText.Text = string.Empty;
        NotesList.ItemsSource = null;
        NotesScroll.IsVisible = false;
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

        FileText = this.FindControl<TextBlock>(nameof(FileText))
            ?? throw new InvalidOperationException("导入提示里没有名为 FileText 的文本");
        HeadlineText = this.FindControl<TextBlock>(nameof(HeadlineText))
            ?? throw new InvalidOperationException("导入提示里没有名为 HeadlineText 的文本");
        NotesScroll = this.FindControl<ScrollViewer>(nameof(NotesScroll))
            ?? throw new InvalidOperationException("导入提示里没有名为 NotesScroll 的滚动区");
        NotesList = this.FindControl<ItemsControl>(nameof(NotesList))
            ?? throw new InvalidOperationException("导入提示里没有名为 NotesList 的列表");
        CloseButton = this.FindControl<Button>(nameof(CloseButton))
            ?? throw new InvalidOperationException("导入提示里没有名为 CloseButton 的按钮");
    }
}
