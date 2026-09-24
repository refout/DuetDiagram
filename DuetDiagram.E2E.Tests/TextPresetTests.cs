using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using DuetDiagram.App;
using DuetDiagram.App.Controls;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Commands;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 文本预设面板：清单、新建改删各进一条历史，以及应用到选中之后的批量撤销。
/// </summary>
/// <remarks>
/// <para>
/// 与调色板面板同一套判据：操作一律经由界面上的控件触发（填字、回车、点按钮），
/// 验的是"控件把动作送到了面板"，而不是面板自己的方法。
/// </para>
/// <para>
/// 应用那一条用例钉的是任务清单里那两句约束：**叠加不是替换**——节点自己调过的
/// 字号不被只声明字体的预设抹掉；**批量是一次命令**——多选之后点一次「应用到选中」
/// 进一条历史，撤销按一次全部还原。
/// </para>
/// </remarks>
public sealed class TextPresetTests
{
    #region 增改删（Category=TextPreset）

    /// <summary>新建一个预设进一条历史，撤销一次整份还原。</summary>
    [Fact]
    [Trait("Category", "TextPreset")]
    public async Task Defining_a_preset_emits_one_command_and_undo_removes_it()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var before = HistoryOf(window);

            Fill(window, "textpreset.new-name", "标题");
            Press(window, "textpreset.define").Should().Be(1, "新建：进一条历史");

            window.Session.Document.TextPresets.Should().ContainSingle(p => p.Name == "标题");
            window.TextPresets.Rows.Select(row => row.Name).Should().Contain("标题");

            window.Session.Undo();

            window.Session.Document.TextPresets.Should().NotContain(p => p.Name == "标题", "撤销一次整份还原");
            window.TextPresets.Rows.Select(row => row.Name).Should().NotContain("标题");

            HistoryOf(window).Should().Be(before, "撤销本身不进历史");
            window.TextPresets.Error.Should().BeNull();

            window.Close();
        });
    }

    /// <summary>改预设的一个成员进一条历史，撤销把它改回来。</summary>
    [Fact]
    [Trait("Category", "TextPreset")]
    public async Task Editing_a_member_emits_one_command()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = OpenWithPreset();

            window.TextPresets.Select("preset1");

            Fill(window, "textpreset.preset.fontSize", "24");
            Blur(window, "textpreset.preset.fontSize").Should().Be(1, "改一个成员：进一条历史");

            window.Session.Document.TextPresets.Single(p => p.Id == "preset1")
                .Style.FontSize.Should().Be(24);

            window.Session.Undo();
            window.Session.Document.TextPresets.Single(p => p.Id == "preset1")
                .Style.FontSize.Should().BeNull("撤销把成员改回去");

            window.Close();
        });
    }

    /// <summary>删一个预设进一条历史；按值应用的样式不长在别人身上，删除不需要先问谁在用。</summary>
    [Fact]
    [Trait("Category", "TextPreset")]
    public async Task Deleting_a_preset_emits_one_command_even_after_it_was_applied()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = OpenWithPreset();

            // 先应用过一次：就算这样，删除也不需要查引用——应用是按值抄过去的。
            window.Session.SetSelection(["start"]);
            window.Session.ApplyTextPreset("preset1").IsEffectiveSuccess.Should().BeTrue();
            window.TextPresets.Refresh();

            window.TextPresets.Select("preset1");
            Press(window, "textpreset.remove").Should().Be(1, "删除：进一条历史");

            window.Session.Document.TextPresets.Should().NotContain(p => p.Id == "preset1");
            window.TextPresets.SelectedId.Should().BeNull("选中的预设没了，选中跟着落空");

            window.Session.Undo();
            window.Session.Document.TextPresets.Should().Contain(p => p.Id == "preset1", "撤销把预设整份拿回来");

            window.Close();
        });
    }

    /// <summary>名为空被挡下，面板上给出结构化错误，文档不动。</summary>
    [Fact]
    [Trait("Category", "TextPreset")]
    public async Task An_empty_name_is_refused()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            Press(window, "textpreset.define").Should().Be(0);
            window.TextPresets.Error.Should().NotBeNull("挡下来要说一句，不能什么都没发生");

            window.Session.Document.TextPresets.Should().BeEmpty("被挡下的那次没有改动文档");

            window.Close();
        });
    }

    #endregion

    #region 应用到选中（Category=TextPreset）

    /// <summary>选一个节点应用预设：声明了的成员盖上去，没声明的保持节点自己的值。</summary>
    [Fact]
    [Trait("Category", "TextPreset")]
    public async Task Applying_overlays_declared_members_and_keeps_the_others()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = OpenWithPreset();

            // start 自己调过字号 14；预设只声明字体。
            window.Session.SetSelection(["start"]);
            window.Session.Apply(FieldNames.TextFontSize, "14").IsEffectiveSuccess.Should().BeTrue();
            window.TextPresets.Refresh();

            window.TextPresets.Select("preset1");
            Press(window, "textpreset.apply").Should().Be(1, "应用：进一条历史");

            var text = window.Session.Document.Nodes.Single(n => n.Id == "start").Text;
            text.Should().NotBeNull();
            text!.FontFamily.Should().Be("Consolas", "预设声明了的成员盖上去");
            text.FontSize.Should().Be(14, "预设没声明的成员保持节点自己的值——是叠加，不是替换");

            window.TextPresets.Error.Should().BeNull();
            window.Close();
        });
    }

    /// <summary>多选之后应用：一条命令，撤销一次全部还原，字体的字号跟着变。</summary>
    [Fact]
    [Trait("Category", "TextPreset")]
    public async Task A_batch_application_is_one_command_and_one_undo_restores_all()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = OpenWithPreset(fontFamily: null, fontSize: 20);

            window.Session.SetSelection(["start", "check", "pass"]);
            window.TextPresets.Refresh();

            window.TextPresets.Select("preset1");
            Press(window, "textpreset.apply").Should().Be(1, "批量是一次命令，不是每个元素各发一次");

            window.Session.Document.Nodes.Single(n => n.Id == "start").Text!.FontSize.Should().Be(20);
            window.Session.Document.Nodes.Single(n => n.Id == "check").Text!.FontSize.Should().Be(20);
            window.Session.Document.Nodes.Single(n => n.Id == "pass").Text!.FontSize.Should().Be(20);

            window.Session.Undo();

            window.Session.Document.Nodes.Single(n => n.Id == "start").Text.Should().BeNull("撤销一次全部还原");
            window.Session.Document.Nodes.Single(n => n.Id == "check").Text.Should().BeNull();
            window.Session.Document.Nodes.Single(n => n.Id == "pass").Text.Should().BeNull();
            window.Session.Document.TextPresets.Should().Contain(p => p.Id == "preset1", "撤销应用不动预设本身");

            window.Close();
        });
    }

    /// <summary>画布上没有选中节点时点「应用到选中」：命令被挡下，面板把原因摆出来。</summary>
    [Fact]
    [Trait("Category", "TextPreset")]
    public async Task Applying_without_a_selection_shows_a_structured_error()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = OpenWithPreset();

            var before = HistoryOf(window);

            window.TextPresets.Select("preset1");
            Press(window, "textpreset.apply").Should().Be(0, "没有点名任何节点，命令不发");

            window.TextPresets.Error.Should().NotBeNull();
            HistoryOf(window).Should().Be(before);

            window.Close();
        });
    }

    #endregion

    #region 辅助

    /// <summary>
    /// 开一个窗口并定义一个预设，成员走会话的入口补上（成员编辑器另有用例盯着）。
    /// </summary>
    private static MainWindow OpenWithPreset(string? fontFamily = "Consolas", double? fontSize = null)
    {
        var window = HeadlessFixture.Open();

        window.Session.DefineTextPreset("标题").IsEffectiveSuccess.Should().BeTrue();

        if (fontFamily is not null)
        {
            window.Session.UpdateTextPreset("preset1", FieldNames.PresetFontFamily, fontFamily)
                .IsEffectiveSuccess.Should().BeTrue();
        }

        if (fontSize is not null)
        {
            window.Session.UpdateTextPreset(
                "preset1",
                FieldNames.PresetFontSize,
                fontSize.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
                .IsEffectiveSuccess.Should().BeTrue();
        }

        window.TextPresets.Refresh();

        return window;
    }

    /// <summary>历史里现在有几条。</summary>
    private static int HistoryOf(MainWindow window) =>
        window.Session.Bus.Context.History.UndoEntries().Count;

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
