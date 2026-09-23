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
/// 图层面板：列表、两个开关、新建改名挪次序，以及归属入口。
/// </summary>
/// <remarks>
/// <para>
/// 这一层验的是「面板把每一件事都送到了命令层」。列表本身与开关只是外壳——
/// 它们的取值都来自文档，而改动一律经命令层，所以判据是**每一条操作都恰好进一条历史**，
/// 以及**撤销之后面板与文档两边都对得上**。
/// </para>
/// <para>
/// 操作一律经由界面上的控件触发（复选框就改它的勾选状态，按钮就发一次点击事件），
/// 这样验到的是"控件把动作送到了面板"，而不是面板自己的方法。
/// </para>
/// </remarks>
public sealed class LayerPanelTests
{
    #region 列表

    [Fact]
    [Trait("Category", "LayerPanel")]
    public async Task The_panel_lists_the_layers_in_document_order()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            // 声明顺序与次序字段是反的：面板要按次序排，不是按声明顺序。
            window.Layers.Rows.Select(row => row.Id).Should().Equal(["base", "top"]);
            window.Layers.Rows.Select(row => row.Name).Should().Equal(["底层", "上层"]);

            window.Layers.CurrentLayerId.Should().Be("base", "没有选过的时候顶上那一层就是当前图层");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "LayerPanel")]
    public async Task A_row_says_how_many_elements_are_on_that_layer()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            window.Layers.Rows[0].ElementCountText.Should().Be("1 个元素");
            window.Layers.Rows[1].ElementCountText.Should().Be("1 个元素");

            // 没有归属的那个节点不算进任何一层。
            window.Session.AssignLayer(["loose"], "base");

            window.Layers.Rows[0].ElementCountText.Should().Be("2 个元素");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "LayerPanel")]
    public async Task The_panel_is_on_screen()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            HeadlessFixture.Layers(window).IsVisible.Should().BeTrue("常驻面板，不靠开关显隐");

            window.Close();
        });
    }

    #endregion

    #region 每一条操作进一条历史

    [Fact]
    [Trait("Category", "LayerPanel")]
    public async Task Every_operation_takes_exactly_one_history_entry()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            // 逐条从控件走一遍并数历史。少进一条说明那一步绕过命令层改了文档；
            // 多进一条说明一次操作被拆成了好几条，撤销时用户要按好几次。
            Recheck(window, "点一下显示开关", "layer.visible.top", () => Tick(window, "layer.visible.top", false));

            Recheck(window, "点一下锁定开关", "layer.locked.top", () => Tick(window, "layer.locked.top", true));

            Recheck(window, "挪一次次序", "layer.up.top", () =>
            {
                window.Layers.Select("top");

                return Press(window, "layer.up.top");
            });

            Recheck(window, "改一次名", "layer.rename", () =>
            {
                window.Layers.Select("top");
                Fill(window, "layer.rename-name", "改过名的");

                return Press(window, "layer.rename");
            });

            Recheck(window, "新建一层", "layer.new", () =>
            {
                Fill(window, "layer.new-name", "新层");

                return Press(window, "layer.new");
            });

            Recheck(window, "一次移入", "layer.assign", () =>
            {
                window.Session.SetSelection(["loose"]);
                window.Layers.Select("base");

                return Press(window, "layer.assign");
            });

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "LayerPanel")]
    public async Task Undoing_an_assignment_puts_the_panel_back()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            window.Session.SetSelection(["loose"]);
            window.Layers.Select("top");
            Press(window, "layer.assign").Should().Be(1);

            window.Layers.Rows[1].ElementCountText.Should().Be("2 个元素");

            window.Session.Undo().IsSuccess.Should().BeTrue();

            // 一次撤销把整批还原，面板上的计数跟着回来。
            window.Layers.Rows[1].ElementCountText.Should().Be("1 个元素");
            window.Session.Document.Nodes.Single(node => node.Id == "loose").Layer.Should().BeNull();

            window.Close();
        });
    }

    #endregion

    #region 新建、改名、次序

    [Fact]
    [Trait("Category", "LayerPanel")]
    public async Task Creating_a_layer_makes_it_the_current_one()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            Fill(window, "layer.new-name", "第三层");
            Press(window, "layer.new");

            // 刚建一层，接下来多半要往里放东西。当前图层还停在别处的话，
            // 那一次「移入」会落到另一层上，而用户以为自己刚选好了。
            window.Layers.CurrentLayerName.Should().Be("第三层");
            window.Layers.Rows.Should().HaveCount(3, "新的一层出现在列表里");

            window.Session.SetSelection(["loose"]);
            Press(window, "layer.assign");

            window.Session.Document.Nodes.Single(node => node.Id == "loose").Layer
                .Should().Be(window.Layers.CurrentLayerId);

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "LayerPanel")]
    public async Task Renaming_the_current_layer_keeps_its_identity()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            window.Layers.Select("top");
            Fill(window, "layer.rename-name", "最上面");
            Press(window, "layer.rename");

            window.Layers.Rows.Select(row => row.Id).Should().Equal(["base", "top"], "改名不动标识与位置");
            window.Layers.Rows[1].Name.Should().Be("最上面");
            window.Session.Document.Layers.Single(layer => layer.Id == "top").Name.Should().Be("最上面");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "LayerPanel")]
    public async Task The_rename_box_starts_from_the_current_layers_name()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            window.Layers.Select("top");

            // 预填当前那一层的名字，用户接着改。不预填的话他得先把整名字敲一遍，
            // 而多数时候改的只是一个字。
            ById<TextBox>(window, "layer.rename-name").Text.Should().Be("上层");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "LayerPanel")]
    public async Task Moving_a_layer_up_changes_the_document_order()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            window.Layers.Select("top");
            Press(window, "layer.up.top");

            // 次序变了，列表跟着变——面板不自己维护一份顺序。
            window.Layers.Rows.Select(row => row.Id).Should().Equal(["top", "base"]);
            window.Session.Document.Layers.Single(layer => layer.Id == "top").Order
                .Should().BeLessThan(window.Session.Document.Layers.Single(layer => layer.Id == "base").Order);

            window.Session.Undo();

            window.Layers.Rows.Select(row => row.Id).Should().Equal(["base", "top"], "撤销之后次序也回来");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "LayerPanel")]
    public async Task The_ends_of_the_list_cannot_move_further()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            window.Layers.Rows[0].CanMoveUp.Should().BeFalse("它已经在最上面");
            window.Layers.Rows[0].CanMoveDown.Should().BeTrue();
            window.Layers.Rows[1].CanMoveUp.Should().BeTrue();
            window.Layers.Rows[1].CanMoveDown.Should().BeFalse("它已经在最下面");

            window.Close();
        });
    }

    #endregion

    #region 开关与画面

    [Fact]
    [Trait("Category", "LayerRender")]
    public async Task Unticking_visibility_on_a_row_takes_the_elements_off_the_canvas()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            Drawn(window).Should().Contain("b");

            Tick(window, "layer.visible.top", false);

            Drawn(window).Should().NotContain("b", "从面板上关掉的那一层，画布上跟着没了");
            Drawn(window).Should().Contain("a", "另一层原样画着");

            Tick(window, "layer.visible.top", true);

            Drawn(window).Should().Contain("b", "放出来之后又画上了");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "LayerPanel")]
    public async Task Locking_a_row_leaves_it_drawn_but_unselectable()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            Tick(window, "layer.locked.top", true);

            Drawn(window).Should().Contain("b", "锁定是「看着别动」，不是「别看」");
            window.Session.SetSelection(window.Session.AllNodeIds);
            window.Session.SelectedIds.Should().NotContain("b");

            window.Close();
        });
    }

    #endregion

    #region 归属的两个方向

    [Fact]
    [Trait("Category", "LayerPanel")]
    public async Task Elements_cannot_be_moved_into_a_locked_layer()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            Tick(window, "layer.locked.top", true);
            window.Session.SetSelection(["loose"]);

            var result = window.Session.AssignLayer(["loose"], "top");

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.LayerLocked);
            window.Session.Document.Nodes.Single(node => node.Id == "loose").Layer.Should().BeNull();

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "LayerPanel")]
    public async Task Elements_on_a_locked_layer_cannot_be_moved_out()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            Tick(window, "layer.locked.base", true);
            window.Session.SetSelection(["a"]);

            var result = window.Session.AssignLayer(["a"], "top");

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.LayerLocked);
            window.Session.Document.Nodes.Single(node => node.Id == "a").Layer.Should().Be("base");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "LayerPanel")]
    public async Task The_assign_button_explains_itself_when_there_is_nothing_selected()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            // 面板是常驻的，没有选中时它也在。按不动的那一下要说得出为什么。
            var assign = ById<Button>(window, "layer.assign");

            assign.IsEnabled.Should().BeFalse("没选中元素时它点不动");
            window.Layers.AssignLabel.Should().Be("把选中的元素移入");

            window.Session.SetSelection(["a"]);

            ById<Button>(window, "layer.assign").IsEnabled.Should().BeTrue();
            window.Layers.AssignLabel.Should().Be("把选中的 1 个元素移入");

            window.Close();
        });
    }

    #endregion

    #region 只读

    [Fact]
    [Trait("Category", "LayerPanel")]
    public async Task A_read_only_window_refuses_every_operation_but_still_lists()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();
            var path = temp.File("layers.json");

            File.WriteAllText(path, DiagramSerializer.SerializeFull(Document()));

            // 冒充另一个进程：先拿住这份文档的独占所有权。
            using var other = DocumentLock.Acquire(path);

            var window = new MainWindow(DocumentLaunch.File(path));

            window.Show();
            window.CaptureRenderedFrame();

            window.Session.IsReadOnly.Should().BeTrue("另一个进程正在编辑这份文档");

            // 列表照样看得见——只读是改不动，不是看不见。
            window.Layers.Rows.Should().HaveCount(2);
            window.Layers.HasReadOnlyNote.Should().BeTrue();

            window.Layers.Create("新层").IsSuccess.Should().BeFalse();
            window.Session.SetSelection(["a"]);
            window.Layers.Select("base");
            window.Layers.Rename("改个名").IsSuccess.Should().BeFalse();
            window.Layers.ToggleVisible("base").IsSuccess.Should().BeFalse();
            window.Layers.ToggleLocked("base").IsSuccess.Should().BeFalse();
            window.Layers.MoveUp().IsSuccess.Should().BeFalse();

            window.Layers.Select("top");
            window.Layers.AssignSelection().IsSuccess.Should().BeFalse();

            // 一个开关都没动过。
            window.Session.Document.Layers.Should().HaveCount(2);
            window.Session.Document.Layers.Should().OnlyContain(layer => layer.Visible && !layer.Locked);
            window.Session.Document.Nodes.Single(node => node.Id == "a").Layer.Should().Be("base");

            window.Close();
        });
    }

    #endregion

    #region 辅助

    /// <summary>画布上现在画着的元素标识。</summary>
    private static string[] Drawn(MainWindow window) =>
        [.. window.Model.DrawList.Commands.Select(command => command.ElementId).Distinct(StringComparer.Ordinal)];

    /// <summary>按自动化标识找一个控件。</summary>
    private static T ById<T>(MainWindow window, string id)
        where T : Control =>
        HeadlessFixture.Editor<T>(window, id);

    /// <summary>勾上或取消某一行的开关。</summary>
    /// <returns>这一下之后历史里多了几条。</returns>
    private static int Tick(MainWindow window, string id, bool on)
    {
        var before = window.Session.Bus.Context.History.UndoEntries().Count;

        ById<CheckBox>(window, id).IsChecked = on;

        return window.Session.Bus.Context.History.UndoEntries().Count - before;
    }

    /// <summary>往一个输入框里填字。</summary>
    private static void Fill(MainWindow window, string id, string text) =>
        ById<TextBox>(window, id).Text = text;

    /// <summary>点一个按钮。</summary>
    /// <returns>这一下之后历史里多了几条。</returns>
    private static int Press(MainWindow window, string id)
    {
        var before = window.Session.Bus.Context.History.UndoEntries().Count;

        ById<Button>(window, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        return window.Session.Bus.Context.History.UndoEntries().Count - before;
    }

    /// <summary>
    /// 一条操作恰好进一条历史。
    /// </summary>
    /// <remarks>
    /// 写成"先确认那个控件在、再数历史"：控件找不到时失败在找控件上，
    /// 而不是失败在"历史没多一条"上——后者会让人去查命令层，而问题在界面没接上。
    /// </remarks>
    private static void Recheck(MainWindow window, string what, string controlId, Func<int> act)
    {
        ArgumentNullException.ThrowIfNull(act);

        _ = ById<Control>(window, controlId);

        act().Should().Be(1, $"{what}：进一条历史");
    }

    /// <summary>
    /// 开一个窗口，看一份带两个图层的文档。
    /// </summary>
    /// <remarks>
    /// 每次各建一个目录，于是路径不同、工作区不同——共用的话，前一个用例改过的那一份
    /// 会被后一个用例拿到，而那种失败看起来像面板算错了。
    /// </remarks>
    private static MainWindow Open()
    {
        var temp = new TempDirectory();
        var path = temp.File("layers.json");

        File.WriteAllText(path, DiagramSerializer.SerializeFull(Document()));

        var window = new MainWindow(DocumentLaunch.File(path));

        window.Show();
        window.CaptureRenderedFrame();

        return window;
    }

    private static DiagramDocument Document() =>
        DiagramDocument.CreateFromContent(
            "layers",
            DiagramKind.Flowchart,
            Direction.TB,
            layers:
            [
                new LayerDef { Id = "top", Name = "上层", Order = 1 },
                new LayerDef { Id = "base", Name = "底层", Order = 0 },
            ],
            nodes:
            [
                new NodeDef { Id = "a", Label = "甲", Layer = "base" },
                new NodeDef { Id = "b", Label = "乙", Layer = "top" },
                new NodeDef { Id = "loose", Label = "丙" },
            ],
            edges: [new EdgeDef { Id = "e", From = "a", To = "b" }]);

    #endregion
}
