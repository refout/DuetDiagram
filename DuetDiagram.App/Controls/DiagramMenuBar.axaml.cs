using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using DuetDiagram.App.Services;

namespace DuetDiagram.App.Controls;

/// <summary>
/// 菜单栏。顶级菜单与下面的项都由注册表生成。
/// </summary>
/// <remarks>
/// <para>
/// 与工具栏读同一份注册表，所以同一个动作在两处的启用判据必然一致。
/// 各读一份的话，会出现"菜单里能点、工具栏上是灰的"——两种写法都自洽，
/// 只有把两份摆在一起才看得出来。
/// </para>
/// <para>
/// **菜单项自带理由。** 点不动的那一项在提示里说明为什么，理由由条目自己给出。
/// </para>
/// </remarks>
public sealed partial class DiagramMenuBar : UserControl
{
    private readonly List<(MenuEntry Entry, MenuItem Item)> _items = [];

    private MenuContext? _context;

    public DiagramMenuBar() => InitializeComponent();

    /// <summary>接上某个窗口，按注册表把菜单搭出来。</summary>
    public void Attach(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _context = new MenuContext(window);
        _items.Clear();
        Body.Items.Clear();

        foreach (var group in MenuGroups.MenuOrder)
        {
            var entries = MenuRegistry.Default.On(MenuSurface.Menu, group);

            if (entries.Count == 0)
            {
                continue;
            }

            var top = new MenuItem { Header = group };

            foreach (var entry in entries)
            {
                var item = Item(entry);

                top.Items.Add(item);
                _items.Add((entry, item));
            }

            Body.Items.Add(top);
        }

        Refresh();
    }

    /// <summary>按当前上下文刷新每一项的启用状态与理由。</summary>
    public void Refresh()
    {
        if (_context is null)
        {
            return;
        }

        foreach (var (entry, item) in _items)
        {
            var reason = entry.Refusal(_context);

            item.IsEnabled = reason is null;

            // 理由挂在提示上，不塞进项的字里。塞进去的话，一个禁用的项会显示成
            // 一整句话，菜单会被撑得没法看——而"为什么不能点"这件事只在悬停时才需要。
            ToolTip.SetTip(item, reason ?? Shortcut(entry));
        }
    }

    /// <summary>按标识取一项。找不到返回空。</summary>
    public MenuItem? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        foreach (var (entry, item) in _items)
        {
            if (string.Equals(entry.Id, id, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>菜单里此刻摆着的条目标识，按顶级菜单与项的顺序。</summary>
    public IReadOnlyList<string> Ids => [.. _items.Select(pair => pair.Entry.Id)];

    /// <summary>顶级菜单的名字，按从左到右的顺序。</summary>
    public IReadOnlyList<string> TopLevels =>
        [.. Body.Items.OfType<MenuItem>().Select(item => item.Header?.ToString() ?? string.Empty)];

    private MenuItem Item(MenuEntry entry)
    {
        var item = new MenuItem { Header = entry.Label, InputGesture = Gesture(entry) };

        item.Click += (_, _) => Run(entry);

        return item;
    }

    /// <summary>
    /// 点一下。
    /// </summary>
    /// <remarks>
    /// 与工具栏同一形状：再判一次启用状态。禁用项在界面上点不动，
    /// 但键盘与自动化还能触发它，那一下不判的话，一条本该被拒的操作会真的发出去。
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

    /// <summary>
    /// 项上那个右对齐的快捷键。
    /// </summary>
    /// <remarks>
    /// 它只管显示：真正的按键由主窗口处理，而主窗口走的也是注册表里这一条。
    /// 两处各绑一次的话，显示出来的组合键与实际生效的那个迟早会对不上。
    /// </remarks>
    private static KeyGesture? Gesture(MenuEntry entry) =>
        entry.Shortcut is { Length: > 0 } shortcut ? KeyGesture.Parse(shortcut) : null;

    private static string? Shortcut(MenuEntry entry) =>
        entry.Shortcut is { Length: > 0 } shortcut ? $"{entry.Label}（{shortcut}）" : null;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        Body = this.FindControl<Menu>(nameof(Body))
            ?? throw new InvalidOperationException("菜单栏的界面标记里没有名为 Body 的菜单容器");
    }
}
