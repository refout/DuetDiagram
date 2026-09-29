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
            frame!.PixelSize.Width.Should().Be(
                (int)window.Bounds.Width,
                "抓到的就是窗口那一块，尺寸对不上说明抓的不是窗口");
            frame.PixelSize.Height.Should().Be((int)window.Bounds.Height);

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

            // 点一下把焦点收过来。这一条量的是"点过之后画布拿到焦点"，
            // 而空格本身不依赖它——另外几条量的是没点过、以及焦点在别处时照样能拖。
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

    [Fact]
    [Trait("Category", "Canvas")]
    public async Task Space_drags_the_view_before_anything_has_been_clicked()
    {
        // 窗口刚打开时没有任何控件有焦点。空格若只认"画布拿到了焦点"，
        // 这一下什么都不会发生——而用户眼里的表现正是"画布拖不动"。
        await HeadlessFixture.Run(() =>
        {
            var window = Open();
            var canvas = Canvas(window);
            var model = window.Model;

            canvas.IsFocused.Should().BeFalse("这一条要的正是「还没点过任何地方」那一档");

            var start = ToWindow(canvas, window, CenterOf(canvas));

            // 指针先移到画布上。空格归不归画布，看的就是指针在哪儿。
            window.MouseMove(start);

            var before = model.Viewport;

            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(-35, 45));
            window.MouseUp(start + new Vector(-35, 45), MouseButton.Left);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            model.Viewport.OffsetX.Should().BeApproximately(before.OffsetX - 35, 1e-6);
            model.Viewport.OffsetY.Should().BeApproximately(before.OffsetY + 45, 1e-6);

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Canvas")]
    public async Task Space_drags_the_view_while_a_toolbar_button_has_focus()
    {
        // 点过工具栏上任意一颗按钮之后，焦点落在那颗按钮上。
        // 空格若只认画布自己有没有焦点，这一档同样拖不动。
        await HeadlessFixture.Run(() =>
        {
            var window = Open();
            var canvas = Canvas(window);
            var model = window.Model;
            var start = ToWindow(canvas, window, CenterOf(canvas));

            var button = ToolbarButton(window, "全选");

            window.MouseDown(SpotOf(button, window), MouseButton.Left);
            window.MouseUp(SpotOf(button, window), MouseButton.Left);

            button.IsFocused.Should().BeTrue("点过之后焦点在按钮上，这一条要的正是这个前提");

            window.MouseMove(start);

            var before = model.Viewport;

            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(25, 15));
            window.MouseUp(start + new Vector(25, 15), MouseButton.Left);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            model.Viewport.OffsetX.Should().BeApproximately(before.OffsetX + 25, 1e-6);
            model.Viewport.OffsetY.Should().BeApproximately(before.OffsetY + 15, 1e-6);

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Canvas")]
    public async Task Space_over_the_canvas_does_not_press_the_focused_button()
    {
        // 空格同时是按钮的按下键。指针在画布上时它归画布——
        // 否则用户按着空格拖画布，顺手把上一颗点过的按钮又按了一遍。
        await HeadlessFixture.Run(() =>
        {
            var window = Open();
            var canvas = Canvas(window);
            var model = window.Model;
            var start = ToWindow(canvas, window, CenterOf(canvas));

            ToolbarButton(window, "全选").Focus();
            window.Session.SetSelection([]);

            window.MouseMove(start);

            var before = model.Viewport;

            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(20, 10));
            window.MouseUp(start + new Vector(20, 10), MouseButton.Left);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            window.Session.SelectedIds.Should().BeEmpty("空格被画布认下了，就不该再去按那颗按钮");
            model.Viewport.OffsetX.Should().BeApproximately(before.OffsetX + 20, 1e-6);
            model.Viewport.OffsetY.Should().BeApproximately(before.OffsetY + 10, 1e-6);

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Canvas")]
    public async Task Space_over_a_button_still_presses_it()
    {
        // 上一条的对照。少了它，一个"把空格整个吞掉"的实现也能全绿——
        // 而那会把键盘用户按空格激活按钮的路堵死。
        await HeadlessFixture.Run(() =>
        {
            var window = Open();

            var button = ToolbarButton(window, "全选");

            window.MouseMove(SpotOf(button, window));
            button.Focus();
            window.Session.SetSelection([]);

            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            window.Session.SelectedIds.Should().HaveCount(
                window.Session.Document.Nodes.Count,
                "指针不在画布上，空格该留给聚焦的那颗按钮");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Canvas")]
    public async Task Space_while_a_text_box_has_focus_does_not_drag_the_view()
    {
        // 就地编辑标签那一层盖在画布上，打字时指针本来就在画布范围内。
        // 空格在那儿是一个字符，画布不能抢。
        await HeadlessFixture.Run(() =>
        {
            var window = Open();
            var canvas = Canvas(window);
            var model = window.Model;
            var start = ToWindow(canvas, window, CenterOf(canvas));

            window.GetVisualDescendants().OfType<TextBox>().First().Focus();

            window.MouseMove(start);

            var before = model.Viewport;

            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(40, 30));
            window.MouseUp(start + new Vector(40, 30), MouseButton.Left);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            model.Viewport.Should().Be(before);

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

    private static MainWindow Open() => HeadlessFixture.Open();

    private static DiagramCanvas Canvas(Window window) => HeadlessFixture.Canvas(window);

    private static Point CenterOf(DiagramCanvas canvas) => HeadlessFixture.CenterOf(canvas);

    private static Point ToWindow(DiagramCanvas canvas, Window window, Point local) =>
        HeadlessFixture.ToWindow(canvas, window, local);

    /// <summary>工具栏上那一颗按钮，按它显示的字去找。</summary>
    private static Button ToolbarButton(Window window, string content) =>
        HeadlessFixture.Button(HeadlessFixture.ToolBar(window), content);

    /// <summary>一个控件在窗口坐标下的中心点。指针事件给的是窗口坐标。</summary>
    private static Point SpotOf(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
        ?? throw new InvalidOperationException("这个控件不在窗口的视觉树里，量不出它在窗口里的位置");
}
