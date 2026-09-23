using DuetDiagram.App.ViewModels;

namespace DuetDiagram.App.Services;

/// <summary>一条条目出现在哪一处的界面上。</summary>
public enum MenuSurface
{
    /// <summary>只在工具栏上。</summary>
    ToolBar,

    /// <summary>只在菜单栏里。</summary>
    Menu,

    /// <summary>两处都有。</summary>
    Both,
}

/// <summary>
/// 一条可点的条目：工具栏上的一颗按钮，或菜单里的一项。
/// </summary>
/// <remarks>
/// <para>
/// 它只描述"点了做什么"与"什么时候不能点"，不描述自己长什么样、摆在哪一格。
/// 摆在哪由 <see cref="Group"/> 决定，长什么样由画它的那个控件决定。
/// </para>
/// <para>
/// **不能点的时候要给一句理由，不许只是灰掉。** 灰掉而不说为什么，用户会以为程序坏了；
/// 而"为什么不能点"这件事只有这一条自己知道——判据写在这里，理由也就只能写在这里。
/// </para>
/// </remarks>
/// <param name="Id">稳定标识。用例按它找控件，所以它不随显示名变化。</param>
/// <param name="Group">归在哪一档，取值见 <see cref="MenuGroups"/>。</param>
/// <param name="Label">界面上显示的字。</param>
/// <param name="Shortcut">快捷键的显示写法。没有绑定的条目传空。</param>
/// <param name="Surface">出现在工具栏、菜单，还是两处都有。</param>
/// <param name="Refusal">此刻不能点的理由；能点时返回空。</param>
/// <param name="Run">点下去做什么。前提是 <paramref name="Refusal"/> 返回了空。</param>
public sealed record MenuEntry(
    string Id,
    string Group,
    string Label,
    string? Shortcut,
    MenuSurface Surface,
    Func<MenuContext, string?> Refusal,
    Action<MenuContext> Run)
{
    /// <summary>此刻能不能点。</summary>
    public bool IsEnabled(MenuContext context) => Refusal(context) is null;
}

/// <summary>
/// 条目判启用与执行时要看的东西。
/// </summary>
/// <remarks>
/// 它把条目要用到的那几样一次给全：会话（文档、选中、撤销栈、只读门）、
/// 画布状态（面板开关）与状态栏（要把理由说出来）。
/// 条目自己去窗口上东摸一样西摸一样的话，"这条为什么不能点"就散在好几处。
/// </remarks>
public sealed class MenuContext
{
    public MenuContext(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        Window = window;
    }

    /// <summary>这一份上下文属于哪个窗口。</summary>
    public MainWindow Window { get; }

    /// <summary>文档、选中状态与命令入口。</summary>
    public DiagramSession Session => Window.Session;

    /// <summary>画布与面板的状态。</summary>
    public CanvasViewModel Model => Window.Model;

    /// <summary>状态栏。理由与结果都往这里说。</summary>
    public StatusBarViewModel Status => Window.Status;

    /// <summary>当前选中的元素标识。</summary>
    public IReadOnlyList<string> Selected => Session.SelectedIds;

    /// <summary>有没有选中东西。对齐这一类条目的启用判据就是它。</summary>
    public bool HasSelection => Session.SelectedIds.Count > 0;

    /// <summary>撤销栈上还有东西。</summary>
    public bool CanUndo => Session.CanUndo;

    /// <summary>重做栈上还有东西。</summary>
    public bool CanRedo => Session.CanRedo;

    /// <summary>这份文档能不能改。</summary>
    public bool IsReadOnly => Session.IsReadOnly;
}

/// <summary>
/// 档的名字。工具栏上从左到右的那几档，与菜单栏上那几个顶级菜单，都取这里的常量。
/// </summary>
/// <remarks>
/// 写成字面量的话，注册时写错一个字不会报错——那条条目会落进一个不存在的档里，
/// 而它只是**不显示**，看起来像"这一轮没做"。
/// </remarks>
public static class MenuGroups
{
    public const string File = "文件";
    public const string Edit = "编辑";
    public const string Align = "对齐";
    public const string Layout = "布局";
    public const string View = "视图";
    public const string Export = "导出";

    /// <summary>工具栏上从左到右的档。只有菜单里出现的那两档不在其中。</summary>
    public static IReadOnlyList<string> ToolBarOrder { get; } = [Edit, Align, Layout, Export];

    /// <summary>菜单栏上从左到右的顶级菜单。</summary>
    public static IReadOnlyList<string> MenuOrder { get; } = [File, Edit, Align, Layout, View, Export];
}

/// <summary>
/// 工具栏与菜单栏共用的那张条目表。
/// </summary>
/// <remarks>
/// <para>
/// **两处共用一份，是因为它们本来就是同一件事的两个入口。** 各维护一份的话，
/// 同一件事在两个地方会各自长出不同的启用判据，而用户看到的是
/// "菜单里能点、工具栏上是灰的"——两种写法都自洽，只有把两份摆在一起才看得出来。
/// </para>
/// <para>
/// **条目按注册的方式组织，内容各写各的文件。** 这一层只提供注册与分组，
/// 每一块功能在自己的文件里把自己的条目注册进来（见 <see cref="Build"/>）。
/// 把清单写死在一处的话，每加一块功能都要改同一个文件，
/// 而两个人同时改一个文件就是一次冲突。
/// </para>
/// </remarks>
public sealed class MenuRegistry
{
    private readonly List<MenuEntry> _entries = [];

    private MenuRegistry()
    {
    }

    /// <summary>这一份注册表里的全部条目，按注册顺序。</summary>
    public IReadOnlyList<MenuEntry> Entries => _entries;

    /// <summary>
    /// 默认那一份：内置的几档都在里面。
    /// </summary>
    /// <remarks>
    /// 只有一份，因为条目本身是无状态的纯函数——它们看的是传进来的那份上下文，
    /// 不持有窗口。每个窗口各建一份的话，注册表的内容会随窗口创建的时机而不同。
    /// </remarks>
    public static MenuRegistry Default { get; } = Build();

    /// <summary>
    /// 注册一条。标识重复直接抛。
    /// </summary>
    /// <remarks>
    /// 重复标识的表现是"后一条把前一条顶掉了"，而工具栏上只是少了一颗按钮——
    /// 那种少法看起来像"这一轮没做"，不像是出错。
    /// </remarks>
    public void Add(MenuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_entries.Any(existing => string.Equals(existing.Id, entry.Id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"条目标识重复：{entry.Id}");
        }

        if (MenuGroups.ToolBarOrder.Contains(entry.Group, StringComparer.Ordinal) is false
            && MenuGroups.MenuOrder.Contains(entry.Group, StringComparer.Ordinal) is false)
        {
            throw new InvalidOperationException($"条目 {entry.Id} 落在了一个不存在的档里：{entry.Group}");
        }

        _entries.Add(entry);
    }

    /// <summary>某一档里出现在指定界面上的条目，按注册顺序。</summary>
    public IReadOnlyList<MenuEntry> On(MenuSurface surface, string group) =>
        [.. _entries.Where(entry =>
            string.Equals(entry.Group, group, StringComparison.Ordinal)
            && (entry.Surface == surface || entry.Surface == MenuSurface.Both))];

    /// <summary>按标识取一条。找不到返回空。</summary>
    public MenuEntry? Find(string id) =>
        _entries.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.Ordinal));

    private static MenuRegistry Build()
    {
        var registry = new MenuRegistry();

        FileEntries.Register(registry);
        EditEntries.Register(registry);
        AlignEntries.Register(registry);
        LayoutEntries.Register(registry);
        ViewEntries.Register(registry);
        ExportEntries.Register(registry);

        return registry;
    }
}
