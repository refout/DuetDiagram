using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DuetDiagram.App;
using DuetDiagram.App.Controls;
using DuetDiagram.App.ViewModels;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 属性面板：选中元素后它出现，改字段走命令层写回文档，多选时共用字段与"多个值"。
/// </summary>
/// <remarks>
/// <para>
/// 这一层只验"面板在真实窗口里接上了没有"：选一个节点后面板上要有六个分节、
/// 改一个字段要真让文档版本加一、多选下取值不一致的字段要报"多个值"。
/// 字段怎么分发、校验怎么走由应用层与命令层各自的单元测试管。
/// </para>
/// <para>
/// 改字段一律经由界面上的控件：把它的值换掉，再触发它失焦，让控件自己去报。
/// 直接调命令层的话，验的是命令层而不是面板——而面板这一轮要验的正是
/// "控件把字送到了命令层"。
/// </para>
/// </remarks>
public sealed class PropertyPanelTests
{
    /// <summary>七个分节的标题，按界面上的顺序。</summary>
    private static readonly string[] ExpectedSections =
    [
        "形状",
        "样式与调色板",
        "文本与字体",
        "布局约束",
        "图层",
        "端口",
        "动作与链接",
    ];

    #region 选中后出现

    [Fact]
    [Trait("Category", "PropertyPanel")]
    public async Task Selecting_a_node_shows_six_sections()
    {
        // 这一条在验收里逐字写着：选一个节点后面板出现六个分节。
        // 少一节的话读者分不清"这一轮没做"还是"那节漏接了"。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var panel = HeadlessFixture.Properties(window);

            // 没选中时面板不该有任何可改控件，否则看起来像点哪都改得到。
            window.Properties.HasSelection.Should().BeFalse();

            window.Session.Select("start");

            window.Properties.HasSelection.Should().BeTrue("选中了一个节点，面板就该有内容");
            window.Properties.Sections.Count.Should().Be(7, "节点属性分七节：形状、样式、文本、约束、图层、端口、动作");
            window.Properties.Sections.Select(s => s.Title)
                .Should().Equal(ExpectedSections, "分节的顺序与标题是固定的，调一下顺序用例就喊得出名字");
            panel.IsVisible.Should().BeTrue("面板要在界面上真的看得见");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "PropertyPanel")]
    public async Task The_panel_reads_the_selected_nodes_values()
    {
        // 面板显示的值要来自文档，而不是上次敲进去的字。这里只验"读对了"，
        // 写回去由下面的用例验。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            window.Session.Select("start");

            Field(window, "label").Value.Should().Be("开始", "选中节点上写着「开始」");
            Field(window, "shape").Value.Should().Be("Stadium", "示例里 start 是个 stadium 形状");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "PropertyPanel")]
    public async Task The_layer_section_shows_where_the_selection_lives()
    {
        // 归属在这里只显示、不给控件：改它的入口在图层面板上（那里看得到全部图层，
        // 也多选之后一次移入）。可选取值的下拉做不到——那段取值表是编译期写死的，
        // 而图层标识是运行期才有的。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            window.Session.Select("start");

            Section(window, "图层").IsReadOnly.Should().BeTrue("这一节没有可改控件");
            Text(window, "图层").Should().Contain("缺省层", "示例里的节点不属于任何图层");

            var created = window.Session.CreateLayer("上层");
            var layerId = created.AffectedIds[0];

            window.Session.AssignLayer(["start"], layerId);

            Text(window, "图层").Should().Contain("上层", "归属换了，这一节跟着换");
            Text(window, "图层").Should().Contain("图层面板", "它要说清改归属去哪里改");

            window.Close();
        });
    }

    #endregion

    #region 改字段写回文档

    [Fact]
    [Trait("Category", "PropertyPanel")]
    public async Task Editing_a_field_increments_the_document_version()
    {
        // 验收第二条：改一个字段后文档版本加一。版本不涨说明字没写进命令层，
        // 而命令层是唯一能改文档的地方。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            window.Session.Select("start");

            var before = window.Session.Document.Version;
            var box = HeadlessFixture.Editor<TextBox>(window, "label");

            box.Text = "改名了";
            Commit(box);

            window.Session.Document.Version.Should().BeGreaterThan(
                before,
                "改了标签，文档版本要跟着涨——那是命令层写回文档留下的痕迹");

            // 同一份改动要落到文档上，不只是版本号动了。
            window.Session.Document.Nodes.Single(n => n.Id == "start").Label
                .Should().Be("改名了", "面板改的字要真写进节点，而不是只在界面上跳一下");

            // 写回去之后面板上的值要跟着文档走，而不是停在用户敲的那一刻。
            Field(window, "label").Value.Should().Be("改名了");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "PropertyPanel")]
    public async Task A_rejected_value_is_not_written_and_stays_on_the_field()
    {
        // "描边粗细"是数值字段。给它一段不是数的字，命令会被拒，
        // 而面板要保留用户敲的那段字并显示错误——不然用户以为写进去了，其实没有。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            window.Session.Select("start");

            var before = window.Session.Document.Version;
            var box = HeadlessFixture.Editor<TextBox>(window, "style.weight");

            box.Text = "不是数";
            Commit(box);

            window.Session.Document.Version.Should().Be(
                before,
                "不合法的字段值不该写进文档，版本号不能偷偷涨");
            Field(window, "style.weight").HasError.Should().BeTrue("被拒的字段要在自己身上挂一条错误");

            window.Close();
        });
    }

    #endregion

    #region 多选

    [Fact]
    [Trait("Category", "PropertyPanel")]
    public async Task Multi_select_marks_fields_with_conflicting_values()
    {
        // 验收：值不一致的字段显示为「多个值」。start 与 end 的标签不一样，
        // 一起选中时"标签"这一项要报多个值，而不是随便挑其中一个显示。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            window.Session.Select("start");
            window.Session.Toggle("end");

            window.Properties.Title.Should().Be("选中 2 个元素");
            window.Properties.HasSelectionNote.Should().BeTrue("多选要写一句「改一个字段等于给它们都赋同一个值」");
            window.Properties.SelectionNote.Should().Contain("多个值", "那一句里要明说值不一致的字段显示为多个值");

            Field(window, "label").IsMixed.Should().BeTrue("两个节点标签不同，这一项要报多个值");
            Field(window, "shape").IsMixed.Should().BeFalse("两个节点形状都是 stadium，这一项不该报多个值");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "PropertyPanel")]
    public async Task Editing_in_multi_select_writes_to_every_selected_node()
    {
        // 多选下改一个字段等于给每个选中节点赋同一个值。不验这个的话，
        // 一个"只写给当前那个节点"的实现也能通过上面的用例，而用户会以为全改了。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            window.Session.Select("start");
            window.Session.Toggle("end");

            var box = HeadlessFixture.Editor<TextBox>(window, "label");

            box.Text = "统一名称";
            Commit(box);

            window.Session.Document.Nodes.Single(n => n.Id == "start").Label
                .Should().Be("统一名称", "多选编辑要写到每一个选中的节点");
            window.Session.Document.Nodes.Single(n => n.Id == "end").Label
                .Should().Be("统一名称", "另一个被选中节点也要跟着改");

            window.Close();
        });
    }

    #endregion

    /// <summary>把控件的值换掉并触发失焦，让控件把改动报上去。</summary>
    /// <remarks>
    /// 失焦是控件提交改动的时机之一（另一个是回车）。直接调命令层会绕过面板，
    /// 这条用例要验的正是控件把字送到了命令层。失焦事件的参数必须是
    /// <see cref="FocusChangedEventArgs"/>，否则事件路由在把参数转给处理器时会转型失败。
    /// </remarks>
    private static void Commit(TextBox box)
    {
        ArgumentNullException.ThrowIfNull(box);

        box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
    }

    /// <summary>按字段名从面板里取那个可编辑字段。</summary>
    private static PropertyFieldViewModel Field(MainWindow window, string name) =>
        window.Properties.Sections
            .SelectMany(s => s.Fields)
            .Single(f => string.Equals(f.Field, name, StringComparison.Ordinal));

    /// <summary>按标题取一个分节。</summary>
    private static PropertySectionViewModel Section(MainWindow window, string title) =>
        window.Properties.Sections.Single(s => string.Equals(s.Title, title, StringComparison.Ordinal));

    /// <summary>取一个只读分节上的那段文字。</summary>
    private static string Text(MainWindow window, string title) =>
        Section(window, title).Text ?? string.Empty;
}
