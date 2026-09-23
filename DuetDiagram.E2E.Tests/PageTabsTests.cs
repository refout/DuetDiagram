using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using DuetDiagram.App;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 标签栏：文档里有哪几页、翻页之后画布上剩什么、新建与删页。
/// </summary>
/// <remarks>
/// <para>
/// 这一层验的是"翻页真的换了一批元素"。命令层与渲染层各自有用例证明归属写进去了、
/// 绘制列表会跟着变；这里验的是整条链路——翻一页之后画布上确实是另一批东西。
/// </para>
/// <para>
/// 操作一律经由界面上的控件触发（页签与按钮都发一次真实的事件），
/// 这样验到的是"控件把动作送到了会话"。
/// </para>
/// </remarks>
public sealed class PageTabsTests
{
    #region 列表

    [Fact]
    [Trait("Category", "PageTabs")]
    public async Task The_tabs_list_the_pages_in_document_order()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            // 声明顺序与次序字段是反的：标签栏按次序排。
            window.Pages.Tabs.Select(tab => tab.Id).Should().Equal(["p1", "p2"]);
            window.Pages.CurrentPageId.Should().Be("p1", "没有翻过的时候缺省页就是当前页");
            window.Pages.Tabs[0].IsCurrent.Should().BeTrue();
            window.Pages.Tabs[1].IsCurrent.Should().BeFalse();

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "PageTabs")]
    public async Task The_tabs_are_on_screen()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            HeadlessFixture.PageTabs(window).IsVisible.Should().BeTrue("标签栏常驻，不靠开关显隐");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "PageTabs")]
    public async Task A_tab_says_how_many_elements_are_on_that_page()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            // 翻页之后给一个"这一页不是空的"的凭据：页签本身只说名字，
            // 而名字看不出来里面有没有东西。
            window.Pages.CurrentElementCount.Should().Be(1, "p1 上放着一个节点");

            Press(window, "page.tab.p2");

            window.Pages.CurrentElementCount.Should().Be(2, "p2 上放着两个节点");

            window.Close();
        });
    }

    #endregion

    #region 翻页

    [Fact]
    [Trait("Category", "PageTabs")]
    public async Task Switching_a_tab_leaves_only_that_pages_elements_on_the_canvas()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            Drawn(window).Should().Contain("a");
            Drawn(window).Should().NotContain("b");

            Press(window, "page.tab.p2");

            // 翻页之后画布上只剩这一页的东西。别的页面上的元素不进布局、也不进绘制列表。
            Drawn(window).Should().Contain("b");
            Drawn(window).Should().Contain("c");
            Drawn(window).Should().NotContain("a");

            Press(window, "page.tab.p1");

            Drawn(window).Should().Contain("a");
            Drawn(window).Should().NotContain("b");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "PageTabs")]
    public async Task An_edge_between_pages_is_drawn_on_neither()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            // 那条边的一端在 p1、另一端在 p2。两页都不该画它。
            foreach (var page in new[] { "p1", "p2" })
            {
                Press(window, $"page.tab.{page}");

                Drawn(window).Should().NotContain("e", $"{page}：跨页的边哪一页都不画");
            }

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "PageTabs")]
    public async Task Selecting_everything_only_takes_the_current_page()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            // 全选只收当前这一页上的元素：不在这一页上的点不中，
            // 留着它们会让属性面板显示一个画布上根本看不见的东西的字段。
            window.Session.SetSelection(window.Session.AllNodeIds);

            window.Session.SelectedIds.Should().Equal(["a"], "第一页上只有它一个");

            Press(window, "page.tab.p2");

            // 翻了页之后，上一次选中的那个已经不在这一页上了，于是一个都不剩。
            window.Session.SelectedIds.Should().BeEmpty();

            window.Session.SetSelection(window.Session.AllNodeIds);

            window.Session.SelectedIds.Should().BeEquivalentTo("b", "c");
        });
    }

    #endregion

    #region 新建与删页

    [Fact]
    [Trait("Category", "PageTabs")]
    public async Task Creating_a_page_switches_to_it()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            Fill(window, "page.new-name", "第三页");

            Count(window, "page.new").Should().Be(1, "新建一页进一条历史");

            window.Pages.Tabs.Should().HaveCount(3, "新的那一页出现在标签栏上");
            window.Pages.CurrentPageName.Should().Be("第三页", "刚建一页，接下来多半要往里放东西");

            // 新页上什么都没有，画布上因此是空的。
            Drawn(window).Should().BeEmpty();

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "PageTabs")]
    public async Task Deleting_the_current_page_falls_back_to_the_default()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            Press(window, "page.tab.p2");

            Count(window, "page.delete").Should().Be(1, "删一页进一条历史");

            window.Pages.Tabs.Select(tab => tab.Id).Should().Equal(["p1"]);
            window.Pages.CurrentPageId.Should().Be("p1", "当前页没了就回缺省页");

            // 删页不是删元素：原来在 p2 上的那两个退回缺省页，因此现在画在第一页上。
            Drawn(window).Should().Contain("b");
            Drawn(window).Should().Contain("c");
            window.Session.Document.Nodes.Should().HaveCount(3);

            window.Session.Undo();

            window.Pages.Tabs.Select(tab => tab.Id).Should().Equal(["p1", "p2"], "撤销之后页面回来了");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "PageTabs")]
    public async Task The_last_page_cannot_be_deleted()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            Press(window, "page.tab.p2");
            Press(window, "page.delete");

            // 只剩一页时那个按钮点不动：页面集合为空之后渲染层无页面可画。
            ById<Button>(window, "page.delete").IsEnabled.Should().BeFalse();
            window.Pages.CanDelete.Should().BeFalse();

            window.Close();
        });
    }

    #endregion

    #region 只读

    [Fact]
    [Trait("Category", "PageTabs")]
    public async Task A_read_only_window_refuses_the_writes_but_still_lists()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();
            var path = temp.File("pages.json");

            File.WriteAllText(path, DiagramSerializer.SerializeFull(Document()));

            // 冒充另一个进程：先拿住这份文档的独占所有权。
            using var other = DocumentLock.Acquire(path);

            var window = new MainWindow(DocumentLaunch.File(path));

            window.Show();
            window.CaptureRenderedFrame();

            window.Session.IsReadOnly.Should().BeTrue("另一个进程正在编辑这份文档");

            // 标签照样看得见、也翻得动——翻页不写文档。
            window.Pages.Tabs.Should().HaveCount(2);
            window.Pages.HasReadOnlyNote.Should().BeTrue();
            window.Pages.Select("p2").Should().BeTrue();

            window.Pages.Create("新页").IsSuccess.Should().BeFalse();
            window.Pages.DeleteCurrent().IsSuccess.Should().BeFalse();

            window.Session.Document.Pages.Should().HaveCount(2);

            window.Close();
        });
    }

    #endregion

    #region 辅助

    /// <summary>画布上现在画着的元素标识。</summary>
    private static string[] Drawn(MainWindow window) =>
        [.. window.Model.DrawList.Commands.Select(command => command.ElementId).Distinct(StringComparer.Ordinal)];

    private static T ById<T>(MainWindow window, string id)
        where T : Control =>
        HeadlessFixture.Editor<T>(window, id);

    private static void Fill(MainWindow window, string id, string text) =>
        ById<TextBox>(window, id).Text = text;

    /// <summary>点一个按钮或页签。</summary>
    /// <returns>这一下之后历史里多了几条。</returns>
    private static int Press(MainWindow window, string id)
    {
        ById<Button>(window, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        return 0;
    }

    /// <summary>点一个按钮，并数它进了几条历史。</summary>
    private static int Count(MainWindow window, string id)
    {
        var before = window.Session.Bus.Context.History.UndoEntries().Count;

        ById<Button>(window, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        return window.Session.Bus.Context.History.UndoEntries().Count - before;
    }

    /// <summary>
    /// 开一个窗口，看一份带两页的文档。
    /// </summary>
    /// <remarks>
    /// 每次各建一个目录，于是路径不同、工作区不同——共用的话，前一个用例翻过页、
    /// 删过页的那一份会被后一个用例拿到。
    /// </remarks>
    private static MainWindow Open()
    {
        var temp = new TempDirectory();
        var path = temp.File("pages.json");

        File.WriteAllText(path, DiagramSerializer.SerializeFull(Document()));

        var window = new MainWindow(DocumentLaunch.File(path));

        window.Show();
        window.CaptureRenderedFrame();

        return window;
    }

    /// <summary>
    /// 两页的一份文档：第一页上一个节点，第二页上两个，另加一条跨页的边。
    /// </summary>
    /// <remarks>
    /// 页面按次序字段排，所以声明顺序故意与次序反过来。
    /// </remarks>
    private static DiagramDocument Document() =>
        DiagramDocument.CreateFromContent(
            "pages",
            DiagramKind.Flowchart,
            Direction.TB,
            pages:
            [
                new PageDef { Id = "p2", Name = "第二页", Order = 1 },
                new PageDef { Id = "p1", Name = "第一页", Order = 0 },
            ],
            nodes:
            [
                new NodeDef { Id = "a", Label = "甲", Page = "p1" },
                new NodeDef { Id = "b", Label = "乙", Page = "p2" },
                new NodeDef { Id = "c", Label = "丙", Page = "p2" },
            ],
            edges:
            [
                new EdgeDef { Id = "e", From = "a", To = "b" },
                new EdgeDef { Id = "e2", From = "b", To = "c" },
            ]);

    #endregion
}
