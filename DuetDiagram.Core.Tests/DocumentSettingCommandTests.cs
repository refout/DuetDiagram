using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 文档级设置的两条命令：改图类型与改画布设置。
/// </summary>
/// <remarks>
/// <para>
/// 两条命令改的都是**文档自己**的东西，不是某个元素的字段，所以都进不了元素字段表。
/// 但它们的哈希口径不同：图类型决定分层的语义，进结构哈希；画布设置（网格、背景、
/// 纸张尺寸）不改变任何坐标，只进视觉哈希。这一组的核心就是这两条不同的口径。
/// </para>
/// <para>
/// 画布设置的六个成员各自发一条变更明细。粒度由明细决定而不由参数个数决定——
/// 一次调用改了三项就该有三条，否则"一边调网格、一边调背景色"会被判成冲突。
/// </para>
/// </remarks>
public sealed class DocumentSettingCommandTests
{
    #region 图类型

    [Fact]
    [Trait("Category", "IrHashing")]
    public void Changing_the_kind_moves_the_structural_hash()
    {
        using var harness = new Harness();

        var structuralBefore = harness.Document.StructuralHash;
        var visualBefore = harness.Document.VisualHash;

        var result = harness.SetKind(DiagramKind.Block);

        harness.Document.Kind.Should().Be(DiagramKind.Block);

        // 图类型决定分层的语义与推进方向，已算出的坐标不一定还有效。
        result.StructuralChanged.Should().BeTrue();
        result.VisualChanged.Should().BeTrue();
        harness.Document.StructuralHash.Should().NotBe(structuralBefore);
        harness.Document.VisualHash.Should().NotBe(visualBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Setting_the_same_kind_is_a_no_op()
    {
        using var harness = new Harness();

        var versionBefore = harness.Document.Version;

        harness.SetKind(DiagramKind.Flowchart).IsNoOp.Should().BeTrue();

        harness.Document.Version.Should().Be(versionBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_undefined_kind_is_rejected()
    {
        using var harness = new Harness();

        var before = harness.Snapshot();

        // 枚举值可能来自反序列化或外部协议。落在定义之外时不能悄悄当成某个类型。
        var result = harness.SetKind((DiagramKind)99);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.FieldValueInvalid);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void The_kind_change_belongs_to_the_document()
    {
        using var harness = new Harness();

        var change = harness.SetKind(DiagramKind.State).FieldChanges.Should().ContainSingle().Which;

        // 这一次改动没有落在任何元素上。硬指一个节点的话，界面会把高亮打在无关的节点上。
        change.ElementId.Should().Be(harness.Document.Id);
        change.Field.Should().Be(FieldNames.Kind);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undoing_a_kind_change_puts_the_content_back()
    {
        using var harness = new Harness();

        // 先造一点内容。空文档的两个哈希都是空串，拿它当基线的话这条断言是空转的。
        harness.AddNode("a").IsEffectiveSuccess.Should().BeTrue();

        var structuralBefore = harness.Document.StructuralHash;

        harness.SetKind(DiagramKind.State);
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Document.Kind.Should().Be(DiagramKind.Flowchart);
        harness.Document.StructuralHash.Should().Be(structuralBefore);
    }

    #endregion

    #region 画布设置

    [Fact]
    [Trait("Category", "IrHashing")]
    public void Changing_a_canvas_member_is_a_visual_change_only()
    {
        using var harness = new Harness();

        var structuralBefore = harness.Document.StructuralHash;

        var result = harness.SetCanvas(grid: GridStyle.Dots, gridSize: 32);

        // 网格与背景不改变任何坐标，所以结构哈希不动，视觉哈希动。
        result.StructuralChanged.Should().BeFalse();
        result.VisualChanged.Should().BeTrue();
        harness.Document.StructuralHash.Should().Be(structuralBefore);
        harness.Document.Canvas.Grid.Should().Be(GridStyle.Dots);
        harness.Document.Canvas.GridSize.Should().Be(32);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Each_changed_member_gets_its_own_change_entry()
    {
        using var harness = new Harness();

        var changes = harness.SetCanvas(
            grid: GridStyle.Lines,
            orientation: CanvasOrientation.Portrait).FieldChanges;

        // 冲突判定按"哪个元素的哪个字段"算，所以粒度由变更明细决定、不由参数个数决定。
        // 一起发的话，一边调网格、一边调纸张方向会被判成冲突。
        changes.Select(c => c.Field).Should().Equal(
            FieldNames.CanvasGrid,
            FieldNames.CanvasOrientation);

        changes.Should().OnlyContain(c => c.ElementId == harness.Document.Id);
        changes.Should().OnlyContain(c => FieldRegistry.IsKnown(c.Field));
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_member_given_but_unchanged_does_not_produce_an_entry()
    {
        using var harness = new Harness();

        // 网格本来就是 None。传了值但值没变的那一项不算改过。
        var changes = harness.SetCanvas(grid: GridStyle.None, gridSize: 40).FieldChanges;

        changes.Select(c => c.Field).Should().Equal(FieldNames.CanvasGridSize);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Setting_nothing_is_rejected()
    {
        using var harness = new Harness();

        var before = harness.Snapshot();

        // 一项都不给就是一次什么都不做的调用。报成成功会让调用方以为自己改了东西。
        var result = harness.SetCanvas();

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.FieldValueInvalid);
        harness.Snapshot().Should().Be(before);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [Trait("Category", "Atomicity")]
    public void An_invalid_grid_size_is_rejected(double gridSize)
    {
        using var harness = new Harness();

        var before = harness.Snapshot();

        var result = harness.SetCanvas(gridSize: gridSize);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.FieldValueInvalid);
        harness.Snapshot().Should().Be(before);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(double.NaN, 100)]
    [Trait("Category", "Atomicity")]
    public void An_invalid_page_size_is_rejected(double width, double height)
    {
        using var harness = new Harness();

        var before = harness.Snapshot();

        var result = harness.SetCanvas(pageSize: new Size(width, height));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.FieldValueInvalid);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_undefined_grid_style_is_rejected()
    {
        using var harness = new Harness();

        var result = harness.SetCanvas(grid: (GridStyle)42);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.FieldValueInvalid);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_undefined_orientation_is_rejected()
    {
        using var harness = new Harness();

        var result = harness.SetCanvas(orientation: (CanvasOrientation)42);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.FieldValueInvalid);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_empty_background_clears_it_and_a_null_one_leaves_it_alone()
    {
        using var harness = new Harness();

        harness.SetCanvas(background: "#fafafa").IsEffectiveSuccess.Should().BeTrue();
        harness.Document.Canvas.Background.Should().Be("#fafafa");

        // 空引用表示"这一项不动"。改网格不该顺手把背景色抹掉。
        harness.SetCanvas(grid: GridStyle.Lines).IsEffectiveSuccess.Should().BeTrue();
        harness.Document.Canvas.Background.Should().Be("#fafafa");

        // 空串表示清除。空引用已经占了"不动"这个意思，清除只能另给一个信号。
        harness.SetCanvas(background: "").IsEffectiveSuccess.Should().BeTrue();
        harness.Document.Canvas.Background.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Setting_everything_to_the_same_values_is_a_no_op()
    {
        using var harness = new Harness();

        var versionBefore = harness.Document.Version;

        // 面板上的应用按钮点两次很常见。每次都推进版本、都广播一遍，是白付的代价。
        harness.SetCanvas(
            grid: GridStyle.None,
            gridSize: 20,
            pageSize: new Size(1123, 794),
            orientation: CanvasOrientation.Landscape,
            infinite: true).IsNoOp.Should().BeTrue();

        harness.Document.Version.Should().Be(versionBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undoing_a_canvas_change_puts_the_content_back()
    {
        using var harness = new Harness();

        // 先造一点内容。空文档的视觉哈希是空串，拿它当基线的话这条断言是空转的。
        harness.AddNode("a").IsEffectiveSuccess.Should().BeTrue();

        var visualBefore = harness.Document.VisualHash;

        harness.SetCanvas(
            grid: GridStyle.Dots,
            gridSize: 8,
            background: "#000000",
            infinite: false);
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        // 一次调用改到四项，只还原其中一项是不够的。
        harness.Document.Canvas.Should().Be(new CanvasSettings());
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    #endregion

    #region Memento 往返

    [Fact]
    [Trait("Category", "MementoRegistration")]
    public void The_document_setting_mementos_survive_a_serialization_round_trip()
    {
        using var harness = new Harness();
        harness.SetCanvas(grid: GridStyle.Lines, background: "#eeeeee").IsEffectiveSuccess.Should().BeTrue();

        var kind = (SetKindMemento)RoundTrip(new SetKindMemento { Previous = DiagramKind.State });
        var canvas = (CanvasMemento)RoundTrip(new CanvasMemento { Previous = harness.Document.Canvas });

        kind.Previous.Should().Be(DiagramKind.State);
        canvas.Previous.Should().Be(harness.Document.Canvas);
        canvas.Previous.Background.Should().Be("#eeeeee");
    }

    private static CommandMemento RoundTrip(CommandMemento memento) =>
        DiagramSerializer.DeserializeMemento(DiagramSerializer.SerializeMemento(memento));

    #endregion
}
