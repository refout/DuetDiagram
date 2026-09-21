using DuetDiagram.Core.Model;
using DuetDiagram.Dsl.Parsing;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Dsl.Tests;

/// <summary>
/// DSL 的语法分析。
/// </summary>
/// <remarks>
/// 每条断言都对着一条语法约定。规格里没有明说的
/// （引号、转义、错误恢复）在这里被钉成默认值，改动必须是有意的。
/// </remarks>
public sealed class ParserTests
{

    #region 头部

    [Fact]
    [Trait("Category", "DslParsing")]
    public void The_header_carries_version_kind_and_direction()
    {
        var document = Parse("dsl 1\nkind flowchart\ndirection LR");

        document.Version.Should().Be(1);
        document.Kind.Should().Be(DiagramKind.Flowchart);
        document.Direction.Should().Be(Direction.LR);
        document.IsClean.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void An_empty_source_gives_an_empty_document_with_the_defaults()
    {
        var document = Parse(string.Empty);

        document.Version.Should().Be(DslVersion.Current);
        document.Kind.Should().Be(DiagramKind.Flowchart);
        document.Direction.Should().Be(Direction.TB);
        document.Nodes.Should().BeEmpty();
        document.IsClean.Should().BeTrue();
    }

    [Theory]
    [InlineData("TD", Direction.TB)]
    [InlineData("TB", Direction.TB)]
    [InlineData("BT", Direction.BT)]
    [InlineData("LR", Direction.LR)]
    [InlineData("RL", Direction.RL)]
    [Trait("Category", "DslParsing")]
    public void Both_spellings_of_top_to_bottom_collapse_to_TB(string text, Direction expected)
    {
        // TD 与 TB 同义，而 IR 只保留 TB。与 Mermaid 侧同一处理：
        // 两种都认，只往外给一个。留着两个等价取值会让下游到处写"或"。
        Parse($"direction {text}").Direction.Should().Be(expected);
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void An_unknown_version_is_recorded_but_still_parsed()
    {
        // 旧文件在新版本里仍要能打开，给出可用的结果比整份拒绝有用。
        var document = Parse("dsl 99\na \"A\"");

        document.Version.Should().Be(99);
        document.Nodes.Should().ContainSingle().Which.Id.Should().Be("a");
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("99");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void An_unknown_kind_falls_back_to_flowchart_and_says_so()
    {
        var document = Parse("kind nonsense");

        document.Kind.Should().Be(DiagramKind.Flowchart);
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("nonsense");
    }

    [Theory]
    [InlineData("flow", DiagramKind.Flow)]
    [InlineData("block", DiagramKind.Block)]
    [InlineData("state", DiagramKind.State)]
    [Trait("Category", "DslParsing")]
    public void The_other_declared_kinds_are_recognized(string text, DiagramKind expected)
    {
        Parse($"kind {text}").Kind.Should().Be(expected);
    }

    #endregion

    #region 节点

    [Fact]
    [Trait("Category", "DslParsing")]
    public void A_node_carries_every_declared_attribute()
    {
        var node = Parse("""start "开始" shape=stadium style=danger layer=top desc="说明文字" """).Nodes.Single();

        node.Id.Should().Be("start");
        node.Label.Should().Be("开始");
        node.Shape.Should().Be(NodeShape.Stadium);
        node.StyleToken.Should().Be("danger");
        node.Layer.Should().Be("top");
        node.Description.Should().Be("说明文字");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void A_node_without_a_label_leaves_it_null_for_the_consumer_to_default()
    {
        // 显示文本省略时由消费方回退到标识。解析器不替它决定，
        // 因为"用标识当显示文本"是渲染层的事，不是语法层的事。
        var node = Parse("tmp").Nodes.Single();

        node.Id.Should().Be("tmp");
        node.Label.Should().BeNull();
        node.Shape.Should().Be(NodeShape.Rect);
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void Ports_are_parsed_into_name_and_side()
    {
        var node = Parse("api \"API\" ports=req:left,resp:right").Nodes.Single();

        node.Ports.Should().HaveCount(2);
        node.Ports[0].Should().Be(new DslPortDeclaration("req", PortSide.Left));
        node.Ports[1].Should().Be(new DslPortDeclaration("resp", PortSide.Right));
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void A_malformed_port_entry_is_diagnosed_without_losing_the_others()
    {
        var document = Parse("api \"API\" ports=req:left,bogus,resp:right");

        document.Nodes.Single().Ports.Should().HaveCount(2);
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("bogus");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void An_unknown_attribute_is_ignored_but_the_node_survives()
    {
        // 忽略而不是丢弃整条声明：将来加属性时，旧解析器仍能吃新文件。
        var document = Parse("a \"A\" bogus=1");

        document.Nodes.Single().Id.Should().Be("a");
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("bogus");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void An_unknown_shape_falls_back_to_a_rectangle()
    {
        var document = Parse("a shape=blob");

        document.Nodes.Single().Shape.Should().Be(NodeShape.Rect);
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("blob");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void An_attribute_without_a_value_is_diagnosed()
    {
        var document = Parse("a shape=");

        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("shape");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void A_stray_port_on_a_node_declaration_is_reported()
    {
        // 端口只属于边。不报的话这个记号会被静默跳过，
        // 用户看不出自己把一条边写成了节点声明。
        var document = Parse("a.req");

        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("端口");
    }

    #endregion

    #region 边

    [Fact]
    [Trait("Category", "DslParsing")]
    public void A_plain_edge_has_an_arrow_and_a_solid_line()
    {
        var edge = Parse("a -> b").Edges.Single();

        edge.Id.Should().BeNull();
        edge.From.Should().Be("a");
        edge.To.Should().Be("b");
        edge.Label.Should().BeNull();
        edge.Arrow.Should().Be(ArrowStyle.Arrow);
        edge.Line.Should().Be(LineStyle.Solid);
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void A_double_dash_is_an_edge_without_an_arrow()
    {
        Parse("a -- b").Edges.Single().Arrow.Should().Be(ArrowStyle.None);
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void An_edge_label_comes_from_quoted_text()
    {
        Parse("check -> fail \"否\"").Edges.Single().Label.Should().Be("否");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void An_edge_can_carry_an_identifier()
    {
        // 同一对节点之间有多条边时靠它区分。
        var edges = Parse("e1: a -> b \"第一条\"\ne2: a -> b \"第二条\"").Edges;

        edges.Should().HaveCount(2);
        edges[0].Id.Should().Be("e1");
        edges[1].Id.Should().Be("e2");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void An_edge_can_carry_attributes()
    {
        var edge = Parse("a -> b \"标签\" line=dashed arrow=circle style=warning").Edges.Single();

        edge.Label.Should().Be("标签");
        edge.Line.Should().Be(LineStyle.Dashed);
        edge.Arrow.Should().Be(ArrowStyle.Circle);
        edge.StyleToken.Should().Be("warning");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void A_to_port_is_parsed_from_the_endpoint()
    {
        var edge = Parse("web -> api.req \"请求\"").Edges.Single();

        edge.To.Should().Be("api");
        edge.ToPort.Should().Be("req");
        edge.FromPort.Should().BeNull();
        edge.Label.Should().Be("请求");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void A_from_port_is_parsed_from_the_endpoint()
    {
        // 少了这条分支的后果很隐蔽：整条边会被静默跳过，
        // 图上少一条线而没有任何提示。
        var edge = Parse("api.req -> web").Edges.Single();

        edge.From.Should().Be("api");
        edge.FromPort.Should().Be("req");
        edge.To.Should().Be("web");
        edge.ToPort.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void An_identified_edge_can_also_carry_a_from_port()
    {
        var edge = Parse("e1: api.req -> web").Edges.Single();

        edge.Id.Should().Be("e1");
        edge.From.Should().Be("api");
        edge.FromPort.Should().Be("req");
        edge.To.Should().Be("web");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void An_edge_without_a_connector_is_diagnosed()
    {
        var document = Parse("a b");

        document.Edges.Should().BeEmpty();
        document.Diagnostics.Should().ContainSingle();
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void An_edge_without_a_target_is_diagnosed()
    {
        var document = Parse("a ->");

        document.Edges.Should().BeEmpty();
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("终点");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void Implicit_node_creation_is_left_to_the_mapping_stage()
    {
        // 语法层只如实记录"这条边连到 a 和 b"，不替它们建节点。
        //
        // 这是一个**有意的分工**，不是漏了。Mermaid 侧同样不在解析层建节点
        // （它的语料恰好总是显式声明，所以那条不变量是靠语料本身满足的）。
        // 两边保持一致，两种格式的对比才是在比格式差异而不是比实现差异；
        // 隐式创建由映射阶段统一补，两个格式共用同一套规则。
        var document = Parse("a -> b");

        document.Nodes.Should().BeEmpty();
        document.Edges.Should().ContainSingle();
        document.IsClean.Should().BeTrue();
    }

    #endregion

    #region 分组

    [Fact]
    [Trait("Category", "DslParsing")]
    public void Group_members_are_derived_from_the_node_parents()
    {
        var document = Parse("group backend \"后端\"\n  api \"API\"\n  db \"库\"\nend");

        var group = document.Groups.Single();

        group.Kind.Should().Be(DslGroupKind.Group);
        group.Id.Should().Be("backend");
        group.Label.Should().Be("后端");
        group.Parent.Should().BeNull();
        group.Members.Should().Equal("api", "db");

        document.Nodes.Should().OnlyContain(n => n.Parent == "backend");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void The_three_group_keywords_are_told_apart()
    {
        Parse("group a \"A\"\nend").Groups.Single().Kind.Should().Be(DslGroupKind.Group);
        Parse("lane a \"A\"\nend").Groups.Single().Kind.Should().Be(DslGroupKind.Lane);
        Parse("subflow a \"A\"\nend").Groups.Single().Kind.Should().Be(DslGroupKind.Subflow);
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void A_group_without_a_label_uses_its_identifier()
    {
        var group = Parse("group backend\nend").Groups.Single();

        group.Id.Should().Be("backend");
        group.Label.Should().Be("backend");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void A_group_without_an_identifier_gets_a_positional_name_and_a_diagnostic()
    {
        var document = Parse("group \"只有文本\"\nend");

        document.Groups.Single().Id.Should().Be("group1");
        document.Diagnostics.Should().ContainSingle();
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void Nested_groups_list_the_inner_group_among_the_outer_members()
    {
        // 成员里既有节点也有嵌套分组，与 IR 的约定一致：
        // 校验器先按节点解析成员、解析不到再按组合解析。
        var document = Parse("group outer \"外\"\n  a \"A\"\n  group inner \"内\"\n    b \"B\"\n  end\nend");

        var outer = document.Groups.Single(g => g.Id == "outer");
        var inner = document.Groups.Single(g => g.Id == "inner");

        outer.Members.Should().Equal("a", "inner");
        inner.Members.Should().Equal("b");
        inner.Parent.Should().Be("outer");
        document.Nodes.Single(n => n.Id == "b").Parent.Should().Be("inner");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void Edges_are_not_affected_by_group_boundaries()
    {
        // 边可以写在任何位置并引用任何节点，包括跨组的。
        var document = Parse("group a \"A\"\n  x \"X\"\nend\ny \"Y\"\nx -> y");

        document.Edges.Single().From.Should().Be("x");
        document.Edges.Single().To.Should().Be("y");
        document.IsClean.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void A_bare_end_with_no_open_group_is_diagnosed()
    {
        var document = Parse("end");

        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("end");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void End_is_an_ordinary_identifier_when_no_group_is_open()
    {
        // 真实语料逼出来的：S07 里 end 是终点的名字。
        // 无条件当关键字的话，那一行被吞掉、节点没声明，
        // 引用它的边与 order 全部指向不存在的节点——整份图错位，
        // 而诊断只有一条"没有对应分组的 end"，指向的原因完全不对。
        var document = Parse("start \"开始\"\nend \"结束\" shape=stadium\nstart -> end\norder start: end");

        document.Nodes.Select(n => n.Id).Should().Equal("start", "end");
        document.Nodes.Single(n => n.Id == "end").Label.Should().Be("结束");
        document.Edges.Single().To.Should().Be("end");
        document.Layout.Single().Nodes.Should().Equal("end");
        document.IsClean.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void End_still_closes_a_group_that_is_open()
    {
        var document = Parse("group g \"G\"\n  a \"A\"\nend\nb \"B\"");

        document.Nodes.Single(n => n.Id == "a").Parent.Should().Be("g");
        document.Nodes.Single(n => n.Id == "b").Parent.Should().BeNull();
        document.IsClean.Should().BeTrue();
    }

    #endregion

    #region 布局意图

    [Fact]
    [Trait("Category", "DslParsing")]
    public void Same_rank_collects_the_node_list()
    {
        var intent = Parse("same-rank pass, fail").Layout.Single();

        intent.Kind.Should().Be(DslLayoutIntentKind.SameRank);
        intent.Nodes.Should().Equal("pass", "fail");
        intent.Subject.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void Order_carries_a_subject_and_the_out_edge_sequence()
    {
        var intent = Parse("order check: pass, fail").Layout.Single();

        intent.Kind.Should().Be(DslLayoutIntentKind.Order);
        intent.Subject.Should().Be("check");
        intent.Nodes.Should().Equal("pass", "fail");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void Align_collects_the_node_list()
    {
        var intent = Parse("align pass, fail").Layout.Single();

        intent.Kind.Should().Be(DslLayoutIntentKind.Align);
        intent.Nodes.Should().Equal("pass", "fail");
    }

    [Theory]
    [InlineData("right-of", PlaceRelation.RightOf)]
    [InlineData("left-of", PlaceRelation.LeftOf)]
    [InlineData("above", PlaceRelation.Above)]
    [InlineData("below", PlaceRelation.Below)]
    [Trait("Category", "DslParsing")]
    public void Place_carries_a_subject_a_relation_and_a_reference(string text, PlaceRelation expected)
    {
        var intent = Parse($"place fail {text} pass").Layout.Single();

        intent.Kind.Should().Be(DslLayoutIntentKind.Place);
        intent.Subject.Should().Be("fail");
        intent.Relation.Should().Be(expected);
        intent.Nodes.Should().Equal("pass");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void Pin_carries_absolute_coordinates()
    {
        var intent = Parse("pin fail at 640, 320").Layout.Single();

        intent.Kind.Should().Be(DslLayoutIntentKind.Pin);
        intent.Subject.Should().Be("fail");
        intent.X.Should().Be(640);
        intent.Y.Should().Be(320);
        intent.Nodes.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void Pin_accepts_negative_coordinates()
    {
        var intent = Parse("pin fail at -10, -20").Layout.Single();

        intent.X.Should().Be(-10);
        intent.Y.Should().Be(-20);
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void Pin_accepts_fractional_coordinates()
    {
        var intent = Parse("pin fail at 12.5, 3.25").Layout.Single();

        intent.X.Should().Be(12.5);
        intent.Y.Should().Be(3.25);
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void Same_rank_with_fewer_than_two_nodes_is_diagnosed()
    {
        var document = Parse("same-rank pass");

        document.Layout.Should().BeEmpty();
        document.Diagnostics.Should().ContainSingle();
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void A_place_without_a_known_relation_is_diagnosed()
    {
        var document = Parse("place fail beside pass");

        document.Layout.Should().BeEmpty();
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("关系");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void A_pin_without_at_is_diagnosed()
    {
        var document = Parse("pin fail 640, 320");

        document.Layout.Should().BeEmpty();
        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("at");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void Spacing_declarations_are_kept_when_present()
    {
        var document = Parse("node-spacing 40\nlayer-spacing 70");

        document.NodeSpacing.Should().Be(40);
        document.LayerSpacing.Should().Be(70);
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void Spacing_stays_unset_when_absent_so_the_layout_default_wins()
    {
        // 用 null 而不是填一个解析器自己的默认值：缺省值属于布局引擎，
        // 填在这里会让"用户没写"和"用户写了 40"变成同一件事。
        var document = Parse("a \"A\"");

        document.NodeSpacing.Should().BeNull();
        document.LayerSpacing.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void A_negative_spacing_is_diagnosed()
    {
        Parse("node-spacing -5").Diagnostics.Should().ContainSingle();
    }

    #endregion

    #region 错误恢复与纯度

    [Fact]
    [Trait("Category", "DslParsing")]
    public void A_bad_line_is_skipped_and_the_rest_is_kept()
    {
        // 与 Mermaid 侧同一口径：跳过坏行、记诊断、继续。
        // 口径不定的话，对比测试量到的是口径差异而不是格式差异。
        var document = Parse("a \"A\"\n%%% 坏行\nb \"B\"\nc -> d");

        document.Nodes.Select(n => n.Id).Should().Equal("a", "b");
        document.Edges.Should().ContainSingle();
        document.Diagnostics.Should().ContainSingle().Which.Line.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void Comments_and_blank_lines_are_ignored()
    {
        var document = Parse("# 头注释\n\ndsl 1\n\na \"A\" # 尾注释\n");

        document.Nodes.Should().ContainSingle();
        document.IsClean.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void An_unterminated_string_is_reported_as_such_not_as_a_stray_character()
    {
        var document = Parse("a \"没有收尾");

        document.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("收尾");
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void Parsing_strips_code_fences()
    {
        // 语料的 c-dsl 组里有三成回答带围栏。不剥的话那十五份
        // 会在第一个记号处就失败，而失败原因与被测的东西毫无关系。
        var document = Parse("```dsl\ndsl 1\na \"A\"\n```");

        document.Nodes.Should().ContainSingle().Which.Id.Should().Be("a");
        document.IsClean.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void Parsing_the_same_source_twice_gives_the_same_result()
    {
        // 解析必须是纯函数。用集合而不是列表会让顺序随哈希漂移，
        // 表现为"同一份输入两次导入出不同的图"。
        const string Source = """
            dsl 1
            kind flowchart
            direction LR
            group outer "外"
              a "A" shape=stadium
              group inner "内"
                b "B" style=danger
              end
            end
            a -> b "边"
            same-rank a, b
            order a: b
            pin b at 10, 20
            node-spacing 40
            """;

        var first = Parse(Source);
        var second = Parse(Source);

        AstProjection.All(second).Should().Equal(AstProjection.All(first));
        second.Diagnostics.Should().Equal(first.Diagnostics);
    }

    [Fact]
    [Trait("Category", "DslParsing")]
    public void Collections_follow_first_appearance_order()
    {
        // 顺序是层内次序的依据。用集合会让同一份输入两次解析出不同的图。
        var document = Parse("z \"Z\"\nm \"M\"\na \"A\"\nz -> m\nm -> a");

        document.Nodes.Select(n => n.Id).Should().Equal("z", "m", "a");
        document.Edges.Select(e => e.From).Should().Equal("z", "m");
    }

    private static DslDocument Parse(string source) => DslParser.Parse(source);

    #endregion
}
