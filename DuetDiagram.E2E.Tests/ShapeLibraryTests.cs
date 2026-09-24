using Avalonia.Controls;
using Avalonia.Interactivity;
using DuetDiagram.App;
using DuetDiagram.Core.Model;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 形状面板：点一个形状把选中的节点换成它，撤销还原。
/// </summary>
/// <remarks>
/// 操作一律经由界面上的按钮触发（点那颗按钮），验的是"按钮把动作送到了面板"，
/// 而不是面板自己的方法。形状是节点自己的字段，所以这里量的是字段值有没有变。
/// </remarks>
public sealed class ShapeLibraryTests
{
    #region 换形状（Category=ShapeLibrary）

    /// <summary>选中一个节点，点一个形状：节点换成它，撤销还原。</summary>
    [Fact]
    [Trait("Category", "ShapeLibrary")]
    public async Task Clicking_a_shape_replaces_the_selected_node_and_undo_restores_it()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            // 样例图里 start 是个胶囊。
            Node(window, "start").Shape.Should().Be(NodeShape.Stadium);

            window.Session.SetSelection(["start"]);
            window.Shapes.Refresh();

            var before = HistoryOf(window);

            Press(window, "shape.entry.Diamond").Should().Be(1, "换形状：进一条历史");
            Node(window, "start").Shape.Should().Be(NodeShape.Diamond);

            window.Session.Undo();

            Node(window, "start").Shape.Should().Be(NodeShape.Stadium, "撤销还原成原来的形状");
            HistoryOf(window).Should().Be(before, "撤销本身不进历史");

            window.Close();
        });
    }

    /// <summary>多选之后点一个形状：每个节点各发一条命令，撤销一次退回一个。</summary>
    /// <remarks>
    /// 与属性面板里改同一个字段的表现一致——两处对同一件事不该有两种撤销手感。
    /// </remarks>
    [Fact]
    [Trait("Category", "ShapeLibrary")]
    public async Task Applying_to_a_multi_selection_changes_every_selected_node()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            window.Session.SetSelection(["start", "check"]);
            window.Shapes.Refresh();

            Press(window, "shape.entry.Hexagon").Should().Be(2, "两个节点各一条命令");

            Node(window, "start").Shape.Should().Be(NodeShape.Hexagon);
            Node(window, "check").Shape.Should().Be(NodeShape.Hexagon);

            window.Session.Undo();
            Node(window, "check").Shape.Should().Be(NodeShape.Diamond, "撤销退回一个节点");

            window.Session.Undo();
            Node(window, "start").Shape.Should().Be(NodeShape.Stadium);

            window.Close();
        });
    }

    /// <summary>没有选中节点时点形状：被挡下，面板给一句话，文档不动。</summary>
    [Fact]
    [Trait("Category", "ShapeLibrary")]
    public async Task Clicking_a_shape_without_a_selection_is_refused()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            window.Session.SetSelection([]);
            window.Shapes.Refresh();

            var before = HistoryOf(window);

            Press(window, "shape.entry.Diamond").Should().Be(0, "没有落点，命令不发");
            window.Shapes.Error.Should().NotBeNull("挡下来要说一句，不能什么都没发生");
            HistoryOf(window).Should().Be(before);

            window.Close();
        });
    }

    /// <summary>选中的节点都是同一个形状时，那一行带记号。</summary>
    [Fact]
    [Trait("Category", "ShapeLibrary")]
    public async Task The_current_shape_is_marked()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            // 样例图里 check 是菱形。
            window.Session.SetSelection(["check"]);
            window.Shapes.Refresh();

            window.Shapes.Rows.Single(row => row.Shape == NodeShape.Diamond).IsCurrent.Should().BeTrue();
            window.Shapes.Rows.Single(row => row.Shape == NodeShape.Stadium).IsCurrent.Should().BeFalse();

            // 选了两个不同形状的节点时不标任何一行——标第一个会让人以为两个都是它。
            window.Session.SetSelection(["start", "check"]);
            window.Shapes.Refresh();

            window.Shapes.Rows.Should().OnlyContain(row => !row.IsCurrent);

            window.Close();
        });
    }

    /// <summary>面板把八个形状都摆出来了。</summary>
    [Fact]
    [Trait("Category", "ShapeLibrary")]
    public async Task The_panel_lists_every_builtin_shape()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            foreach (var shape in Enum.GetValues<NodeShape>())
            {
                var id = $"shape.entry.{shape}";

                FluentActions.Invoking(() => HeadlessFixture.Editor<Button>(window, id))
                    .Should().NotThrow($"面板上应当有 {shape} 这一格");
            }

            window.Close();
        });
    }

    #endregion

    #region 辅助

    private static NodeDef Node(MainWindow window, string id) =>
        window.Session.Document.Nodes.Single(node => node.Id == id);

    /// <summary>历史里现在有几条。</summary>
    private static int HistoryOf(MainWindow window) =>
        window.Session.Bus.Context.History.UndoEntries().Count;

    /// <summary>点一颗按钮。</summary>
    /// <returns>这一下之后历史里多了几条。</returns>
    private static int Press(MainWindow window, string id)
    {
        var before = HistoryOf(window);

        HeadlessFixture.Editor<Button>(window, id)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        return HistoryOf(window) - before;
    }

    #endregion
}
