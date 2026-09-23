using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using DuetDiagram.App.Services;

namespace DuetDiagram.App.Controls;

/// <summary>
/// 工具栏。四档按钮，档与档之间一条竖线。
/// </summary>
/// <remarks>
/// <para>
/// **按钮由注册表生成，不写死在界面标记里。** 条目与命令的对应关系只在一处
/// （<see cref="MenuRegistry"/>），菜单栏读的是同一份。两处各写一份的话，
/// 同一件事会在两个地方长出不同的启用判据，而用户看到的是"菜单里能点、工具栏上是灰的"。
/// </para>
/// <para>
/// **不能点的按钮把理由挂在提示上。** 只灰掉不说为什么，用户会以为程序坏了；
/// 理由由条目自己给出，因为"为什么不能点"这件事只有它知道。
/// </para>
/// <para>
/// 它不持有文档：点下去的动作全部经由 <see cref="MenuEntry.Run"/> 走会话的命令入口。
/// 自己改文档的话，"人和 LLM 能力对等"当场不成立——LLM 那边没有工具栏可点。
/// </para>
/// </remarks>
public sealed partial class DiagramToolBar : UserControl
{
    private readonly List<(MenuEntry Entry, Button Button)> _buttons = [];

    private MenuContext? _context;

    public DiagramToolBar() => InitializeComponent();

    /// <summary>
    /// 接上某个窗口，按注册表把按钮搭出来。
    /// </summary>
    /// <remarks>
    /// 只在窗口建起来时调一次。之后换选中、换文档都只走 <see cref="Refresh"/>——
    /// 重建的话，连续点选时工具栏会明显闪，而连续点选正是用户在找元素时的常态。
    /// </remarks>
    public void Attach(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _context = new MenuContext(window);
        _buttons.Clear();
        Body.Children.Clear();

        foreach (var group in MenuGroups.ToolBarOrder)
        {
            var entries = MenuRegistry.Default.On(MenuSurface.ToolBar, group);

            if (entries.Count == 0)
            {
                continue;
            }

            if (Body.Children.Count > 0)
            {
                Body.Children.Add(Separator());
            }

            var cluster = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

            foreach (var entry in entries)
            {
                var button = Button(entry);

                cluster.Children.Add(button);
                _buttons.Add((entry, button));
            }

            Body.Children.Add(cluster);
        }

        Refresh();
    }

    /// <summary>按当前上下文刷新每颗按钮的启用状态与理由。</summary>
    public void Refresh()
    {
        if (_context is null)
        {
            return;
        }

        foreach (var (entry, button) in _buttons)
        {
            var reason = entry.Refusal(_context);

            button.IsEnabled = reason is null;

            // 能点时把快捷键那条提示也撤掉：留着一句"现在不能点"的旧提示，
            // 而按钮明明是亮的，用户会以为提示没刷新。
            ToolTip.SetTip(button, reason ?? Shortcut(entry));
        }
    }

    /// <summary>按标识取一颗按钮。找不到返回空。</summary>
    public Button? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        foreach (var (entry, button) in _buttons)
        {
            if (string.Equals(entry.Id, id, StringComparison.Ordinal))
            {
                return button;
            }
        }

        return null;
    }

    /// <summary>工具栏上此刻摆着的条目标识，按从左到右的顺序。</summary>
    public IReadOnlyList<string> Ids => [.. _buttons.Select(pair => pair.Entry.Id)];

    private Button Button(MenuEntry entry)
    {
        var button = new Button
        {
            Content = entry.Label,
            FontSize = 12,
            Padding = new Avalonia.Thickness(10, 4),
        };

        button.Click += (_, _) => Run(entry);

        return button;
    }

    /// <summary>
    /// 点一下。
    /// </summary>
    /// <remarks>
    /// 再判一次启用状态，而不是直接执行：按钮被禁用时点不动，但键盘与自动化还能触发它。
    /// 那一下落到这里而不判的话，一条本该被拒的操作会真的发出去。
    /// 拒绝时把理由摆到状态栏上——按钮上那句话只有悬停才看得见。
    /// </remarks>
    private void Run(MenuEntry entry)
    {
        if (_context is null)
        {
            return;
        }

        if (entry.Refusal(_context) is { } reason)
        {
            _context.Status.Show(new ErrorPresentation(ErrorPresentationKind.StatusBarMuted, reason));

            return;
        }

        entry.Run(_context);
        Refresh();
    }

    private static string? Shortcut(MenuEntry entry) =>
        entry.Shortcut is { Length: > 0 } shortcut ? $"{entry.Label}（{shortcut}）" : null;

    private static Control Separator() => new Border
    {
        Width = 1,
        Margin = new Avalonia.Thickness(2, 2),
        Background = PanelPalette.Muted,
    };

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        Body = this.FindControl<StackPanel>(nameof(Body))
            ?? throw new InvalidOperationException("工具栏的界面标记里没有名为 Body 的容器");
    }
}
