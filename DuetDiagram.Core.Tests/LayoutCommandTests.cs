using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 改布局的三条命令：主方向、两个间距、相对位置约束。
/// </summary>
/// <remarks>
/// <para>
/// 这三条改的都是**文档自己的属性**，不是元素字段——方向与间距落在文档上，
/// 相对位置落在布局提示上。所以它们各自单立一条命令，而校验、原子性与撤销的
/// 写法与元素字段那一组同构。
/// </para>
/// <para>
/// 与"改没改"有关的断言一律用完整序列化结果比较。只看某一个字段的话，
/// 一次改动顺手带坏了别的字段不会被发现。
/// </para>
/// </remarks>
public sealed class LayoutCommandTests
{
    #region 主方向

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void Changing_the_direction_moves_the_structural_hash()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var hashBefore = harness.Document.StructuralHash;

        var result = harness.SetDirection(Direction.RL);

        // 方向决定层往哪边推进，是布局的输入。报成纯外观的话宿主只重绘不重排，
        // 而画面上的层还是按旧方向排的——所以这条断言盯的是结构那一位。
        result.IsEffectiveSuccess.Should().BeTrue();
        result.StructuralChanged.Should().BeTrue();
        result.VisualChanged.Should().BeTrue();

        harness.Document.Direction.Should().Be(Direction.RL);
        harness.Document.StructuralHash.Should().NotBe(hashBefore);
    }

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void Setting_the_same_direction_is_a_no_op()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var versionBefore = harness.Document.Version;
        var undoBefore = harness.Context.History.UndoCount;

        var result = harness.SetDirection(harness.Document.Direction);

        // 批量下发时重复给同一个方向很常见。每次都推进版本、每次重排一遍布局，
        // 是白付的代价——所以它必须落在 NoOp 那一档而不是"成功且改了"。
        result.IsSuccess.Should().BeTrue();
        result.IsNoOp.Should().BeTrue();
        result.IsEffectiveSuccess.Should().BeFalse();

        harness.Document.Version.Should().Be(versionBefore);
        harness.Context.History.UndoCount.Should().Be(undoBefore);
    }

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void Undoing_a_direction_change_puts_the_content_back()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        // 撤销会让版本往前走，所以整份序列化结果比不了——版本号是它的一部分。
        // 比内容用两个哈希，它们覆盖方向、间距、四类约束与调色板，且不含版本。
        var structuralBefore = harness.Document.StructuralHash;
        var visualBefore = harness.Document.VisualHash;

        harness.SetDirection(Direction.BT);
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Document.Direction.Should().Be(Direction.LR);
        harness.Document.StructuralHash.Should().Be(structuralBefore);
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_undefined_direction_is_rejected_and_leaves_the_document_alone()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var before = harness.Snapshot();

        // 枚举值可能来自反序列化或外部协议。落在定义之外时不能悄悄当成某个方向——
        // 那会让一次参数错误表现成"方向改了但改错了"。
        var result = harness.SetDirection((Direction)99);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.FieldValueInvalid);

        harness.Snapshot().Should().Be(before);
    }

    #endregion

    #region 间距

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void Only_the_spacing_that_actually_changed_produces_a_change_entry()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var previous = harness.Document.Layout;

        // 层间距与缺省值相同：它没被改过，不该出现在变更明细里。
        // 冲突判定是按"哪个元素的哪个字段"算的，多一条明细就多一次假冲突。
        var result = harness.SetSpacing(nodeSpacing: previous.NodeSpacing + 12);

        result.IsEffectiveSuccess.Should().BeTrue();
        result.FieldChanges.Should().ContainSingle();
        result.FieldChanges[0].Field.Should().Be(FieldNames.NodeSpacing);
        result.FieldChanges[0].ElementId.Should().Be(harness.Document.Id);

        harness.Document.Layout.NodeSpacing.Should().Be(previous.NodeSpacing + 12);
        harness.Document.Layout.LayerSpacing.Should().Be(previous.LayerSpacing, "没给的那一项不动");
    }

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void Giving_both_spacings_produces_two_change_entries()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var previous = harness.Document.Layout;

        var result = harness.SetSpacing(
            nodeSpacing: previous.NodeSpacing + 5,
            layerSpacing: previous.LayerSpacing + 7);

        // 粒度由变更明细决定、不由命令的参数个数决定。两个字段都真变了就发两条，
        // 这样"一边调层间距、一边调同层间距"才不会因为挤在一条明细里被判成冲突。
        result.FieldChanges.Should().HaveCount(2);
        result.FieldChanges.Select(c => c.Field).Should().BeEquivalentTo(
            [FieldNames.NodeSpacing, FieldNames.LayerSpacing]);

        harness.Document.Layout.NodeSpacing.Should().Be(previous.NodeSpacing + 5);
        harness.Document.Layout.LayerSpacing.Should().Be(previous.LayerSpacing + 7);
    }

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void Passing_a_spacing_equal_to_the_current_one_is_a_no_op()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var versionBefore = harness.Document.Version;

        var result = harness.SetSpacing(nodeSpacing: harness.Document.Layout.NodeSpacing);

        result.IsNoOp.Should().BeTrue();
        harness.Document.Version.Should().Be(versionBefore);
    }

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void Undoing_a_spacing_change_puts_the_content_back()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AddConstraint(LayoutConstraintSpec.SameRank(["a", "b"])).IsEffectiveSuccess.Should().BeTrue();

        var structuralBefore = harness.Document.StructuralHash;
        var visualBefore = harness.Document.VisualHash;

        harness.SetSpacing(nodeSpacing: 55, layerSpacing: 88);
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        // 记的是整份布局提示，所以还原时那条同层约束也一并回到原样。
        // 只记那两个数的话，这里会看出来约束少了一条。
        harness.Document.Layout.NodeSpacing.Should().Be(LayoutHintsDefaults.NodeSpacing);
        harness.Document.Layout.SameRank.Should().ContainSingle();
        harness.Document.StructuralHash.Should().Be(structuralBefore);
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    [Theory]
    [InlineData(null, null, "两个都不给")]
    [InlineData(0d, null, "零")]
    [InlineData(-1d, null, "负数")]
    [InlineData(double.NaN, null, "非数")]
    [InlineData(double.PositiveInfinity, null, "无穷")]
    [InlineData(null, 0d, "层间距为零")]
    [Trait("Category", "Atomicity")]
    public void An_unusable_spacing_is_rejected_and_leaves_the_document_alone(
        double? nodeSpacing,
        double? layerSpacing,
        string because)
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var before = harness.Snapshot();

        var result = harness.SetSpacing(nodeSpacing, layerSpacing);

        result.IsSuccess.Should().BeFalse($"{because}都不是可用的间距");
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.FieldValueInvalid);
        harness.Snapshot().Should().Be(before);
    }

    #endregion

    #region 相对位置

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void A_place_constraint_lands_in_the_layout_hints_and_moves_the_structural_hash()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");

        var hashBefore = harness.Document.StructuralHash;

        var result = harness.SetPlace("a", "b", PlaceRelation.RightOf);

        result.IsEffectiveSuccess.Should().BeTrue();
        result.StructuralChanged.Should().BeTrue();

        var place = harness.Document.Layout.Place.Should().ContainSingle().Subject;
        place.Value.NodeId.Should().Be("a");
        place.Value.RelativeTo.Should().Be("b");
        place.Value.Relation.Should().Be(PlaceRelation.RightOf);
        place.Owner.Should().Be(ConstraintOwner.Human);

        harness.Document.StructuralHash.Should().NotBe(hashBefore);
    }

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void Setting_the_same_pair_again_replaces_it_instead_of_appending()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");

        harness.SetPlace("a", "b", PlaceRelation.RightOf);
        harness.SetPlace("a", "b", PlaceRelation.Above);

        // 追加几次就积下几条互相矛盾的相对位置，而求解器取的是列表里的第一条，
        // 于是生效的会是最早那一次——用户看到的是"我改了但它没动"。
        var place = harness.Document.Layout.Place.Should().ContainSingle().Subject;
        place.Value.Relation.Should().Be(PlaceRelation.Above);
    }

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void The_same_pair_under_another_owner_is_a_separate_constraint()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");

        harness.SetPlace("a", "b", PlaceRelation.RightOf, ConstraintOwner.Human);
        harness.SetPlace("a", "b", PlaceRelation.Above, ConstraintOwner.Llm);

        // 归属方是降级矩阵的输入：解不出来时先丢谁由它决定。混成一条的话，
        // 模型提的那一条会把人工定的那一条覆盖掉，而用户什么都没做。
        harness.Document.Layout.Place.Should().HaveCount(2);
        harness.Document.Layout.GetPlace(ConstraintOwner.Human)
            .Should().ContainSingle().Which.Relation.Should().Be(PlaceRelation.RightOf);
        harness.Document.Layout.GetPlace(ConstraintOwner.Llm)
            .Should().ContainSingle().Which.Relation.Should().Be(PlaceRelation.Above);
    }

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void Clearing_a_place_constraint_removes_it()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");

        harness.SetPlace("a", "b", PlaceRelation.RightOf);
        var result = harness.SetPlace("a", "b", relation: null);

        result.IsEffectiveSuccess.Should().BeTrue();
        harness.Document.Layout.Place.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void Clearing_a_place_constraint_that_was_never_set_is_a_no_op()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");

        var versionBefore = harness.Document.Version;

        harness.SetPlace("a", "b", relation: null).IsNoOp.Should().BeTrue();

        harness.Document.Version.Should().Be(versionBefore);
    }

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void Adding_a_place_constraint_keeps_the_constraints_that_were_already_there()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AddNode("c");

        harness.AddConstraint(LayoutConstraintSpec.SameRank(["a", "b"])).IsEffectiveSuccess.Should().BeTrue();
        harness.AddConstraint(LayoutConstraintSpec.Align(["b", "c"])).IsEffectiveSuccess.Should().BeTrue();

        harness.SetPlace("a", "b", PlaceRelation.RightOf);

        // 写入时必须合并。整份换掉的话，加一条相对位置会把之前加的同层约束
        // 悄悄删掉，而用户看到的只是"我加了个相对位置"。
        harness.Document.Layout.SameRank.Should().ContainSingle();
        harness.Document.Layout.Align.Should().ContainSingle();
        harness.Document.Layout.Place.Should().ContainSingle();
    }

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void Undoing_a_place_constraint_puts_the_content_back()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AddConstraint(LayoutConstraintSpec.SameRank(["a", "b"])).IsEffectiveSuccess.Should().BeTrue();

        var structuralBefore = harness.Document.StructuralHash;
        var visualBefore = harness.Document.VisualHash;

        harness.SetPlace("a", "b", PlaceRelation.LeftOf);
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Document.Layout.Place.Should().BeEmpty();
        harness.Document.StructuralHash.Should().Be(structuralBefore);
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_node_cannot_be_placed_relative_to_itself()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var before = harness.Snapshot();

        // "自己摆在自己的右边"描述不出任何位置，而且它会让求解器陷入自指。
        var result = harness.SetPlace("a", "a", PlaceRelation.RightOf);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.LayoutConstraintInvalid);
        harness.Snapshot().Should().Be(before);
    }

    [Theory]
    [InlineData("a", "ghost", "参照节点不在")]
    [InlineData("ghost", "a", "主语节点不在")]
    [Trait("Category", "Atomicity")]
    public void A_place_constraint_naming_a_missing_node_is_rejected(
        string nodeId,
        string relativeTo,
        string because)
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var before = harness.Snapshot();

        var result = harness.SetPlace(nodeId, relativeTo, PlaceRelation.RightOf);

        result.IsSuccess.Should().BeFalse($"{because}，这一条约束落不下去");
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.LayoutNodeMissing);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_undefined_place_relation_is_rejected_and_leaves_the_document_alone()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");

        var before = harness.Snapshot();

        var result = harness.SetPlace("a", "b", (PlaceRelation)42);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.FieldValueInvalid);
        harness.Snapshot().Should().Be(before);
    }

    #endregion

    #region Memento 往返

    [Fact]
    [Trait("Category", "MementoRegistration")]
    public void A_place_memento_survives_a_serialization_round_trip()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.SetPlace("a", "b", PlaceRelation.Above);

        // 布局提示里四个约束列表形状各异，序列化时最容易出问题的是那个带归属方与
        // 创建时间的包装类型。往返一趟才能确认反序列化出来的那份还能被还原回去。
        var memento = new LayoutConstraintMemento { Previous = harness.Document.Layout };

        var restored = (LayoutConstraintMemento)DiagramSerializerRoundTrip(memento);

        restored.Previous.Should().Be(harness.Document.Layout);
        restored.Previous.Place.Should().ContainSingle()
            .Which.Value.Relation.Should().Be(PlaceRelation.Above);
    }

    [Fact]
    [Trait("Category", "MementoRegistration")]
    public void A_direction_memento_survives_a_serialization_round_trip()
    {
        var memento = new SetDirectionMemento { Previous = Direction.BT };

        var restored = (SetDirectionMemento)DiagramSerializerRoundTrip(memento);

        restored.Previous.Should().Be(Direction.BT);
    }

    /// <summary>把一个 memento 序列化再反序列化回来。</summary>
    private static CommandMemento DiagramSerializerRoundTrip(CommandMemento memento) =>
        DuetDiagram.Core.Serialization.DiagramSerializer.DeserializeMemento(
            DuetDiagram.Core.Serialization.DiagramSerializer.SerializeMemento(memento));

    #endregion
}
