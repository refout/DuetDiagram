using DuetDiagram.Core.Model;
using DuetDiagram.Mermaid.Export;
using DuetDiagram.Mermaid.Parsing;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Mermaid.Tests;

/// <summary>
/// IR 写成 Mermaid 文本。
/// </summary>
/// <remarks>
/// 断言分两类：一类是"写出来的文本读回来还是那份 IR"，一类是"写不出来的东西有没有报"。
/// 后者同样要紧——静默丢失会让用户以为导出的文件就是全部内容。
/// </remarks>
public sealed class ExporterTests
{
    #region 头部与稀疏写法

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void The_header_carries_the_direction()
    {
        Text(Document(direction: Direction.RL)).Should().StartWith("flowchart RL\n");
    }

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void A_rectangular_node_whose_label_equals_its_id_is_written_bare()
    {
        // 真实语料里大半的节点是这么写的，省掉方括号之后导出的文本更接近人写的样子。
        Text(Document(nodes: [new NodeDef { Id = "A", Label = "A" }])).Should().Be("flowchart TB\n\nA\n");
    }

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void A_label_different_from_the_id_gets_brackets()
    {
        Text(Document(nodes: [new NodeDef { Id = "A", Label = "开始" }])).Should().Contain("A[\"开始\"]");
    }

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void An_empty_label_is_written_out_rather_than_dropped()
    {
        // 空标签与"没写标签"是两回事：前者渲染出空方框，后者回退到标识。
        var text = Text(Document(nodes: [new NodeDef { Id = "A", Label = string.Empty }]));

        text.Should().Contain("A[\"\"]");
    }

    [Theory]
    [Trait("Category", "MermaidExport")]
    [InlineData(NodeShape.Rect, "A[\"甲\"]")]
    [InlineData(NodeShape.Rounded, "A(\"甲\")")]
    [InlineData(NodeShape.Stadium, "A([\"甲\"])")]
    [InlineData(NodeShape.Diamond, "A{\"甲\"}")]
    [InlineData(NodeShape.Circle, "A((\"甲\"))")]
    [InlineData(NodeShape.Hexagon, "A{{\"甲\"}}")]
    [InlineData(NodeShape.Cylinder, "A[(\"甲\")]")]
    public void Each_shape_gets_its_own_delimiters(NodeShape shape, string expected)
    {
        Text(Document(nodes: [new NodeDef { Id = "A", Label = "甲", Shape = shape }])).Should().Contain(expected);
    }

    #endregion

    #region 边

    [Theory]
    [Trait("Category", "MermaidExport")]
    [InlineData(LineStyle.Solid, ArrowStyle.Arrow, "A-->B")]
    [InlineData(LineStyle.Solid, ArrowStyle.None, "A---B")]
    [InlineData(LineStyle.Dotted, ArrowStyle.Arrow, "A-.->B")]
    [InlineData(LineStyle.Dotted, ArrowStyle.None, "A-.-B")]
    public void Each_line_and_arrow_pair_gets_its_token(LineStyle line, ArrowStyle arrow, string expected)
    {
        var document = Document(edges: [new EdgeDef { Id = "e1", From = "A", To = "B", Style = new EdgeStyle { Line = line, Arrow = arrow } }]);

        Text(document).Should().Contain(expected);
    }

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void An_edge_label_is_always_quoted()
    {
        // 裸标签里出现竖线就会把标签切开，而"什么时候会出问题"要去翻语法才说得清。
        // 统一加引号没有这个心智负担。
        var document = Document(edges: [new EdgeDef { Id = "e1", From = "A", To = "B", Label = "是" }]);

        Text(document).Should().Contain("A-->|\"是\"|B");
    }

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void An_edge_without_a_label_gets_no_pipe_section()
    {
        Text(Document(edges: [new EdgeDef { Id = "e1", From = "A", To = "B" }])).Should().NotContain("|");
    }

    #endregion

    #region 组合

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void A_subgraph_is_written_as_a_block_with_its_members()
    {
        var document = Document(
            nodes: [new NodeDef { Id = "A", Label = "A", Parent = "后端" }],
            composites: [new GroupDef { Id = "后端", Label = "后端", Members = ["A"] }]);

        Text(document).Should().Contain("subgraph 后端\n  A\nend");
    }

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void A_nested_subgraph_is_written_inside_the_outer_one()
    {
        var document = Document(
            nodes: [new NodeDef { Id = "A", Label = "A", Parent = "外" }, new NodeDef { Id = "B", Label = "B", Parent = "内" }],
            composites:
            [
                new GroupDef { Id = "外", Members = ["A", "内"] },
                new GroupDef { Id = "内", Parent = "外", Members = ["B"] },
            ]);

        var text = Text(document);

        text.Should().Contain("subgraph 外\n  A\n  subgraph 内\n    B\n  end\nend");
    }

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void A_subgraph_direction_is_written_out()
    {
        var document = Document(composites: [new GroupDef { Id = "G", Direction = Direction.LR, Members = [] }]);

        Text(document).Should().Contain("subgraph G\n  direction LR\nend");
    }

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void The_indent_is_configurable_and_does_not_change_the_shape()
    {
        // 缩进只影响可读性，不影响语义——子图边界由 end 决定。
        var document = Document(composites: [new GroupDef { Id = "G", Members = [] }]);
        var options = new ExportOptions { Indent = "\t" };

        var text = MermaidExporter.Export(document, options).Text;

        text.Should().Contain("subgraph G\nend").And.NotContain("\t");
    }

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void Membership_comes_from_the_member_list_not_from_the_parent_field()
    {
        // 两者不一致时按成员表算。从父级反推会在不一致时悄悄按另一份事实走，
        // 而"按哪一份"没有依据。
        var document = Document(
            nodes: [new NodeDef { Id = "A", Label = "A", Parent = "G" }],
            composites: [new GroupDef { Id = "G", Members = [] }]);

        Text(document).Should().Contain("subgraph G\nend").And.Contain("\n\nA\n");
    }

    #endregion

    #region 样式

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void Writable_style_fields_become_a_style_statement()
    {
        var document = Document(nodes:
        [
            new NodeDef
            {
                Id = "A",
                Label = "A",
                Style = new NodeStyle { Fill = "#f9f", Stroke = "#333", Text = "#fff", Weight = 2 },
            },
        ]);

        Text(document).Should().Contain("style A fill:#f9f,stroke:#333,color:#fff,stroke-width:2px");
    }

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void An_unwritable_style_field_is_reported()
    {
        var document = Document(nodes: [new NodeDef { Id = "A", Label = "A", Style = new NodeStyle { Fill = "#f9f", Radius = 6 } }]);

        var report = MermaidExporter.Export(document, new ExportOptions()).Report;

        Text(document).Should().Contain("style A fill:#f9f");
        report.Dropped.Should().ContainSingle(d => d.Feature == "样式字段").Which.Ids.Should().Equal("A");
    }

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void A_subgraph_style_is_written_the_same_way()
    {
        var document = Document(composites: [new GroupDef { Id = "G", Members = [], Style = new NodeStyle { Fill = "#eee" } }]);

        Text(document).Should().Contain("style G fill:#eee");
    }

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void A_broken_ownership_chain_does_not_lose_elements()
    {
        // 组合的父级指向一个不存在的组合，而某个节点只在那张成员表里。
        // 按"父级为空才算顶层"去遍历的话，这个组合一个都不写，那个节点也跟着消失——
        // 而"少了一个节点"极难归因到导出这一层。
        // 导出可以产出有问题的文本，但不能产出少了东西的文本。
        var document = Document(
            nodes: [new NodeDef { Id = "A", Label = "A", Parent = "X" }],
            composites: [new GroupDef { Id = "X", Label = "X", Parent = "查无此组", Members = ["A"] }]);

        var text = Text(document);

        text.Should().Contain("subgraph X");
        text.Should().Contain("  A");
    }

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void A_subgraph_referenced_twice_is_written_once()
    {
        // 同一个组合同时出现在两张成员表里时，写两遍会让它读回来变成两块。
        var document = Document(composites:
        [
            new GroupDef { Id = "外", Members = ["内"] },
            new GroupDef { Id = "内", Parent = "外", Members = [] },
        ]);

        Text(document).Split("subgraph 内").Should().HaveCount(2);
    }

    #endregion

    #region 写不出来的东西

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void An_unwritable_id_is_renamed_and_the_references_follow()
    {
        // Mermaid 的标识只能是标识字符，没有"带引号的标识"这种写法。
        // 换名要贯穿到引用处，漏一处就是断掉的图。
        var document = Document(
            nodes: [new NodeDef { Id = "订单 流程", Label = "下单" }, new NodeDef { Id = "B", Label = "B" }],
            edges: [new EdgeDef { Id = "e1", From = "订单 流程", To = "B" }]);

        var result = MermaidExporter.Export(document, new ExportOptions());

        result.Text.Should().Contain("订单_流程[\"下单\"]").And.Contain("订单_流程-->B");
        result.Report.Dropped.Should().ContainSingle(d => d.Feature == "标识");
    }

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void A_dashed_edge_falls_back_to_solid_and_is_reported()
    {
        // Mermaid 没有虚线。退回实线而不是报错：退回之后图还是对的，只是线型变了，
        // 而报错会让整份导出失败——那比线型不对严重得多。
        var document = Document(edges: [new EdgeDef { Id = "e1", From = "A", To = "B", Style = new EdgeStyle { Line = LineStyle.Dashed } }]);

        var result = MermaidExporter.Export(document, new ExportOptions());

        result.Text.Should().Contain("A-->B");
        result.Report.Dropped.Should().ContainSingle(d => d.Feature == "边线型");
    }

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void An_unwritable_arrow_falls_back_and_is_reported()
    {
        var document = Document(edges: [new EdgeDef { Id = "e1", From = "A", To = "B", Style = new EdgeStyle { Arrow = ArrowStyle.Circle } }]);

        var result = MermaidExporter.Export(document, new ExportOptions());

        result.Text.Should().Contain("A-->B");
        result.Report.Dropped.Should().ContainSingle(d => d.Feature == "边箭头");
    }

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void An_unwritable_shape_falls_back_to_rect_and_is_reported()
    {
        var document = Document(nodes: [new NodeDef { Id = "A", Label = "甲", Shape = NodeShape.Parallelogram }]);

        var result = MermaidExporter.Export(document, new ExportOptions());

        result.Text.Should().Contain("A[\"甲\"]");
        result.Report.Dropped.Should().ContainSingle(d => d.Feature == "节点形状");
    }

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void Document_level_constructs_are_reported()
    {
        // 静默丢失会让用户以为导出的文件就是全部内容，而实际不是。
        var document = DiagramDocument.CreateFromContent(
            "doc",
            nodes: [new NodeDef { Id = "A", Label = "A", Layer = "fore" }],
            pages: [new PageDef { Id = "p1" }],
            tags: [new TagDef { Id = "t1" }],
            layout: new LayoutHints { NodeSpacing = 99, SameRank = [Constraint(ConstraintOwner.Llm)] });

        var report = MermaidExporter.Export(document, new ExportOptions()).Report;

        report.Dropped.Select(d => d.Feature).Should().Contain(
            ["页面", "标签", "布局约束", "间距设置", "节点所属图层"]);
    }

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void A_plain_document_reports_nothing_dropped()
    {
        // 报告为空不等于无损，只等于没有东西落进已知的丢失清单。
        // 一份纯结构的文档必须落在"没丢"这一侧，否则清单就是过报的。
        var document = Document(
            nodes: [new NodeDef { Id = "A", Label = "甲", Shape = NodeShape.Diamond }],
            edges: [new EdgeDef { Id = "e1", From = "A", To = "A", Label = "自环" }]);

        MermaidExporter.Export(document, new ExportOptions()).Report.Dropped.Should().BeEmpty();
    }

    #endregion

    #region 确定性与行尾

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void Exporting_twice_gives_the_same_bytes()
    {
        // 导出必须确定性：集合顺序固定，不得依赖哈希迭代顺序。
        var document = Document(
            nodes: [new NodeDef { Id = "B" }, new NodeDef { Id = "A" }, new NodeDef { Id = "C" }],
            edges: [new EdgeDef { Id = "e2", From = "B", To = "C" }, new EdgeDef { Id = "e1", From = "A", To = "B" }]);

        var first = MermaidExporter.Export(document, new ExportOptions()).Text;
        var second = MermaidExporter.Export(document, new ExportOptions()).Text;

        first.Should().Be(second);
    }

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void Line_endings_are_always_line_feed()
    {
        // 跟着平台走的话，同一份文档在两台机器上导出会得到不同的文件，
        // 而"逐字节相同"这条判据在跨平台时就失效了。
        Text(Document(nodes: [new NodeDef { Id = "A" }])).Should().NotContain("\r");
    }

    #endregion

    [Fact]
    [Trait("Category", "MermaidExport")]
    public void The_exported_text_parses_back_without_diagnostics()
    {
        var document = Document(
            nodes: [new NodeDef { Id = "A", Label = "甲", Shape = NodeShape.Stadium }],
            edges: [new EdgeDef { Id = "e1", From = "A", To = "B", Label = "去" }]);

        MermaidParser.Parse(Text(document)).Diagnostics.Should().BeEmpty();
    }

    private static Constraint<SameRankConstraint> Constraint(ConstraintOwner owner) =>
        new(new SameRankConstraint(["A", "B"]), owner, DateTimeOffset.UnixEpoch);

    private static DiagramDocument Document(
        Direction direction = Direction.TB,
        IReadOnlyList<NodeDef>? nodes = null,
        IReadOnlyList<EdgeDef>? edges = null,
        IReadOnlyList<CompositeDef>? composites = null) =>
        DiagramDocument.CreateFromContent(
            "doc",
            DiagramKind.Flowchart,
            direction,
            nodes: nodes,
            edges: edges,
            composites: composites);

    private static string Text(DiagramDocument document) =>
        MermaidExporter.Export(document, new ExportOptions()).Text;
}
