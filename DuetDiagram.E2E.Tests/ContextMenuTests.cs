using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using DuetDiagram.App;
using DuetDiagram.App.Controls;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 右键菜单：条目随点在哪里变，点下去真的做那件事。
/// </summary>
/// <remarks>
/// <para>
/// 这一层验的是"右键真的把菜单弹出来了、里面摆的正是该摆的那几条、点下去真的发命令"。
/// </para>
/// <para>
/// 条目从注册表来，所以这里按标识断言（<c>context.…</c>），不按显示的字——
/// 改一个字不会让用例失败，而少一条会。
/// </para>
/// </remarks>
public sealed class ContextMenuTests
{
    #region 两套条目

    [Fact]
    [Trait("Category", "ContextMenu")]
    public async Task Right_clicking_blank_space_offers_the_blank_entries()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            Open(window, canvas, onElement: false);

            Ids(canvas).Should().Equal(
                "context.edit.select-all",
                "context.edit.select-none",
                "context.group.create-lane");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ContextMenu")]
    public async Task Right_clicking_an_element_offers_the_element_entries()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            Open(window, canvas, onElement: true);

            // 四类组合各有各的条目，不合成一个「新建组合」——它们的参数与语义都不同。
            Ids(canvas).Should().Equal(
                "context.edit.delete",
                "context.group.create-group",
                "context.group.create-lane",
                "context.group.create-subflow",
                "context.group.create-combo",
                "context.group.dissolve");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ContextMenu")]
    public async Task Right_clicking_does_not_change_the_selection()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            window.Session.Select("fail");

            // 右键不改选中：一次误触就换掉用户攒起来的选中集合，代价太大。
            Open(window, canvas, onElement: true);

            window.Session.SelectedIds.Should().Equal(["fail"]);

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ContextMenu")]
    public async Task An_entry_that_cannot_run_says_why()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            // 没有选中任何东西，而右击落在元素上（右键不选中）。
            Open(window, canvas, onElement: true);

            var delete = Item(canvas, "context.edit.delete");

            delete.IsEnabled.Should().BeFalse();
            delete.Header!.ToString().Should().Contain("删", "灰掉的那一条要把理由写在标题上");

            // 泳道可以在空白处建，所以它此刻仍然点得动：一条空泳道先划出来，
            // 再把节点拖进去是常见走法。
            Item(canvas, "context.group.create-lane").IsEnabled.Should().BeTrue();

            window.Close();
        });
    }

    #endregion

    #region 点下去真的做事

    [Fact]
    [Trait("Category", "ContextMenu")]
    public async Task Three_nodes_become_one_group_in_one_command()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            window.Session.SetSelection(window.Session.AllNodeIds);

            var members = window.Session.SelectedIds.ToArray();

            members.Should().HaveCountGreaterThan(2);

            Open(window, canvas, onElement: true);

            Click(window, canvas, "context.group.create-group")
                .Should().Be(1, "建分组是一条命令，不是「先建空的再逐个移入」");

            var group = window.Session.Document.Composites.Should().ContainSingle().Subject;

            group.Members.Should().BeEquivalentTo(members);
            group.Should().BeOfType<GroupDef>("四条组合条目各自建出各自那一类");

            // 选中不动：组合现在选不中（选中集合里只有节点），把选中清掉会让
            // 用户刚框起来的这几个东西一下子全没了。
            window.Session.SelectedIds.Should().BeEquivalentTo(members);

            window.Session.Undo();

            window.Session.Document.Composites.Should().BeEmpty("一次撤销把整组建回来");
            window.Session.Document.Nodes.Should().HaveCount(members.Length, "成员不跟着删");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ContextMenu")]
    public async Task Each_composite_entry_builds_its_own_kind()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);
            var expected = new (string Entry, Type Kind)[]
            {
                ("context.group.create-lane", typeof(LaneDef)),
                ("context.group.create-subflow", typeof(SubflowDef)),
                ("context.group.create-combo", typeof(ComboDef)),
            };

            foreach (var (entry, kind) in expected)
            {
                window.Session.SetSelection(["start", "check"]);
                Open(window, canvas, onElement: true);

                Click(window, canvas, entry).Should().Be(1, entry);
                window.Session.Document.Composites.Should().ContainSingle().Subject.Should().BeOfType(kind);

                window.Session.Undo();
            }

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ContextMenu")]
    public async Task An_empty_lane_can_be_created_from_blank_space()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            window.Session.SetSelection([]);
            Open(window, canvas, onElement: false);

            Click(window, canvas, "context.group.create-lane").Should().Be(1);

            window.Session.Document.Composites.Should().ContainSingle().Subject.Members.Should().BeEmpty();

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ContextMenu")]
    public async Task Dissolving_needs_the_right_click_to_land_on_a_composite()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            window.Session.SetSelection(["start", "check"]);
            window.Session.CreateGroup(window.Session.SelectedIds);

            var groupId = window.Session.Document.Composites[0].Id;

            // 右键落在一个节点上：解散点不动。判据看的是"右键落在谁身上"，
            // 而不是"选中了谁"——组合现在选不中，按选中判的话这一条永远点不动。
            var onNode = new MenuContext(window, "start");
            var onGroup = new MenuContext(window, groupId);

            Dissolve(window, "start").IsEnabled(onNode).Should().BeFalse();
            Dissolve(window, groupId).IsEnabled(onGroup).Should().BeTrue();

            var before = window.Session.Bus.Context.History.UndoEntries().Count;

            Dissolve(window, groupId).Run(onGroup);

            window.Session.Bus.Context.History.UndoEntries().Count.Should().Be(before + 1);
            window.Session.Document.Composites.Should().BeEmpty();
            window.Session.Document.Nodes.Select(node => node.Id)
                .Should().Contain(["start", "check"], "成员回到外层，不跟着删");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ContextMenu")]
    public async Task The_selection_entries_change_the_selection_without_a_command()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            window.Session.SetSelection([]);
            Open(window, canvas, onElement: false);

            var before = window.Session.Bus.Context.History.UndoEntries().Count;

            Click(window, canvas, "context.edit.select-all");

            window.Session.SelectedIds.Should().NotBeEmpty();

            // 全选与清空改的是选中，不是文档；两者都不该进历史。
            // 它们与菜单栏上那两条是同一份条目，所以这里也就验了"两处共用一份判据"。
            window.Session.Bus.Context.History.UndoEntries().Count.Should().Be(before);

            Open(window, canvas, onElement: false);
            Click(window, canvas, "context.edit.select-none");

            window.Session.SelectedIds.Should().BeEmpty();

            window.Close();
        });
    }

    #endregion

    #region 只读

    [Fact]
    [Trait("Category", "ContextMenu")]
    public async Task A_read_only_window_refuses_the_writes()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();
            var path = temp.File("menu.json");

            File.WriteAllText(path, DiagramSerializer.SerializeFull(SampleDiagram.Document()));

            using var other = DuetDiagram.Core.Workspace.DocumentLock.Acquire(path);

            var window = new MainWindow(DocumentLaunch.File(path));

            window.Show();
            window.CaptureRenderedFrame();

            window.Session.IsReadOnly.Should().BeTrue("另一个进程正在编辑这份文档");

            var canvas = HeadlessFixture.Canvas(window);

            Open(window, canvas, onElement: true);

            // 改文档的那几条都点不动，并把理由写在标题上。
            Item(canvas, "context.group.create-group").IsEnabled.Should().BeFalse();
            Item(canvas, "context.group.create-lane").IsEnabled.Should().BeFalse();
            Item(canvas, "context.edit.delete").IsEnabled.Should().BeFalse();

            window.Close();
        });
    }

    #endregion

    #region 辅助

    /// <summary>右键。落在元素上时点的是某个节点的中心，落在空白处时点的是画布正中。</summary>
    private static void Open(MainWindow window, DiagramCanvas canvas, bool onElement)
    {
        var local = onElement
            ? HeadlessFixture.CenterOf(canvas, "start")
            : HeadlessFixture.CenterOf(canvas);
        var point = HeadlessFixture.ToWindow(canvas, window, local);

        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
    }

    /// <summary>菜单里摆出来的条目标识，按顺序。</summary>
    private static string[] Ids(DiagramCanvas canvas)
    {
        var menu = canvas.ContextMenu ?? throw new InvalidOperationException("右键没有弹出菜单");

        return
        [
            .. menu.Items.OfType<MenuItem>()
                .Select(item => AutomationProperties.GetAutomationId(item) ?? item.Header?.ToString() ?? "?"),
        ];
    }

    /// <summary>按标识取菜单上的一条。</summary>
    private static MenuItem Item(DiagramCanvas canvas, string id) =>
        (canvas.ContextMenu ?? throw new InvalidOperationException("右键没有弹出菜单")).Items
            .OfType<MenuItem>()
            .Single(item => AutomationProperties.GetAutomationId(item) == id);

    /// <summary>菜单上「解散这一组」那一条，按右键落在谁身上算。</summary>
    private static MenuEntry Dissolve(MainWindow window, string target) =>
        ContextMenuBuilder.Build(new MenuContext(window, target), onElement: true)
            .Single(entry => entry.Id == "group.dissolve");

    /// <summary>点一条。</summary>
    /// <returns>这一下之后历史里多了几条。</returns>
    private static int Click(MainWindow window, DiagramCanvas canvas, string id)
    {
        var before = window.Session.Bus.Context.History.UndoEntries().Count;

        Item(canvas, id).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        return window.Session.Bus.Context.History.UndoEntries().Count - before;
    }

    #endregion
}
