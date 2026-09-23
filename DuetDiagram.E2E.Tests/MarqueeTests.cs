using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using DuetDiagram.App;
using DuetDiagram.App.Controls;
using DuetDiagram.App.Interaction;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.App.Services;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 框选：按在空白处、拖出一个选框、松手选中所覆盖的元素。
/// </summary>
/// <remarks>
/// <para>
/// 这一层验的是"框选真的在窗口里接上了"：指针事件进去、选中出来。
/// 阈值、选框几何与"谁在框里"那几条判据由 <see cref="MarqueeSession"/> 自己的用例管。
/// </para>
/// <para>
/// 坐标一律从绘制列表反推（见夹具里的包围盒），不写死像素——
/// 写死的话，改一次示例文档或改一次内边距，用例就会以"没选中"的形式失败。
/// </para>
/// </remarks>
public sealed class MarqueeTests
{
    #region 框选落定

    [Fact]
    [Trait("Category", "Marquee")]
    public async Task A_marquee_selects_the_elements_inside_it()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);
            var (from, to) = BoxAround(window, canvas, "start", "check");

            Drag(window, from, to);

            window.Session.SelectedIds.Should().Contain("start");
            window.Session.SelectedIds.Should().Contain("check");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Marquee")]
    public async Task A_marquee_leaves_out_what_it_does_not_cover()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);
            var (from, to) = BoxAround(window, canvas, "start", "check");

            Drag(window, from, to);

            // 「fail」在另一头，不该被框进来。框选的判据与命中测试同一条，
            // 所以这里量的就是"选框覆盖了谁"。
            window.Session.SelectedIds.Should().NotContain("fail");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Marquee")]
    public async Task A_marquee_settles_the_selection_once_on_release()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            var before = window.Session.Document.Version;
            var changes = 0;

            window.Session.SelectionChanged += () => changes++;

            var (from, to) = BoxAround(window, canvas, "start", "check");
            Drag(window, from, to, steps: 8);

            // 拖动中报过好几次，但选中只在松手那一下落定一次。
            changes.Should().Be(1, "松手才算一次选中");
            window.Session.Document.Version.Should().Be(before, "框选不改文档");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Marquee")]
    public async Task A_press_and_release_without_moving_clears_the_selection()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            window.Session.Select("start");

            var (from, _) = BoxAround(window, canvas, "start", "check");

            // 抖一两个像素仍然算点选：用户想清空选中，却因为手抖框出一个空选框，
            // 结果一样，但看上去像没反应。
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(from + new Vector(1, 1));
            window.MouseUp(from + new Vector(1, 1), MouseButton.Left);

            window.Session.SelectedIds.Should().BeEmpty();

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Marquee")]
    public async Task An_additive_marquee_joins_the_existing_selection()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            window.Session.Select("fail");

            var (from, to) = BoxAround(window, canvas, "start", "check");
            Drag(window, from, to, additive: true);

            window.Session.SelectedIds.Should().Contain("fail", "按住 Ctrl 框选是并进已有选中");
            window.Session.SelectedIds.Should().Contain("start");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Marquee")]
    public async Task A_plain_marquee_replaces_the_selection()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            window.Session.Select("fail");

            var (from, to) = BoxAround(window, canvas, "start", "check");
            Drag(window, from, to);

            window.Session.SelectedIds.Should().NotContain("fail", "不按修饰键时框选换掉原来的选中");

            window.Close();
        });
    }

    #endregion

    #region 命中判据与命中测试同一条

    [Fact]
    [Trait("Category", "Marquee")]
    public async Task A_marquee_skips_the_elements_that_cannot_be_hit()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = OpenWithLayers();

            // 把 check 所在的层锁上：命中测试跳过它，框选也必须跳过，
            // 否则锁上之后还能被批量改。
            window.Session.SetLayerLocked("top", true).IsEffectiveSuccess.Should().BeTrue();

            var canvas = HeadlessFixture.Canvas(window);
            var (from, to) = BoxAround(window, canvas, "start", "check");

            Drag(window, from, to);

            window.Session.SelectedIds.Should().Contain("start");
            window.Session.SelectedIds.Should().NotContain("check", "锁定层上的元素框不进来");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Marquee")]
    public async Task A_marquee_skips_the_elements_on_a_hidden_layer()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = OpenWithLayers();

            window.Session.SetLayerVisible("top", false).IsEffectiveSuccess.Should().BeTrue();

            var canvas = HeadlessFixture.Canvas(window);
            var (from, to) = BoxAround(window, canvas, "start", "check");

            Drag(window, from, to);

            // 藏起来的那一层没有指令，也就框不进来。
            window.Session.SelectedIds.Should().NotContain("check");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Marquee")]
    public async Task A_marquee_on_the_far_page_takes_nothing()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = OpenWithPages();
            var canvas = HeadlessFixture.Canvas(window);

            // 第二页上只剩 b 与 c；第一页上的 a 此刻不在画布上。
            window.Session.SwitchPage("p2").Should().BeTrue();

            var (from, to) = BoxAround(window, canvas, "b", "c");
            Drag(window, from, to);

            window.Session.SelectedIds.Should().Contain("b");
            window.Session.SelectedIds.Should().NotContain("a", "别的页面上的元素框不进来");

            window.Close();
        });
    }

    #endregion

    #region 阈值

    [Fact]
    [Trait("Category", "Marquee")]
    public async Task A_short_drag_is_not_a_marquee()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);
            var (from, _) = BoxAround(window, canvas, "start", "check");

            window.Session.Select("start");

            // 两像素：小于阈值，因此还是一个"点"，落在空白处就是清空选中。
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(from + new Vector(2, 0));
            window.MouseUp(from + new Vector(2, 0), MouseButton.Left);

            window.Session.SelectedIds.Should().BeEmpty();

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Marquee")]
    public async Task The_session_decides_a_marquee_by_distance()
    {
        // 会话自己那一层：没超过阈值时不给选框，超过之后给的是两次位置围出来的那一块。
        var session = new MarqueeSession(new DrawPoint(10, 10), additive: false, scale: 1);

        session.Move(new DrawPoint(12, 12)).Should().BeNull();
        session.IsMarquee.Should().BeFalse();

        var area = session.Move(new DrawPoint(40, 30));

        session.IsMarquee.Should().BeTrue();
        area.Should().Be(new SpatialRect(10, 10, 30, 20));
    }

    [Fact]
    [Trait("Category", "Marquee")]
    public async Task The_threshold_is_measured_on_screen_not_in_the_document()
    {
        // 放大四倍时，屏幕上的三像素只对应文档里的 0.75——同一个屏幕距离，
        // 不换算的话放大之后轻轻一抖就被当成框选。
        var zoomed = new MarqueeSession(new DrawPoint(0, 0), additive: false, scale: 4);

        zoomed.Move(new DrawPoint(0.5, 0)).Should().BeNull("屏幕上还不到三像素");

        var area = zoomed.Move(new DrawPoint(1, 0));

        area.Should().NotBeNull();
        area!.Value.Width.Should().Be(1);
    }

    #endregion

    #region 辅助

    /// <summary>在画布上拖一次。</summary>
    private static void Drag(MainWindow window, Point from, Point to, int steps = 3, bool additive = false)
    {
        var modifiers = additive ? RawInputModifiers.Control : RawInputModifiers.None;

        window.MouseDown(from, MouseButton.Left, modifiers);

        for (var step = 1; step <= steps; step++)
        {
            var t = (double)step / steps;

            window.MouseMove(
                new Point(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t)),
                modifiers);
        }

        window.MouseUp(to, MouseButton.Left, modifiers);
    }

    /// <summary>
    /// 围住这几个元素的一个矩形，四周各让开 40 像素，换成窗口坐标。
    /// </summary>
    /// <remarks>
    /// 让开那一段是必须的：按下那一点要落在空白处，落在元素上就不是框选，
    /// 而是拖拽或点选。40 像素比节点大，也比标签宽。
    /// </remarks>
    private static (Point From, Point To) BoxAround(MainWindow window, DiagramCanvas canvas, params string[] ids)
    {
        var bounds = HeadlessFixture.BoundsOf(canvas, ids);

        var from = new Point(bounds.X - 40, bounds.Y - 40);
        var to = new Point(bounds.Right + 40, bounds.Bottom + 40);

        return (HeadlessFixture.ToWindow(canvas, window, from), HeadlessFixture.ToWindow(canvas, window, to));
    }

    /// <summary>一份带两个图层的文档：start 在底层，check 在上层。</summary>
    private static MainWindow OpenWithLayers()
    {
        var document = DiagramDocument.CreateFromContent(
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
                new NodeDef { Id = "start", Label = "开始", Layer = "base" },
                new NodeDef { Id = "check", Label = "校验", Layer = "top" },
            ],
            edges: [new EdgeDef { Id = "e", From = "start", To = "check" }]);

        return Open(document);
    }

    /// <summary>一份两页的文档：a 在第一页，b 与 c 在第二页。</summary>
    private static MainWindow OpenWithPages()
    {
        var document = DiagramDocument.CreateFromContent(
            "pages",
            DiagramKind.Flowchart,
            Direction.TB,
            pages: [new PageDef { Id = "p1", Order = 0 }, new PageDef { Id = "p2", Order = 1 }],
            nodes:
            [
                new NodeDef { Id = "a", Label = "甲", Page = "p1" },
                new NodeDef { Id = "b", Label = "乙", Page = "p2" },
                new NodeDef { Id = "c", Label = "丙", Page = "p2" },
            ],
            edges: [new EdgeDef { Id = "e", From = "b", To = "c" }]);

        return Open(document);
    }

    private static MainWindow Open(DiagramDocument document)
    {
        var temp = new TempDirectory();
        var path = temp.File("marquee.json");

        File.WriteAllText(path, DiagramSerializer.SerializeFull(document));

        var window = new MainWindow(DocumentLaunch.File(path));

        window.Show();
        window.CaptureRenderedFrame();

        return window;
    }

    #endregion
}
