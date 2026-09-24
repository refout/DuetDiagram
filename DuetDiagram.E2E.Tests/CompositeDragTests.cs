using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using DuetDiagram.App;
using DuetDiagram.App.Controls;
using DuetDiagram.App.Services;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 组合在画布上的呈现与操作：点得中、拖得动，拖动带动全部成员。
/// </summary>
/// <remarks>
/// <para>
/// **组合没有坐标。** 框由成员的位置算出来，所以"拖组合"实际改的是全部成员的固定位置——
/// 断言因此落在成员的布局坐标上，而不是去找一个不存在的组合坐标。
/// </para>
/// <para>
/// 坐标一律从绘制列表反推（见夹具里的中心与包围盒），不写死像素。
/// 指针事件走真实的手势路径：按下、移动、松手，与人在画布上的操作同一条路。
/// </para>
/// </remarks>
public sealed class CompositeDragTests
{
    #region 拖动（Category=Composite）

    /// <summary>拖组合框带动全部成员，松手把这一拖作为一条操作落定。</summary>
    [Fact]
    [Trait("Category", "Composite")]
    public async Task Dragging_a_composite_moves_all_of_its_members()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = OpenWithGroup(out var canvas, out var groupId);
            var session = window.Session;

            var startBefore = PlacedOf(session, "start");
            var checkBefore = PlacedOf(session, "check");

            // 框的左下角在成员外面（那里是框的内边距），按下去命中的是组合本身。
            var blank = FrameBlank(window, canvas, groupId);

            Drag(window, blank, blank + new Point(90, 45));

            var startAfter = PlacedOf(session, "start");
            var checkAfter = PlacedOf(session, "check");

            // 两个成员挪了同样的偏移——框没有自己的坐标，动的是成员。
            (startAfter.X - startBefore.X).Should().BeApproximately(checkAfter.X - checkBefore.X, 0.01);
            (startAfter.Y - startBefore.Y).Should().BeApproximately(checkAfter.Y - checkBefore.Y, 0.01);
            (startAfter.X - startBefore.X).Should().BePositive("拖动朝右下");

            // 松手算一次操作：两个成员一起固定。
            session.PinnedNodes.Keys.Should().BeEquivalentTo(["start", "check"]);

            window.Close();
        });
    }

    /// <summary>撤销把整次组合拖动当成一步退回。</summary>
    [Fact]
    [Trait("Category", "Composite")]
    public async Task Undo_reverts_a_composite_drag_as_one_step()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = OpenWithGroup(out var canvas, out var groupId);
            var session = window.Session;

            var startBefore = PlacedOf(session, "start");
            var blank = FrameBlank(window, canvas, groupId);

            Drag(window, blank, blank + new Point(90, 45));

            session.PinnedNodes.Should().ContainKeys("start", "check");
            session.CanUndoPin.Should().BeTrue();

            session.UndoPin();

            session.PinnedNodes.Should().BeEmpty("整次拖动一步退回");
            PlacedOf(session, "start").X.Should().BeApproximately(startBefore.X, 0.01, "回到拖动前的位置");

            window.Close();
        });
    }

    /// <summary>没有成员的组合没有可拖的东西：按下不进入拖拽，但选中照做。</summary>
    [Fact]
    [Trait("Category", "Composite")]
    public async Task An_empty_composite_has_nothing_to_drag()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var session = window.Session;

            session.CreateLane([]);

            var laneId = session.Document.Composites[0].Id;

            session.SetSelection([laneId]);
            session.SelectedIds.Should().Equal([laneId], "空组合也选得中");

            var start = PlacedOf(session, "start");

            session.BeginDrag(laneId, additive: false, new DrawPoint(start.X, start.Y)).Should().BeNull("没有成员，也就没有跟着动的东西");
            session.PinnedNodes.Should().BeEmpty();

            window.Close();
        });
    }

    #endregion

    #region 命中（Category=HitTest）

    /// <summary>点组合框的空白处选中的是组合本身。</summary>
    [Fact]
    [Trait("Category", "HitTest")]
    public async Task Clicking_a_frame_blank_selects_the_composite()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = OpenWithGroup(out var canvas, out var groupId);

            Click(window, FrameBlank(window, canvas, groupId));

            window.Session.SelectedIds.Should().Equal([groupId]);

            window.Close();
        });
    }

    /// <summary>点成员选中的是成员，组合不跟着进选中。</summary>
    [Fact]
    [Trait("Category", "HitTest")]
    public async Task Clicking_a_member_selects_the_member()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = OpenWithGroup(out var canvas, out var groupId);

            Click(window, HeadlessFixture.ToWindow(canvas, window, HeadlessFixture.CenterOf(canvas, "start")));

            window.Session.SelectedIds.Should().Equal(["start"]);

            window.Close();
        });
    }

    /// <summary>框选只收节点：框住组合的成员，组合本身不进选中。</summary>
    /// <remarks>
    /// 组合的框与它的成员共用一片区域，框到成员必然同时碰到组合的框；
    /// 把组合收进来的话，一次「拖整批」会把它的全部成员都拖走——比用户框的多。
    /// </remarks>
    [Fact]
    [Trait("Category", "HitTest")]
    public async Task A_marquee_does_not_pick_up_composites()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = OpenWithGroup(out var canvas, out var groupId);

            var (from, to) = BoxAround(window, canvas, "start", "check");

            Drag(window, from, to);

            window.Session.SelectedIds.Should().BeEquivalentTo(["start", "check"]);
            window.Session.SelectedIds.Should().NotContain(groupId);

            window.Close();
        });
    }

    #endregion

    #region 场景与手势

    /// <summary>开一个窗口，把 start 与 check 包成一个分组。</summary>
    private static MainWindow OpenWithGroup(out DiagramCanvas canvas, out string groupId)
    {
        var window = HeadlessFixture.Open();
        canvas = HeadlessFixture.Canvas(window);

        window.Session.CreateGroup(["start", "check"]);

        groupId = window.Session.Document.Composites[0].Id;

        return window;
    }

    /// <summary>组合框里一块没有成员的空白，按窗口坐标。</summary>
    /// <remarks>
    /// 框比成员大出一圈内边距与标题高度，左下角那一小条只有框自己——
    /// 按在那里命中测试返回的是组合的标识。
    /// </remarks>
    private static Point FrameBlank(MainWindow window, DiagramCanvas canvas, string groupId)
    {
        var frame = HeadlessFixture.BoundsOf(canvas, [groupId]);

        return HeadlessFixture.ToWindow(canvas, window, new Point(frame.X + 3, frame.Bottom - 3));
    }

    /// <summary>某节点当前的布局位置。组合没有坐标，断言都落在成员上。</summary>
    private static (double X, double Y) PlacedOf(DiagramSession session, string id)
    {
        var placed = session.Scene.Layout.Find(id)
            ?? throw new InvalidOperationException($"布局结果里没有 {id}");

        return (placed.X, placed.Y);
    }

    /// <summary>在窗口的这个点上按一下再松开。</summary>
    private static void Click(MainWindow window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    /// <summary>按下、分几步挪过去、松开。</summary>
    private static void Drag(MainWindow window, Point from, Point to, int steps = 3)
    {
        window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);

        for (var step = 1; step <= steps; step++)
        {
            var t = (double)step / steps;

            window.MouseMove(
                new Point(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t)),
                RawInputModifiers.None);
        }

        window.MouseUp(to, MouseButton.Left, RawInputModifiers.None);
    }

    /// <summary>围住这几个元素的一个矩形，四周各让开 40 像素，换成窗口坐标。</summary>
    private static (Point From, Point To) BoxAround(MainWindow window, DiagramCanvas canvas, params string[] ids)
    {
        var bounds = HeadlessFixture.BoundsOf(canvas, ids);

        var from = new Point(bounds.X - 40, bounds.Y - 40);
        var to = new Point(bounds.Right + 40, bounds.Bottom + 40);

        return (HeadlessFixture.ToWindow(canvas, window, from), HeadlessFixture.ToWindow(canvas, window, to));
    }

    #endregion
}
