using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Dsl.Mapping;
using DuetDiagram.Dsl.Parsing;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Dsl.Tests;

/// <summary>
/// DSL 语法树到 IR 的映射。
/// </summary>
/// <remarks>
/// 断言分两类：一类是"字段搬对了没有"，一类是"消歧规则是不是照定的那样"。
/// 后者更要紧——撞名与补节点是这一层仅有的两处真正做决定的地方，
/// 决定错了图还是能画出来，只是画错。
/// </remarks>
public sealed class MapperTests
{
    // ---- 节点 ----

    [Fact]
    [Trait("Category", "DslMapping")]
    public void Node_fields_carry_over()
    {
        var result = Map("""
            api "API 服务" shape=hexagon style=primary layer=fore desc="对外接口" ports=req:left,resp:right
            """);

        var node = result.Document.Nodes.Single();

        node.Id.Should().Be("api");
        node.Label.Should().Be("API 服务");
        node.Shape.Should().Be(NodeShape.Hexagon);
        node.StyleToken.Should().Be("primary");
        node.Layer.Should().Be("fore");
        node.Desc.Should().Be("对外接口");
        node.Ports.Select(p => (p.Name, p.Side)).Should().Equal(("req", PortSide.Left), ("resp", PortSide.Right));
    }

    [Fact]
    [Trait("Category", "DslMapping")]
    public void A_node_without_a_label_falls_back_to_its_id()
    {
        // 语法层如实产出"没写显示文本"，回退是映射层的事。
        // IR 里的 label 是要拿去渲染的，不能是空串。
        Map("tmp").Document.Nodes.Single().Label.Should().Be("tmp");
    }

    [Fact]
    [Trait("Category", "DslMapping")]
    public void Declared_ports_are_marked_custom()
    {
        // 自动端口会被重算，自定义端口不会。DSL 里写出来的端口是明确指定的，
        // 标错的话用户摆好的端口每次重排都会跑回默认位置。
        Map("api ports=req:left").Document.Nodes.Single().Ports.Single().IsCustom.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "DslMapping")]
    public void Node_order_is_the_declaration_order()
    {
        // 顺序不是装饰：节点在集合里的位置是层内次序的依据。
        Map("c\na\nb").Document.Nodes.Select(n => n.Id).Should().Equal("c", "a", "b");
    }

    // ---- 边 ----

    [Fact]
    [Trait("Category", "DslMapping")]
    public void Edge_fields_carry_over()
    {
        var result = Map("""
            a "甲" ports=out:right
            b "乙"
            e1: a.out -> b "是" line=dashed arrow=open
            """);

        var edge = result.Document.Edges.Single();

        edge.Id.Should().Be("e1");
        edge.From.Should().Be("a");
        edge.To.Should().Be("b");
        edge.FromPort.Should().Be("out");
        edge.ToPort.Should().BeNull();
        edge.Label.Should().Be("是");
        edge.Style.Line.Should().Be(LineStyle.Dashed);
        edge.Style.Arrow.Should().Be(ArrowStyle.OpenArrow);
    }

    [Fact]
    [Trait("Category", "DslMapping")]
    public void Default_line_and_arrow_are_left_unset()
    {
        // 显式写一个与缺省相同的值不增加信息，却会让"同一张图的两种文本"产出不同的 IR
        // （视觉哈希也就跟着不同）。EdgeDef 的两个便捷属性本来就会回退到这两个值。
        var edge = Map("a -> b").Document.Edges.Single();

        edge.Style.Line.Should().BeNull();
        edge.Style.Arrow.Should().BeNull();
        edge.Line.Should().Be(LineStyle.Solid);
        edge.Arrow.Should().Be(ArrowStyle.Arrow);
    }

    [Fact]
    [Trait("Category", "DslMapping")]
    public void A_line_without_an_arrow_writes_the_non_default()
    {
        // `--` 是 arrow=none，与缺省不同，因此照写。
        Map("a -- b").Document.Edges.Single().Style.Arrow.Should().Be(ArrowStyle.None);
    }

    [Fact]
    [Trait("Category", "DslMapping")]
    public void Unnamed_edges_get_generated_ids_that_dodge_the_taken_ones()
    {
        var result = Map("""
            a -> b
            e1: b -> c
            b -> c
            """);

        result.Document.Edges.Select(e => e.Id).Should().Equal("e2", "e1", "e3");
    }

    // ---- 分组 ----

    [Fact]
    [Trait("Category", "DslMapping")]
    public void The_three_group_keywords_map_to_the_three_derived_types()
    {
        var result = Map("""
            group g "分组"
            end
            lane l "泳道"
            end
            subflow s "子流程"
            end
            """);

        result.Document.Composites.Should().HaveCount(3);
        result.Document.Composites.Single(c => c.Id == "g").Should().BeOfType<GroupDef>();
        result.Document.Composites.Single(c => c.Id == "l").Should().BeOfType<LaneDef>();
        result.Document.Composites.Single(c => c.Id == "s").Should().BeOfType<SubflowDef>();
    }

    [Fact]
    [Trait("Category", "DslMapping")]
    public void Members_hold_nodes_first_then_nested_groups()
    {
        // 与语法层同一顺序。成员表在 IR 里是权威（节点的 parent 是冗余索引），
        // 所以它同时决定布局的嵌套关系。
        var result = Map("""
            group outer "外层"
              a "甲"
              group inner "内层"
                b "乙"
              end
            end
            """);

        result.Document.Composites.Single(c => c.Id == "outer").Members.Should().Equal("a", "inner");
        result.Document.Composites.Single(c => c.Id == "inner").Members.Should().Equal("b");
        result.Document.Nodes.Single(n => n.Id == "a").Parent.Should().Be("outer");
        result.Document.Nodes.Single(n => n.Id == "b").Parent.Should().Be("inner");
        result.Document.Composites.Single(c => c.Id == "inner").Parent.Should().Be("outer");
    }

    // ---- 补节点 ----

    [Fact]
    [Trait("Category", "DslMapping")]
    public void An_undeclared_endpoint_becomes_a_rectangular_node()
    {
        // 只声明起点，终点留给映射层补。
        var result = Map("a \"甲\"\na -> b");

        var created = result.Document.Nodes.Single(n => n.Id == "b");

        created.Label.Should().Be("b");
        created.Shape.Should().Be(NodeShape.Rect);
        created.Parent.Should().BeNull();

        result.Report.CreatedNodes.Should().ContainSingle()
            .Which.Should().Be(new CreatedNode("b", "e1"));
    }

    [Fact]
    [Trait("Category", "DslMapping")]
    public void Created_nodes_come_after_the_declared_ones_in_first_reference_order()
    {
        // 语法树里没有行号，所以"插到首次引用处"做不到。代价是补出来的节点
        // 在层内次序上排在最后——这是规则，不是意外。
        var result = Map("""
            a "甲"
            a -> x
            y -> a
            """);

        result.Document.Nodes.Select(n => n.Id).Should().Equal("a", "x", "y");
    }

    [Fact]
    [Trait("Category", "DslMapping")]
    public void An_endpoint_pointing_at_a_group_does_not_create_a_node()
    {
        // 分层架构图里 `ODS --> DWD` 是拿分组当端点的写法，IR 容得下它。
        // 补一个同名节点反而会撞名。
        var result = Map("""
            group ods "原始层"
              a "同步"
            end
            group dwd "明细层"
              b "明细"
            end
            ods -> dwd
            """);

        result.Document.Nodes.Select(n => n.Id).Should().Equal("a", "b");
        result.Document.Edges.Single().From.Should().Be("ods");
        result.Report.CreatedNodes.Should().BeEmpty();
    }

    // ---- 撞名 ----

    [Fact]
    [Trait("Category", "DslMapping")]
    public void A_group_that_collides_with_a_node_is_the_one_that_yields()
    {
        var result = Map("""
            lane pay "支付服务"
              payStart "发起支付"
            end
            pay "完成支付"
            payStart -> pay
            """);

        // 节点保留原名，容器改名，标签不动。
        result.Document.Nodes.Select(n => n.Id).Should().Contain("pay");
        result.Document.Composites.Single().Id.Should().Be("pay-lane");
        result.Document.Composites.Single().Label.Should().Be("支付服务");

        result.Report.Renames.Should().ContainSingle();
        result.Report.Renames.Single().OriginalId.Should().Be("pay");
        result.Report.Renames.Single().NewId.Should().Be("pay-lane");
    }

    [Fact]
    [Trait("Category", "DslMapping")]
    public void Renaming_a_container_keeps_its_members_and_parents_pointing_at_it()
    {
        // 改名要贯穿到父级与成员表，漏一处就是"某个成员指向了不存在的组合"，
        // 而那要等到校验器才报出来。
        var result = Map("""
            group outer "外层"
              lane pay "支付服务"
                a "甲"
              end
            end
            pay "完成支付"
            """);

        var lane = result.Document.Composites.Single(c => c.Label == "支付服务");

        lane.Id.Should().Be("pay-lane");
        lane.Members.Should().Equal("a");
        lane.Parent.Should().Be("outer");
        result.Document.Composites.Single(c => c.Id == "outer").Members.Should().Equal("pay-lane");
        result.Document.Nodes.Single(n => n.Id == "a").Parent.Should().Be("pay-lane");
    }

    [Fact]
    [Trait("Category", "DslMapping")]
    public void After_a_rename_an_edge_still_points_at_the_node()
    {
        // 一条边到底想连节点还是连容器，从文本上分不出来（两者同名）。
        // 节点优先，与 IR 的端点解析口径一致。改名记录把这件事写下来，
        // 于是"容器连不上了"是查得到的，不是猜的。
        var result = Map("""
            lane pay "支付服务"
            end
            pay "完成支付"
            a -> pay
            """);

        result.Document.Edges.Single().To.Should().Be("pay");
        result.Document.Nodes.Single(n => n.Id == "pay").Label.Should().Be("完成支付");
    }

    [Fact]
    [Trait("Category", "DslMapping")]
    public void A_rename_that_would_collide_again_gets_a_free_suffix()
    {
        var result = Map("""
            lane pay "甲泳道"
            end
            group pay-lane "乙分组"
            end
            pay "节点"
            """);

        result.Document.Composites.Select(c => c.Id).Should().Equal("pay-lane-2", "pay-lane");
    }

    [Fact]
    [Trait("Category", "DslMapping")]
    public void A_rename_is_recorded_rather_than_silent()
    {
        // 静默改名会让"为什么图上的标识和文本对不上"变成一个查不出来的问题。
        var result = Map("""
            group g "分组"
            end
            g "节点"
            """);

        result.Report.Renames.Should().ContainSingle();
        result.Report.Renames.Single().OriginalId.Should().Be("g");
        result.Report.Renames.Single().NewId.Should().Be("g-group");
        result.Report.Renames.Single().Reason.Should().NotBeNullOrWhiteSpace();
    }

    // ---- 布局提示 ----

    [Fact]
    [Trait("Category", "DslMapping")]
    public void Spacing_is_carried_over_and_the_other_one_falls_back()
    {
        var result = Map("node-spacing 12");

        result.Document.Layout.NodeSpacing.Should().Be(12);
        result.Document.Layout.LayerSpacing.Should().Be(LayoutHintsDefaults.LayerSpacing);
    }

    [Fact]
    [Trait("Category", "DslMapping")]
    public void No_spacing_declaration_gives_the_defaults()
    {
        var result = Map("a -> b");

        result.Document.Layout.NodeSpacing.Should().Be(LayoutHintsDefaults.NodeSpacing);
        result.Document.Layout.LayerSpacing.Should().Be(LayoutHintsDefaults.LayerSpacing);
    }

    // ---- 产物本身 ----

    [Fact]
    [Trait("Category", "DslMapping")]
    public void The_product_passes_the_validator()
    {
        var result = Map("""
            dsl 1
            direction LR
            start "开始" shape=stadium
            check "校验" shape=diamond ports=out:right
            start -> check
            check -> ok "是" line=dashed
            same-rank ok, bad
            """);

        DiagramValidator.Validate(result.Document).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "DslMapping")]
    public void The_product_starts_at_version_zero_with_hashes_filled_in()
    {
        var result = Map("a -> b");

        result.Document.Version.Should().Be(0);
        result.Document.StructuralHash.Should().NotBeEmpty();
        result.Document.VisualHash.Should().NotBeEmpty();
    }

    [Fact]
    [Trait("Category", "DslMapping")]
    public void The_document_id_comes_from_the_options()
    {
        var result = DslMapper.Map(DslParser.Parse("a -> b"), new MappingOptions { DocumentId = "订单流程" });

        result.Document.Id.Should().Be("订单流程");
    }

    [Fact]
    [Trait("Category", "DslMapping")]
    public void Kind_and_direction_carry_over()
    {
        var result = Map("kind flow\ndirection RL");

        result.Document.Kind.Should().Be(DiagramKind.Flow);
        result.Document.Direction.Should().Be(Direction.RL);
    }

    [Fact]
    [Trait("Category", "DslMapping")]
    public void Parse_diagnostics_are_carried_into_the_report()
    {
        // 解析没看懂的地方不能在这一层被吃掉：映射层不重新判语法，
        // 它只把语法层的结论带下去。
        var parsed = DslParser.Parse("a -> b\n这不是合法的声明 = = =");
        var result = DslMapper.Map(parsed, new MappingOptions { DocumentId = "dsl" });

        parsed.Diagnostics.Should().NotBeEmpty();
        result.Report.Diagnostics.Should().Equal(parsed.Diagnostics);
    }

    [Fact]
    [Trait("Category", "DslMapping")]
    public void An_empty_source_gives_an_empty_document()
    {
        var result = Map(string.Empty);

        result.Document.Nodes.Should().BeEmpty();
        result.Document.Edges.Should().BeEmpty();
        result.Document.Composites.Should().BeEmpty();
        DiagramValidator.Validate(result.Document).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "DslMapping")]
    public void Mapping_the_same_source_twice_gives_the_same_hashes()
    {
        // 映射要可复现：同一份文本两次映射必须给出同一对哈希，
        // 否则"打开两次同一份文件"会被判成内容变了。
        const string Source = """
            lane l "泳道"
              a "甲" shape=diamond
            end
            a "乙"
            a -> b "是"
            """;

        var first = Map(Source).Document;
        var second = Map(Source).Document;

        first.StructuralHash.Should().Be(second.StructuralHash);
        first.VisualHash.Should().Be(second.VisualHash);
    }

    // ---- 语料 ----

    [Fact]
    [Trait("Category", "DslMapping")]
    public void Every_corpus_answer_maps_to_a_document_the_validator_accepts()
    {
        // 这是 P1-17 的验收口径：校验器就是给这种不经过命令层的外部输入准备的。
        // 手写用例干净得不真实，五十份真实回答才是它要能吃下的东西。
        var failures = new List<string>();

        foreach (var (entry, parsed) in Corpus.Load().Select(entry => (entry, DslParser.Parse(entry.Content))))
        {
            var result = DslMapper.Map(parsed, new MappingOptions { DocumentId = $"{entry.Arm}-{entry.PromptId}" });
            var issues = DiagramValidator.Validate(result.Document);

            if (issues.Count > 0)
            {
                failures.Add($"{entry.Arm}/{entry.PromptId}: {string.Join("；", issues.Select(i => $"{i.Code} {i.Message}"))}");
            }
        }

        failures.Should().BeEmpty();
    }

    private static MappingResult Map(string source) =>
        DslMapper.Map(DslParser.Parse(source), new MappingOptions { DocumentId = "dsl" });
}
