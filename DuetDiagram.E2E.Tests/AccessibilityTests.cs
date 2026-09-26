using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using DuetDiagram.App;
using DuetDiagram.App.Controls;
using DuetDiagram.App.Services;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 无障碍：每个交互控件都有可访问名称，键盘能走到每一块面板，焦点顺序跟着功能分组走。
/// </summary>
/// <remarks>
/// <para>
/// **这一层能验的是"有没有名字、能不能到达"，验不了"读起来顺不顺"。**
/// 对比度够不够、读屏软件把那句话念成什么样，要真人戴上读屏走一遍。这里判的是底座：
/// 名字存在、名字说得出这个控件是哪一个、Tab 走得到每一块面板、号是显式给的而不是
/// 碰巧顺着文件里的先后排。
/// </para>
/// <para>
/// **判据从注册表与行数据来，不从视觉树的先后来。** 名字该是什么由条目自己与行数据决定，
/// 所以用例读的是同一份数据，而不是"第几个控件叫什么"——后者会在加一个控件时静默错位。
/// </para>
/// </remarks>
public sealed class AccessibilityTests
{
    #region 名字

    [Fact]
    [Trait("Category", "Accessibility")]
    public async Task Every_interactive_control_in_the_window_has_a_name()
    {
        // 屏幕阅读器读的是名字不是坐标。没有名字的控件在阅读器里等于不存在：
        // 用户能 Tab 到它，却听不到它是干什么的，只能靠猜。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var controls = Interactive(window);

            // 默认窗口里一百来个交互控件。低于这个数说明有一整块面板没被走到，
            // 而"走全了"正是这一条要保证的——漏掉的那一块里有没有名字，就没人看了。
            controls.Count.Should().BeGreaterThanOrEqualTo(80, "窗口里的交互控件上百个，太少说明这一趟没走全");

            var unnamed = controls
                .Where(control => string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)))
                .Select(Describe)
                .ToList();

            unnamed.Should().BeEmpty("每个交互控件都要有可访问名称");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Accessibility")]
    public async Task Every_interactive_control_can_be_reached_by_the_keyboard()
    {
        // 名字有了而 Tab 走不到，等于给了一把够不着的东西。焦点框与键盘操作
        // 都要靠"这个控件能被聚焦"才成立。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            var unreachable = Interactive(window)
                .Where(control => !IsContainer(control))
                .Where(control => !PointerOnly.Contains(
                    AutomationProperties.GetAutomationId(control) ?? string.Empty,
                    StringComparer.Ordinal))
                .Where(control => !control.IsTabStop || !control.Focusable)
                .Select(Describe)
                .ToList();

            unreachable.Should().BeEmpty("键盘够不到的控件，对不用鼠标的人来说就是不存在");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Accessibility")]
    public async Task Names_say_which_control_it_is_not_just_what_kind_it_is()
    {
        // 「按钮」三个字念二十遍，与什么都没念是一回事。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            var kinds = Interactive(window)
                .Select(control => AutomationProperties.GetName(control) ?? string.Empty)
                .Where(Kinds.Contains)
                .ToList();

            kinds.Should().BeEmpty("名字要说出这个控件是哪一个、干什么，不能只说它是个什么控件");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Accessibility")]
    public async Task Rows_that_look_alike_are_told_apart_by_their_names()
    {
        // 三层就是三组一模一样的勾选框。名字里不带图层名的话，阅读器念出来的是
        // 三遍"这一层画不画"，用户分不出手上这一颗属于哪一层。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var panel = HeadlessFixture.Layers(window);

            // 默认文档里一层都没有，先造两层出来——三层才是"长得一模一样"的最小规模。
            AddLayer(window, "上层");
            AddLayer(window, "下层");

            var rows = (panel.Model ?? throw new InvalidOperationException("图层面板还没有数据")).Rows;

            rows.Count.Should().BeGreaterThan(1, "要验的是「两行长得一样怎么分辨」，一行分不出什么");

            var names = Names(panel);

            names.Should().OnlyHaveUniqueItems("同一个面板里同名的话，用户分不出点的是哪一个");

            foreach (var row in rows)
            {
                names.Any(name => name.Contains($"「{row.Name}」", StringComparison.Ordinal))
                    .Should().BeTrue($"图层「{row.Name}」那一行的控件名字里要带上它");
            }

            window.Close();
        });
    }

    #endregion

    #region 焦点顺序

    [Fact]
    [Trait("Category", "Accessibility")]
    public async Task Every_panel_puts_its_list_before_the_controls_that_act_on_it()
    {
        // 默认次序是"控件在视觉树里的先后"，而这几份界面标记把底部那一块声明在列表之前，
        // 于是 Tab 会先落到"新建/删除"上，再落到列表上——用户得先跳过一排
        // "对谁做事"的按钮，才能走到"对谁"。号是显式给的，所以判据读号不读文件次序。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            // 默认文档里这几块都是空的，而它们要验的正是"行在底部那一块之前"。
            AddLayer(window, "上层");
            AddPage(window, "第二页");
            AddPaletteToken(window, "brand");
            AddPreset(window, "标题");

            Before(
                Tabs(HeadlessFixture.Layers(window)),
                name => name.Contains('「', StringComparison.Ordinal),
                name => !name.Contains('「', StringComparison.Ordinal),
                "图层面板：先挑层，再对它做事");

            Before(
                Tabs(HeadlessFixture.PageTabs(window)),
                name => name.StartsWith("第 ", StringComparison.Ordinal),
                name => name.StartsWith("新建页面", StringComparison.Ordinal),
                "标签栏：先挑页，再动页");

            Before(
                Tabs(HeadlessFixture.Palette(window)),
                name => name.StartsWith("选中调色板条目", StringComparison.Ordinal),
                name => name.StartsWith("新建调色板令牌", StringComparison.Ordinal),
                "调色板面板：先挑条目，再对它做事");

            Before(
                Tabs(HeadlessFixture.Presets(window)),
                name => name.StartsWith("选中文本预设", StringComparison.Ordinal),
                name => name.StartsWith("新建文本预设", StringComparison.Ordinal),
                "文本预设面板：先挑预设，再对它做事");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Accessibility")]
    public async Task Tab_walks_into_every_panel()
    {
        // 号排得再好看，键盘走不到也是空的。从画布起步按 Tab，一路把焦点串下来，
        // 看它有没有进过每一块面板。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            canvas.Focus();

            var visited = new List<Control>();

            for (var step = 0; step < 160; step++)
            {
                window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);

                if (window.FocusManager?.GetFocusedElement() is Control focused && !visited.Contains(focused))
                {
                    visited.Add(focused);
                }
            }

            visited.Should().NotBeEmpty("按 Tab 之后焦点要动，一动不动说明这条路根本没通");

            Inside<DiagramCanvas>(visited, "画布");
            Inside<LayerPanel>(visited, "图层面板");
            Inside<ShapeLibraryPanel>(visited, "形状面板");
            Inside<TemplatePanel>(visited, "模板面板");
            Inside<PalettePanel>(visited, "调色板面板");
            Inside<TextPresetPanel>(visited, "文本预设面板");
            Inside<PropertyPanel>(visited, "属性面板");
            Inside<PageTabs>(visited, "标签栏");
            Inside<DiagramToolBar>(visited, "工具栏");
            Inside<DiagramMenuBar>(visited, "菜单栏");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Accessibility")]
    public async Task The_menu_opens_from_the_keyboard()
    {
        // 焦点收得住而展不开，等于给了一个打不开的抽屉。菜单里那几条
        // （新建窗口、导入、保存、存为模板、性能诊断面板）在界面上只有这一个入口，
        // 展不开就没有键盘通路。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var body = HeadlessFixture.MenuBar(window).GetVisualDescendants().OfType<Menu>().Single();
            var top = body.Items.OfType<MenuItem>().First();

            top.Focus();

            top.IsFocused.Should().BeTrue("顶级菜单要收得住焦点");

            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);

            top.IsSubMenuOpen.Should().BeTrue("焦点落在顶级菜单上时，方向键要把它展开");

            window.Close();
        });
    }

    #endregion

    #region 画布与注册表

    [Fact]
    [Trait("Category", "Accessibility")]
    public async Task The_canvas_says_what_it_is_and_which_keys_work()
    {
        // 画布是一整块自绘的区域：里面画了哪些节点、哪一条边，阅读器读不到。
        // 能读的只有这个名字与这句说明，所以键盘在这块地方能做什么必须写在里面。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            AutomationProperties.GetName(canvas)
                .Should().NotBeNullOrWhiteSpace("画布是自绘的，阅读器能读的只有这个名字");

            var help = AutomationProperties.GetHelpText(canvas);
            help.Should().NotBeNullOrWhiteSpace("画布上的操作键要写在说明里，不然键盘用户不知道它能干什么");

            foreach (var gesture in (string[])["空格", "滚轮", "双击", "右键"])
            {
                help.Should().Contain(gesture, $"画布上的{gesture}操作要说出来");
            }

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Accessibility")]
    public async Task The_tool_bar_and_the_menu_take_their_names_from_the_registry()
    {
        // 名字跟着注册表走，所以新加一条条目会自动带上名字，漏掉一条会当场失败。
        // 两处的构造方式不同：工具栏上不显示档名，菜单里档名由上一级念出来。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var toolBar = HeadlessFixture.ToolBar(window);
            var menu = HeadlessFixture.MenuBar(window);

            foreach (var entry in MenuRegistry.Default.On(MenuSurface.ToolBar, MenuGroups.ToolBarOrder))
            {
                var button = toolBar.Find(entry.Id)
                    ?? throw new InvalidOperationException($"工具栏上没有 {entry.Id}");

                AutomationProperties.GetName(button)
                    .Should().Be($"{entry.Group}：{entry.Label}", $"工具栏上看不见档名，{entry.Id} 的名字要自己带上");
            }

            foreach (var entry in MenuRegistry.Default.On(MenuSurface.Menu, MenuGroups.MenuOrder))
            {
                var item = menu.Find(entry.Id)
                    ?? throw new InvalidOperationException($"菜单里没有 {entry.Id}");

                AutomationProperties.GetName(item)
                    .Should().Be(entry.Label, $"{entry.Id} 的档名由上一级菜单念出来，名字不重复拼");
            }

            var body = menu.GetVisualDescendants().OfType<Menu>().Single();
            var tops = body.Items.OfType<MenuItem>().ToList();

            tops.Select(item => item.Header?.ToString())
                .Should().Equal(MenuGroups.MenuOrder, "顶级菜单从左到右就是这几档");

            foreach (var top in tops)
            {
                AutomationProperties.GetName(top)
                    .Should().Be(top.Header?.ToString(), "顶级菜单的名字就是档名");
            }

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Accessibility")]
    public async Task Context_menu_items_have_names_too()
    {
        // 右键菜单不在窗口的视觉树里，要弹出来才看得到，所以单独验一遍。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);
            var local = HeadlessFixture.CenterOf(canvas, "start");
            var point = HeadlessFixture.ToWindow(canvas, window, local);

            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);

            var menu = canvas.ContextMenu ?? throw new InvalidOperationException("右键没有弹出菜单");
            var items = menu.Items.OfType<MenuItem>().ToList();

            items.Should().NotBeEmpty();

            var unnamed = items
                .Where(item => string.IsNullOrWhiteSpace(AutomationProperties.GetName(item)))
                .Select(item => AutomationProperties.GetAutomationId(item) ?? item.Header?.ToString() ?? "?")
                .ToList();

            unnamed.Should().BeEmpty("右键菜单上的条目也要有名字");

            window.Close();
        });
    }

    #endregion

    #region 辅助

    /// <summary>
    /// 就地编辑器里那四颗样式按钮的自动化标识。
    /// </summary>
    /// <remarks>
    /// 它们是刻意不接收焦点的：读的是编辑框此刻的选区，而选区是编辑框上的活状态，
    /// 焦点一挪走就读不到那一段了。名字照样要有，键盘那一条留给人工验收。
    /// </remarks>
    private static readonly string[] PointerOnly =
        ["richtext.bold", "richtext.italic", "richtext.underline", "richtext.strikethrough"];

    /// <summary>只说"这是个什么控件"的那几个名字。念出来等于没念。</summary>
    private static readonly string[] Kinds =
        ["按钮", "Button", "文本框", "输入框", "勾选框", "下拉框", "控件", "点这里", "未命名"];

    /// <summary>
    /// 窗口里由这一层搭出来的交互控件。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **模板里长出来的不算。** 主题自带的零件（滚动条两端的箭头、下拉框右半边那颗按钮）
    /// 不是这一层能命名或该命名的东西，把它们算进来只会让这一条永远绿不了。
    /// 判据是"模板父级为空"：自己搭的控件都是直接 new 出来的，模板生成的都有父级。
    /// </para>
    /// <para>
    /// 视觉树与逻辑树都要走。菜单项在菜单展开之前只挂在逻辑树上，
    /// 只走视觉树的话它们会被漏掉，而那正是"看不见的地方最容易没有名字"。
    /// </para>
    /// </remarks>
    private static List<Control> Interactive(Window window)
    {
        var found = new List<Control>();

        foreach (var control in window.GetVisualDescendants().OfType<Control>()
            .Concat(window.GetLogicalDescendants().OfType<Control>()))
        {
            if (control.TemplatedParent is null && IsInteractive(control) && !found.Contains(control))
            {
                found.Add(control);
            }
        }

        return found;
    }

    private static bool IsInteractive(Control control) =>
        control is Button or TextBox or CheckBox or ComboBox or MenuItem or Menu or DiagramCanvas;

    /// <summary>
    /// 只做容器、本身不操作的控件。
    /// </summary>
    /// <remarks>
    /// 菜单容器自己不收焦点：收的是它里面的顶级菜单项，而那几项既可聚焦也能用
    /// 方向键与回车操作。容器要名字是因为阅读器要说出这一块是什么，
    /// 不是因为它自己要被按——所以"有没有名字"那一关照样要过它。
    /// </remarks>
    private static bool IsContainer(Control control) => control is Menu;

    /// <summary>面板里每个可聚焦控件的名字与号，按控件在树里的先后。</summary>
    private static List<(string Name, int Tab)> Tabs(Control root) =>
    [
        .. root.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control is Button or TextBox or CheckBox or ComboBox)
            .Select(control => (AutomationProperties.GetName(control) ?? string.Empty, control.TabIndex)),
    ];

    /// <summary>
    /// 前一类控件的号都小于后一类。
    /// </summary>
    /// <remarks>
    /// 两类各要至少命中一个：都落空的话两边都是 -1，这一条会假绿。
    /// </remarks>
    private static void Before(
        List<(string Name, int Tab)> tabs,
        Func<string, bool> first,
        Func<string, bool> second,
        string because)
    {
        var before = tabs.Where(tab => first(tab.Name)).ToList();
        var after = tabs.Where(tab => second(tab.Name)).ToList();

        before.Should().NotBeEmpty($"{because}——前一类要能找到");
        after.Should().NotBeEmpty($"{because}——后一类要能找到");

        before.Max(tab => tab.Tab).Should().BeLessThan(after.Min(tab => tab.Tab), because);
    }

    /// <summary>这一趟焦点有没有进过某一块地方。</summary>
    private static void Inside<T>(List<Control> visited, string what)
        where T : Control
    {
        visited.Any(control => control is T || control.FindAncestorOfType<T>() is not null)
            .Should().BeTrue($"Tab 要能走进{what}");
    }

    private static List<string> Names(Control root) =>
        [.. root.GetVisualDescendants().OfType<Control>().Select(AutomationProperties.GetName).OfType<string>()];

    private static string Describe(Control control) =>
        $"{control.GetType().Name}（自动化标识 {AutomationProperties.GetAutomationId(control) ?? "无"}，"
        + $"Tab 停靠={control.IsTabStop}，可聚焦={control.Focusable}）";

    /// <summary>
    /// 造一层出来。
    /// </summary>
    /// <remarks>
    /// 默认文档里一层都没有，而"每一层一行"这件事要先有层才验得到。
    /// 造完补一帧：刚加进去的控件要等一次排布才进视觉树，不等的话量到的是空的。
    /// </remarks>
    private static void AddLayer(MainWindow window, string name) =>
        Add(window, "layer.new-name", "layer.new", name);

    /// <summary>造一页出来。理由与造图层相同：默认文档里一页都没有。</summary>
    private static void AddPage(MainWindow window, string name) =>
        Add(window, "page.new-name", "page.new", name);

    /// <summary>造一个调色板条目出来。默认文档里一个都没有。</summary>
    private static void AddPaletteToken(MainWindow window, string name) =>
        Add(window, "palette.new-name", "palette.define", name);

    /// <summary>造一个文本预设出来。默认文档里一个都没有。</summary>
    private static void AddPreset(MainWindow window, string name) =>
        Add(window, "textpreset.new-name", "textpreset.define", name);

    /// <summary>
    /// 在某个面板的"新建"那一行上填名字并按下去。
    /// </summary>
    /// <remarks>
    /// 补一帧那一步不能省：刚加进去的控件要等一次排布才进视觉树，
    /// 而这里量名字正是从视觉树上量的，不等的话量到的还是上一批。
    /// </remarks>
    private static void Add(MainWindow window, string boxId, string buttonId, string name)
    {
        HeadlessFixture.Editor<TextBox>(window, boxId).Text = name;
        HeadlessFixture.Editor<Button>(window, buttonId)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        window.CaptureRenderedFrame();
    }

    #endregion
}
