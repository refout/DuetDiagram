using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Mermaid.Import;
using DuetDiagram.Mermaid.Parsing;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Mermaid.Tests;

/// <summary>
/// Mermaid 语法树到 IR 的映射。
/// </summary>
/// <remarks>
/// 断言分两类：一类是"字段搬对了没有"，一类是"消歧规则是不是照定的那样"。
/// 后者更要紧——分组不因被引用而变成节点、成员表要含嵌套子图，这两条错了图还是画得出来，
/// 只是画错。
/// </remarks>
public sealed class ImporterTests
{
    #region 节点

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void Node_fields_carry_over()
    {
        var result = Import("flowchart LR\nA[开始]\nB{判定}\nC((圆))");

        result.Document.Nodes.Select(n => (n.Id, n.Label, n.Shape)).Should().Equal(
            ("A", "开始", NodeShape.Rect),
            ("B", "判定", NodeShape.Diamond),
            ("C", "圆", NodeShape.Circle));

        result.Document.Direction.Should().Be(Direction.LR);
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void A_node_without_a_label_falls_back_to_its_id()
    {
        // 解析器如实产出"没写显示文本"，回退是这一层的事。
        // IR 里的 label 要拿去渲染，不能是空串。
        Import("flowchart TD\nA").Document.Nodes.Single().Label.Should().Be("A");
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void Node_order_is_the_first_appearance_order()
    {
        // 顺序不是装饰：节点在集合里的位置是层内次序的依据。
        Import("flowchart TD\nC --> A\nB").Document.Nodes.Select(n => n.Id).Should().Equal("C", "A", "B");
    }

    #endregion

    #region 边

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void Link_fields_carry_over()
    {
        var result = Import("flowchart TD\nA -->|是| B");

        var edge = result.Document.Edges.Single();

        edge.Id.Should().Be("e1");
        edge.From.Should().Be("A");
        edge.To.Should().Be("B");
        edge.Label.Should().Be("是");
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void Default_line_and_arrow_are_left_unset()
    {
        // 显式写一个与缺省相同的值不增加信息，却会让"同一张图的两种文本"产出不同的 IR。
        var edge = Import("flowchart TD\nA --> B").Document.Edges.Single();

        edge.Style.Line.Should().BeNull();
        edge.Style.Arrow.Should().BeNull();
        edge.Line.Should().Be(LineStyle.Solid);
        edge.Arrow.Should().Be(ArrowStyle.Arrow);
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void Non_default_line_and_arrow_are_written_out()
    {
        var edge = Import("flowchart TD\nA -.-> B").Document.Edges.Single();

        edge.Style.Line.Should().Be(LineStyle.Dotted);
        edge.Style.Arrow.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void Links_get_generated_ids_that_dodge_the_node_ids()
    {
        // Mermaid 的连线没有标识，而 IR 的边必须有——不然命令层引用不到、sidecar 也存不了。
        var result = Import("flowchart TD\ne1[e1]\nA --> B\nB --> C");

        result.Document.Edges.Select(e => e.Id).Should().Equal("e2", "e3");
    }

    #endregion

    #region 子图

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void A_subgraph_becomes_a_group_with_its_members()
    {
        var result = Import("""
            flowchart TD
            subgraph 后端
              A[接口]
              B[数据库]
            end
            """);

        var group = result.Document.Composites.Single();

        group.Should().BeOfType<GroupDef>();
        group.Label.Should().Be("后端");
        group.Members.Should().Equal("A", "B");
        result.Document.Nodes.Select(n => n.Parent).Should().AllBe("后端");
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void A_nested_subgraph_is_a_member_of_the_outer_one()
    {
        // 语法树的成员表里**只有节点**，嵌套子图不在其中——子图的父级能反向指认出来，
        // 但成员表里没有。而 IR 的约定是"成员里既有节点也有子组合"，
        // 从这里照抄会让外层组合的成员表与父级字段对不上。
        var result = Import("""
            flowchart TD
            subgraph 外
              A[甲]
              subgraph 内
                B[乙]
              end
            end
            """);

        result.Document.Composites.Single(c => c.Label == "外").Members.Should().Equal("A", "内");
        result.Document.Composites.Single(c => c.Label == "内").Members.Should().Equal("B");
        result.Document.Composites.Single(c => c.Label == "内").Parent.Should().Be("外");
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void A_subgraph_direction_is_kept()
    {
        var result = Import("""
            flowchart TD
            subgraph 后端
              direction LR
              A[甲]
            end
            """);

        result.Document.Composites.Single().Direction.Should().Be(Direction.LR);
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void A_node_that_shares_a_subgraph_id_is_dropped()
    {
        // 解析器读到连线时会顺手给两端各造一个节点，于是 `subgraph ODS` 之后写
        // `ODS --> DWD` 会让同一个名字既在子图表里又在节点表里。
        // 丢掉节点那一份：Mermaid 渲染的就是两个框之间的连线，
        // 而且节点与组合共用命名空间，两份都留会撞名。
        var result = Import("""
            flowchart TD
            subgraph ODS[原始层]
              A1[同步]
            end
            subgraph DWD[明细层]
              B1[明细]
            end
            ODS --> DWD
            """);

        result.Document.Nodes.Select(n => n.Id).Should().Equal("A1", "B1");
        result.Document.Edges.Single().From.Should().Be("ODS");
        result.Document.Edges.Single().To.Should().Be("DWD");

        result.Report.DroppedNodes.Select(d => d.Id).Should().Equal("ODS", "DWD");
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void Dropping_a_node_is_recorded_rather_than_silent()
    {
        // 用户看到的是"我明明写了 ODS 这个节点怎么不见了"，
        // 而答案（它被当成子图了）只有报告能回答。
        var result = Import("""
            flowchart TD
            subgraph ODS[原始层]
            end
            ODS --> B
            """);

        var dropped = result.Report.DroppedNodes.Single();

        dropped.Id.Should().Be("ODS");
        dropped.SubgraphId.Should().Be("ODS");
    }

    #endregion

    #region 样式与类

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void A_style_statement_lands_on_the_node()
    {
        var result = Import("flowchart TD\nA[甲]\nstyle A fill:#f9f,stroke:#333,stroke-width:2px");

        var style = result.Document.Nodes.Single().Style;

        style.Should().NotBeNull();
        style!.Fill.Should().Be("#f9f");
        style.Stroke.Should().Be("#333");
        style.Weight.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void Color_maps_to_the_text_colour()
    {
        var result = Import("flowchart TD\nA[甲]\nstyle A color:#fff");

        result.Document.Nodes.Single().Style!.Text.Should().Be("#fff");
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void A_style_statement_on_a_subgraph_lands_on_the_composite()
    {
        var result = Import("""
            flowchart TD
            subgraph 外
              A[甲]
            end
            style 外 fill:#eee
            """);

        result.Document.Composites.Single().Style!.Fill.Should().Be("#eee");
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void A_class_definition_applies_through_the_class_statement()
    {
        var result = Import("""
            flowchart TD
            A[甲]
            B[乙]
            classDef 强调 fill:#FFF3CD,stroke:#B8860B
            class A,B 强调
            """);

        result.Document.Nodes.Select(n => n.Style!.Fill).Should().AllBe("#FFF3CD");
        result.Document.Nodes.Select(n => n.Style!.Stroke).Should().AllBe("#B8860B");
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void A_class_used_before_its_definition_still_applies()
    {
        // Mermaid 要求类先定义后套用，但两趟做没有代价，
        // 而只有一趟时"先套用后定义"会**静默丢掉样式**——那种丢失在图上几乎看不出来。
        var result = Import("""
            flowchart TD
            A[甲]
            class A 强调
            classDef 强调 fill:#FFF3CD
            """);

        result.Document.Nodes.Single().Style!.Fill.Should().Be("#FFF3CD");
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void A_node_style_beats_the_class_it_was_given()
    {
        var result = Import("""
            flowchart TD
            A[甲]
            classDef 强调 fill:#FFF3CD
            class A 强调
            style A fill:#000
            """);

        result.Document.Nodes.Single().Style!.Fill.Should().Be("#000");
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void An_unknown_style_property_is_reported()
    {
        // 丢样式比丢节点隐蔽得多：图还是画得出来，只是少了一处颜色，
        // 而"少了一处"在图上几乎看不出来。
        var result = Import("flowchart TD\nA[甲]\nstyle A fill:#f9f,stroke-dasharray:5 5");

        result.Document.Nodes.Single().Style!.Fill.Should().Be("#f9f");
        result.Report.IgnoredStyleProperties.Should().ContainSingle()
            .Which.Property.Should().Contain("stroke-dasharray");
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void A_style_with_nothing_recognisable_does_not_attach_an_empty_style()
    {
        // 全是空字段的样式会被算进视觉哈希，于是"没有样式"与"空样式"变成两回事。
        var result = Import("flowchart TD\nA[甲]\nstyle A nope:1");

        result.Document.Nodes.Single().Style.Should().BeNull();
        result.Report.IgnoredStyleProperties.Should().ContainSingle();
    }

    #endregion

    #region 产物本身

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void The_product_passes_the_validator()
    {
        var result = Import("""
            flowchart LR
            subgraph 后端
              A[接口]
              B[(库)]
            end
            subgraph 前端
              C[页面]
            end
            C -->|请求| A
            A --> B
            后端 --> 前端
            classDef 强调 fill:#FFF3CD
            class C 强调
            """);

        DiagramValidator.Validate(result.Document).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void The_product_starts_at_version_zero_with_hashes_filled_in()
    {
        var result = Import("flowchart TD\nA --> B");

        result.Document.Version.Should().Be(0);
        result.Document.StructuralHash.Should().NotBeEmpty();
        result.Document.VisualHash.Should().NotBeEmpty();
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void The_document_id_comes_from_the_options()
    {
        var result = MermaidImporter.Import(
            MermaidParser.Parse("flowchart TD\nA"),
            new ImportOptions { DocumentId = "订单流程" });

        result.Document.Id.Should().Be("订单流程");
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void Kind_is_the_flowchart_kind()
    {
        // Mermaid 的解析器只在图类型不是流程图时整份放弃，所以进来的一定是流程图。
        Import("flowchart TD\nA").Document.Kind.Should().Be(DiagramKind.Flowchart);
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void Parse_diagnostics_are_carried_into_the_report()
    {
        // 认不出来的内容不能在这一层被吃掉：解析器已经记了诊断，导入层只把它带下去。
        var chart = MermaidParser.Parse("flowchart TD\nA --> B\nlinkStyle 0 stroke:#f00");
        var result = MermaidImporter.Import(chart, new ImportOptions { DocumentId = "m" });

        chart.Diagnostics.Should().NotBeEmpty();
        result.Report.Diagnostics.Should().Equal(chart.Diagnostics);
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void An_empty_chart_gives_an_empty_document()
    {
        var result = Import(string.Empty);

        result.Document.Nodes.Should().BeEmpty();
        result.Document.Edges.Should().BeEmpty();
        result.Document.Composites.Should().BeEmpty();
        DiagramValidator.Validate(result.Document).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "MermaidImport")]
    public void Importing_the_same_source_twice_gives_the_same_hashes()
    {
        const string Source = """
            flowchart TD
            subgraph 外
              A[甲]
            end
            A --> B
            style A fill:#f9f
            """;

        var first = Import(Source).Document;
        var second = Import(Source).Document;

        first.StructuralHash.Should().Be(second.StructuralHash);
        first.VisualHash.Should().Be(second.VisualHash);
    }

    #endregion

    private static ImportResult Import(string source) =>
        MermaidImporter.Import(MermaidParser.Parse(source), new ImportOptions { DocumentId = "mermaid" });
}
