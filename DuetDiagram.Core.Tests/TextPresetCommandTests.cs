using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 文本预设的四条命令：定义、改一个成员、删除、批量应用。
/// </summary>
/// <remarks>
/// <para>
/// 四条都只计外观：预设进视觉哈希，定义、改、删不改变任何坐标。
/// 应用那一条改的是节点上的文本样式——字体变化会改变标签宽度，
/// 这一点由布局层对文本样式变化的既有处置负责，命令层不替它做决定。
/// </para>
/// <para>
/// 删除与调色板那条口径不同，而且**刻意**不同：应用是按值把成员抄到节点上，
/// IR 里没有任何一处回指预设，删除不会造成悬空引用——所以这里只查存在，
/// 不查「谁在用」。有一条用例把这一点钉住。
/// </para>
/// </remarks>
public sealed class TextPresetCommandTests
{
    #region 定义

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Defining_a_preset_adds_it_without_moving_the_structural_hash()
    {
        using var harness = new Harness();

        var structuralBefore = harness.Document.StructuralHash;
        var visualBefore = harness.Document.VisualHash;

        var result = harness.DefinePreset("tp1", "标题", new TextStyle { FontSize = 20 });

        result.IsEffectiveSuccess.Should().BeTrue();
        result.StructuralChanged.Should().BeFalse("预设进视觉哈希，定义一个预设不改变坐标");
        result.VisualChanged.Should().BeTrue();

        harness.Document.StructuralHash.Should().Be(structuralBefore);
        harness.Document.VisualHash.Should().NotBe(visualBefore);
        harness.Document.TextPresets.Should().ContainSingle(p => p.Id == "tp1");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_duplicate_id_is_rejected_and_the_existing_preset_is_left_alone()
    {
        using var harness = new Harness();
        harness.DefinePreset("tp1", "标题", new TextStyle { FontSize = 20 });

        var before = harness.Snapshot();

        // 标识在九个集合里撞车会让按标识定位的操作变得有歧义——节点也算。
        var result = harness.DefinePreset("tp1", "另一个");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.DuplicateId);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_id_that_collides_with_a_node_is_rejected()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var result = harness.DefinePreset("a", "标题");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.DuplicateId);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_empty_id_or_name_is_rejected()
    {
        using var harness = new Harness();

        // 预设靠名字被挑中，空名字的行在清单上分不出彼此。
        harness.DefinePreset("  ", "标题").IsSuccess.Should().BeFalse();
        harness.DefinePreset("tp1", "   ").IsSuccess.Should().BeFalse();

        harness.Document.TextPresets.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undoing_a_definition_puts_the_collection_back()
    {
        using var harness = new Harness();
        harness.DefinePreset("tp1", "标题");

        var visualBefore = harness.Document.VisualHash;

        harness.DefinePreset("tp2", "正文", new TextStyle { FontSize = 12 });
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Document.TextPresets.Should().ContainSingle(p => p.Id == "tp1");
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    #endregion

    #region 改一个成员

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Updating_one_member_leaves_the_others_alone()
    {
        using var harness = new Harness();
        harness.DefinePreset("tp1", "标题", new TextStyle { FontSize = 20, FontWeight = FontWeight.Bold });

        var result = harness.UpdatePreset("tp1", FieldNames.PresetFontSize, "24");

        result.IsEffectiveSuccess.Should().BeTrue();
        result.StructuralChanged.Should().BeFalse();

        var preset = harness.Document.TextPresets.Single(p => p.Id == "tp1");
        preset.Style.FontSize.Should().Be(24);
        preset.Style.FontWeight.Should().Be(FontWeight.Bold, "只改了一个成员，别的成员不该跟着动");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_unknown_field_is_rejected_with_the_available_fields_listed()
    {
        using var harness = new Harness();
        harness.DefinePreset("tp1", "标题");

        var before = harness.Snapshot();

        var result = harness.UpdatePreset("tp1", "preset.background", "#ffffff");

        result.IsSuccess.Should().BeFalse();
        var error = result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.FieldUnknown).Subject;

        // 白名单挡的是"渲染层只会静默按默认画"的未知字段——错误里要告诉调用方
        // 还有哪些字段可用，不然他只能逐个试。
        error.Payload.Should().Contain(FieldNames.PresetFontFamily).And.Contain(FieldNames.PresetFontSize);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Updating_a_missing_preset_is_rejected_and_does_not_create_it()
    {
        using var harness = new Harness();

        var before = harness.Snapshot();

        // 顺手建的话，一次拼错标识的调用会造出一个只有一半成员的预设。
        var result = harness.UpdatePreset("ghost", FieldNames.PresetFontSize, "12");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.TextPresetMissing);
        harness.Document.TextPresets.Should().BeEmpty();
        harness.Snapshot().Should().Be(before);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [Trait("Category", "Atomicity")]
    public void An_unusable_font_size_is_rejected(string value)
    {
        using var harness = new Harness();
        harness.DefinePreset("tp1", "标题");

        var before = harness.Snapshot();

        var result = harness.UpdatePreset("tp1", FieldNames.PresetFontSize, value);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.FieldValueInvalid);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Clearing_a_member_writes_nothing_rather_than_an_empty_string()
    {
        using var harness = new Harness();
        harness.DefinePreset("tp1", "标题", new TextStyle { FontFamily = "Arial" });

        var result = harness.UpdatePreset("tp1", FieldNames.PresetFontFamily, "   ");

        result.IsEffectiveSuccess.Should().BeTrue();
        harness.Document.TextPresets.Single(p => p.Id == "tp1")
            .Style.FontFamily.Should().BeNull("空文本表达的是「没有设置」，与空串在哈希与序列化上不是一回事");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Writing_the_same_value_back_is_a_no_op()
    {
        using var harness = new Harness();
        harness.DefinePreset("tp1", "标题", new TextStyle { FontSize = 20 });

        var versionBefore = harness.Document.Version;

        harness.UpdatePreset("tp1", FieldNames.PresetFontSize, "20").IsNoOp.Should().BeTrue();

        harness.Document.Version.Should().Be(versionBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undoing_an_update_puts_the_content_back()
    {
        using var harness = new Harness();
        harness.DefinePreset("tp1", "标题", new TextStyle { FontSize = 20 });
        harness.DefinePreset("tp2", "正文", new TextStyle { FontSize = 12 });

        var visualBefore = harness.Document.VisualHash;

        harness.UpdatePreset("tp1", FieldNames.PresetFontSize, "24");
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        // 记的是整份预设集合，另一个预设也一并回到原样。
        harness.Document.TextPresets.Single(p => p.Id == "tp1").Style.FontSize.Should().Be(20);
        harness.Document.TextPresets.Single(p => p.Id == "tp2").Style.FontSize.Should().Be(12);
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    #endregion

    #region 删除

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Removing_a_preset_succeeds_even_though_nodes_were_styled_from_it()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.DefinePreset("tp1", "标题", new TextStyle { FontSize = 20 });
        harness.ApplyPreset("tp1", ["a"]).IsEffectiveSuccess.Should().BeTrue();

        // 应用是按值把成员抄到节点上的，节点身上不存预设的标识。
        // 删掉之后已经应用的样式原样长在节点上——这正是这条用例要钉住的行为。
        var nodeTextBefore = harness.Node("a").Text;

        var result = harness.RemovePreset("tp1");

        result.IsEffectiveSuccess.Should().BeTrue();
        harness.Document.TextPresets.Should().BeEmpty();
        harness.Node("a").Text.Should().Be(nodeTextBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Removing_a_missing_preset_is_rejected()
    {
        using var harness = new Harness();

        var result = harness.RemovePreset("ghost");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.TextPresetMissing);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undoing_a_removal_puts_the_collection_back()
    {
        using var harness = new Harness();
        harness.DefinePreset("tp1", "标题", new TextStyle { FontSize = 20 });
        harness.DefinePreset("tp2", "正文", new TextStyle { FontSize = 12 });

        var visualBefore = harness.Document.VisualHash;

        harness.RemovePreset("tp1");
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Document.TextPresets.Single(p => p.Id == "tp1").Style.FontSize.Should().Be(20);
        harness.Document.TextPresets.Single(p => p.Id == "tp2").Style.FontSize.Should().Be(12);
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    #endregion

    #region 应用

    [Fact]
    [Trait("Category", "TextPreset")]
    public void Applying_overlays_declared_members_and_leaves_undeclared_ones_alone()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        // 节点自己调过的字号与颜色；预设只声明字体与斜体。
        harness.SetField("a", FieldNames.Text, """{"fontSize":18,"fontColor":"#3366cc"}""")
            .IsEffectiveSuccess.Should().BeTrue();
        harness.DefinePreset("tp1", "标题", new TextStyle { FontFamily = "Arial", Italic = true });

        var result = harness.ApplyPreset("tp1", ["a"]);

        result.IsEffectiveSuccess.Should().BeTrue();

        var text = harness.Node("a").Text;
        text.Should().NotBeNull();
        text!.FontFamily.Should().Be("Arial", "预设声明了的成员要盖上去");
        text.Italic.Should().BeTrue();
        text.FontSize.Should().Be(18, "预设没声明的成员保持节点自己的值——是叠加，不是替换");
        text.FontColor.Should().Be("#3366cc");
    }

    [Fact]
    [Trait("Category", "TextPreset")]
    public void Applying_to_a_node_without_any_text_style_uses_only_the_declared_members()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.DefinePreset("tp1", "标题", new TextStyle { FontSize = 20 });

        harness.ApplyPreset("tp1", ["a"]).IsEffectiveSuccess.Should().BeTrue();

        var text = harness.Node("a").Text;
        text.Should().NotBeNull();
        text!.FontSize.Should().Be(20);
        text.FontFamily.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "TextPreset")]
    public void A_batch_application_is_one_command_and_one_undo()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AddNode("c");
        harness.DefinePreset("tp1", "标题", new TextStyle { FontSize = 20 });

        var historyBefore = harness.Bus.Context.History.UndoEntries().Count;

        var result = harness.ApplyPreset("tp1", ["a", "b", "c"]);

        // 多选之后一次应用是一条命令：撤销按一次就把这一批全部还原，
        // 而不是每个元素各发一次、按三次撤销。
        result.IsEffectiveSuccess.Should().BeTrue();
        result.AffectedIds.Should().BeEquivalentTo(["a", "b", "c"]);
        harness.Bus.Context.History.UndoEntries().Count.Should().Be(historyBefore + 1);

        harness.Node("a").Text!.FontSize.Should().Be(20);
        harness.Node("b").Text!.FontSize.Should().Be(20);
        harness.Node("c").Text!.FontSize.Should().Be(20);

        harness.Bus.Undo().IsSuccess.Should().BeTrue();
        harness.Node("a").Text.Should().BeNull();
        harness.Node("b").Text.Should().BeNull();
        harness.Node("c").Text.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "TextPreset")]
    public void Duplicate_ids_in_a_batch_are_counted_once()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.DefinePreset("tp1", "标题", new TextStyle { FontSize = 20 });

        var result = harness.ApplyPreset("tp1", ["a", "a"]);

        result.IsEffectiveSuccess.Should().BeTrue();
        result.AffectedIds.Should().ContainSingle(id => id == "a");
    }

    [Fact]
    [Trait("Category", "TextPreset")]
    public void Applying_a_preset_that_declares_nothing_the_node_has_is_a_no_op()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        // 预设是空样式，节点也没设任何文本样式：没有东西可叠，按约定报 NoOp。
        harness.DefinePreset("tp1", "空样式");

        var versionBefore = harness.Document.Version;

        var result = harness.ApplyPreset("tp1", ["a"]);

        result.IsNoOp.Should().BeTrue();
        harness.Document.Version.Should().Be(versionBefore);
    }

    [Fact]
    [Trait("Category", "TextPreset")]
    public void Applying_a_missing_preset_is_rejected()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var result = harness.ApplyPreset("ghost", ["a"]);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.TextPresetMissing);
        harness.Node("a").Text.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "TextPreset")]
    public void Applying_to_a_missing_node_fails_without_touching_the_others()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.DefinePreset("tp1", "标题", new TextStyle { FontSize = 20 });

        var before = harness.Snapshot();

        // 原子性靠两遍走：有一个节点不在，整条失败，一个都不写。
        var result = harness.ApplyPreset("tp1", ["a", "ghost"]);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.NodeMissing)
            .Which.Payload.Should().Contain("ghost");
        harness.Node("a").Text.Should().BeNull();
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "TextPreset")]
    public void An_empty_batch_is_rejected()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.DefinePreset("tp1", "标题", new TextStyle { FontSize = 20 });

        var result = harness.ApplyPreset("tp1", []);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.InvalidId);
    }

    [Fact]
    [Trait("Category", "TextPreset")]
    public void Undoing_an_application_restores_every_node_and_keeps_the_preset()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.SetField("a", FieldNames.TextFontSize, "14").IsEffectiveSuccess.Should().BeTrue();
        harness.DefinePreset("tp1", "标题", new TextStyle { FontSize = 20, FontWeight = FontWeight.Bold });

        var visualBefore = harness.Document.VisualHash;

        harness.ApplyPreset("tp1", ["a", "b"]);

        harness.Node("a").Text!.FontSize.Should().Be(20);
        harness.Node("a").Text!.FontWeight.Should().Be(FontWeight.Bold);

        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Node("a").Text!.FontSize.Should().Be(14, "节点自己原来的字号要回来");
        harness.Node("a").Text!.FontWeight.Should().BeNull();
        harness.Node("b").Text.Should().BeNull();
        harness.Document.TextPresets.Should().ContainSingle(p => p.Id == "tp1", "撤销应用不动预设本身");
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    #endregion

    #region Memento 往返

    [Fact]
    [Trait("Category", "MementoRegistration")]
    public void A_preset_memento_survives_a_serialization_round_trip()
    {
        using var harness = new Harness();
        harness.DefinePreset("tp1", "标题", new TextStyle { FontSize = 20, FontFamily = "Arial" });
        harness.DefinePreset("tp2", "正文", new TextStyle { FontSize = 12 });

        var memento = new TextPresetMemento { PreviousPresets = [.. harness.Document.TextPresets] };

        var restored = (TextPresetMemento)DiagramSerializer.DeserializeMemento(
            DiagramSerializer.SerializeMemento(memento));

        restored.PreviousPresets.Should().HaveCount(2);
        restored.PreviousPresets.Single(p => p.Id == "tp1").Style.FontSize.Should().Be(20);
        restored.PreviousPresets.Single(p => p.Id == "tp1").Style.FontFamily.Should().Be("Arial");
    }

    [Fact]
    [Trait("Category", "MementoRegistration")]
    public void An_apply_memento_survives_a_serialization_round_trip()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.SetField("a", FieldNames.TextFontSize, "14").IsEffectiveSuccess.Should().BeTrue();

        var memento = new TextPresetApplyMemento
        {
            Previous = [new PresetTextPlacement("a", harness.Node("a").Text)],
        };

        var restored = (TextPresetApplyMemento)DiagramSerializer.DeserializeMemento(
            DiagramSerializer.SerializeMemento(memento));

        restored.Previous.Should().ContainSingle(p => p.NodeId == "a");
        restored.Previous[0].Text!.FontSize.Should().Be(14);
    }

    #endregion
}
