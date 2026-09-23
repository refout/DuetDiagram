using Avalonia.Headless;
using DuetDiagram.App;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 图层开关在界面上真的起了作用：藏起来的东西从画布上消失，锁上的东西拖不动。
/// </summary>
/// <remarks>
/// <para>
/// 命令层已经有用例证明那两个布尔被写进去了、渲染层也有用例证明绘制列表会跟着变。
/// 这一层要验的是**整条链路接通了**：命令写进去之后会话重算、画布拿到新的那一份。
/// 少接一环的话，两个下层的用例各自都还是绿的。
/// </para>
/// <para>
/// 分层取样：藏起来那一组看的是绘制列表与坐标，锁上那一组看的是拖拽与状态栏。
/// </para>
/// </remarks>
public sealed class LayerVisibilityTests
{
    #region 藏起来

    [Fact]
    [Trait("Category", "LayerRender")]
    public async Task Hiding_a_layer_takes_its_elements_off_the_canvas()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            Drawn(window).Should().Contain("b");

            window.Session.SetLayerVisible("top", false).IsEffectiveSuccess.Should().BeTrue();

            // 被藏起来的那一层上的节点消失，连着它的边也消失——一条线连着看不见的东西，
            // 画出来是一根悬空的线。
            Drawn(window).Should().NotContain("b");
            Drawn(window).Should().NotContain("e");

            // 另一层原样画着。
            Drawn(window).Should().Contain("a");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "LayerRender")]
    public async Task Showing_it_again_brings_the_elements_back()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            window.Session.SetLayerVisible("top", false);
            window.Session.SetLayerVisible("top", true).IsEffectiveSuccess.Should().BeTrue();

            Drawn(window).Should().Contain("b");
            Drawn(window).Should().Contain("e");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "LayerRender")]
    public async Task Hiding_a_layer_does_not_move_anything()
    {
        // 藏起来只是不画。让被藏起来的元素退出布局的话，藏一个元素会让整张图重排——
        // 而用户以为自己只是把它藏起来了。
        await HeadlessFixture.Run(() =>
        {
            var window = Open();
            var canvas = HeadlessFixture.Canvas(window);

            var before = HeadlessFixture.CenterOf(canvas, "a");

            window.Session.SetLayerVisible("top", false);

            var after = HeadlessFixture.CenterOf(canvas, "a");

            after.X.Should().BeApproximately(before.X, 0.5);
            after.Y.Should().BeApproximately(before.Y, 0.5);

            window.Close();
        });
    }

    #endregion

    #region 锁定

    [Fact]
    [Trait("Category", "LayerRender")]
    public async Task A_locked_layer_still_shows_but_cannot_be_dragged()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            window.Session.SetLayerLocked("top", true).IsEffectiveSuccess.Should().BeTrue();

            // 照常画着：锁定是"看着别动"，不是"别看"。
            Drawn(window).Should().Contain("b");

            var preview = window.Session.BeginDrag("b", additive: false, new DrawPoint(0, 0));

            preview.Should().BeNull("锁着的那一层上拖不动");

            // 理由要摆出来，而不是只是没反应。
            window.Status.HasMessage.Should().BeTrue();
            window.Status.Message.Should().Contain(
                ErrorPresenterTable.For(ErrorCodes.LayerLocked).Message);

            // 拒绝的是"这条命令不能落"，文档一个字都没动。
            window.Session.PinnedNodes.Should().BeEmpty("被拒的拖动不该留下固定位置");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "LayerRender")]
    public async Task Unlocking_gives_the_drag_back()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            window.Session.SetLayerLocked("top", true);
            window.Session.SetLayerLocked("top", false).IsEffectiveSuccess.Should().BeTrue();

            var preview = window.Session.BeginDrag("b", additive: false, new DrawPoint(0, 0));

            preview.Should().NotBeNull("解锁之后这一层又能动了");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "LayerRender")]
    public async Task A_locked_layer_refusal_is_not_the_read_only_one()
    {
        // 两道门的原因与处置都不同：只读说的是整份文档（另一个进程拿着文件），
        // 处置是等对方放开或者另存一份；锁定说的是某一层，处置是解锁。
        // 合成一个码的话，同一句提示会在两种情形下出现，而其中一种的处置用户做不到。
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            window.Session.SetLayerLocked("top", true);
            window.Session.BeginDrag("b", additive: false, new DrawPoint(0, 0));

            window.Status.Message.Should().Contain(
                ErrorPresenterTable.For(ErrorCodes.LayerLocked).Message);
            window.Status.Message.Should().NotContain(
                ErrorPresenterTable.For(ErrorCodes.DocumentReadOnly).Message);

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "LayerRender")]
    public async Task A_locked_node_is_not_picked_up_by_select_all()
    {
        // 命中测试已经挡住了点选，但选中集合不止由点选驱动：全选、撤销之后的重整
        // 都走 SetSelection 那条路。漏掉那一处的话，全选会把锁着的元素也选上，
        // 接着一次删除就把它们删了。
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            window.Session.SetLayerLocked("top", true);
            window.Session.SetSelection(window.Session.AllNodeIds);

            window.Session.SelectedIds.Should().NotContain("b");
            window.Session.SelectedIds.Should().Contain("a");

            window.Close();
        });
    }

    #endregion

    #region 辅助

    /// <summary>画布上现在画着的元素标识。</summary>
    private static string[] Drawn(MainWindow window) =>
        [.. window.Model.DrawList.Commands.Select(command => command.ElementId).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// 开一个窗口，看一份带两个图层的文档。
    /// </summary>
    /// <remarks>
    /// 走文件那条路是因为示例文档没有图层，而这一组要验的正是图层。
    /// 每个用例各建一个目录，于是路径不同、工作区不同——共用的话，
    /// 前一个用例把图层藏起来之后，后一个用例拿到的就是藏起来的那一份。
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
                new LayerDef { Id = "base", Name = "底层", Order = 0 },
                new LayerDef { Id = "top", Name = "上层", Order = 1 },
            ],
            nodes:
            [
                new NodeDef { Id = "a", Label = "甲", Layer = "base" },
                new NodeDef { Id = "b", Label = "乙", Layer = "top" },
            ],
            edges: [new EdgeDef { Id = "e", From = "a", To = "b" }]);

    #endregion
}
