using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using DuetDiagram.App;
using DuetDiagram.App.Controls;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 调色板面板：清单、真渲染的预览、新建改删各进一条历史，以及「谁在用」的名单。
/// </summary>
/// <remarks>
/// <para>
/// 与图层面板同一套判据：**每一条操作都恰好进一条历史**，撤销之后面板与文档两边都对得上。
/// 操作一律经由界面上的控件触发（往输入框里填字、点一次按钮），这样验到的是
/// "控件把动作送到了面板"，而不是面板自己的方法。
/// </para>
/// <para>
/// 预览那一格不在这里比像素——它要与画布同一条解析路径这件事由
/// <see cref="PalettePanelViewModel"/> 的构造方式保证（读的就是会话的出笔主题），
/// 这里验的是"改了条目，用那个令牌的元素跟着变色"，那是预览存在的意义。
/// </para>
/// </remarks>
public sealed class PalettePanelTests
{
    #region 增改删（Category=PalettePanel）

    /// <summary>新建一个条目进一条历史，撤销一次整份还原。</summary>
    [Fact]
    [Trait("Category", "PalettePanel")]
    public async Task Defining_an_entry_emits_one_command_and_undo_removes_it()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var before = HistoryOf(window);

            Fill(window, "palette.new-name", "brand");
            Press(window, "palette.define").Should().Be(1, "新建：进一条历史");

            window.Session.Document.Palette.Find("brand").Should().NotBeNull();
            window.Palette.Rows.Select(row => row.Name).Should().Contain("brand");
            window.Palette.Rows.First(row => row.Name == "brand").Fill.Should().NotBeNull("新条目带主题的缺省配色，预览看得见");

            window.Session.Undo();

            window.Session.Document.Palette.Find("brand").Should().BeNull("撤销一次整份还原");
            window.Palette.Rows.Select(row => row.Name).Should().NotContain("brand");

            HistoryOf(window).Should().Be(before, "撤销本身不进历史");
            window.Palette.Error.Should().BeNull();

            window.Close();
        });
    }

    /// <summary>改条目的一个成员进一条历史。</summary>
    [Fact]
    [Trait("Category", "PalettePanel")]
    public async Task Editing_a_member_emits_one_command()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = OpenWithBrand();

            window.Palette.Select("brand");

            Fill(window, "palette.palette.fill", "#123456");
            Blur(window, "palette.palette.fill").Should().Be(1, "改一个成员：进一条历史");

            window.Session.Document.Palette.Find("brand")!.Fill.Should().Be("#123456");

            window.Close();
        });
    }

    /// <summary>删一个没人用的条目进一条历史，撤销把它整份拿回来。</summary>
    [Fact]
    [Trait("Category", "PalettePanel")]
    public async Task Deleting_an_unused_entry_emits_one_command()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = OpenWithBrand();

            window.Palette.Select("brand");
            Press(window, "palette.remove").Should().Be(1, "删除：进一条历史");

            window.Session.Document.Palette.Find("brand").Should().BeNull();
            window.Palette.SelectedName.Should().BeNull("选中的条目没了，选中跟着落空");

            window.Session.Undo();

            window.Session.Document.Palette.Find("brand").Should().NotBeNull("撤销把条目整份拿回来");

            window.Close();
        });
    }

    /// <summary>名为空与重名都被挡下，面板上给出结构化错误，文档不动。</summary>
    [Fact]
    [Trait("Category", "PalettePanel")]
    public async Task An_empty_or_duplicate_name_is_refused()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            // 空名：按钮点了等于没点。
            Press(window, "palette.define").Should().Be(0);
            window.Palette.Error.Should().NotBeNull("挡下来要说一句，不能什么都没发生");
            window.Palette.Error.Should().Contain("空");

            // 重名：与示例文档里已有的节点标识无关，与已有条目比对。
            window.Session.DefinePaletteEntry(new PaletteEntry { Name = "brand", Fill = "#101010" });
            window.Palette.Refresh();

            Fill(window, "palette.new-name", "brand");
            Press(window, "palette.define").Should().Be(0, "重名不覆盖");
            window.Palette.Error.Should().NotBeNull().And.Contain("brand");

            window.Session.Document.Palette.Entries.Should().ContainKey("brand", "被挡下的那次没有改动文档");

            window.Close();
        });
    }

    #endregion

    #region 谁在用（Category=ErrorPresentation）

    /// <summary>删一个还在被引用的条目：不发命令，面板把引用它的元素列出来。</summary>
    /// <remarks>
    /// 命令层也会挡（错误消息里带着同一份名单），但面板在按下**之前**就查一遍——
    /// 错误消息里那串名字挤在一句话里，而「谁在用」值得整块地方摆出来。
    /// 两处读的是同一个方法，不会对出两份不一样的答案。
    /// </remarks>
    [Fact]
    [Trait("Category", "ErrorPresentation")]
    public async Task Deleting_an_entry_in_use_lists_the_referrers()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = OpenWithBrand();

            // start 用上了这个令牌。
            window.Session.SetSelection(["start"]);
            window.Session.Apply(FieldNames.StyleToken, "brand").IsSuccess.Should().BeTrue();

            var before = HistoryOf(window);

            window.Palette.Select("brand");
            Press(window, "palette.remove").Should().Be(0, "还在被引用，命令根本不发");

            window.Palette.Error.Should().NotBeNull();
            window.Palette.Error.Should().Contain("start", "名单里要有引用它的元素");

            window.Session.Document.Palette.Find("brand").Should().NotBeNull("条目还在");
            HistoryOf(window).Should().Be(before);

            window.Close();
        });
    }

    #endregion

    #region 与画布对上（Category=PropertyPanel）

    /// <summary>在面板上改一个条目的填充色，用那个令牌的元素跟着变色。</summary>
    /// <remarks>
    /// 这一条同时验着两件事：调色板真的进了解析路径（会话的出笔主题带上了文档的调色板），
    /// 以及面板预览与画布看到的是同一个来源——预览读的就是这份主题。
    /// </remarks>
    [Fact]
    [Trait("Category", "PropertyPanel")]
    public async Task Editing_an_entry_recolors_the_elements_using_its_token()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = OpenWithBrand();

            window.Session.SetSelection(["start"]);
            window.Session.Apply(FieldNames.StyleToken, "brand").IsSuccess.Should().BeTrue();

            FillFor(window, "start").Should().NotBe("#ff8800", "前提：改之前不是那个颜色");

            window.Palette.Select("brand");
            Fill(window, "palette.palette.fill", "#ff8800");
            Blur(window, "palette.palette.fill");

            FillFor(window, "start").Should().Be("#ff8800", "令牌条目改了，用它的元素跟着变色");

            // 预览读的是同一份解析结果，所以色块也换了颜色。
            window.Palette.Rows.First(row => row.Name == "brand").Fill.Should().Be("#ff8800");

            window.Close();
        });
    }

    #endregion

    #region 辅助

    /// <summary>开一个窗口并定义一个 brand 条目。</summary>
    private static MainWindow OpenWithBrand()
    {
        var window = HeadlessFixture.Open();

        window.Session.DefinePaletteEntry(new PaletteEntry
        {
            Name = "brand",
            Fill = "#e9eef5",
            Stroke = "#93a0b0",
        });

        window.Palette.Refresh();

        return window;
    }

    /// <summary>历史里现在有几条。</summary>
    private static int HistoryOf(MainWindow window) =>
        window.Session.Bus.Context.History.UndoEntries().Count;

    /// <summary>某个节点现在的填充色，从绘制列表里读。</summary>
    private static string FillFor(MainWindow window, string nodeId)
    {
        var shape = window.Model.DrawList.Commands
            .OfType<DuetDiagram.Render.DrawShape>()
            .First(command => command.ElementId == nodeId);

        return shape.Fill;
    }

    /// <summary>往一个输入框里填字。</summary>
    private static void Fill(MainWindow window, string id, string text) =>
        HeadlessFixture.Editor<TextBox>(window, id).Text = text;

    /// <summary>在输入框里按一次回车——文本框在这一刻提交。</summary>
    /// <returns>这一下之后历史里多了几条。</returns>
    private static int Blur(MainWindow window, string id)
    {
        var before = HistoryOf(window);

        HeadlessFixture.Editor<TextBox>(window, id).RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
        });

        return HistoryOf(window) - before;
    }

    /// <summary>点一个按钮。</summary>
    /// <returns>这一下之后历史里多了几条。</returns>
    private static int Press(MainWindow window, string id)
    {
        var before = HistoryOf(window);

        HeadlessFixture.Editor<Button>(window, id)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        return HistoryOf(window) - before;
    }

    #endregion
}
