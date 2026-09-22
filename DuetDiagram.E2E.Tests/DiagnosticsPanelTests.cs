using Avalonia.Headless;
using Avalonia.Input;
using DuetDiagram.App;
using DuetDiagram.App.Controls;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 性能诊断面板：快捷键开关，以及面板上的数字跟不跟得上帧。
/// </summary>
/// <remarks>
/// <para>
/// 这一层盯的是"面板在真实窗口里接上了没有"。分位数怎么算、窗口怎么转由渲染层的
/// 单元测试管，这里只验按键到面板、帧到数字这两条线。
/// </para>
/// <para>
/// **面板开着的代价不在这里判。** 真实窗口里量单帧耗时的噪声比面板本身的开销还大，
/// 在这里定一条一成的线会把测试变成掷骰子。那条判据落在帧率基准上
/// （`--benchmark-frames --diagnostics`），它量的是同一件事但噪声小得多。
/// </para>
/// </remarks>
public sealed class DiagnosticsPanelTests
{
    /// <summary>面板每多少帧换一次字。渲染够这个数才能看到它动。</summary>
    private const int RefreshIntervalFrames = 8;

    #region 开关

    [Fact]
    [Trait("Category", "Diagnostics")]
    public async Task The_panel_starts_hidden()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var panel = HeadlessFixture.Panel(window);

            window.Model.Diagnostics.IsOpen.Should().BeFalse();
            panel.IsVisible.Should().BeFalse("诊断面板要用户叫出来才出现，不能一开机就挡着图");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Diagnostics")]
    public async Task The_shortcut_opens_and_closes_the_panel()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var panel = HeadlessFixture.Panel(window);

            Shortcut(window);

            window.Model.Diagnostics.IsOpen.Should().BeTrue();
            panel.IsVisible.Should().BeTrue("快捷键开了，面板上就该看得见");

            Shortcut(window);

            window.Model.Diagnostics.IsOpen.Should().BeFalse();
            panel.IsVisible.Should().BeFalse("再按一次要收起来");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Diagnostics")]
    public async Task The_shortcut_needs_all_three_keys()
    {
        // 没有这一条，一个"只要按了 P 就开"的实现也能通过上一条——
        // 而那种实现会让用户在图上打不了字。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);

            window.Model.Diagnostics.IsOpen.Should().BeFalse("少了 Shift 不算这个快捷键");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Diagnostics")]
    public async Task The_close_button_goes_through_the_same_entry()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            Shortcut(window);

            window.Model.Diagnostics.IsOpen.Should().BeTrue();

            // 按钮与快捷键各写一套开关逻辑的话，两处迟早会不一致，
            // 而表现是"快捷键关了但面板还显示着"——看不出是哪一处错了。
            window.Model.Diagnostics.Toggle();

            window.Model.Diagnostics.IsOpen.Should().BeFalse();

            window.Close();
        });
    }

    #endregion

    #region 数字跟着帧

    [Fact]
    [Trait("Category", "Diagnostics")]
    public async Task An_empty_window_says_so_instead_of_showing_zeros()
    {
        // 显示一堆零的话读起来像"一切正常"，而真相是还没量过。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            Shortcut(window);

            window.Model.Diagnostics.Text.Should().Contain("还没有样本");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Diagnostics")]
    public async Task The_numbers_follow_the_frames()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            Shortcut(window);

            // 面板每八帧换一次字。渲染够八帧，那一批帧才刚进窗口。
            for (var index = 0; index < RefreshIntervalFrames; index++)
            {
                HeadlessFixture.Frame(window, canvas);
            }

            var first = window.Model.Diagnostics.Text;

            first.Should().NotContain("还没有样本", "已经画了这么多帧，窗口里不该还是空的");

            for (var index = 0; index < RefreshIntervalFrames; index++)
            {
                HeadlessFixture.Frame(window, canvas);
            }

            window.Model.Diagnostics.Text.Should().NotBe(
                first,
                "又画了八帧，窗口里的帧数变了，面板上的字要跟着变——"
                + "数字冻住的话，它显示的是打开那一刻的情况，而用户以为看到的是现在");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Diagnostics")]
    public async Task The_panel_reports_what_the_canvas_actually_drew()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            Shortcut(window);

            for (var index = 0; index < RefreshIntervalFrames; index++)
            {
                HeadlessFixture.Frame(window, canvas);
            }

            // 示例文档只有五个节点，走的是即时档，所以整份列表都该画出来。
            // 面板报的数与画布自己记的数对不上的话，面板就是在报一个别的东西。
            window.Model.Diagnostics.Text.Should().Contain($"画 {canvas.DrawnCommands}");
            window.Model.Diagnostics.Text.Should().Contain("即时");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Diagnostics")]
    public async Task Nothing_is_recorded_while_the_panel_is_closed()
    {
        // 关着的时候采样点每帧都会被调一次，所以它除了判断之外不能做任何事。
        // 没有这一条，一个"反正记下来也没人看"的实现也能让上面几条通过，
        // 而它每帧都在为没人看的东西记账。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            for (var index = 0; index < RefreshIntervalFrames * 2; index++)
            {
                HeadlessFixture.Frame(window, canvas);
            }

            window.Model.Diagnostics.Enabled.Should().BeFalse();

            Shortcut(window);

            window.Model.Diagnostics.Text.Should().Contain(
                "还没有样本",
                "面板关着的时候不该攒下任何样本");

            window.Close();
        });
    }

    #endregion

    /// <summary>按一次 Ctrl+Shift+P。</summary>
    private static void Shortcut(MainWindow window) =>
        window.KeyPressQwerty(
            PhysicalKey.P,
            RawInputModifiers.Control | RawInputModifiers.Shift);
}
