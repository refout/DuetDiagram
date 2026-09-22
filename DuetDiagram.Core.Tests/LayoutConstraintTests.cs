using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 增删布局约束这两条命令。
/// </summary>
/// <remarks>
/// <para>
/// 分两段看：一段是"失败整条回滚、撤销逐字节还原"，与其它命令同一口径；
/// 另一段是这两条命令自己的语义——归属跟着约束走、同层按集合算、
/// 同一个主语上只留一条次序。
/// </para>
/// <para>
/// 次序那一条尤其要盯：它是"每个主语一条"而不是"每加一次一条"。
/// 追加的话，用户拖几次就会在同一对节点上积下几十条互相矛盾的次序，
/// 而求解器按列表顺序取第一条——生效的是最早那次，后面拖的全白做。
/// </para>
/// </remarks>
public sealed class LayoutConstraintTests
{
    #region 原子性

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Adding_a_constraint_over_a_missing_node_is_rolled_back()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var before = harness.Snapshot();
        var versionBefore = harness.Document.Version;

        var result = harness.AddConstraint(LayoutConstraintSpec.SameRank(["a", "ghost"]));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.LayoutNodeMissing);
        harness.Snapshot().Should().Be(before);
        harness.Document.Version.Should().Be(versionBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Adding_an_order_constraint_over_a_foreign_edge_is_rolled_back()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AddNode("c");
        harness.AddNode("d");
        harness.Connect("e1", "b", "c");
        harness.Connect("e2", "b", "d");

        var before = harness.Snapshot();

        // 两条都不是 a 的出边。层内次序讲的是"某个节点的几条出边谁先谁后"，
        // 拿别人的边进来，求解器会在主语节点里找不到成员，然后一声不吭地跳过。
        var result = harness.AddConstraint(LayoutConstraintSpec.Order("a", ["e1", "e2"]));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.LayoutOrderEdgeMissing);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_order_constraint_needs_at_least_two_edges()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.Connect("e1", "a", "b");

        var result = harness.AddConstraint(LayoutConstraintSpec.Order("a", ["e1"]));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.LayoutConstraintInvalid);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_order_constraint_needs_a_subject()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AddNode("c");
        harness.Connect("e1", "a", "b");
        harness.Connect("e2", "a", "c");

        var result = harness.AddConstraint(LayoutConstraintSpec.Order(string.Empty, ["e1", "e2"]));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.LayoutConstraintInvalid);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_group_constraint_needs_at_least_two_members()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var before = harness.Snapshot();

        var result = harness.AddConstraint(LayoutConstraintSpec.SameRank(["a"]));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.LayoutConstraintInvalid);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_group_constraint_must_not_carry_a_subject()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");

        // 同层与对齐是一组平级的节点，没有主语。带着主语进来，说明调用方把
        // 次序那一类的形状套到这一类上了，而按主语去处理会得到一条谁也不认识的约束。
        var result = harness.AddConstraint(
            new LayoutConstraintSpec(LayoutConstraintKind.SameRank, "a", ["a", "b"]));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.LayoutConstraintInvalid);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Repeated_members_are_rejected()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");

        var result = harness.AddConstraint(LayoutConstraintSpec.Align(["a", "b", "a"]));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.LayoutConstraintInvalid);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Removing_a_constraint_that_is_not_there_is_rejected()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");

        var before = harness.Snapshot();

        var result = harness.RemoveConstraint(LayoutConstraintSpec.SameRank(["a", "b"]));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.LayoutConstraintMissing);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Adding_the_same_constraint_twice_is_a_no_op()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AddConstraint(LayoutConstraintSpec.SameRank(["a", "b"]));

        var versionBefore = harness.Document.Version;
        var undoBefore = harness.Context.History.UndoCount;

        var result = harness.AddConstraint(LayoutConstraintSpec.SameRank(["b", "a"]));

        result.IsNoOp.Should().BeTrue("成员一样就是同一条约束，写的先后不算差别");
        harness.Document.Version.Should().Be(versionBefore);
        harness.Context.History.UndoCount.Should().Be(undoBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Adding_a_constraint_then_undoing_restores_the_previous_hints()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");

        var original = harness.Document.Layout;

        harness.AddConstraint(LayoutConstraintSpec.SameRank(["a", "b"]))
            .IsSuccess.Should().BeTrue();

        harness.Document.Layout.SameRank.Should().ContainSingle();

        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        // 整份布局提示换回去，四个列表一个都不能少还原——少还原一个只会体现在哈希上。
        harness.Document.Layout.Should().Be(original);

        harness.Bus.Redo().IsSuccess.Should().BeTrue();
        harness.Document.Layout.SameRank.Should().ContainSingle();
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Removing_a_constraint_then_undoing_puts_it_back()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.Connect("e1", "a", "b");
        harness.Connect("e2", "a", "b");

        var spec = LayoutConstraintSpec.Order("a", ["e2", "e1"]);
        harness.AddConstraint(spec).IsSuccess.Should().BeTrue();

        var withConstraint = harness.Document.Layout;

        harness.RemoveConstraint(spec).IsSuccess.Should().BeTrue();
        harness.Document.Layout.Order.Should().BeEmpty();

        harness.Bus.Undo().IsSuccess.Should().BeTrue();
        harness.Document.Layout.Should().Be(withConstraint);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Adding_a_constraint_changes_the_structural_hash()
    {
        // 哈希要回答的是"要不要重新求解布局"。约束改了而哈希不变的话，
        // 宿主会认为这份文档与上一份一样，于是不重排——用户看到的是"我设了但没生效"。
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");

        var before = harness.Document.StructuralHash;

        harness.AddConstraint(LayoutConstraintSpec.SameRank(["a", "b"]));

        harness.Document.StructuralHash.Should().NotBe(before);
    }

    #endregion

    #region 约束语义

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void An_order_constraint_for_the_same_subject_replaces_the_previous_one()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AddNode("c");
        harness.Connect("e1", "a", "b");
        harness.Connect("e2", "a", "c");

        harness.AddConstraint(LayoutConstraintSpec.Order("a", ["e1", "e2"])).IsSuccess.Should().BeTrue();
        harness.AddConstraint(LayoutConstraintSpec.Order("a", ["e2", "e1"])).IsSuccess.Should().BeTrue();

        var order = harness.Document.Layout.Order.Should().ContainSingle().Which;

        order.Value.Order.Should().Equal(["e2", "e1"], "后加的那一次要顶掉前一次，而不是再积一条");
    }

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void Replacing_an_order_constraint_can_be_undone()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AddNode("c");
        harness.Connect("e1", "a", "b");
        harness.Connect("e2", "a", "c");

        harness.AddConstraint(LayoutConstraintSpec.Order("a", ["e1", "e2"]));
        harness.AddConstraint(LayoutConstraintSpec.Order("a", ["e2", "e1"]));

        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        // 合并走的是"换掉"这条路，撤销要能换回原来那条次序。
        // 只把新加的那条摘掉的话，摘完两条都没了。
        harness.Document.Layout.Order.Should().ContainSingle()
            .Which.Value.Order.Should().Equal("e1", "e2");
    }

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void The_owner_travels_with_the_constraint()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");

        harness.AddConstraint(LayoutConstraintSpec.SameRank(["a", "b"]), ConstraintOwner.Llm)
            .IsSuccess.Should().BeTrue();

        // 归属是降级矩阵的输入：解不出来时先丢模型提的、保住人工定的。
        // 写错归属的表现是"降级时把我设的约束丢了"，而这一点在画面上完全看不出来。
        harness.Document.Layout.SameRank.Should().ContainSingle()
            .Which.Owner.Should().Be(ConstraintOwner.Llm);
    }

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void The_same_members_under_different_owners_are_two_constraints()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");

        harness.AddConstraint(LayoutConstraintSpec.SameRank(["a", "b"]), ConstraintOwner.Llm);
        harness.AddConstraint(LayoutConstraintSpec.SameRank(["a", "b"]), ConstraintOwner.Human)
            .IsSuccess.Should().BeTrue();

        harness.Document.Layout.SameRank.Should().HaveCount(2, "归属不同就是两条，降级时各丢各的");
    }

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void Removing_a_constraint_of_another_owner_does_nothing()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AddConstraint(LayoutConstraintSpec.SameRank(["a", "b"]), ConstraintOwner.Llm);

        var result = harness.RemoveConstraint(LayoutConstraintSpec.SameRank(["a", "b"]), ConstraintOwner.Human);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.LayoutConstraintMissing);
        harness.Document.Layout.SameRank.Should().ContainSingle();
    }

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void Two_different_groups_coexist()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AddNode("c");
        harness.AddNode("d");

        harness.AddConstraint(LayoutConstraintSpec.SameRank(["a", "b"])).IsSuccess.Should().BeTrue();
        harness.AddConstraint(LayoutConstraintSpec.SameRank(["c", "d"])).IsSuccess.Should().BeTrue();

        harness.Document.Layout.SameRank.Should().HaveCount(2);
    }

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void A_constraint_survives_a_serialization_round_trip()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AddNode("c");
        harness.Connect("e1", "a", "b");
        harness.Connect("e2", "a", "c");

        harness.AddConstraint(LayoutConstraintSpec.SameRank(["b", "c"]));
        harness.AddConstraint(LayoutConstraintSpec.Align(["b", "c"]));
        harness.AddConstraint(LayoutConstraintSpec.Order("a", ["e2", "e1"]));

        var json = DiagramSerializer.SerializeFull(harness.Document);
        var restored = DiagramSerializer.DeserializeFull(json);

        restored.Layout.Should().Be(harness.Document.Layout);
        restored.StructuralHash.Should().Be(harness.Document.StructuralHash);
    }

    #endregion

    #region 命令契约

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void The_specs_field_name_is_stable_and_machine_readable()
    {
        // 变更明细里的字段名是审计与增量同步的稳定标识，不是给人读的句子。
        LayoutConstraintSpec.SameRank(["a", "b"]).Field.Should().Be("layout.same-rank");
        LayoutConstraintSpec.Align(["a", "b"]).Field.Should().Be("layout.align");
        LayoutConstraintSpec.Order("a", ["e1", "e2"]).Field.Should().Be("layout.order");
    }

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void The_two_commands_report_their_identifiers()
    {
        new AddLayoutConstraintCommand(LayoutConstraintSpec.SameRank(["a", "b"]), ConstraintOwner.Human)
            .CommandId.Should().Be("add-layout-constraint");

        new RemoveLayoutConstraintCommand(LayoutConstraintSpec.SameRank(["a", "b"]), ConstraintOwner.Human)
            .CommandId.Should().Be("remove-layout-constraint");
    }

    [Fact]
    [Trait("Category", "LayoutConstraint")]
    public void Adding_a_constraint_is_a_structural_change()
    {
        // 报成纯外观的话，宿主会只重绘不重排，而画面上的坐标根本没跟着约束变。
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");

        var result = harness.AddConstraint(LayoutConstraintSpec.SameRank(["a", "b"]));

        result.StructuralChanged.Should().BeTrue();
        result.VisualChanged.Should().BeTrue();
    }

    #endregion
}
