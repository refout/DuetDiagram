using System.Resources;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using DuetDiagram.App;
using DuetDiagram.App.Controls;
using DuetDiagram.App.Resources;
using DuetDiagram.App.Services;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 界面文字来自资源文件，不是散落在代码里的字面量；换一种语言，菜单与面板标题真的会变。
/// </summary>
/// <remarks>
/// <para>
/// **这一层能验的是"文字从资源里取"，验不了"翻得对不对"。** 第二种语言在这里的用处不是交付一份译文，
/// 而是**证明取文字这条路真的通**：文字不跟着语言变，就说明它还是硬编码的，
/// 而硬编码与"来自资源"在只看一种语言时分不开。
/// </para>
/// <para>
/// **语言是显式指定的，不是切换运行期文化。** 这一份程序集在不变量全球化下运行，
/// 具名文化建不出来（实测会抛），卫星程序集也就永远选不出来。
/// 所以用例设的是 <see cref="Strings.Language"/>，判据落在"窗口搭出来之后文字是什么"。
/// </para>
/// <para>
/// **改语言这件事必须收干净。** 语言是一个静态开关，窗口是在界面线程上搭的，
/// 而无头会话把每一段测试体都排在同一个界面线程上——所以在测试体里改、在 finally 里改回来，
/// 别的用例看不到中间那一段。
/// </para>
/// </remarks>
public sealed class I18nTests
{
    #region 资源与代码对得上

    [Fact]
    [Trait("Category", "I18n")]
    public void Every_key_has_a_reader_and_every_reader_has_a_key()
    {
        // 两个方向都要管。只查一个方向的话，"资源里有一条没人用的键"（翻译者白翻一条）
        // 与"代码里取了一条不存在的键"（运行时才炸）各漏一半。
        var root = RepositoryRoot();
        var code = KeysUsedInCode(Path.Combine(root, "DuetDiagram.App", "Resources", "Strings.cs"));
        var neutral = KeysInResource(Path.Combine(root, "DuetDiagram.App", "Resources", "Strings.resx"));
        var english = KeysInResource(Path.Combine(root, "DuetDiagram.App", "Resources", "Strings.en.resx"));

        code.Should().NotBeEmpty("代码里总得有取文字的地方");
        neutral.Should().NotBeEmpty("中性资源里总得有键");

        neutral.Should().BeEquivalentTo(code, "资源里的键与代码里取用的键要一一对应");
        english.Should().BeSubsetOf(neutral, "第二语言只允许少，不允许多——多出来的键没人取得到");
        english.Count.Should().BeGreaterThanOrEqualTo(40, "第二语言要真的成一份，只有几条说明不了这条路通");
    }

    [Fact]
    [Trait("Category", "I18n")]
    public void Every_property_on_the_strings_facade_resolves()
    {
        // 属性名写错编译不过，但**键**写错编译得过——只有取的时候才会发现。
        // 逐个取一遍，缺的键当场抛出来。
        var properties = typeof(Strings)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(string) && property.Name != nameof(Strings.Language))
            .ToList();

        properties.Should().NotBeEmpty("取文字的属性总得有几个");

        foreach (var language in Strings.Languages)
        {
            Strings.Language = language;

            foreach (var property in properties)
            {
                var value = property.GetValue(null) as string;

                value.Should().NotBeNullOrWhiteSpace(
                    $"{property.Name} 在 {language} 下要取得到文字——缺键会回落中性资源，两边都没有才是错");
            }
        }

        Strings.Language = Strings.Chinese;
    }

    [Fact]
    [Trait("Category", "I18n")]
    public void The_registry_files_carry_no_hardcoded_visible_text()
    {
        // 已迁的那几个文件里不许再冒出中文：新加一条条目时忘了写键，屏幕上照样有字，
        // 而它不会跟着语言变——只有这一条能拦住。
        var root = RepositoryRoot();
        var migrated = new[]
        {
            Path.Combine("DuetDiagram.App", "Services", "MenuEntries.cs"),
            Path.Combine("DuetDiagram.App", "Services", "ContextEntries.cs"),
            Path.Combine("DuetDiagram.App", "Services", "MenuRegistry.cs"),
            Path.Combine("DuetDiagram.App", "Services", "AccessibleName.cs"),
        };

        var offenders = new List<string>();

        foreach (var relative in migrated)
        {
            foreach (var (line, text) in ChineseInStringLiterals(Path.Combine(root, relative)))
            {
                offenders.Add($"{relative}:{line} {text}");
            }
        }

        offenders.Should().BeEmpty("这几个文件里的界面文字要从资源里取，不能再写字面量");
    }

    [Fact]
    [Trait("Category", "I18n")]
    public void The_markup_files_have_no_visible_chinese_left()
    {
        // 标记里的可见文字一律走资源引用。两处不受此限：
        // **注释**是给改代码的人看的，不是屏幕上会出现的字；
        // **可访问名称**是另一层的东西（它只进无障碍树，不进画面），
        // 标记里那几处要与代码里那几十处一起迁，只迁标记这一半会让那一层半新半旧。
        var root = RepositoryRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(root, "DuetDiagram.App"), "*.axaml", SearchOption.AllDirectories))
        {
            var markup = StripXmlComments(File.ReadAllText(file));

            foreach (Match match in Regex.Matches(markup, "=\"[^\"]*[\\u4e00-\\u9fff][^\"]*\""))
            {
                var attribute = AttributeName(markup, match.Index);

                if (attribute is "AutomationProperties.Name" or "AutomationProperties.HelpText")
                {
                    continue;
                }

                offenders.Add($"{Path.GetRelativePath(root, file)} {attribute}{match.Value}");
            }
        }

        offenders.Should().BeEmpty("界面标记里的可见文字要从资源里取");
    }

    /// <summary>某个属性值往前数，紧挨着它的那个属性名。</summary>
    private static string AttributeName(string markup, int valueIndex)
    {
        var equals = markup.LastIndexOf('=', valueIndex);
        var start = equals;

        while (start > 0 && (char.IsLetterOrDigit(markup[start - 1])
            || markup[start - 1] is '.' or ':' or '_'))
        {
            start--;
        }

        return markup[start..equals];
    }

    #endregion

    #region 换语言之后界面真的变

    [Fact]
    [Trait("Category", "I18n")]
    public async Task Switching_the_language_changes_the_menu_and_the_panel_titles()
    {
        // 这一条是本任务的判据本身：换一种语言，屏幕上的字真的换了。
        // 只断言"取到了非空字符串"是不够的——硬编码的中文也非空。
        await HeadlessFixture.Run(() =>
        {
            var chinese = Snapshot();

            try
            {
                Strings.Language = Strings.English;

                var english = Snapshot();

                english.Tops.Should().NotEqual(chinese.Tops, "顶级菜单的字要跟着语言变");
                english.LayersTitle.Should().NotBe(chinese.LayersTitle, "面板标题要跟着语言变");
                english.PaletteTitle.Should().NotBe(chinese.PaletteTitle, "面板标题要跟着语言变");
                english.UndoLabel.Should().NotBe(chinese.UndoLabel, "条目文字要跟着语言变");

                // 光"变了"还不够：要变成第二语言里写的那一句，才说明取的是资源而不是别的东西。
                english.Tops.Should().Equal(MenuGroups.MenuOrder.Select(MenuGroups.Display));
                english.LayersTitle.Should().Be(Strings.Get("panel.layers.title"));
                english.PaletteTitle.Should().Be(Strings.Get("panel.palette.title"));
            }
            finally
            {
                Strings.Language = Strings.Chinese;
            }
        });
    }

    /// <summary>
    /// 两份资源里都没有的键要当场抛，不许给空串。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这一条原先验的是「第二语言缺某条时回落中性资源」。**现在第二语言是齐的**——
    /// 两份资源逐键对得上，那条回落分支没有现成的例子可举。
    /// 为了让用例有东西可验而故意少翻一条，等于让产品去迁就用例，所以不那么做。
    /// </para>
    /// <para>
    /// 因此这里验的是这条链的末端：中性资源里也没有的键要抛
    /// <see cref="MissingManifestResourceException"/>。给空串的话，屏幕上会出现一个
    /// 空白的按钮，而没有任何地方说得出它是从哪来的。
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Category", "I18n")]
    public async Task A_key_that_is_in_neither_resource_is_an_error_not_a_blank()
    {
        await HeadlessFixture.Run(() =>
        {
            foreach (var language in Strings.Languages)
            {
                Strings.Language = language;

                try
                {
                    var read = () => Strings.Get("menu.refusal.nothing-like-this");

                    read.Should().Throw<MissingManifestResourceException>();
                }
                finally
                {
                    Strings.Language = Strings.Chinese;
                }
            }
        });
    }

    [Fact]
    [Trait("Category", "I18n")]
    public void An_unknown_language_code_is_refused()
    {
        // 认不出的代码默默退回中文的话，配置里写错一个字母的表现是"界面没变"，
        // 而没人会想到去查那个字母。
        var set = () => Strings.Language = "fr";

        set.Should().Throw<ArgumentException>().WithParameterName("value");
        Strings.Language.Should().Be(Strings.Chinese, "抛出去之后语言不该被改动");
    }

    [Fact]
    [Trait("Category", "I18n")]
    public async Task The_group_identity_does_not_change_with_the_language()
    {
        // 档名换语言会变，但档的**标识**不能变：注册表按标识分组、用例按标识找控件。
        // 拿显示出来的字当标识的话，换一种语言就找不到自己了。
        await HeadlessFixture.Run(() =>
        {
            var before = MenuRegistry.Default.On(MenuSurface.Menu, MenuGroups.Edit).Select(entry => entry.Id).ToList();

            before.Should().NotBeEmpty();

            try
            {
                Strings.Language = Strings.English;

                MenuGroups.Display(MenuGroups.Edit).Should().Be(Strings.Get("menu.groupname.edit"));
                MenuRegistry.Default.On(MenuSurface.Menu, MenuGroups.Edit).Select(entry => entry.Id)
                    .Should().Equal(before, "换语言不该改到分组的结果");
            }
            finally
            {
                Strings.Language = Strings.Chinese;
            }
        });
    }

    [Fact]
    [Trait("Category", "I18n")]
    public async Task The_menu_and_the_tool_bar_show_the_same_label_in_either_language()
    {
        // 两处读的是同一份注册表、同一个资源键，所以同一条条目在两处的字必须一样。
        // 各取各的键的话，屏幕上会出现"菜单里叫撤销、工具栏上叫退回"。
        await HeadlessFixture.Run(() =>
        {
            foreach (var language in Strings.Languages)
            {
                try
                {
                    Strings.Language = language;

                    var window = HeadlessFixture.Open();
                    var menu = HeadlessFixture.MenuBar(window);
                    var toolBar = HeadlessFixture.ToolBar(window);

                    foreach (var entry in MenuRegistry.Default.Entries
                        .Where(entry => entry.Surface == MenuSurface.Both))
                    {
                        var item = menu.Find(entry.Id);
                        var button = toolBar.Find(entry.Id);

                        item.Should().NotBeNull();
                        button.Should().NotBeNull();

                        item!.Header?.ToString().Should().Be(entry.Label, $"{entry.Id} 在 {language} 下菜单里的字");
                        button!.Content?.ToString().Should().Be(entry.Label, $"{entry.Id} 在 {language} 下工具栏上的字");
                    }

                    window.Close();
                }
                finally
                {
                    Strings.Language = Strings.Chinese;
                }
            }
        });
    }

    #endregion

    #region 从右到左不破版

    [Fact]
    [Trait("Category", "I18n")]
    public async Task Right_to_left_mirrors_the_layout_without_changing_any_panel()
    {
        // 不变量全球化下文化给不出"这是从右到左"，所以方向由用例直接驱动。
        //
        // 判据不是"每块都在客户区里"：这个窗口尺寸下左栏本来就装不下（见下面那条注释），
        // 那种溢出左右两个方向一模一样，与方向无关。判据是**镜像之后逐块对得上**：
        // 尺寸不变、位置正好是绕窗口竖中线翻过来的、本来就放得下的翻过来也放得下。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            var leftToRight = Panels(window);

            leftToRight.Should().NotBeEmpty("窗口里总得有几块面板");

            window.FlowDirection = FlowDirection.RightToLeft;
            window.CaptureRenderedFrame();

            var rightToLeft = Panels(window);
            var width = window.Bounds.Size.Width;
            var offenders = new List<string>();

            foreach (var (name, mirrored) in rightToLeft)
            {
                if (leftToRight.TryGetValue(name, out var original) is false)
                {
                    offenders.Add($"{name}：从左到右时没量到它");
                    continue;
                }

                if (Math.Abs(mirrored.Width - original.Width) > 0.5 || Math.Abs(mirrored.Height - original.Height) > 0.5)
                {
                    offenders.Add($"{name}：尺寸变了 {original.Size} → {mirrored.Size}");
                }

                var expected = width - original.X - original.Width;

                if (Math.Abs(mirrored.X - expected) > 0.5 || Math.Abs(mirrored.Y - original.Y) > 0.5)
                {
                    offenders.Add($"{name}：位置 {mirrored.Position} 不是 {original.Position} 绕竖中线翻过来的（该在 {expected}, {original.Y}）");
                }

                var fitsLeftToRight = original.X >= -0.5 && original.X + original.Width <= width + 0.5;
                var fitsRightToLeft = mirrored.X >= -0.5 && mirrored.X + mirrored.Width <= width + 0.5;

                if (fitsLeftToRight && fitsRightToLeft is false)
                {
                    offenders.Add($"{name}：从左到右放得下（{original}），翻过来放不下了（{mirrored}）");
                }
            }

            offenders.Should().BeEmpty(
                "从右到左该是同一份布局绕竖中线翻过来，不该有任何一块错位或改尺寸；"
                + $"客户区 {window.Bounds.Size}；从左到右 {Show(leftToRight)}；从右到左 {Show(rightToLeft)}");

            HeadlessFixture.Canvas(window).Bounds.Width.Should().BeGreaterThan(0, "画布那一格在从右到左之下也要有地方");

            window.FlowDirection = FlowDirection.LeftToRight;
            window.Close();
        });
    }

    #endregion

    #region 辅助

    /// <summary>当前语言下屏幕上那几处文字。</summary>
    private static (IReadOnlyList<string> Tops, string LayersTitle, string PaletteTitle, string UndoLabel) Snapshot()
    {
        var window = HeadlessFixture.Open();

        var tops = HeadlessFixture.MenuBar(window).TopLevels;
        var layers = Title(HeadlessFixture.Layers(window));
        var palette = Title(HeadlessFixture.Palette(window));
        var undo = HeadlessFixture.MenuBar(window).Find("edit.undo")?.Header?.ToString() ?? string.Empty;

        window.Close();

        return (tops, layers, palette, undo);
    }

    /// <summary>一块面板里那个说"这一块是什么"的标题。</summary>
    /// <remarks>
    /// 取的是面板里最靠上的那个加粗文本：这几块面板的标题都是这么摆的，
    /// 而按控件名取的话，名字是给读屏用的，与屏幕上的字是两回事。
    /// </remarks>
    private static string Title(Control panel) =>
        panel.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.FontWeight >= FontWeight.SemiBold)
            .Select(text => text.Text ?? string.Empty)
            .FirstOrDefault(text => text.Length > 0)
        ?? string.Empty;

    /// <summary>窗口里那几块面板在窗口坐标里的矩形。</summary>
    /// <remarks>
    /// **两个角都要量，不能只量左上角再拿尺寸拼。**
    /// 从右到左不是把每个控件挪个位置，而是给整棵树挂一层镜像；
    /// 镜像之下左上角量出来的那个点已经跑到框的右边去了，再往右加一个宽度，
    /// 得到的是一个真实位置上不存在的框——左栏会被算成"越出右边界"。
    /// 量两个角交给矩形归一化，左右两种方向下都得到真实占位。
    /// </remarks>
    private static Dictionary<string, Rect> Panels(Window window)
    {
        var found = new Dictionary<string, Rect>(StringComparer.Ordinal);

        foreach (var (name, control) in new (string, Control)[]
        {
            ("图层面板", HeadlessFixture.Layers(window)),
            ("形状面板", HeadlessFixture.Shapes(window)),
            ("模板面板", HeadlessFixture.Templates(window)),
            ("调色板面板", HeadlessFixture.Palette(window)),
            ("文本预设面板", HeadlessFixture.Presets(window)),
            ("属性面板", HeadlessFixture.Properties(window)),
            ("标签栏", HeadlessFixture.PageTabs(window)),
            ("工具栏", HeadlessFixture.ToolBar(window)),
            ("菜单栏", HeadlessFixture.MenuBar(window)),
            ("画布", HeadlessFixture.Canvas(window)),
        })
        {
            var size = control.Bounds.Size;
            var first = control.TranslatePoint(new Point(0, 0), window);
            var second = control.TranslatePoint(new Point(size.Width, size.Height), window);

            if (first is { } corner && second is { } opposite)
            {
                // 自己归一化：矩形那两个角的先后在镜像之下是反的，
                // 直接拼会得到一个宽度为负的框，而负宽度看着像"尺寸变了"。
                found[name] = new Rect(
                    Math.Min(corner.X, opposite.X),
                    Math.Min(corner.Y, opposite.Y),
                    Math.Abs(opposite.X - corner.X),
                    Math.Abs(opposite.Y - corner.Y));
            }
        }

        return found;
    }

    /// <summary>把量到的矩形排成一行，失败信息里用。</summary>
    private static string Show(Dictionary<string, Rect> panels) =>
        string.Join("、", panels.Select(pair => $"{pair.Key}={pair.Value}"));

    /// <summary>代码里取用的资源键，按出现顺序。</summary>
    /// <remarks>
    /// 带占位符的那几句走 <c>Format</c>，其余走 <c>Get</c>，两种都要算进来——
    /// 只认一种的话，另一种用的键会变成"资源里有、代码里没人取"，而那是假红。
    /// </remarks>
    private static List<string> KeysUsedInCode(string file) =>
        [.. Regex.Matches(File.ReadAllText(file), "(?:Get|Format)\\(\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)];

    /// <summary>资源文件里声明的键。</summary>
    private static List<string> KeysInResource(string file) =>
        [.. Regex.Matches(File.ReadAllText(file), "<data name=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)];

    /// <summary>代码里那些字符串字面量中的中文，跳过注释与异常消息。</summary>
    /// <remarks>
    /// <para>
    /// 逐字符走一遍而不是按行找子串：注释里出现中文是正当的（这一段就是这么写的），
    /// 而"这一行里有引号"并不等于"引号里那句是界面文字"。
    /// </para>
    /// <para>
    /// **异常消息不算界面文字。** 它只给改代码的人看，界面上不会出现——
    /// 界面上出现的失败提示走的是错误码呈现表那一套。把它算进来的话，
    /// 这一条会逼着人把异常消息也搬进资源，而那些句子没人翻。
    /// </para>
    /// </remarks>
    private static List<(int Line, string Text)> ChineseInStringLiterals(string file)
    {
        var text = File.ReadAllText(file);
        var found = new List<(int, string)>();
        var builder = new StringBuilder();
        var line = 1;
        var start = 1;
        var statementStart = 0;
        var inString = false;
        var index = 0;

        while (index < text.Length)
        {
            var current = text[index];

            if (inString is false && current == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                while (index < text.Length && text[index] != '\n')
                {
                    index++;
                }

                continue;
            }

            if (inString is false && current == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                index += 2;

                while (index + 1 < text.Length && (text[index] != '*' || text[index + 1] != '/'))
                {
                    if (text[index] == '\n')
                    {
                        line++;
                    }

                    index++;
                }

                index += 2;
                continue;
            }

            if (inString is false && current is ';' or '{' or '}')
            {
                statementStart = index + 1;
            }

            if (current == '"' && (index == 0 || text[index - 1] != '\\'))
            {
                if (inString)
                {
                    var literal = builder.ToString();
                    var statement = text[statementStart..index];

                    if (literal.Any(IsChinese)
                        && statement.Contains("throw new", StringComparison.Ordinal) is false)
                    {
                        found.Add((start, literal));
                    }
                }
                else
                {
                    start = line;
                    builder.Clear();
                }

                inString = !inString;
                index++;
                continue;
            }

            if (current == '\n')
            {
                line++;
            }

            if (inString)
            {
                builder.Append(current);
            }

            index++;
        }

        return found;
    }

    private static bool IsChinese(char value) => value is >= '\u4e00' and <= '\u9fff';

    /// <summary>把标记里的注释整段去掉，剩下的才是屏幕上会出现的属性值。</summary>
    private static string StripXmlComments(string markup) =>
        Regex.Replace(markup, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    /// <summary>从测试程序集的位置逐级上溯，找到含解决方案文件的目录。</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DuetDiagram.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("从测试程序集的位置找不到仓库根。");
    }

    #endregion
}
