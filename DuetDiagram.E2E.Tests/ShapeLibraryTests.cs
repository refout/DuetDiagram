using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using DuetDiagram.App;
using DuetDiagram.App.Controls;
using DuetDiagram.App.Interaction;
using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 形状面板：选中了节点就把选中的换成它，没选中就新建一个；拖到画布上在落点新建。
/// </summary>
/// <remarks>
/// 操作一律经由界面上的入口触发（点那颗按钮、往画布上拖一包数据），验的是"界面把动作
/// 送到了会话"，而不是面板自己的方法。形状是节点自己的字段，所以这里量的是字段值与落点。
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

    /// <summary>
    /// 没有选中节点时点形状：新建一个这个形状的节点，落在用户正看着的地方。
    /// </summary>
    /// <remarks>
    /// 落点取视口中心。面板不知道用户在看哪一块，那一根线由窗口接上，
    /// 所以这里量的是"新节点有没有出现在视口正中"，而不只是"文档里多了一个节点"。
    /// </remarks>
    [Fact]
    [Trait("Category", "ShapeLibrary")]
    public async Task Clicking_a_shape_without_a_selection_creates_a_node()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            window.Session.SetSelection([]);
            window.Shapes.Refresh();

            var before = window.Session.Document.Nodes.Count;
            var history = HistoryOf(window);
            var centre = CentreOfView(canvas);

            Press(window, "shape.entry.Diamond").Should().Be(1, "新建：进一条历史");

            window.Session.Document.Nodes.Should().HaveCount(before + 1);

            var added = window.Session.Document.Nodes[^1];

            added.Shape.Should().Be(NodeShape.Diamond);
            added.Id.Should().NotBeNullOrWhiteSpace("标识由会话拼，不让界面猜");
            added.Label.Should().Be(added.Id, "新节点先拿标识当文字，否则它在画布上没有字");
            window.Session.SelectedIds.Should().Equal([added.Id], "建完就选中，接着改字不用再点一次");

            var placed = Placed(window, added.Id);

            placed.CenterX.Should().BeApproximately(centre.X, 0.5, "落在视口中心");
            placed.CenterY.Should().BeApproximately(centre.Y, 0.5);

            // 一次撤销退回新建之前：节点没了，历史也回到原来那一条数。
            window.Session.Undo();
            window.Session.Document.Nodes.Should().HaveCount(before);
            HistoryOf(window).Should().Be(history, "撤销本身不进历史");

            window.Close();
        });
    }

    /// <summary>
    /// 把形状拖到画布上：在落点新建一个这个形状的节点。
    /// </summary>
    /// <remarks>
    /// 落点给的是屏幕上的点，而节点位置记在文档坐标系里，两者之间差一次换算。
    /// 取一个偏离画布中心的点，换算错了就对不上——取中心的话，换算写不写都碰巧一样。
    /// </remarks>
    [Fact]
    [Trait("Category", "ShapeLibrary")]
    public async Task Dropping_a_shape_on_the_canvas_creates_a_node_at_the_drop_point()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);
            var model = canvas.Model
                ?? throw new InvalidOperationException("画布还没有数据上下文");

            var before = window.Session.Document.Nodes.Count;

            // 落点取画布内的一个偏位置，避开正中心。
            var local = new Point(canvas.Bounds.Width * 0.7, canvas.Bounds.Height * 0.3);
            var expected = model.Viewport.Transform.ToDocument(local.X, local.Y);
            var onWindow = HeadlessFixture.ToWindow(canvas, window, local);
            var payload = ShapeDrag.Pack(NodeShape.Hexagon.ToString());

            // 先"进入"再"放下"。放下那一下用的是进入时记下的那个目标，
            // 只发放下的话，平台那边还没有目标可放，事件根本不会送到画布上。
            window.DragDrop(onWindow, RawDragEventType.DragEnter, payload, DragDropEffects.Copy);
            window.DragDrop(onWindow, RawDragEventType.Drop, payload, DragDropEffects.Copy);

            window.Session.Document.Nodes.Should().HaveCount(before + 1);

            var added = window.Session.Document.Nodes[^1];

            added.Shape.Should().Be(NodeShape.Hexagon);

            var placed = Placed(window, added.Id);

            placed.CenterX.Should().BeApproximately(expected.X, 0.5, "落在松手的那一点");
            placed.CenterY.Should().BeApproximately(expected.Y, 0.5);

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

    /// <summary>节点在布局结果里的位置。找不到说明它没进这一轮布局。</summary>
    private static PlacedNode Placed(MainWindow window, string id) =>
        window.Session.Scene.Layout.Find(id)
            ?? throw new InvalidOperationException($"布局结果里没有 {id} 这个节点");

    /// <summary>视口正中那一点，按文档坐标算。</summary>
    /// <remarks>
    /// 新节点该落在这儿，所以量它得用视口自己算出来的那一块，而不是窗口尺寸——
    /// 窗口尺寸是屏幕上的数，与文档里的坐标不在一个坐标系里。
    /// </remarks>
    private static (double X, double Y) CentreOfView(DiagramCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        var model = canvas.Model
            ?? throw new InvalidOperationException("画布还没有数据上下文");

        var visible = model.Viewport.VisibleDocumentRect;

        return (visible.CenterX, visible.CenterY);
    }

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
