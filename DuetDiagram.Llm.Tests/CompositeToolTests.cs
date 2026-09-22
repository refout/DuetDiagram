using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Llm.Tools;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Llm.Tests;

/// <summary>
/// 组合类动作：四种组合的创建、移入与解散，以及成环与深度上限。
/// </summary>
public sealed class CompositeToolTests
{
    #region 创建

    [Theory]
    [InlineData("group", typeof(GroupDef))]
    [InlineData("lane", typeof(LaneDef))]
    [InlineData("subflow", typeof(SubflowDef))]
    [InlineData("combo", typeof(ComboDef))]
    [Trait("Category", "CompositeTool")]
    public void Each_kind_creates_its_own_record_type(string kind, Type expected)
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Invoke(
            registry,
            DiagramToolset.Composite,
            $$"""{"action":"create","id":"g1","kind":"{{kind}}","label":"一组"}""")
            .IsSuccess.Should().BeTrue();

        document.Composites.Should().ContainSingle().Which.Should().BeOfType(expected,
            "四种组合的字段形状一样，差别只在类型名，所以种类由记录类型表达");
    }

    [Fact]
    [Trait("Category", "CompositeTool")]
    public void Members_are_taken_over_from_their_old_container()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        Harness.Edit(registry, """{"action":"add-node","id":"b"}""");

        Harness.Invoke(
            registry,
            DiagramToolset.Composite,
            """{"action":"create","id":"g1","kind":"group","memberIds":["a"]}""")
            .IsSuccess.Should().BeTrue();

        Harness.Invoke(
            registry,
            DiagramToolset.Composite,
            """{"action":"create","id":"g2","kind":"group","memberIds":["a","b"]}""")
            .IsSuccess.Should().BeTrue();

        Composite(document, "g1").Members.Should().BeEmpty("一个节点只能属于一个组合");
        Composite(document, "g2").Members.Should().Equal("a", "b");
        Node(document, "a").Parent.Should().Be("g2", "成员列表说了算，而父级字段是它的冗余索引");
    }

    [Fact]
    [Trait("Category", "CompositeTool")]
    public void An_unknown_kind_lists_the_available_ones()
    {
        var registry = Harness.Registry(new DiagramDocument("doc"));

        var result = Harness.Invoke(
            registry,
            DiagramToolset.Composite,
            """{"action":"create","id":"g1","kind":"swimlane"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Parameter.Should().Be("kind");
        result.Errors[0].Expected.Should().Contain("group").And.Contain("subflow");
    }

    [Fact]
    [Trait("Category", "CompositeTool")]
    public void A_duplicate_identifier_reports_the_command_layer_code()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");

        var result = Harness.Invoke(
            registry,
            DiagramToolset.Composite,
            """{"action":"create","id":"a","kind":"group"}""");

        Harness.CodeOf(result).Should().Be(ErrorCodes.DuplicateId,
            "九个集合共用一个命名空间，只查组合那一张表的话，组合会与节点撞标识");
    }

    #endregion

    #region 移入

    [Fact]
    [Trait("Category", "CompositeTool")]
    public void Moving_a_member_into_another_composite_takes_it_out_of_the_old_one()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        Harness.Invoke(registry, DiagramToolset.Composite, """{"action":"create","id":"g1","kind":"group","memberIds":["a"]}""");
        Harness.Invoke(registry, DiagramToolset.Composite, """{"action":"create","id":"g2","kind":"group"}""");

        Harness.Invoke(registry, DiagramToolset.Composite, """{"action":"move-into","id":"a","targetId":"g2"}""")
            .IsSuccess.Should().BeTrue();

        Composite(document, "g1").Members.Should().BeEmpty();
        Composite(document, "g2").Members.Should().Equal("a");
    }

    [Fact]
    [Trait("Category", "CompositeTool")]
    public void Moving_a_member_without_a_target_lifts_it_to_the_top()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        Harness.Invoke(registry, DiagramToolset.Composite, """{"action":"create","id":"g1","kind":"group","memberIds":["a"]}""");

        Harness.Invoke(registry, DiagramToolset.Composite, """{"action":"move-into","id":"a"}""")
            .IsSuccess.Should().BeTrue();

        Composite(document, "g1").Members.Should().BeEmpty();
        Node(document, "a").Parent.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "CompositeTool")]
    public void Moving_a_composite_into_its_own_descendant_is_blocked()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Invoke(registry, DiagramToolset.Composite, """{"action":"create","id":"outer","kind":"group"}""");
        Harness.Invoke(registry, DiagramToolset.Composite, """{"action":"create","id":"inner","kind":"group"}""");

        // 先把 inner 套进 outer，于是 inner 成了 outer 的后代。
        Harness.Invoke(registry, DiagramToolset.Composite, """{"action":"move-into","id":"inner","targetId":"outer"}""")
            .IsSuccess.Should().BeTrue();

        var result = Harness.Invoke(
            registry,
            DiagramToolset.Composite,
            """{"action":"move-into","id":"outer","targetId":"inner"}""");

        result.IsSuccess.Should().BeFalse("成环不挡的话布局会无限递归，而栈溢出的现场离这条命令很远");
        Composite(document, "outer").Parent.Should().BeNull();
        Composite(document, "inner").Parent.Should().Be("outer");
    }

    [Fact]
    [Trait("Category", "CompositeTool")]
    public void A_member_that_is_not_there_reports_the_command_layer_code()
    {
        var registry = Harness.Registry(new DiagramDocument("doc"));

        var result = Harness.Invoke(registry, DiagramToolset.Composite, """{"action":"move-into","id":"ghost"}""");

        Harness.CodeOf(result).Should().Be(ErrorCodes.GroupMemberMissing);
    }

    [Fact]
    [Trait("Category", "CompositeTool")]
    public void Moving_without_a_member_is_rejected_before_any_command_runs()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        var result = Harness.Invoke(registry, DiagramToolset.Composite, """{"action":"move-into","targetId":"g1"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentMissing);
        result.Errors[0].Parameter.Should().Be("id");
        document.Version.Should().Be(0);
    }

    #endregion

    #region 解散

    [Fact]
    [Trait("Category", "CompositeTool")]
    public void Dissolving_hands_the_members_to_the_outer_composite()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        Harness.Invoke(registry, DiagramToolset.Composite, """{"action":"create","id":"outer","kind":"group"}""");
        Harness.Invoke(
            registry,
            DiagramToolset.Composite,
            """{"action":"create","id":"inner","kind":"group","memberIds":["a"]}""");

        Harness.Invoke(registry, DiagramToolset.Composite, """{"action":"move-into","id":"inner","targetId":"outer"}""")
            .IsSuccess.Should().BeTrue();

        Harness.Invoke(registry, DiagramToolset.Composite, """{"action":"dissolve","id":"inner"}""")
            .IsSuccess.Should().BeTrue();

        document.Composites.Should().NotContain(composite => composite.Id == "inner");
        Composite(document, "outer").Members.Should().Equal(["a"], "成员接过 inner 在外层占的那一位");
        Node(document, "a").Parent.Should().Be("outer",
            "成员回到父级而不是顶层；回到顶层会把层级结构悄悄压平");
    }

    [Fact]
    [Trait("Category", "CompositeTool")]
    public void Dissolving_something_that_is_not_there_reports_the_command_layer_code()
    {
        var registry = Harness.Registry(new DiagramDocument("doc"));

        var result = Harness.Invoke(registry, DiagramToolset.Composite, """{"action":"dissolve","id":"ghost"}""");

        Harness.CodeOf(result).Should().Be(ErrorCodes.CompositeMissing);
    }

    #endregion

    #region 深度上限

    [Fact]
    [Trait("Category", "CompositeTool")]
    public void Nesting_past_the_limit_is_rejected_by_the_command_layer()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        // 先在顶层把 MaxDepth 个组合建出来，再逐层套成一条链，
        // 于是 c{MaxDepth} 正好落在上限那一层。工具层不判深度——
        // 判的话上限会有两份，而两份迟早对不上。
        for (var depth = 1; depth <= CompositeLimits.MaxDepth; depth++)
        {
            Harness.Invoke(
                registry,
                DiagramToolset.Composite,
                $$"""{"action":"create","id":"c{{depth}}","kind":"group"}""")
                .IsSuccess.Should().BeTrue();
        }

        for (var depth = 2; depth <= CompositeLimits.MaxDepth; depth++)
        {
            Harness.Invoke(
                registry,
                DiagramToolset.Composite,
                $$"""{"action":"move-into","id":"c{{depth}}","targetId":"c{{depth - 1}}"}""")
                .IsSuccess.Should().BeTrue($"第 {depth} 层在上限之内");
        }

        var top = CompositeLimits.MaxDepth + 1;

        Harness.Invoke(registry, DiagramToolset.Composite, $$"""{"action":"create","id":"c{{top}}","kind":"group"}""")
            .IsSuccess.Should().BeTrue("新建的组合在顶层，深度是 1");

        var result = Harness.Invoke(
            registry,
            DiagramToolset.Composite,
            $$"""{"action":"move-into","id":"c{{top}}","targetId":"c{{CompositeLimits.MaxDepth}}"}""");

        result.IsSuccess.Should().BeFalse();
        Harness.CodeOf(result).Should().Be(ErrorCodes.CompositeTooDeep);
    }

    #endregion

    #region 未知动作

    [Fact]
    [Trait("Category", "CompositeTool")]
    public void An_unknown_composite_action_lists_the_available_ones()
    {
        var result = Harness.Invoke(Harness.Registry(), DiagramToolset.Composite, """{"action":"split"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Parameter.Should().Be("action");
        result.Errors[0].Expected.Should().Contain("create").And.Contain("dissolve");
    }

    #endregion

    #region 夹具

    /// <summary>按标识取一个组合。取不到就是这条用例的前提已经不成立，直接让断言报出来。</summary>
    private static CompositeDef Composite(DiagramDocument document, string id) =>
        document.Composites.Should().ContainSingle(c => c.Id == id).Which;

    /// <summary>按标识取一个节点。</summary>
    private static NodeDef Node(DiagramDocument document, string id) =>
        document.Nodes.Should().ContainSingle(n => n.Id == id).Which;

    #endregion
}
