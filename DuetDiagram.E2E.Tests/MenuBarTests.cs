using Avalonia.Controls;
using Avalonia.Headless;
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
/// 菜单栏：顶级菜单、与工具栏的一致性、只读时哪些条目点不动。
/// </summary>
/// <remarks>
/// 菜单栏与工具栏读的是同一份条目表，所以"两处对同一件事给出同样的启用判据"
/// 是这一层最该验的一条——各读一份的话，两边各自都自洽，
/// 只有把两份摆在一起才看得出来。
/// </remarks>
public sealed class MenuBarTests
{
    #region 摆出来的东西

    [Fact]
    [Trait("Category", "MenuBar")]
    public async Task The_menu_shows_every_group_in_a_fixed_order()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            HeadlessFixture.MenuBar(window).TopLevels.Should().Equal(MenuGroups.MenuOrder);

            var expected = MenuGroups.MenuOrder
                .SelectMany(group => MenuRegistry.Default.On(MenuSurface.Menu, group))
                .Select(entry => entry.Id);

            HeadlessFixture.MenuBar(window).Ids.Should().Equal(expected);

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "MenuBar")]
    public async Task Both_surfaces_agree_on_what_can_be_clicked()
    {
        // 菜单里能点、工具栏上是灰的——两种写法各自都自洽，只有把两份摆在一起才看得出来。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var menu = HeadlessFixture.MenuBar(window);
            var toolBar = HeadlessFixture.ToolBar(window);

            var shared = MenuRegistry.Default.Entries
                .Where(entry => entry.Surface == MenuSurface.Both)
                .ToList();

            shared.Should().NotBeEmpty();

            foreach (var entry in shared)
            {
                var fromMenu = menu.Find(entry.Id);
                var fromToolBar = toolBar.Find(entry.Id);

                fromMenu.Should().NotBeNull($"{entry.Id} 两处都该有");
                fromToolBar.Should().NotBeNull($"{entry.Id} 两处都该有");

                fromMenu!.IsEnabled.Should().Be(fromToolBar!.IsEnabled, $"{entry.Id} 在两处的启用状态要一致");
            }

            window.Session.Select("pass");
            menu.Refresh();
            toolBar.Refresh();

            foreach (var entry in shared)
            {
                menu.Find(entry.Id)!.IsEnabled.Should()
                    .Be(toolBar.Find(entry.Id)!.IsEnabled, $"{entry.Id} 换选中之后两处仍然要一致");
            }

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "MenuBar")]
    public async Task An_entry_without_a_selection_explains_itself()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var delete = HeadlessFixture.MenuBar(window).Find("edit.delete");

            delete.Should().NotBeNull();
            delete!.IsEnabled.Should().BeFalse();
            ToolTip.GetTip(delete).Should().Be("先选中要删的元素");

            window.Close();
        });
    }

    #endregion

    #region 只读

    [Fact]
    [Trait("Category", "MenuBar")]
    public async Task A_read_only_window_refuses_the_writes_and_keeps_the_rest()
    {
        // 只读时该挡的挡住，不该挡的别挡：全选、清空选择与重排都不改文档，
        // 一并挡住的话，用户在一份只读的图上连看都看不舒服。
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();
            var path = temp.File("doc.json");

            File.WriteAllText(path, DiagramSerializer.SerializeFull(SampleDiagram.Document()));

            // 冒充另一个进程：先拿住这份文档的独占所有权。
            using var other = DocumentLock.Acquire(path);

            var window = new MainWindow(DocumentLaunch.File(path));

            window.Show();
            window.CaptureRenderedFrame();

            window.Session.IsReadOnly.Should().BeTrue("另一个进程正在编辑这份文档");
            window.Session.SetSelection(["pass", "fail"]);

            var menu = HeadlessFixture.MenuBar(window);

            menu.Refresh();

            foreach (var id in new[] { "edit.delete", "align.same-rank", "align.align", "layout.direction-lr", "layout.spacing-tight" })
            {
                menu.Find(id)!.IsEnabled.Should().BeFalse($"{id} 要改文档，只读时点不动");
            }

            foreach (var id in new[] { "edit.select-all", "edit.select-none", "layout.relayout", "file.new-window" })
            {
                menu.Find(id)!.IsEnabled.Should().BeTrue($"{id} 不改文档，只读时照样能用");
            }

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "MenuBar")]
    public async Task A_read_only_refusal_uses_the_same_sentence_as_the_error_table()
    {
        // 界面上的"不能点"与命令层拒绝写入时给的那句话必须是同一句。
        // 两处各写一句的话，用户会以为遇到的是两个问题。
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();
            var path = temp.File("doc.json");

            File.WriteAllText(path, DiagramSerializer.SerializeFull(SampleDiagram.Document()));

            using var other = DocumentLock.Acquire(path);

            var window = new MainWindow(DocumentLaunch.File(path));

            window.Show();
            window.CaptureRenderedFrame();

            window.Session.SetSelection(["pass"]);

            var delete = HeadlessFixture.MenuBar(window).Find("edit.delete");

            delete.Should().NotBeNull();
            ToolTip.GetTip(delete!).Should().Be(ErrorPresenterTable.For(ErrorCodes.DocumentReadOnly).Message);

            // 真去点一下：拒绝的理由要摆到状态栏上，而不是只在悬停时才看得见。
            window.Invoke("edit.delete");

            window.Status.HasMessage.Should().BeTrue();
            window.Status.Message.Should().Contain(ErrorPresenterTable.For(ErrorCodes.DocumentReadOnly).Message);
            window.Session.Document.Nodes.Should().HaveCount(5, "被拒的操作一个字都不该写进文档");

            window.Close();
        });
    }

    #endregion
}
