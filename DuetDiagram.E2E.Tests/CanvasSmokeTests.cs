using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using DuetDiagram.App;
using DuetDiagram.App.Controls;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 画布：起窗口、画一帧、滚轮缩放、拖拽平移。
/// </summary>
/// <remarks>
/// <para>
/// 这一层测的是"用户那样操作，界面那样反应"。绘制列表本身对不对由渲染层的快照管，
/// 视口算得对不对由渲染层的单元测试管，这里只盯两者在真实窗口里有没有接上。
/// </para>
/// <para>
/// **每条断言都要有一个"不这么做就不该动"的对照。** 少了对照，一个把输入整个忽略掉的实现
/// 也能让"拖了之后有变化"这类断言通过——因为它在别的方向上本来就没动过。
/// </para>
/// </remarks>
public sealed class CanvasSmokeTests
{
    #region 起窗口与画一帧

    [Fact]
    [Trait("Category", "Canvas")]
    public async Task The_window_draws_the_whole_draw_list()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();
            var canvas = Canvas(window);
            var commands = window.Model.DrawList.Commands;

            commands.Should().NotBeEmpty("示例文档要真的画出东西来");

            window.CaptureRenderedFrame();

            canvas.DrawnCommands.Should().Be(
                commands.Count,
                "列表里的每一条指令都要被执行——没被执行的指令在屏幕上就是少画了一块");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Canvas")]
    public async Task A_frame_comes_out_as_a_bitmap_of_the_window()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            var frame = window.CaptureRenderedFrame();

            frame.Should().NotBeNull("无头模式用的是真实的绘图后端，抓不到帧说明后端没起来");
            frame!.PixelSize.Width.Should().Be(900);
            frame.PixelSize.Height.Should().Be(600);

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Canvas")]
    public async Task The_draw_list_is_fitted_into_the_window()
    {
        // 不适配的话内容会停在左上角，看上去像视口算错了，而其实是它根本没适配过。
        await HeadlessFixture.Run(() =>
        {
            var window = Open();
            var model = window.Model;

            var onScreen = model.Viewport.Transform.ToScreen(
                new SpatialRect(0, 0, model.DrawList.Width, model.DrawList.Height));

            onScreen.X.Should().BeGreaterThanOrEqualTo(-1e-6);
            onScreen.Y.Should().BeGreaterThanOrEqualTo(-1e-6);
            onScreen.Right.Should().BeLessThanOrEqualTo(model.Viewport.Width + 1e-6);
            onScreen.Bottom.Should().BeLessThanOrEqualTo(model.Viewport.Height + 1e-6);

            window.Close();
        });
    }

    #endregion

    #region 滚轮缩放

    [Fact]
    [Trait("Category", "Canvas")]
    public async Task The_wheel_zooms_around_the_cursor()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();
            var canvas = Canvas(window);
            var model = window.Model;

            var anchor = CenterOf(canvas);
            var underCursor = model.Viewport.Transform.ToDocument(anchor.X, anchor.Y);
            var before = model.Viewport.Scale;

            window.MouseWheel(ToWindow(canvas, window, anchor), new Vector(0, 1));

            model.Viewport.Scale.Should().BeGreaterThan(before, "向上滚是放大");

            var after = model.Viewport.Transform.ToDocument(anchor.X, anchor.Y);

            after.X.Should().BeApproximately(underCursor.X, 1e-6, "光标底下的那一处不该跑掉");
            after.Y.Should().BeApproximately(underCursor.Y, 1e-6);

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Canvas")]
    public async Task The_wheel_stops_at_the_zoom_limits()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();
            var canvas = Canvas(window);
            var model = window.Model;
            var at = ToWindow(canvas, window, CenterOf(canvas));

            for (var i = 0; i < 40; i++)
            {
                window.MouseWheel(at, new Vector(0, 1));
            }

            model.Viewport.Scale.Should().Be(Theme.Default.MaxZoom);

            for (var i = 0; i < 80; i++)
            {
                window.MouseWheel(at, new Vector(0, -1));
            }

            model.Viewport.Scale.Should().Be(Theme.Default.MinZoom);

            window.Close();
        });
    }

    #endregion

    #region 拖拽平移

    [Fact]
    [Trait("Category", "Canvas")]
    public async Task The_middle_button_drags_the_view()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();
            var canvas = Canvas(window);
            var model = window.Model;

            var start = ToWindow(canvas, window, CenterOf(canvas));
            var before = model.Viewport;

            window.MouseDown(start, MouseButton.Middle);
            window.MouseMove(start + new Vector(60, -25));
            window.MouseUp(start + new Vector(60, -25), MouseButton.Middle);

            model.Viewport.OffsetX.Should().BeApproximately(before.OffsetX + 60, 1e-6);
            model.Viewport.OffsetY.Should().BeApproximately(before.OffsetY - 25, 1e-6);
            model.Viewport.Scale.Should().Be(before.Scale, "平移不该改变缩放");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Canvas")]
    public async Task Space_and_the_left_button_drag_the_view()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();
            var canvas = Canvas(window);
            var model = window.Model;

            var start = ToWindow(canvas, window, CenterOf(canvas));

            // 先点一下把焦点收过来。键盘消息只发给有焦点的控件，
            // 收不到焦点的话空格键永远到不了画布，而表现是"空格拖拽坏了"。
            window.MouseDown(start, MouseButton.Left);
            window.MouseUp(start, MouseButton.Left);

            canvas.IsFocused.Should().BeTrue("点过之后画布要拿到焦点");

            var before = model.Viewport;

            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(-40, 30));
            window.MouseUp(start + new Vector(-40, 30), MouseButton.Left);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            model.Viewport.OffsetX.Should().BeApproximately(before.OffsetX - 40, 1e-6);
            model.Viewport.OffsetY.Should().BeApproximately(before.OffsetY + 30, 1e-6);

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Canvas")]
    public async Task A_plain_left_drag_leaves_the_view_alone()
    {
        // 没有这一条，一个"左键按下就平移"的实现也能通过上一条——
        // 而那种实现会把以后的框选与拖节点全部吃掉。
        await HeadlessFixture.Run(() =>
        {
            var window = Open();
            var canvas = Canvas(window);
            var model = window.Model;

            var start = ToWindow(canvas, window, CenterOf(canvas));
            var before = model.Viewport;

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(70, 40));
            window.MouseUp(start + new Vector(70, 40), MouseButton.Left);

            model.Viewport.Should().Be(before);
            model.PointerText.Should().NotBe(
                "—",
                "先确认指针事件真的到了画布上。事件根本没送到的话，这一条测的是"
                + "\"没送到\"而不是\"左键不平移\"，而前者是另一种缺陷");

            window.Close();
        });
    }

    #endregion

    #region 状态栏

    [Fact]
    [Trait("Category", "Canvas")]
    public async Task The_status_text_follows_the_pointer_and_the_zoom()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = Open();
            var canvas = Canvas(window);
            var model = window.Model;

            model.PointerText.Should().Be("—", "光标还没进过画布");

            window.MouseMove(ToWindow(canvas, window, CenterOf(canvas)));

            model.PointerText.Should().NotBe("—");

            window.MouseWheel(ToWindow(canvas, window, CenterOf(canvas)), new Vector(0, 1));

            // 缩放之后百分比要跟着变，否则状态栏报的是一个过期数字。
            model.ZoomText.Should().Be(
                string.Create(CultureInfo.InvariantCulture, $"缩放 {model.Viewport.Scale * 100:0}%"));

            window.Close();
        });
    }

    #endregion

    /// <summary>
    /// 起一个窗口，并把测量、排布与渲染都跑完。
    /// </summary>
    /// <remarks>
    /// 那一帧不能省：不起的话画布尺寸还是零，视口也就没被适配过，
    /// 之后量出来的坐标与窗口位置全是错的，而失败会指向视口而不是"还没排布"。
    /// </remarks>
    private static MainWindow Open()
    {
        var window = new MainWindow();

        window.Show();
        window.CaptureRenderedFrame();

        return window;
    }

    private static DiagramCanvas Canvas(Window window) =>
        window.GetVisualDescendants().OfType<DiagramCanvas>().Single();

    /// <summary>画布中心，按画布自己的坐标算。</summary>
    private static Point CenterOf(DiagramCanvas canvas) =>
        new(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2);

    /// <summary>把画布坐标换成窗口坐标。指针事件给的是窗口坐标。</summary>
    private static Point ToWindow(DiagramCanvas canvas, Window window, Point local) =>
        canvas.TranslatePoint(local, window)
        ?? throw new InvalidOperationException("画布不在窗口的视觉树里，量不出它在窗口里的位置");
}
