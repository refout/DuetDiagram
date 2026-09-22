using DuetDiagram.App;
using DuetDiagram.App.Interaction;
using DuetDiagram.App.Services;
using DuetDiagram.App.ViewModels;
using DuetDiagram.Core.Sidecar;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 节点拖拽：拖动中不改文档、不调布局、不发命令；松手才作为一条操作落定（固定位置 + 重布局）。
/// </summary>
/// <remarks>
/// <para>
/// 固定位置在 IR 之外的人工产物里（<see cref="UserSidecar.PinnedNodes"/>），所以一次拖动
/// 不会走命令总线的 IR 命令——也正因为如此，"拖动中不发命令"由总线历史条目数不变来保证，
/// "松手只发一条命令"由"刚好固定一个节点、刚好重布局一次"来保证。撤销重做也按一次拖动算一步。
/// </para>
/// <para>
/// 画布的指针事件只委托给 <see cref="DragController"/>，这里既验会话这一侧的逻辑，
/// 也验它和画布预览之间的桥：按下开始、移动只给偏移、松手落定。
/// </para>
/// </remarks>
public sealed class DragTests
{
    /// <summary>拖动过程中总线历史条目数不变：逐像素没有发命令。</summary>
    [Fact]
    [Trait("Category", "Drag")]
    public async Task A_drag_emits_no_commands_while_moving()
    {
        await HeadlessFixture.Run(() =>
        {
            using var session = new DiagramSession(SampleDiagram.Document());
            var before = session.Bus.Context.History.UndoEntries().Count;

            var start = StartOf(session, "start");
            session.BeginDrag("start", additive: false, start);

            // 模拟拖动中连续收到的几十个指针事件。
            for (var step = 1; step <= 40; step++)
            {
                session.UpdateDrag(new DrawPoint(start.X + step, start.Y + step));
            }

            session.Bus.Context.History.UndoEntries().Count.Should().Be(before, "拖动中不该有任何命令进历史");
            session.PinnedNodes.Should().BeEmpty("松手前还没写固定位置");
        });
    }

    /// <summary>松手把这一拖作为一条操作落定：固定恰好一个节点，且节点到了拖到的位置。</summary>
    [Fact]
    [Trait("Category", "Drag")]
    public async Task Releasing_a_drag_pins_exactly_one_node()
    {
        await HeadlessFixture.Run(() =>
        {
            using var session = new DiagramSession(SampleDiagram.Document());
            var before = session.Bus.Context.History.UndoEntries().Count;

            var start = StartOf(session, "start");
            session.BeginDrag("start", additive: false, start);

            var delta = new DrawPoint(120, 64);
            session.UpdateDrag(new DrawPoint(start.X + delta.X, start.Y + delta.Y));

            var commit = session.CommitDrag();

            commit.Kind.Should().Be(DiagramSession.CommitKind.Pinned);
            commit.Count.Should().Be(1, "一条操作固定一个节点");            session.PinnedNodes.Should().ContainSingle().Which.Key.Should().Be("start");

            // 松手那一刻才写固定位置：之前的几十次移动没有让历史变长。
            session.Bus.Context.History.UndoEntries().Count.Should().Be(before);

            // 重布局后，这个节点应当停在我们拖到的那个左上角。
            var placed = session.Scene.Layout.Find("start");
            placed.Should().NotBeNull();
            placed!.X.Should().BeApproximately(start.X + delta.X, 0.01);
            placed.Y.Should().BeApproximately(start.Y + delta.Y, 0.01);
        });
    }

    /// <summary>落点与另一个已固定节点重叠时，这一拖被挡下，不写任何固定位置。</summary>
    [Fact]
    [Trait("Category", "Drag")]
    public async Task A_drop_that_overlaps_a_pinned_node_is_rejected()
    {
        await HeadlessFixture.Run(() =>
        {
            using var session = new DiagramSession(SampleDiagram.Document());

            // 先把 "start" 固定在它的当前位置。
            var startPos = StartOf(session, "start");
            session.BeginDrag("start", additive: false, startPos);
            session.CommitDrag();

            // 再把 "check" 拖到与 "start" 完全重叠的位置。
            var checkPos = StartOf(session, "check");
            session.BeginDrag("check", additive: false, checkPos);
            var overlap = new DrawPoint(startPos.X - checkPos.X, startPos.Y - checkPos.Y);
            session.UpdateDrag(new DrawPoint(checkPos.X + overlap.X, checkPos.Y + overlap.Y));

            var commit = session.CommitDrag();

            commit.Kind.Should().Be(DiagramSession.CommitKind.Rejected, "不能压在另一个固定节点上");
            session.PinnedNodes.Should().ContainSingle().Which.Key.Should().Be("start");
            session.PinnedNodes.Should().NotContainKey("check", "被挡下后不应把 check 写进固定集合");
        });
    }

    /// <summary>撤销把整次拖动当成一步退回，重做再固定回来。</summary>
    [Fact]
    [Trait("Category", "Drag")]
    public async Task Undo_reverts_a_drag_as_one_step()
    {
        await HeadlessFixture.Run(() =>
        {
            using var session = new DiagramSession(SampleDiagram.Document());
            var start = StartOf(session, "start");

            session.BeginDrag("start", additive: false, start);
            session.UpdateDrag(new DrawPoint(start.X + 90, start.Y + 30));
            session.CommitDrag();

            session.PinnedNodes.Should().ContainKey("start");
            session.CanUndoPin.Should().BeTrue();

            session.UndoPin();
            session.PinnedNodes.Should().NotContainKey("start", "整次拖动一步退回");
            session.Scene.Layout.Find("start")!.X.Should().BeApproximately(start.X, 0.01, "回到自动布局的位置");

            session.CanRedoPin.Should().BeTrue();
            session.RedoPin();
            session.PinnedNodes.Should().ContainKey("start", "重做把固定位置拿回来");
        });
    }

    /// <summary>DragController 把画布的指针手势翻译成预览与落定：移动中画布偏移、松手才固定。</summary>
    [Fact]
    [Trait("Category", "Drag")]
    public async Task DragController_drives_session_and_canvas_preview()
    {
        await HeadlessFixture.Run(() =>
        {
            using var session = new DiagramSession(SampleDiagram.Document());
            var model = new CanvasViewModel();
            model.Load(session.Scene.DrawList);

            var controller = new DragController(session, model);
            var start = StartOf(session, "start");
            controller.Press("start", additive: false, start).Should().BeTrue("按在节点上应当进入拖拽");

            var delta = new DrawPoint(40, 25);
            controller.Move(new DrawPoint(start.X + delta.X, start.Y + delta.Y));
            model.BeginFrame();

            // 预览：画布上这个节点的形状被整体挪了一个偏移，布局没动。
            var shape = model.FrameCommands.OfType<DrawShape>().First(s => s.ElementId == "start");
            shape.Rect.X.Should().BeApproximately(start.X + delta.X, 0.01);
            session.PinnedNodes.Should().BeEmpty("移动中还没固定");

            controller.Release();
            session.PinnedNodes.Should().ContainKey("start", "松手才固定");
        });
    }

    /// <summary>节点的当前左上角，文档坐标。</summary>
    private static DrawPoint StartOf(DiagramSession session, string id)
    {
        var placed = session.Scene.Layout.Find(id)
            ?? throw new InvalidOperationException($"布局结果里没有 {id}");

        return new DrawPoint(placed.X, placed.Y);
    }
}
