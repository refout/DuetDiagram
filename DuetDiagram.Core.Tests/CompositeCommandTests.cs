using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 组合的三条命令：新建、解散、把成员搬进另一个组合。
/// </summary>
/// <remarks>
/// <para>
/// 这一组命令的难点不在增删，而在**成员关系有两处表达**：容器的成员列表，与成员自己的
/// 父级字段。两者必须一致，而以成员列表为准。所以每条用例除了看命令自己的结果，
/// 还要跑一遍整体校验器——不一致的文档能存下去、界面上看不出异常，
/// 只有校验器会报出来。
/// </para>
/// <para>
/// 另一类要盯住的是**成环与深度**：这两件事在写入时看不出来，单独看每一步都是合法的
/// 父子关系，只有连起来才出问题，而症状（布局无限递归、栈溢出）离肇事的那条命令很远。
/// </para>
/// </remarks>
public sealed class CompositeCommandTests
{
    #region 新建

    [Fact]
    [Trait("Category", "Composite")]
    public void Creating_a_group_records_the_membership_in_both_places()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");

        var result = harness.CreateGroup("g1", ["a", "b"], label: "第一组");

        result.IsEffectiveSuccess.Should().BeTrue();
        result.StructuralChanged.Should().BeTrue("归属关系改变会让已算出的坐标失效");

        var group = harness.Document.Composites.Should().ContainSingle().Subject;
        group.Id.Should().Be("g1");
        group.Members.Should().Equal("a", "b");

        // 两处表达都要写。只写成员列表的话，节点自己的父级还是空的，
        // 而校验器会报"成员列表与父级互相矛盾"。
        harness.Node("a").Parent.Should().Be("g1");
        harness.Node("b").Parent.Should().Be("g1");

        DiagramValidator.Validate(harness.Document).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Composite")]
    public void Members_are_taken_out_of_their_previous_container()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.CreateGroup("g1", ["a", "b"]).IsEffectiveSuccess.Should().BeTrue();

        harness.CreateGroup("g2", ["a"]).IsEffectiveSuccess.Should().BeTrue();

        // 一个节点只能属于一个组合。只往新组合里加而不从旧的里摘，
        // 文档就成了一份自相矛盾的东西，而两条命令各自看起来都成功了。
        harness.Composite("g1").Members.Should().Equal("b");
        harness.Composite("g2").Members.Should().Equal("a");
        harness.Node("a").Parent.Should().Be("g2");

        DiagramValidator.Validate(harness.Document).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Composite")]
    public void Nested_composites_are_allowed_up_to_the_depth_limit()
    {
        using var harness = new Harness();

        // 顶层是第 1 层，所以上限为 N 时能套 N 层。
        for (var depth = 1; depth <= CompositeLimits.MaxDepth; depth++)
        {
            var parent = depth == 1 ? null : $"g{depth - 1}";

            harness.CreateGroup($"g{depth}", [], parent: parent)
                .IsEffectiveSuccess.Should().BeTrue($"第 {depth} 层还在上限之内");
        }

        // 归属是两处表达：子组合记着外层是谁，外层也要把子组合列进成员列表。
        harness.Composite("g1").Members.Should().Equal("g2");
        DiagramValidator.Validate(harness.Document).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Going_past_the_depth_limit_is_rejected_and_leaves_the_document_alone()
    {
        using var harness = new Harness();

        for (var depth = 1; depth <= CompositeLimits.MaxDepth; depth++)
        {
            var parent = depth == 1 ? null : $"g{depth - 1}";
            harness.CreateGroup($"g{depth}", [], parent: parent).IsEffectiveSuccess.Should().BeTrue();
        }

        var before = harness.Snapshot();

        var result = harness.CreateGroup(
            "too-deep",
            [],
            parent: $"g{CompositeLimits.MaxDepth}");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.CompositeTooDeep);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_duplicate_id_is_rejected()
    {
        using var harness = new Harness();
        harness.CreateGroup("g1", []).IsEffectiveSuccess.Should().BeTrue();

        var before = harness.Snapshot();

        var result = harness.CreateGroup("g1", []);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.DuplicateId);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_id_that_collides_with_a_node_is_rejected()
    {
        using var harness = new Harness();
        harness.AddNode("x");

        var before = harness.Snapshot();

        // 九个集合共用一个命名空间。只查组合那一张表的话，这个组合会与那个节点同名，
        // 而所有按标识定位的操作从那一刻起就有了歧义。
        var result = harness.CreateGroup("x", []);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.DuplicateId);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_missing_member_is_rejected()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var before = harness.Snapshot();

        var result = harness.CreateGroup("g1", ["a", "查无此物"]);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.GroupMemberMissing);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_missing_parent_is_rejected()
    {
        using var harness = new Harness();

        var result = harness.CreateGroup("g1", [], parent: "查无此物");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.ParentMissing);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_group_cannot_list_itself_as_a_member()
    {
        using var harness = new Harness();

        // 最短的一个环。放进去之后"向上找容器"第一步就回到自己。
        var result = harness.CreateGroup("g1", ["g1"]);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.GroupCycle);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_group_cannot_be_created_inside_its_own_descendant()
    {
        using var harness = new Harness();
        harness.CreateGroup("g1", []).IsEffectiveSuccess.Should().BeTrue();
        harness.CreateGroup("g2", [], parent: "g1").IsEffectiveSuccess.Should().BeTrue();

        var before = harness.Snapshot();

        // g1 已经是 g2 的祖先，再把 g1 塞进 g3（g2 的子）就成环了。
        var result = harness.CreateGroup("g3", ["g1"], parent: "g2");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.GroupCycle);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Composite")]
    public void Undoing_a_creation_puts_the_content_back()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.CreateGroup("g0", ["a"]).IsEffectiveSuccess.Should().BeTrue();

        var structuralBefore = harness.Document.StructuralHash;
        var visualBefore = harness.Document.VisualHash;

        // 新组合把 a 从 g0 手里拿走了，所以撤销要把两处都还回去。
        harness.CreateGroup("g1", ["a", "b"]);
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Document.Composites.Select(c => c.Id).Should().Equal("g0");
        harness.Composite("g0").Members.Should().Equal("a");
        harness.Node("a").Parent.Should().Be("g0");
        harness.Node("b").Parent.Should().BeNull();

        harness.Document.StructuralHash.Should().Be(structuralBefore);
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    #endregion

    #region 解散

    [Fact]
    [Trait("Category", "Composite")]
    public void Dissolving_a_top_level_group_frees_its_members()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.CreateGroup("g1", ["a", "b"]).IsEffectiveSuccess.Should().BeTrue();

        var result = harness.DissolveComposite("g1");

        result.IsEffectiveSuccess.Should().BeTrue();
        harness.Document.Composites.Should().BeEmpty();
        harness.Node("a").Parent.Should().BeNull();
        harness.Node("b").Parent.Should().BeNull();

        // 成员一个都不删。"拆掉这个框"与"删掉框里的东西"是两件事。
        harness.Document.Nodes.Select(n => n.Id).Should().Equal("a", "b");

        DiagramValidator.Validate(harness.Document).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Composite")]
    public void Dissolving_a_nested_group_returns_members_to_its_parent()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.CreateGroup("outer", []).IsEffectiveSuccess.Should().BeTrue();
        harness.CreateGroup("inner", ["a"], parent: "outer").IsEffectiveSuccess.Should().BeTrue();

        harness.DissolveComposite("inner").IsEffectiveSuccess.Should().BeTrue();

        // 回到顶层的话，用户拆掉最里面那一层，结果外面几层的归属也跟着没了，
        // 而画面上只是"少了个框"。
        harness.Node("a").Parent.Should().Be("outer");
        harness.Composite("outer").Members.Should().Equal("a");

        DiagramValidator.Validate(harness.Document).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Composite")]
    public void Released_members_take_the_position_of_the_dissolved_group()
    {
        using var harness = new Harness();
        harness.AddNode("x");
        harness.AddNode("y");
        harness.AddNode("a");
        harness.AddNode("b");

        // 先把 inner 建成，再让 lane 的成员列表里把它夹在 x 与 y 中间。
        harness.CreateGroup("inner", ["a", "b"]).IsEffectiveSuccess.Should().BeTrue();
        harness.CreateGroup("lane", ["x", "inner", "y"]).IsEffectiveSuccess.Should().BeTrue();

        harness.Composite("lane").Members.Should().Equal("x", "inner", "y");

        harness.DissolveComposite("inner").IsEffectiveSuccess.Should().BeTrue();

        // 一律追加到末尾的话，条带次序会跟着变，而这件事在界面上看不出异常。
        harness.Composite("lane").Members.Should().Equal("x", "a", "b", "y");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Dissolving_a_missing_composite_is_rejected()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var before = harness.Snapshot();

        var result = harness.DissolveComposite("查无此物");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.CompositeMissing);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Composite")]
    public void Undoing_a_dissolve_puts_the_content_back()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.CreateGroup("outer", []).IsEffectiveSuccess.Should().BeTrue();
        harness.CreateGroup("inner", ["a", "b"], parent: "outer").IsEffectiveSuccess.Should().BeTrue();

        var structuralBefore = harness.Document.StructuralHash;
        var visualBefore = harness.Document.VisualHash;

        harness.DissolveComposite("inner");
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Document.Composites.Select(c => c.Id).Should().Equal("outer", "inner");
        harness.Composite("outer").Members.Should().Equal("inner");
        harness.Composite("inner").Members.Should().Equal("a", "b");
        harness.Node("a").Parent.Should().Be("inner");

        harness.Document.StructuralHash.Should().Be(structuralBefore);
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    #endregion

    #region 搬移

    [Fact]
    [Trait("Category", "Composite")]
    public void Moving_a_node_into_a_group_sets_both_places()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.CreateGroup("g1", []).IsEffectiveSuccess.Should().BeTrue();

        harness.MoveIntoComposite("a", "g1").IsEffectiveSuccess.Should().BeTrue();

        harness.Composite("g1").Members.Should().Equal("a");
        harness.Node("a").Parent.Should().Be("g1");

        DiagramValidator.Validate(harness.Document).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Composite")]
    public void Moving_a_node_to_the_top_level_takes_it_out_of_its_group()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.CreateGroup("g1", ["a"]).IsEffectiveSuccess.Should().BeTrue();

        harness.MoveIntoComposite("a", null).IsEffectiveSuccess.Should().BeTrue();

        harness.Composite("g1").Members.Should().BeEmpty();
        harness.Node("a").Parent.Should().BeNull();

        DiagramValidator.Validate(harness.Document).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Composite")]
    public void Moving_between_containers_updates_both_of_them()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.CreateGroup("g1", ["a"]).IsEffectiveSuccess.Should().BeTrue();
        harness.CreateGroup("g2", []).IsEffectiveSuccess.Should().BeTrue();

        harness.MoveIntoComposite("a", "g2").IsEffectiveSuccess.Should().BeTrue();

        harness.Composite("g1").Members.Should().BeEmpty();
        harness.Composite("g2").Members.Should().Equal("a");

        DiagramValidator.Validate(harness.Document).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Composite")]
    public void Moving_into_the_container_it_is_already_in_is_a_no_op()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.CreateGroup("g1", ["a"]).IsEffectiveSuccess.Should().BeTrue();

        var versionBefore = harness.Document.Version;

        harness.MoveIntoComposite("a", "g1").IsNoOp.Should().BeTrue();

        harness.Document.Version.Should().Be(versionBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Moving_a_group_into_its_own_descendant_is_rejected()
    {
        using var harness = new Harness();
        harness.CreateGroup("outer", []).IsEffectiveSuccess.Should().BeTrue();
        harness.CreateGroup("inner", [], parent: "outer").IsEffectiveSuccess.Should().BeTrue();

        var before = harness.Snapshot();

        // 不挡的话，"向上找容器"这条链永远走不到头，而栈溢出的现场离这条命令很远。
        var result = harness.MoveIntoComposite("outer", "inner");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.GroupCycle);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Moving_a_group_into_itself_is_rejected()
    {
        using var harness = new Harness();
        harness.CreateGroup("g1", []).IsEffectiveSuccess.Should().BeTrue();

        var result = harness.MoveIntoComposite("g1", "g1");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.GroupCycle);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Moving_something_that_does_not_exist_is_rejected()
    {
        using var harness = new Harness();
        harness.CreateGroup("g1", []).IsEffectiveSuccess.Should().BeTrue();

        var missingMember = harness.MoveIntoComposite("查无此物", "g1");
        missingMember.IsSuccess.Should().BeFalse();
        missingMember.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.GroupMemberMissing);

        harness.AddNode("a");
        var missingTarget = harness.MoveIntoComposite("a", "查无此物");
        missingTarget.IsSuccess.Should().BeFalse();
        missingTarget.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.CompositeMissing);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Moving_a_subtree_that_would_get_too_deep_is_rejected()
    {
        using var harness = new Harness();

        // 先搭一条到上限的链：g1 ⊃ g2 ⊃ … ⊃ gN。
        for (var depth = 1; depth <= CompositeLimits.MaxDepth; depth++)
        {
            var parent = depth == 1 ? null : $"g{depth - 1}";
            harness.CreateGroup($"g{depth}", [], parent: parent).IsEffectiveSuccess.Should().BeTrue();
        }

        // 另一条同样深的链，整体搬到最底下那一层就会超。
        for (var depth = 1; depth <= CompositeLimits.MaxDepth; depth++)
        {
            var parent = depth == 1 ? null : $"h{depth - 1}";
            harness.CreateGroup($"h{depth}", [], parent: parent).IsEffectiveSuccess.Should().BeTrue();
        }
        var before = harness.Snapshot();

        // 只看被搬的那一个是不够的：跟着一起沉下去的还有它里面套着的所有东西。
        var result = harness.MoveIntoComposite(
            $"h1",
            $"g{CompositeLimits.MaxDepth}");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.CompositeTooDeep);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Composite")]
    public void Undoing_a_move_puts_the_content_back()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.CreateGroup("g1", ["a"]).IsEffectiveSuccess.Should().BeTrue();
        harness.CreateGroup("g2", []).IsEffectiveSuccess.Should().BeTrue();

        var structuralBefore = harness.Document.StructuralHash;
        var visualBefore = harness.Document.VisualHash;

        harness.MoveIntoComposite("a", "g2");
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Composite("g1").Members.Should().Equal("a");
        harness.Composite("g2").Members.Should().BeEmpty();
        harness.Node("a").Parent.Should().Be("g1");

        harness.Document.StructuralHash.Should().Be(structuralBefore);
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    [Fact]
    [Trait("Category", "Composite")]
    public void Move_and_dissolve_can_be_replayed_without_duplicating_members()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.CreateGroup("g1", []).IsEffectiveSuccess.Should().BeTrue();

        // 撤销与重做共用还原那一条路径，所以"可重复执行"这件事要真的走两遍才验得到。
        for (var round = 0; round < 2; round++)
        {
            harness.MoveIntoComposite("a", "g1");
            harness.Composite("g1").Members.Should().Equal("a");

            harness.Bus.Undo().IsSuccess.Should().BeTrue();
            harness.Composite("g1").Members.Should().BeEmpty();
            harness.Node("a").Parent.Should().BeNull();
        }
    }

    #endregion

    #region Memento 往返

    [Fact]
    [Trait("Category", "MementoRegistration")]
    public void A_composite_memento_survives_a_serialization_round_trip()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.CreateGroup("g1", ["a", "b"]).IsEffectiveSuccess.Should().BeTrue();

        var memento = new DissolveCompositeCommand("g1").CaptureMemento(harness.Document);

        // 走一遍序列化是验多态标签与那份成员父级记录：标签漏了的话，
        // 普通测试全绿而原生编译下反序列化会失败。
        var restored = DiagramSerializer.DeserializeMemento(DiagramSerializer.SerializeMemento(memento));

        restored.Should().BeOfType<CompositeMemento>();
        restored.AffectedIds.Should().Equal("g1", "a", "b");

        var typed = (CompositeMemento)restored;
        typed.PreviousComposites.Should().ContainSingle().Which.Id.Should().Be("g1");
        typed.PreviousParents.Should().HaveCount(2);

        new DissolveCompositeCommand("g1").RestoreMemento(harness.Document, restored);

        // 还原之后成员关系两处都回到原样，校验器仍然干净。
        harness.Node("a").Parent.Should().Be("g1");
        DiagramValidator.Validate(harness.Document).Should().BeEmpty();
    }

    #endregion
}
