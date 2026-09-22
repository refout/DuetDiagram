using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 视口：坐标换算、缩放锚点、边界钳制与适配。
/// </summary>
/// <remarks>
/// 这一层测的是"同一份输入永远给出同一份输出"。渲染与命中测试都走这两个方法，
/// 任何一处算错都会表现成"点不中元素"——那种偏差在画面上完全看不出来，
/// 所以只能在坐标上验。
/// </remarks>
public sealed class ViewportTests
{
    #region 坐标往返

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(2.5, -120, 40)]
    [InlineData(0.35, 33.75, -910.5)]
    [InlineData(8, 1234.5, -678.25)]
    [Trait("Category", "Viewport")]
    public void A_point_survives_the_round_trip(double scale, double offsetX, double offsetY)
    {
        var transform = new ViewportTransform(scale, offsetX, offsetY);

        var screen = transform.ToScreen(137.25, -46.5);
        var back = transform.ToDocument(screen);

        back.X.Should().BeApproximately(137.25, 1e-9);
        back.Y.Should().BeApproximately(-46.5, 1e-9);
    }

    [Fact]
    [Trait("Category", "Viewport")]
    public void A_rectangle_survives_the_round_trip()
    {
        var transform = new ViewportTransform(1.75, -40, 25);
        var rect = new SpatialRect(120, 60, 160, 48);

        var back = transform.ToDocument(transform.ToScreen(rect));

        back.X.Should().BeApproximately(rect.X, 1e-9);
        back.Y.Should().BeApproximately(rect.Y, 1e-9);
        back.Width.Should().BeApproximately(rect.Width, 1e-9);
        back.Height.Should().BeApproximately(rect.Height, 1e-9);
    }

    [Fact]
    [Trait("Category", "Viewport")]
    public void An_identity_transform_maps_a_point_onto_itself()
    {
        var point = new DrawPoint(12.5, -7.25);

        ViewportTransform.Identity.ToScreen(point).Should().Be(point);
        ViewportTransform.Identity.ToDocument(point).Should().Be(point);
    }

    [Fact]
    [Trait("Category", "Viewport")]
    public void Hit_testing_and_rendering_agree_on_where_a_node_is()
    {
        // 这是"变换必须是纯函数"那条约束的可判定形式：
        // 画的时候把节点框换到屏幕上，点的时候把光标换到文档里，
        // 两条路径必须落在同一个位置。各维护一份视口的话，差一点点就点不中。
        var viewport = Window(scale: 1.7, offsetX: -83, offsetY: 41);
        var node = new SpatialRect(120, 60, 160, 48);

        var onScreen = viewport.Transform.ToScreen(node);
        var cursor = viewport.Transform.ToDocument(onScreen.CenterX, onScreen.CenterY);

        node.Contains(cursor.X, cursor.Y).Should().BeTrue("渲染与命中测试算的必须是同一个位置");
    }

    #endregion

    #region 缩放锚点

    [Fact]
    [Trait("Category", "Viewport")]
    public void Zooming_keeps_the_document_point_under_the_cursor()
    {
        var viewport = Window(scale: 1.5, offsetX: -200, offsetY: -80);

        const double AnchorX = 640;
        const double AnchorY = 120;

        var before = viewport.Transform.ToDocument(AnchorX, AnchorY);
        var zoomed = viewport.ZoomAt(1.25, AnchorX, AnchorY);
        var after = zoomed.Transform.ToDocument(AnchorX, AnchorY);

        zoomed.Scale.Should().BeApproximately(1.875, 1e-12);
        after.X.Should().BeApproximately(before.X, 1e-9);
        after.Y.Should().BeApproximately(before.Y, 1e-9);
    }

    [Fact]
    [Trait("Category", "Viewport")]
    public void Zooming_spreads_everything_else_away_from_the_cursor()
    {
        // 上一条只说"锚点不动"，这一条说"锚点之外确实动了"。
        // 少了它，一个把缩放整个忽略掉的实现也能通过锚点那条——
        // 什么都不动的时候，锚点当然也没动。
        var viewport = Window(scale: 1, offsetX: 0, offsetY: 0);

        const double AnchorX = 200;
        const double AnchorY = 150;

        var target = viewport.Transform.ToDocument(600, 480);
        var before = viewport.Transform.ToScreen(target);
        var after = viewport.ZoomAt(2, AnchorX, AnchorY).Transform.ToScreen(target);

        Math.Abs(after.X - AnchorX).Should().BeGreaterThan(Math.Abs(before.X - AnchorX));
        Math.Abs(after.Y - AnchorY).Should().BeGreaterThan(Math.Abs(before.Y - AnchorY));
    }

    [Fact]
    [Trait("Category", "Viewport")]
    public void Zooming_out_and_back_in_returns_to_the_same_place()
    {
        // 每滚一格都会重算平移量。算得不精确的话误差会累积，
        // 表现是滚一阵之后整张图自己漂走了。
        var viewport = Window(scale: 1, offsetX: -50, offsetY: -30);

        var back = viewport
            .ZoomAt(1.4, 300, 200)
            .ZoomAt(1 / 1.4, 300, 200);

        back.Scale.Should().BeApproximately(1, 1e-9);
        back.OffsetX.Should().BeApproximately(-50, 1e-9);
        back.OffsetY.Should().BeApproximately(-30, 1e-9);
    }

    [Fact]
    [Trait("Category", "Viewport")]
    public void A_non_positive_zoom_factor_is_rejected()
    {
        var viewport = Window();

        var zero = () => viewport.ZoomAt(0, 100, 100);
        var negative = () => viewport.ZoomAt(-1, 100, 100);

        zero.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region 边界钳制

    [Fact]
    [Trait("Category", "Viewport")]
    public void Zoom_is_clamped_to_the_theme_limits()
    {
        var viewport = Window();

        for (var i = 0; i < 40; i++)
        {
            viewport = viewport.ZoomAt(1.5, 400, 300);
        }

        viewport.Scale.Should().Be(Theme.Default.MaxZoom);

        for (var i = 0; i < 60; i++)
        {
            viewport = viewport.ZoomAt(1 / 1.5, 400, 300);
        }

        viewport.Scale.Should().Be(Theme.Default.MinZoom);
    }

    [Fact]
    [Trait("Category", "Viewport")]
    public void The_clamp_does_not_move_the_anchor()
    {
        // 先换算再钳制的话，倍数被改小、平移量却按改之前的倍数算过，
        // 表现是滚到极限之后图会自己往一边滑。
        var viewport = Window(scale: Theme.Default.MaxZoom, offsetX: -30, offsetY: -60);

        const double AnchorX = 700;
        const double AnchorY = 90;

        var before = viewport.Transform.ToDocument(AnchorX, AnchorY);
        var zoomed = viewport.ZoomAt(2, AnchorX, AnchorY);
        var after = zoomed.Transform.ToDocument(AnchorX, AnchorY);

        zoomed.Scale.Should().Be(Theme.Default.MaxZoom);
        after.X.Should().BeApproximately(before.X, 1e-9);
        after.Y.Should().BeApproximately(before.Y, 1e-9);
    }

    [Fact]
    [Trait("Category", "Viewport")]
    public void The_zoom_limits_come_from_the_theme()
    {
        var theme = Theme.Default with { MinZoom = 0.5, MaxZoom = 2 };
        var viewport = Viewport.For(theme).Resize(800, 600);

        viewport.MinZoom.Should().Be(0.5);
        viewport.MaxZoom.Should().Be(2);

        for (var i = 0; i < 20; i++)
        {
            viewport = viewport.ZoomAt(1.5, 400, 300);
        }

        viewport.Scale.Should().Be(2);
    }

    #endregion

    #region 平移

    [Fact]
    [Trait("Category", "Viewport")]
    public void Panning_moves_by_the_screen_delta_and_keeps_the_scale()
    {
        var panned = Window(scale: 2, offsetX: 10, offsetY: 20).PanBy(-30, 45);

        panned.Scale.Should().Be(2);
        panned.OffsetX.Should().Be(-20);
        panned.OffsetY.Should().Be(65);
    }

    [Fact]
    [Trait("Category", "Viewport")]
    public void Panning_shifts_the_visible_region_by_the_same_distance_in_document_units()
    {
        var viewport = Window(scale: 2, offsetX: 10, offsetY: 20);

        var before = viewport.VisibleDocumentRect;
        var after = viewport.PanBy(-30, 45).VisibleDocumentRect;

        // 把内容往左拖三十像素，看得见的区域就向右移；放大一倍，所以文档里只走一半。
        (after.X - before.X).Should().BeApproximately(15, 1e-9);
        (after.Y - before.Y).Should().BeApproximately(-22.5, 1e-9);
        after.Width.Should().BeApproximately(before.Width, 1e-9);
        after.Height.Should().BeApproximately(before.Height, 1e-9);
    }

    [Fact]
    [Trait("Category", "Viewport")]
    public void The_visible_region_maps_back_onto_the_whole_window()
    {
        var viewport = Window(scale: 1.25, offsetX: -310, offsetY: 88);

        var onScreen = viewport.Transform.ToScreen(viewport.VisibleDocumentRect);

        onScreen.X.Should().BeApproximately(0, 1e-9);
        onScreen.Y.Should().BeApproximately(0, 1e-9);
        onScreen.Width.Should().BeApproximately(viewport.Width, 1e-9);
        onScreen.Height.Should().BeApproximately(viewport.Height, 1e-9);
    }

    #endregion

    #region 窗口尺寸

    [Fact]
    [Trait("Category", "Viewport")]
    public void Resizing_keeps_the_scale_and_the_offset()
    {
        // 用户已经摆好的视角不该因为拖了一下窗口边框就变。
        var resized = Window(scale: 1.5, offsetX: -20, offsetY: -40).Resize(1024, 768);

        resized.Scale.Should().Be(1.5);
        resized.OffsetX.Should().Be(-20);
        resized.OffsetY.Should().Be(-40);
        resized.Width.Should().Be(1024);
        resized.Height.Should().Be(768);
    }

    [Fact]
    [Trait("Category", "Viewport")]
    public void A_negative_size_is_clamped_to_zero()
    {
        var resized = Window().Resize(-10, -20);

        resized.Width.Should().Be(0);
        resized.Height.Should().Be(0);
    }

    #endregion

    #region 适配

    [Fact]
    [Trait("Category", "Viewport")]
    public void Fit_puts_the_whole_content_inside_the_window()
    {
        var viewport = Window().FitTo(new SpatialRect(100, 50, 2000, 1000));

        var onScreen = viewport.Transform.ToScreen(new SpatialRect(100, 50, 2000, 1000));

        onScreen.X.Should().BeGreaterThanOrEqualTo(-1e-9);
        onScreen.Y.Should().BeGreaterThanOrEqualTo(-1e-9);
        onScreen.Right.Should().BeLessThanOrEqualTo(800 + 1e-9);
        onScreen.Bottom.Should().BeLessThanOrEqualTo(600 + 1e-9);
    }

    [Fact]
    [Trait("Category", "Viewport")]
    public void Fit_centers_the_content()
    {
        var viewport = Window().FitTo(new SpatialRect(100, 50, 2000, 1000));

        var onScreen = viewport.Transform.ToScreen(new SpatialRect(100, 50, 2000, 1000));

        onScreen.CenterX.Should().BeApproximately(400, 1e-9);
        onScreen.CenterY.Should().BeApproximately(300, 1e-9);
    }

    [Fact]
    [Trait("Category", "Viewport")]
    public void Fit_leaves_the_margin_around_the_content()
    {
        // 内容宽而扁，所以受限的是横向：倍数正好是"可用宽度除以内容宽度"。
        var viewport = Window().FitTo(new SpatialRect(0, 0, 2000, 1000));

        viewport.Scale.Should().BeApproximately((800 - (Viewport.FitMargin * 2)) / 2000, 1e-12);
    }

    [Fact]
    [Trait("Category", "Viewport")]
    public void Fit_uses_the_taller_side_when_the_content_is_tall()
    {
        var viewport = Window().FitTo(new SpatialRect(0, 0, 100, 4000));

        viewport.Scale.Should().BeApproximately((600 - (Viewport.FitMargin * 2)) / 4000, 1e-12);
    }

    [Fact]
    [Trait("Category", "Viewport")]
    public void Fit_clamps_a_tiny_document_instead_of_blowing_it_up()
    {
        // 内容只有一个点大时不钳制会放成几百倍，用户看到的是一个被放大的空白。
        var viewport = Window().FitTo(new SpatialRect(10, 10, 1, 1));

        viewport.Scale.Should().Be(Theme.Default.MaxZoom);
    }

    [Fact]
    [Trait("Category", "Viewport")]
    public void Fit_keeps_the_current_scale_when_there_is_nothing_to_fit()
    {
        // 空文档走这条路径：它没有可适配的范围，但视口不该因此跳到随机位置。
        var viewport = Window(scale: 1.5, offsetX: -10, offsetY: -20).FitTo(new SpatialRect(0, 0, 0, 0));

        viewport.Scale.Should().Be(1.5);
        viewport.Transform.ToScreen(0, 0).X.Should().BeApproximately(400, 1e-9);
        viewport.Transform.ToScreen(0, 0).Y.Should().BeApproximately(300, 1e-9);
    }

    [Fact]
    [Trait("Category", "Viewport")]
    public void Fit_on_an_unmeasured_window_changes_nothing()
    {
        var viewport = Window(scale: 2, offsetX: 5, offsetY: 7).Resize(0, 0);

        viewport.FitTo(new SpatialRect(0, 0, 100, 100)).Should().Be(viewport);
    }

    #endregion

    /// <summary>一个有尺寸的视口。缺省的缩放与平移取"看得见文档原点"的那一份。</summary>
    private static Viewport Window(
        double scale = 1,
        double offsetX = 0,
        double offsetY = 0,
        double width = 800,
        double height = 600) =>
        Viewport.For(Theme.Default) with
        {
            Scale = scale,
            OffsetX = offsetX,
            OffsetY = offsetY,
            Width = width,
            Height = height,
        };
}
