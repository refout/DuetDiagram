using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 窗口里各块面板分到的那一格里，有没有东西被挤没了。
/// </summary>
/// <remarks>
/// <para>
/// 面板被挤到零高度时它还在视觉树里、也能被键盘 Tab 到，只是屏幕上一个像素都不占——
/// 看起来像"这个功能没做"，而功能测试一条都不会红。所以这一条量的是**屏幕上的尺寸**，
/// 不是"控件在不在"。
/// </para>
/// <para>
/// 都要在允许的最小窗口上再量一遍：撑满一屏时看不出问题，挤一挤才看得出。
/// </para>
/// </remarks>
public sealed class CanvasLayoutTests
{
    [Fact]
    [Trait("Category", "Canvas")]
    public async Task No_panel_in_the_left_column_is_squeezed_to_nothing()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            foreach (var panel in LeftPanels(window))
            {
                panel.IsVisible.Should().BeTrue($"{Name(panel)} 是常驻分区，不该被藏起来");
                panel.Bounds.Height.Should().BeGreaterThan(
                    0,
                    $"{Name(panel)} 被压成零高度时它还在视觉树里、也 Tab 得到，"
                    + "但屏幕上一条都看不见");
            }

            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            HeadlessFixture.Frame(window, canvas);

            foreach (var panel in LeftPanels(window))
            {
                panel.Bounds.Height.Should().BeGreaterThan(
                    0,
                    $"缩到允许的最小窗口之后 {Name(panel)} 也不能被挤没——"
                    + "挤得下就摆，挤不下就该让整列滚起来");
            }

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Canvas")]
    public async Task The_toolbar_wraps_instead_of_pushing_buttons_out_of_view()
    {
        // 窗口自己有个最小宽度挡着，所以这里先把它放开，单独看工具栏这一条：
        // 它放不放得下只与按钮条数有关，而条数由注册表定，不由窗口定。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);
            var toolbar = HeadlessFixture.ToolBar(window);

            var oneLine = toolbar.Bounds.Height;

            window.MinWidth = 0;
            window.Width = 640;
            HeadlessFixture.Frame(window, canvas);

            toolbar.Bounds.Height.Should().BeGreaterThan(
                oneLine,
                "一行放不下时要折行；不折行的话后面的按钮跑到可视区外面，点不到也没迹象");

            var buttons = toolbar.GetVisualDescendants().OfType<Button>().ToList();

            buttons.Should().NotBeEmpty("工具栏上的按钮由注册表生成，一个都没有说明没接上");

            foreach (var button in buttons)
            {
                var at = button.TranslatePoint(new Point(0, 0), toolbar)
                    ?? throw new InvalidOperationException("按钮不在工具栏的视觉树里，量不出它在哪儿");

                at.X.Should().BeGreaterThanOrEqualTo(-1e-6, "按钮的左边不能跑到工具栏外面");
                (at.X + button.Bounds.Width).Should().BeLessThanOrEqualTo(
                    toolbar.Bounds.Width + 1e-6,
                    "按钮的右边不能跑到工具栏外面——跑到外面就点不到了");
            }

            window.Close();
        });
    }

    private static IEnumerable<Control> LeftPanels(Window window) =>
        [
            HeadlessFixture.Layers(window),
            HeadlessFixture.Shapes(window),
            HeadlessFixture.Templates(window),
            HeadlessFixture.Palette(window),
            HeadlessFixture.Presets(window),
        ];

    private static string Name(Control panel) => panel.GetType().Name;
}
