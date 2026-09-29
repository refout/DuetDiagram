using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 滚动条范围：内容外留多少余量、滚到哪儿、以及拖回去落在视口的哪个位置。
/// </summary>
/// <remarks>
/// <para>
/// 这一层测的是"范围与位置互为逆运算"，以及"图装得下时范围也不为零"。
/// 两件事都只有对着数字才看得出来——在屏幕上，一个算错的范围与一个算对的范围
/// 长得一模一样，区别只在于拖上去动不动。
/// </para>
/// <para>
/// **平移量不在这一层的钳制范围内。** 滚动条只能表达"到头了"，
/// 而视口可以停在任意位置；这一层只保证条上那个数落在合法区间里。
/// </para>
/// </remarks>
public sealed class ScrollRangeTests
{
    #region 范围

    [Fact]
    [Trait("Category", "ScrollRange")]
    public void A_range_leaves_room_even_when_the_content_fits()
    {
        // 内容比视口小得多。按内容外接框算的话范围是零、拇指占满轨道、拖上去一动不动，
        // 而"图装得下"恰恰是最常见的那一档。
        var viewport = Window(width: 800, height: 600);
        var content = new SpatialRect(0, 0, 100, 80);

        var horizontal = viewport.HorizontalScrollRange(content);

        horizontal.Maximum.Should().BeApproximately(400, 1e-9, "余量取四分之一视口，两边各一份");
        horizontal.Maximum.Should().BeGreaterThan(0);

        var vertical = viewport.VerticalScrollRange(content);

        vertical.Maximum.Should().BeApproximately(300, 1e-9);
    }

    [Fact]
    [Trait("Category", "ScrollRange")]
    public void The_pad_falls_back_to_the_fit_margin_on_a_tiny_viewport()
    {
        // 视口窄到四分之一还不如适配留白时，按比例算出来的余量小得没有意义。
        var content = new SpatialRect(0, 0, 100, 80);

        Window(width: 80, height: 80).HorizontalScrollRange(content).Maximum
            .Should().BeApproximately(68, 1e-9, "余量取适配留白 24，两边各一份");

        Window(width: 800, height: 600).HorizontalScrollRange(content).Maximum
            .Should().BeApproximately(400, 1e-9, "视口够大时按四分之一算");
    }

    [Fact]
    [Trait("Category", "ScrollRange")]
    public void A_larger_content_gives_a_longer_range_and_a_smaller_thumb()
    {
        var viewport = Window(width: 800, height: 600);
        var content = new SpatialRect(0, 0, 5000, 600);

        var horizontal = viewport.HorizontalScrollRange(content);

        horizontal.Maximum.Should().BeApproximately(4600, 1e-9, "内容 5000 加两边各 200 的余量，再减视口 800");
        horizontal.ViewportLength.Should().Be(800);

        // 拇指占比 = 视口长 / 范围总长。内容越长，拇指越小。
        (horizontal.ViewportLength / (horizontal.Maximum + horizontal.ViewportLength))
            .Should().BeApproximately(800 / 5400.0, 1e-9);
    }

    [Fact]
    [Trait("Category", "ScrollRange")]
    public void An_unmeasured_viewport_does_not_produce_a_broken_range()
    {
        // 首次排布之前视口的宽高都是零，而那时内容已经装进来了。
        var viewport = Window(width: 0, height: 0);
        var content = new SpatialRect(0, 0, 100, 80);

        foreach (var range in (ScrollRange[])[viewport.HorizontalScrollRange(content), viewport.VerticalScrollRange(content)])
        {
            range.Maximum.Should().BeGreaterThanOrEqualTo(0);
            range.ViewportLength.Should().Be(0);
            double.IsNaN(range.Maximum).Should().BeFalse();
            double.IsNaN(range.Value).Should().BeFalse();
            range.Value.Should().BeGreaterThanOrEqualTo(0).And.BeLessThanOrEqualTo(range.Maximum);
        }
    }

    #endregion

    #region 位置

    [Theory]
    [InlineData(0, 0, 100, 80)]
    [InlineData(0, 0, 800, 600)]
    [InlineData(0, 0, 5000, 4000)]
    [Trait("Category", "ScrollRange")]
    public void Fit_centers_the_thumb_on_both_axes(double x, double y, double width, double height)
    {
        // 适配把内容摆正中间，于是拇指落在轨道正中间，与内容多大无关。
        var content = new SpatialRect(x, y, width, height);
        var fitted = Window(width: 800, height: 600).FitTo(content);

        var horizontal = fitted.HorizontalScrollRange(content);
        var vertical = fitted.VerticalScrollRange(content);

        horizontal.Value.Should().BeApproximately(horizontal.Maximum / 2, 1e-9);
        vertical.Value.Should().BeApproximately(vertical.Maximum / 2, 1e-9);
    }

    [Fact]
    [Trait("Category", "ScrollRange")]
    public void Scrolling_round_trips()
    {
        var content = new SpatialRect(0, 0, 1200, 900);
        var viewport = Window(width: 800, height: 600);

        var maximum = viewport.HorizontalScrollRange(content).Maximum;

        foreach (var value in (double[])[0, maximum / 4, maximum / 2, maximum])
        {
            var scrolled = viewport.ScrollToX(content, value);

            scrolled.HorizontalScrollRange(content).Value.Should().BeApproximately(value, 1e-9);
            scrolled.Scale.Should().Be(viewport.Scale, "滚动不改缩放");
        }

        var verticalMaximum = viewport.VerticalScrollRange(content).Maximum;

        foreach (var value in (double[])[0, verticalMaximum / 3, verticalMaximum])
        {
            viewport.ScrollToY(content, value).VerticalScrollRange(content).Value
                .Should().BeApproximately(value, 1e-9);
        }
    }

    [Fact]
    [Trait("Category", "ScrollRange")]
    public void Panning_past_the_range_pins_the_thumb_without_clamping_the_offset()
    {
        var content = new SpatialRect(0, 0, 5000, 600);
        var viewport = Window(width: 800, height: 600);

        var farRight = viewport.PanBy(10000, 0);

        farRight.OffsetX.Should().Be(10000, "平移本身没有边界，滚动条到头了也不该把它拉回来");
        farRight.HorizontalScrollRange(content).Value.Should().Be(0);

        var farLeft = viewport.PanBy(-10000, 0);

        farLeft.OffsetX.Should().Be(-10000);
        farLeft.HorizontalScrollRange(content).Value.Should()
            .Be(farLeft.HorizontalScrollRange(content).Maximum);
    }

    [Fact]
    [Trait("Category", "ScrollRange")]
    public void Resizing_changes_the_range_and_keeps_the_offset()
    {
        // 内容 752 摆在 800 宽的视口里居中：余量 24 两边各一份，范围总长 1200，可滚 400。
        var content = new SpatialRect(0, 0, 752, 80);
        var viewport = Window(offsetX: 24, offsetY: 0, width: 800, height: 600);

        viewport.HorizontalScrollRange(content).Maximum.Should().BeApproximately(400, 1e-9);
        viewport.HorizontalScrollRange(content).Value.Should().BeApproximately(200, 1e-9);

        var resized = viewport.Resize(1000, 600);

        resized.OffsetX.Should().Be(24, "改窗口尺寸不重新适配");
        resized.HorizontalScrollRange(content).Maximum.Should().BeApproximately(500, 1e-9);
        resized.HorizontalScrollRange(content).Value.Should().BeApproximately(350, 1e-9);
    }

    [Fact]
    [Trait("Category", "ScrollRange")]
    public void Zooming_recomputes_the_range_around_the_content()
    {
        var content = new SpatialRect(0, 0, 1000, 800);
        var viewport = Window(width: 800, height: 600);

        var before = viewport.HorizontalScrollRange(content);

        var zoomed = viewport.ZoomAt(2, 400, 300);
        var after = zoomed.HorizontalScrollRange(content);

        after.Maximum.Should().BeGreaterThan(before.Maximum, "放大之后内容在屏幕上更长，能滚的距离跟着变长");
        after.Value.Should().BeGreaterThanOrEqualTo(0).And.BeLessThanOrEqualTo(after.Maximum);
        after.ViewportLength.Should().Be(800, "视口长度只跟窗口有关");
    }

    #endregion

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
