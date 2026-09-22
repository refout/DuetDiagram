using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Llm.Tools;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Llm.Tests;

/// <summary>
/// 布局类动作：方向、间距、三类约束、相对位置，以及归属方的判据。
/// </summary>
public sealed class LayoutToolTests
{
    #region 方向与间距

    [Fact]
    [Trait("Category", "LayoutTool")]
    public void Setting_the_direction_goes_through_the_bus()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        var outcome = Harness.Invoke(registry, DiagramToolset.Layout, """{"action":"set-direction","direction":"LR"}""")
            .Data!.Value;

        document.Direction.Should().Be(Direction.LR);
        outcome.GetProperty("structuralChanged").GetBoolean().Should().BeTrue(
            "方向决定层往哪边推，报了纯外观的话宿主只重绘不重排，画面上的层还是按旧方向排的");
    }

    [Fact]
    [Trait("Category", "LayoutTool")]
    public void Setting_the_direction_it_already_has_is_a_no_op()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        var outcome = Harness.Invoke(registry, DiagramToolset.Layout, """{"action":"set-direction","direction":"TB"}""")
            .Data!.Value;

        outcome.GetProperty("noOp").GetBoolean().Should().BeTrue(
            "批量下发时重复给同一个方向很常见，每次都推进版本、每次都重排一遍是白付的代价");
        document.Version.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "LayoutTool")]
    public void An_unknown_direction_is_rejected()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        var result = Harness.Invoke(registry, DiagramToolset.Layout, """{"action":"set-direction","direction":"lr"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Parameter.Should().Be("direction");
        result.Errors[0].Expected.Should().Contain("LR");
        document.Version.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "LayoutTool")]
    public void Setting_one_spacing_leaves_the_other_alone()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        var before = document.Layout.LayerSpacing;

        Harness.Invoke(registry, DiagramToolset.Layout, """{"action":"set-spacing","nodeSpacing":60}""")
            .IsSuccess.Should().BeTrue();

        document.Layout.NodeSpacing.Should().Be(60);
        document.Layout.LayerSpacing.Should().Be(before, "没给的那一项不动");
    }

    [Fact]
    [Trait("Category", "LayoutTool")]
    public void Setting_no_spacing_at_all_is_rejected_before_any_command_runs()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        var result = Harness.Invoke(registry, DiagramToolset.Layout, """{"action":"set-spacing"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentMissing);
        result.Errors[0].Parameter.Should().Be("nodeSpacing");
        document.Version.Should().Be(0);
    }

    #endregion

    #region 三类约束

    [Fact]
    [Trait("Category", "LayoutTool")]
    public void A_same_rank_constraint_takes_a_group_of_nodes()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Nodes(registry, "a", "b");

        Harness.Invoke(
            registry,
            DiagramToolset.Layout,
            """{"action":"add-constraint","kind":"same-rank","memberIds":["a","b"]}""")
            .IsSuccess.Should().BeTrue();

        document.Layout.SameRank.Should().ContainSingle();
        document.Layout.SameRank[0].Value.Nodes.Should().Equal("a", "b");
        document.Layout.SameRank[0].Owner.Should().Be(ConstraintOwner.Llm, "缺省归属是调用这一层的那一方");
    }

    [Fact]
    [Trait("Category", "LayoutTool")]
    public void An_align_constraint_lands_in_its_own_list()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Nodes(registry, "a", "b");

        Harness.Invoke(
            registry,
            DiagramToolset.Layout,
            """{"action":"add-constraint","kind":"align","memberIds":["a","b"]}""")
            .IsSuccess.Should().BeTrue();

        document.Layout.Align.Should().ContainSingle();
        document.Layout.SameRank.Should().BeEmpty("两类约束各自的列表不同，串了的话面板上会显示错的那一类");
    }

    [Fact]
    [Trait("Category", "LayoutTool")]
    public void An_order_constraint_takes_a_subject_and_its_outgoing_edges()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Nodes(registry, "a", "b", "c");
        Harness.Edit(registry, """{"action":"connect-edge","id":"ab","from":"a","to":"b"}""");
        Harness.Edit(registry, """{"action":"connect-edge","id":"ac","from":"a","to":"c"}""");

        Harness.Invoke(
            registry,
            DiagramToolset.Layout,
            """{"action":"add-constraint","kind":"order","subject":"a","memberIds":["ac","ab"]}""")
            .IsSuccess.Should().BeTrue();

        var order = document.Layout.Order.Should().ContainSingle().Subject.Value;

        order.NodeId.Should().Be("a");
        order.Order.Should().Equal(["ac", "ab"], "次序本身就是它的内容，不是集合语义");
    }

    [Fact]
    [Trait("Category", "LayoutTool")]
    public void An_order_constraint_without_a_subject_says_so()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Nodes(registry, "a", "b");

        var result = Harness.Invoke(
            registry,
            DiagramToolset.Layout,
            """{"action":"add-constraint","kind":"order","memberIds":["a","b"]}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentMissing);
        result.Errors[0].Parameter.Should().Be("subject",
            "让命令层回一句「至少两条出边」的话，那句话与调用方看到的参数对不上");
    }

    [Fact]
    [Trait("Category", "LayoutTool")]
    public void An_unknown_constraint_kind_lists_the_available_ones()
    {
        var registry = Harness.Registry();

        var result = Harness.Invoke(
            registry,
            DiagramToolset.Layout,
            """{"action":"add-constraint","kind":"column","memberIds":["a","b"]}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Parameter.Should().Be("kind");
        result.Errors[0].Expected.Should().Contain("same-rank").And.Contain("order");
    }

    [Fact]
    [Trait("Category", "LayoutTool")]
    public void A_constraint_without_members_is_rejected()
    {
        var registry = Harness.Registry();

        var result = Harness.Invoke(registry, DiagramToolset.Layout, """{"action":"add-constraint","kind":"align"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentMissing);
        result.Errors[0].Parameter.Should().Be("memberIds");
    }

    [Fact]
    [Trait("Category", "LayoutTool")]
    public void Removing_a_constraint_that_is_not_there_reports_the_command_layer_code()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Nodes(registry, "a", "b");

        var result = Harness.Invoke(
            registry,
            DiagramToolset.Layout,
            """{"action":"remove-constraint","kind":"align","memberIds":["a","b"]}""");

        Harness.CodeOf(result).Should().Be(ErrorCodes.LayoutConstraintMissing,
            "命令层把「这条不存在」报成错误而不是无操作——那通常说明调用方手上那份列表已经过期");
    }

    [Fact]
    [Trait("Category", "LayoutTool")]
    public void A_constraint_goes_back_out_the_way_it_came_in()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Nodes(registry, "a", "b");

        Harness.Invoke(
            registry,
            DiagramToolset.Layout,
            """{"action":"add-constraint","kind":"same-rank","memberIds":["a","b"]}""")
            .IsSuccess.Should().BeTrue();

        Harness.Invoke(
            registry,
            DiagramToolset.Layout,
            """{"action":"remove-constraint","kind":"same-rank","memberIds":["a","b"]}""")
            .IsSuccess.Should().BeTrue();

        document.Layout.SameRank.Should().BeEmpty();
    }

    #endregion

    #region 归属方

    [Fact]
    [Trait("Category", "LayoutTool")]
    public void The_owner_can_be_asked_for_a_weaker_one()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Nodes(registry, "a", "b");

        Harness.Invoke(
            registry,
            DiagramToolset.Layout,
            """{"action":"add-constraint","kind":"align","memberIds":["a","b"],"owner":"auto"}""")
            .IsSuccess.Should().BeTrue();

        document.Layout.Align[0].Owner.Should().Be(ConstraintOwner.Auto);
    }

    [Fact]
    [Trait("Category", "LayoutTool")]
    public void The_tool_layer_refuses_to_forge_a_human_owned_constraint()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Nodes(registry, "a", "b");

        var result = Harness.Invoke(
            registry,
            DiagramToolset.Layout,
            """{"action":"add-constraint","kind":"align","memberIds":["a","b"],"owner":"human"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Parameter.Should().Be("owner");
        document.Layout.Align.Should().BeEmpty(
            "归属方是降级矩阵的输入，人工定的比模型提的更值得保留；放行的话模型能把它挤掉");
    }

    #endregion

    #region 相对位置

    [Fact]
    [Trait("Category", "LayoutTool")]
    public void A_relative_place_is_written_and_says_the_solver_does_not_use_it_yet()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Nodes(registry, "a", "b");

        var result = Harness.Invoke(
            registry,
            DiagramToolset.Layout,
            """{"action":"set-place","id":"a","relativeTo":"b","relation":"right-of"}""");

        result.IsSuccess.Should().BeTrue();

        var place = document.Layout.Place.Should().ContainSingle().Subject.Value;

        place.NodeId.Should().Be("a");
        place.RelativeTo.Should().Be("b");
        place.Relation.Should().Be(PlaceRelation.RightOf);

        result.Message.Should().Contain("不消费",
            "不说的话，调用方会把「命令没生效」当成一次失败，然后换个做法重试");
    }

    [Fact]
    [Trait("Category", "LayoutTool")]
    public void Clearing_a_relative_place_is_expressed_by_leaving_the_relation_out()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Nodes(registry, "a", "b");

        Harness.Invoke(
            registry,
            DiagramToolset.Layout,
            """{"action":"set-place","id":"a","relativeTo":"b","relation":"above"}""")
            .IsSuccess.Should().BeTrue();

        Harness.Invoke(registry, DiagramToolset.Layout, """{"action":"set-place","id":"a","relativeTo":"b"}""")
            .IsSuccess.Should().BeTrue();

        document.Layout.Place.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "LayoutTool")]
    public void An_unknown_relation_lists_the_available_ones()
    {
        var registry = Harness.Registry(new DiagramDocument("doc"));

        var result = Harness.Invoke(
            registry,
            DiagramToolset.Layout,
            """{"action":"set-place","id":"a","relativeTo":"b","relation":"beside"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Parameter.Should().Be("relation");
        result.Errors[0].Expected.Should().Contain("right-of");
    }

    [Fact]
    [Trait("Category", "LayoutTool")]
    public void A_place_onto_a_missing_node_reports_the_command_layer_code()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Nodes(registry, "a");

        var result = Harness.Invoke(
            registry,
            DiagramToolset.Layout,
            """{"action":"set-place","id":"a","relativeTo":"ghost","relation":"below"}""");

        Harness.CodeOf(result).Should().Be(ErrorCodes.LayoutNodeMissing);
    }

    #endregion

    #region 未知动作

    [Fact]
    [Trait("Category", "LayoutTool")]
    public void An_unknown_layout_action_lists_the_available_ones()
    {
        var result = Harness.Invoke(Harness.Registry(), DiagramToolset.Layout, """{"action":"pin"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Parameter.Should().Be("action");
        result.Errors[0].Expected.Should().Contain("set-direction").And.Contain("set-place");
    }

    #endregion

    /// <summary>造几个节点，布局类的动作大多要有节点才成立。</summary>
    private static void Nodes(ToolRegistry registry, params string[] ids)
    {
        foreach (var id in ids)
        {
            Harness.Edit(registry, $$"""{"action":"add-node","id":"{{id}}"}""").IsSuccess.Should().BeTrue();
        }
    }
}
