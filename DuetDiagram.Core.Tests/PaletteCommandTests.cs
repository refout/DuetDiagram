using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 调色板的三条命令：定义、改一个成员、删除。
/// </summary>
/// <remarks>
/// <para>
/// 调色板是文档的子对象而不是元素字段，所以它单立命令。三条命令都只计外观：
/// 换颜色不改变节点尺寸，已算出的坐标仍然有效，报成结构变更会让换一次主题就重排一遍整张图。
/// </para>
/// <para>
/// 删除那一条与其它两条的口径不同：它要挡住一次**会造成静默损坏**的删除。
/// 渲染层遇到认不出的令牌只是退回元素自己的样式，不会报错、不会崩，
/// 所以用户看到的是一次"什么都没发生"的删除，配上一张颜色不对的图。
/// </para>
/// </remarks>
public sealed class PaletteCommandTests
{
    #region 定义

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Defining_an_entry_adds_it_without_moving_the_structural_hash()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var structuralBefore = harness.Document.StructuralHash;
        var visualBefore = harness.Document.VisualHash;

        var result = harness.DefinePalette("warn", fill: "#ffeeaa", stroke: "#cc8800");

        result.IsEffectiveSuccess.Should().BeTrue();
        result.StructuralChanged.Should().BeFalse("颜色不改变节点尺寸，坐标仍然有效");
        result.VisualChanged.Should().BeTrue();

        harness.Document.StructuralHash.Should().Be(structuralBefore);
        harness.Document.VisualHash.Should().NotBe(visualBefore);

        harness.Document.Palette.Find("warn").Should().NotBeNull();
        harness.Document.Palette.Find("warn")!.Fill.Should().Be("#ffeeaa");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_duplicate_name_is_rejected_and_the_existing_entry_is_left_alone()
    {
        using var harness = new Harness();
        harness.DefinePalette("warn", fill: "#ffeeaa");

        var before = harness.Snapshot();

        // 覆盖的话，一次本想"加个新令牌"的调用会把一个正被几百个节点引用的令牌
        // 悄悄换掉外观，而调用方以为自己只是加了个东西。
        var result = harness.DefinePalette("warn", fill: "#ff0000");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.DuplicateId);

        harness.Snapshot().Should().Be(before);
        harness.Document.Palette.Find("warn")!.Fill.Should().Be("#ffeeaa");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_empty_name_is_rejected()
    {
        using var harness = new Harness();

        var result = harness.DefinePalette("   ", fill: "#ffffff");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.InvalidId);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undoing_a_definition_puts_the_content_back()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.DefinePalette("ok", fill: "#ddffdd");

        // 撤销会让版本往前走，所以整份序列化结果比不了——版本号是它的一部分。
        // 比内容用两个哈希：调色板进的是视觉哈希，而视觉哈希包含结构那部分内容。
        var structuralBefore = harness.Document.StructuralHash;
        var visualBefore = harness.Document.VisualHash;

        harness.DefinePalette("warn", fill: "#ffeeaa");
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Document.Palette.Find("warn").Should().BeNull();
        harness.Document.Palette.Find("ok").Should().NotBeNull();
        harness.Document.StructuralHash.Should().Be(structuralBefore);
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    #endregion

    #region 改一个成员

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Updating_one_member_leaves_the_others_alone()
    {
        using var harness = new Harness();
        harness.DefinePalette("warn", fill: "#ffeeaa", stroke: "#cc8800");

        var result = harness.UpdatePalette("warn", FieldNames.PaletteFill, "#fff0b0");

        result.IsEffectiveSuccess.Should().BeTrue();
        result.StructuralChanged.Should().BeFalse();

        var entry = harness.Document.Palette.Find("warn")!;
        entry.Fill.Should().Be("#fff0b0");
        entry.Stroke.Should().Be("#cc8800", "只改了一个成员，别的成员不该跟着动");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_unknown_field_is_rejected_with_its_own_error_code()
    {
        using var harness = new Harness();
        harness.DefinePalette("warn", fill: "#ffeeaa");

        var before = harness.Snapshot();

        // 字段名认不出与值不合法分成两个码：前者说明调用方拿的是另一套字段表
        // （版本对不上或拼错了），后者只说明这一次输入有问题，处置完全不同。
        var result = harness.UpdatePalette("warn", "palette.background", "#000000");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.FieldUnknown);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Updating_a_missing_entry_is_rejected_and_does_not_create_it()
    {
        using var harness = new Harness();

        var before = harness.Snapshot();

        // 顺手建的话，一次拼错令牌名的调用会造出一个只有一半成员的条目，
        // 而引用它的节点会照着那个半成品上色。
        var result = harness.UpdatePalette("ghost", FieldNames.PaletteFill, "#ffffff");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.PaletteEntryMissing);

        harness.Document.Palette.Entries.Should().BeEmpty();
        harness.Snapshot().Should().Be(before);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [Trait("Category", "Atomicity")]
    public void An_unusable_weight_is_rejected(string value)
    {
        using var harness = new Harness();
        harness.DefinePalette("warn", fill: "#ffeeaa");

        var before = harness.Snapshot();

        var result = harness.UpdatePalette("warn", FieldNames.PaletteWeight, value);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.FieldValueInvalid);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Writing_the_same_value_back_is_a_no_op()
    {
        using var harness = new Harness();
        harness.DefinePalette("warn", fill: "#ffeeaa");

        var versionBefore = harness.Document.Version;

        harness.UpdatePalette("warn", FieldNames.PaletteFill, "#ffeeaa").IsNoOp.Should().BeTrue();

        harness.Document.Version.Should().Be(versionBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undoing_an_update_puts_the_content_back()
    {
        using var harness = new Harness();
        harness.DefinePalette("warn", fill: "#ffeeaa", stroke: "#cc8800");

        var visualBefore = harness.Document.VisualHash;

        harness.UpdatePalette("warn", FieldNames.PaletteStroke, "#884400");
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Document.Palette.Find("warn")!.Stroke.Should().Be("#cc8800");
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    #endregion

    #region 删除

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Removing_an_entry_that_nobody_uses_succeeds()
    {
        using var harness = new Harness();
        harness.DefinePalette("warn", fill: "#ffeeaa");

        var result = harness.RemovePalette("warn");

        result.IsEffectiveSuccess.Should().BeTrue();
        harness.Document.Palette.Find("warn").Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Removing_a_missing_entry_is_rejected()
    {
        using var harness = new Harness();

        var result = harness.RemovePalette("ghost");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.PaletteEntryMissing);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_entry_a_node_still_uses_is_not_removed_and_the_node_is_named()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.DefinePalette("warn", fill: "#ffeeaa");
        harness.SetStyleToken("a", "warn").IsEffectiveSuccess.Should().BeTrue();

        var before = harness.Snapshot();

        var result = harness.RemovePalette("warn");

        // 只说一句"还有人在用"，用户无从知道该先改哪些元素——
        // 所以引用者的标识要随错误一起给出。
        result.IsSuccess.Should().BeFalse();
        var error = result.Errors.Should().ContainSingle(
            e => e.Code == ErrorCodes.PaletteEntryInUse).Subject;
        error.Payload.Should().Contain("a");

        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_entry_an_edge_still_uses_is_not_removed()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.Connect("e1", "a", "b");
        harness.DefinePalette("warn", fill: "#ffeeaa");

        harness.Bus.Execute(new SetEdgeFieldCommand("e1", FieldNames.Style, """{"styleToken":"warn"}""")
            .WithContext(ChangeContext.For(ChangeSource.Human, "tester")))
            .IsEffectiveSuccess.Should().BeTrue();

        var before = harness.Snapshot();

        var result = harness.RemovePalette("warn");

        // 引用者只看样式令牌这一条路，节点与边两处都要扫到。
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.PaletteEntryInUse)
            .Which.Payload.Should().Contain("e1");

        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_entry_becomes_removable_once_the_last_element_stops_using_it()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.DefinePalette("warn", fill: "#ffeeaa");
        harness.SetStyleToken("a", "warn");

        harness.SetStyleToken("a", string.Empty).IsEffectiveSuccess.Should().BeTrue();

        harness.RemovePalette("warn").IsEffectiveSuccess.Should().BeTrue();
        harness.Document.Palette.Find("warn").Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undoing_a_removal_puts_the_content_back()
    {
        using var harness = new Harness();
        harness.DefinePalette("warn", fill: "#ffeeaa", stroke: "#cc8800");
        harness.DefinePalette("ok", fill: "#ddffdd");

        var visualBefore = harness.Document.VisualHash;

        harness.RemovePalette("warn");
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        // 记的是整份调色板，所以另一个条目也一并回到原样。
        harness.Document.Palette.Find("warn")!.Stroke.Should().Be("#cc8800");
        harness.Document.Palette.Find("ok").Should().NotBeNull();
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    #endregion

    #region Memento 往返

    [Fact]
    [Trait("Category", "MementoRegistration")]
    public void A_palette_memento_survives_a_serialization_round_trip()
    {
        using var harness = new Harness();
        harness.DefinePalette("warn", fill: "#ffeeaa", stroke: "#cc8800");
        harness.DefinePalette("ok", fill: "#ddffdd");

        // 调色板的条目表是字典，序列化时最容易出问题的是它的键值对形状。
        // 往返一趟才能确认反序列化出来的那份还能被整份还原回去。
        var memento = new PaletteMemento { Previous = harness.Document.Palette };

        var restored = (PaletteMemento)DiagramSerializer.DeserializeMemento(
            DiagramSerializer.SerializeMemento(memento));

        restored.Previous.Should().Be(harness.Document.Palette);
        restored.Previous.Find("warn")!.Stroke.Should().Be("#cc8800");
    }

    #endregion
}
