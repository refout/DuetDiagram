using Avalonia;
using Avalonia.Layout;
using DuetDiagram.App;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 画布外面的两条滚动条：在不在、位置对不对、拖了视图动不动。
/// </summary>
/// <remarks>
/// <para>
/// 这一层测的是"条与视图之间那条线接上了没有"。范围本身算得对不对由渲染层的单元测试管，
/// 这里只盯两件事：模型给的数有没有真的走到条上，以及用户动条的时候模型有没有被改到。
/// </para>
/// <para>
/// **不合成拖动拇指。** 条上那组"翻页 / 滚到顶 / 滚到底"的公开方法同样会改位置并抛出事件，
/// 走的正是"用户动条 → 模型"那条线，而它们不受命中测试与拖动阈值的影响。
/// </para>
/// </remarks>
public sealed class ScrollBarTests
{
    #region 摆上了没有

    [Fact]
    [Trait("Category", "ScrollBar")]
    public async Task The_window_has_a_visible_horizontal_and_a_vertical_bar()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            foreach (var orientation in (Orientation[])[Orientation.Vertical, Orientation.Horizontal])
            {
                var bar = HeadlessFixture.ScrollBar(window, orientation);

                bar.IsVisible.Should().BeTrue($"{orientation} 那一条要看得见");

                // 默认是两像素、悬停才展开的细线，连命中区域都几乎没有——用户拖不动它。
                bar.AllowAutoHide.Should().BeFalse($"{orientation} 那一条要常显，不能是悬停才展开的细线");
                bar.IsExpanded.Should().BeTrue($"{orientation} 那一条要处在展开状态");

                bar.Bounds.Width.Should().BeGreaterThan(4, $"{orientation} 那一条要占得住，不能是一条缝");
                bar.Bounds.Height.Should().BeGreaterThan(4);
            }

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ScrollBar")]
    public async Task The_bar_range_matches_the_view_model()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var model = window.Model;

            var horizontal = HeadlessFixture.ScrollBar(window, Orientation.Horizontal);
            var vertical = HeadlessFixture.ScrollBar(window, Orientation.Vertical);

            horizontal.Maximum.Should().BeApproximately(model.HorizontalScroll.Maximum, 1e-6);
            horizontal.ViewportSize.Should().BeApproximately(model.HorizontalScroll.ViewportLength, 1e-6);
            horizontal.Value.Should().BeApproximately(model.HorizontalScroll.Value, 1e-6);

            vertical.Maximum.Should().BeApproximately(model.VerticalScroll.Maximum, 1e-6);
            vertical.ViewportSize.Should().BeApproximately(model.VerticalScroll.ViewportLength, 1e-6);
            vertical.Value.Should().BeApproximately(model.VerticalScroll.Value, 1e-6);

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ScrollBar")]
    public async Task The_thumb_sits_in_the_middle_after_fit()
    {
        // 适配把内容摆正中间，于是拇指落在轨道正中间，与内容多大无关。
        // 内容比视口小得多时这一点尤其重要：按内容外接框算范围的话，拇指会贴在一端。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            var horizontal = HeadlessFixture.ScrollBar(window, Orientation.Horizontal);
            var vertical = HeadlessFixture.ScrollBar(window, Orientation.Vertical);

            horizontal.Value.Should().BeApproximately(horizontal.Maximum / 2, 1e-6);
            vertical.Value.Should().BeApproximately(vertical.Maximum / 2, 1e-6);

            window.Close();
        });
    }

    #endregion

    #region 拖了动不动

    [Fact]
    [Trait("Category", "ScrollBar")]
    public async Task Moving_the_bar_moves_the_view()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var model = window.Model;

            var horizontal = HeadlessFixture.ScrollBar(window, Orientation.Horizontal);
            var vertical = HeadlessFixture.ScrollBar(window, Orientation.Vertical);

            // 一格：往右滚，内容往左走，位移正好是一格。
            var beforeX = model.Viewport.OffsetX;

            horizontal.LineRight();

            model.Viewport.OffsetX.Should().BeApproximately(beforeX - horizontal.SmallChange, 1e-6);

            var beforeY = model.Viewport.OffsetY;

            vertical.LineDown();

            model.Viewport.OffsetY.Should().BeApproximately(beforeY - vertical.SmallChange, 1e-6);

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ScrollBar")]
    public async Task The_bar_stops_at_its_range()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var model = window.Model;

            var horizontal = HeadlessFixture.ScrollBar(window, Orientation.Horizontal);

            horizontal.ScrollToHome();

            horizontal.Value.Should().BeApproximately(0, 1e-6);

            var atTheHome = model.Viewport.OffsetX;

            horizontal.ScrollToEnd();

            horizontal.Value.Should().BeApproximately(horizontal.Maximum, 1e-6);
            model.Viewport.OffsetX.Should().BeApproximately(
                atTheHome - horizontal.Maximum,
                1e-6,
                "从头滚到底，视图要正好挪过一整条范围");

            var atTheEnd = model.Viewport.OffsetX;

            horizontal.ScrollToEnd();

            model.Viewport.OffsetX.Should().BeApproximately(atTheEnd, 1e-6, "已经到头了，再滚一次不该继续挪");
            horizontal.Value.Should().BeApproximately(horizontal.Maximum, 1e-6);

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ScrollBar")]
    public async Task Moving_the_view_moves_the_bar()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var model = window.Model;

            var horizontal = HeadlessFixture.ScrollBar(window, Orientation.Horizontal);
            var vertical = HeadlessFixture.ScrollBar(window, Orientation.Vertical);

            // 中键拖动与滚动条是两条路，落点相同。中键把视图往左拖，
            // 条上的位置就该跟着往右走——不跟的话，用户下一次碰条的时候视图会跳。
            var beforeX = horizontal.Value;

            model.PanBy(-50, 0);

            horizontal.Value.Should().BeApproximately(beforeX + 50, 1e-6);

            var beforeY = vertical.Value;

            model.PanBy(0, -30);

            vertical.Value.Should().BeApproximately(beforeY + 30, 1e-6);

            window.Close();
        });
    }

    #endregion
}
