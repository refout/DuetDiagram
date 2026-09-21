using DuetDiagram.Core.Model;
using DuetDiagram.Mermaid.Parsing;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Mermaid.Tests;

/// <summary>
/// Mermaid 流程图的语法分析。
/// </summary>
public sealed class ParserTests
{
    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void A_header_carries_the_direction()
    {
        MermaidParser.Parse("flowchart LR\nA --> B").Direction.Should().Be(Direction.LR);
        MermaidParser.Parse("graph BT\nA --> B").Direction.Should().Be(Direction.BT);
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void Td_and_tb_both_mean_top_to_bottom()
    {
        // Mermaid 里两种写法同义，而 IR 只保留一个。
        // 保留两种会让同一份图两次解析出不同的结果。
        MermaidParser.Parse("flowchart TD\nA").Direction.Should().Be(Direction.TB);
        MermaidParser.Parse("flowchart TB\nA").Direction.Should().Be(Direction.TB);
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void A_missing_direction_falls_back_to_top_to_bottom()
    {
        var chart = MermaidParser.Parse("flowchart\nA --> B");

        chart.Direction.Should().Be(Direction.TB);
        chart.Nodes.Should().HaveCount(2);
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void Nodes_appear_in_first_seen_order()
    {
        // 顺序有意义：它是层内次序的依据。用集合并掉顺序会让同一份输入两次解析出不同的图。
        var chart = MermaidParser.Parse("flowchart TD\nB --> A\nC --> B");

        chart.Nodes.Select(n => n.Id).Should().Equal("B", "A", "C");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void A_node_declaration_carries_label_and_shape()
    {
        var chart = MermaidParser.Parse("flowchart TD\nA([开始])");

        chart.Nodes.Should().ContainSingle();
        chart.Nodes[0].Id.Should().Be("A");
        chart.Nodes[0].Label.Should().Be("开始");
        chart.Nodes[0].Shape.Should().Be(NodeShape.Stadium);
    }

    [Theory]
    [Trait("Category", "MermaidParsing")]
    [InlineData("A[文本]", NodeShape.Rect)]
    [InlineData("A(文本)", NodeShape.Rounded)]
    [InlineData("A([文本])", NodeShape.Stadium)]
    [InlineData("A((文本))", NodeShape.Circle)]
    [InlineData("A{文本}", NodeShape.Diamond)]
    [InlineData("A[(文本)]", NodeShape.Cylinder)]
    [InlineData("A{{文本}}", NodeShape.Hexagon)]
    public void Shape_delimiters_map_to_ir_shapes(string declaration, NodeShape expected)
    {
        MermaidParser.Parse($"flowchart TD\n{declaration}").Nodes.Single().Shape.Should().Be(expected);
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void A_later_declaration_fills_in_the_label()
    {
        // 先连起来、后补说明是常见写法。反过来做会丢掉文本。
        var chart = MermaidParser.Parse("flowchart TD\nA --> B\nA[开始]");

        chart.Nodes.Single(n => n.Id == "A").Label.Should().Be("开始");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void Links_are_produced_for_a_simple_edge()
    {
        var chart = MermaidParser.Parse("flowchart TD\nA --> B");

        chart.Links.Should().ContainSingle();
        chart.Links[0].From.Should().Be("A");
        chart.Links[0].To.Should().Be("B");
        chart.Links[0].Arrow.Should().Be(ArrowStyle.Arrow);
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void A_chain_produces_one_link_per_step()
    {
        var chart = MermaidParser.Parse("flowchart TD\nA --> B --> C");

        chart.Links.Should().HaveCount(2);
        chart.Links.Select(l => (l.From, l.To)).Should().Equal(("A", "B"), ("B", "C"));
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void An_ampersand_fans_out_to_every_target()
    {
        var chart = MermaidParser.Parse("flowchart TD\nA & B --> C");

        chart.Links.Should().HaveCount(2);
        chart.Links.Select(l => l.From).Should().Equal("A", "B");
        chart.Links.Should().OnlyContain(l => l.To == "C");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void An_edge_label_between_pipes_is_read()
    {
        var chart = MermaidParser.Parse("flowchart TD\nA -->|是| B");

        chart.Links.Single().Label.Should().Be("是");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void An_edge_label_between_links_is_read()
    {
        // A -- 是 --> B 这种写法把标签夹在两条线型之间。
        var chart = MermaidParser.Parse("flowchart TD\nA -- 是 --> B");

        chart.Links.Single().Label.Should().Be("是");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void A_quoted_edge_label_between_pipes_loses_its_quotes()
    {
        // 引号是定界符，不是标签的一部分——含逗号、括号这类会打断语法的字符时才必须加。
        // Mermaid 自己是这么做的：它的词法在遇到引号时切进字符串状态，
        // 引号之间的内容单独作为 STR 返回，而和它们之间的引号被丢弃。
        // 不剥的话图上会显示两个多余的引号，而那种偏差只有肉眼能发现。
        var chart = MermaidParser.Parse("flowchart TD\nA -->|\"已处理\"| B");

        chart.Links.Single().Label.Should().Be("已处理");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void A_quoted_edge_label_between_links_loses_its_quotes()
    {
        var chart = MermaidParser.Parse("flowchart TD\nA -- \"已处理\" --> B");

        chart.Links.Single().Label.Should().Be("已处理");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void Quotes_inside_a_quoted_edge_label_are_kept()
    {
        // 只剥最外层那一对。里面的引号是内容，剥掉就改了用户的文字。
        var chart = MermaidParser.Parse("flowchart TD\nA -->|\"他说\"\"你好\"\"\"| B");

        chart.Links.Single().Label.Should().Be("他说\"\"你好\"\"");
    }

    [Theory]
    [Trait("Category", "MermaidParsing")]
    [InlineData("A --> B", ArrowStyle.Arrow, LineStyle.Solid)]
    [InlineData("A --- B", ArrowStyle.None, LineStyle.Solid)]
    [InlineData("A -.-> B", ArrowStyle.Arrow, LineStyle.Dotted)]
    [InlineData("A -.- B", ArrowStyle.None, LineStyle.Dotted)]
    public void Link_styles_map_to_ir_styles(string source, ArrowStyle arrow, LineStyle line)
    {
        var link = MermaidParser.Parse($"flowchart TD\n{source}").Links.Single();

        link.Arrow.Should().Be(arrow);
        link.Line.Should().Be(line);
    }

    #region 子图

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void A_subgraph_collects_its_members()
    {
        var chart = MermaidParser.Parse("""
            flowchart TD
            subgraph 后端
                A --> B
            end
            C --> A
            """);

        var subgraph = chart.Subgraphs.Should().ContainSingle().Subject;

        subgraph.Id.Should().Be("后端");
        subgraph.Label.Should().Be("后端");
        subgraph.Members.Should().Equal("A", "B");

        // 子图外的节点不属于它。
        chart.Nodes.Single(n => n.Id == "C").Parent.Should().BeNull();
        chart.Nodes.Single(n => n.Id == "A").Parent.Should().Be("后端");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void A_subgraph_can_carry_its_own_direction()
    {
        var chart = MermaidParser.Parse("""
            flowchart TD
            subgraph 后端
                direction LR
                A --> B
            end
            """);

        chart.Subgraphs.Single().Direction.Should().Be(Direction.LR);
        chart.Direction.Should().Be(Direction.TB, "外层方向不受影响");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void Nested_subgraphs_assign_the_innermost_parent()
    {
        var chart = MermaidParser.Parse("""
            flowchart TD
            subgraph 外层
                subgraph 内层
                    A
                end
                B
            end
            """);

        chart.Nodes.Single(n => n.Id == "A").Parent.Should().Be("内层");
        chart.Nodes.Single(n => n.Id == "B").Parent.Should().Be("外层");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void A_subgraph_without_a_name_gets_a_generated_id()
    {
        var chart = MermaidParser.Parse("flowchart TD\nsubgraph\nA\nend");

        chart.Subgraphs.Single().Id.Should().Be("subgraph1");
        chart.Diagnostics.Should().Contain(d => d.Message.Contains("没有标识"));
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void An_unmatched_end_is_reported_not_fatal()
    {
        var chart = MermaidParser.Parse("flowchart TD\nend\nA --> B");

        chart.Diagnostics.Should().Contain(d => d.Message.Contains("没有对应"));
        chart.Links.Should().ContainSingle("后面的内容照常解析");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void A_node_declared_before_the_subgraph_still_joins_it()
    {
        // 「先画全局连线，再用 subgraph 划分层次」是真实语料里最常见的写法之一。
        // 子图里那次只是引用、不带标签，但它才是归属的唯一依据。
        var chart = MermaidParser.Parse("""
            flowchart TD
            A[接入] --> B
            subgraph L1[接入层]
                A
            end
            """);

        chart.Nodes.Single(n => n.Id == "A").Parent.Should().Be("L1");
        chart.Subgraphs.Single().Members.Should().Equal("A");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void A_bare_mention_outside_does_not_evict_a_node_from_its_subgraph()
    {
        // 反向的写法：节点在子图里，之后在子图外被再次引用。
        // 不带标签的引用不足以把它搬走，否则归属会随引用次序漂移。
        var chart = MermaidParser.Parse("""
            flowchart TD
            subgraph L1
                A
            end
            A --> B
            """);

        chart.Nodes.Single(n => n.Id == "A").Parent.Should().Be("L1");
        chart.Subgraphs.Single().Members.Should().Equal("A");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void An_explicit_declaration_moves_a_node_to_its_real_subgraph()
    {
        // 共享节点被多个子图引用，最后在自己的归属处被正式声明（带标签）。
        // 只有带标签的声明才有这个分量，否则同一份输入会随引用次序解析出不同结果。
        var chart = MermaidParser.Parse("""
            flowchart TD
            subgraph W1
                X --> R
            end
            subgraph W2
                Y --> R
            end
            subgraph S
                R["统一回滚"]
            end
            """);

        chart.Nodes.Single(n => n.Id == "R").Parent.Should().Be("S");
        chart.Nodes.Single(n => n.Id == "R").Label.Should().Be("统一回滚");

        // 只归一个子图，不是三个都算。
        chart.Subgraphs.Single(s => s.Id == "W1").Members.Should().Equal("X");
        chart.Subgraphs.Single(s => s.Id == "W2").Members.Should().Equal("Y");
        chart.Subgraphs.Single(s => s.Id == "S").Members.Should().Equal("R");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void Subgraph_membership_always_agrees_with_node_parents()
    {
        // 成员表是从节点父级推出来的，两者不可能分叉。
        // 曾经它们是各自登记的两份记录，真实语料里有六处对不上。
        var chart = MermaidParser.Parse("""
            flowchart TD
            A --> B
            subgraph L1
                A
            end
            subgraph L2
                B
            end
            """);

        foreach (var subgraph in chart.Subgraphs)
        {
            var expected = chart.Nodes
                .Where(n => n.Parent == subgraph.Id)
                .Select(n => n.Id);

            subgraph.Members.Should().Equal(expected);
        }
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void A_nested_subgraph_knows_its_outer_subgraph()
    {
        // 分层架构图外面再套一个总览框，是真实语料里的写法。
        // 丢掉这一层会让内层子图在外层里凭空消失。
        var chart = MermaidParser.Parse("""
            flowchart TD
            subgraph 外层
                subgraph 内层
                    A
                end
            end
            """);

        chart.Subgraphs.Single(s => s.Id == "内层").Parent.Should().Be("外层");
        chart.Subgraphs.Single(s => s.Id == "外层").Parent.Should().BeNull();

        // 内层是外层的成员子图，不是外层的成员节点。
        chart.Subgraphs.Single(s => s.Id == "外层").Members.Should().BeEmpty();
        chart.Subgraphs.Single(s => s.Id == "内层").Members.Should().Equal("A");
    }

    #endregion

    #region 样式与类

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void A_style_directive_is_recorded()
    {
        var chart = MermaidParser.Parse("flowchart TD\nA\nstyle A fill:#f9f,stroke:#333");

        chart.Styles.Should().ContainSingle();
        chart.Styles[0].Target.Should().Be("A");
        chart.Styles[0].Properties.Should().Be("fill:#f9f,stroke:#333");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void A_class_definition_and_its_assignment_are_recorded()
    {
        var chart = MermaidParser.Parse("""
            flowchart TD
            A
            classDef danger fill:#fcc
            class A danger
            """);

        chart.Classes.Should().HaveCount(2);
        chart.Classes[0].Name.Should().Be("danger");
        chart.Classes[0].Properties.Should().Be("fill:#fcc");
        chart.Classes[1].Name.Should().Be("danger");
        chart.Classes[1].Targets.Should().Equal("A");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void A_class_assignment_list_is_split_on_commas()
    {
        var chart = MermaidParser.Parse("flowchart TD\nA\nB\nC\nclass A,B,C done");

        chart.Classes.Single().Targets.Should().Equal("A", "B", "C");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void An_inline_class_shorthand_is_recorded()
    {
        var chart = MermaidParser.Parse("flowchart TD\nA:::danger");

        chart.Classes.Single().Targets.Should().Equal("A");
        chart.Classes.Single().Name.Should().Be("danger");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void An_unsupported_directive_is_reported_as_skipped()
    {
        // 记诊断而不是静默丢弃：静默丢弃会让人以为它生效了。
        var chart = MermaidParser.Parse("flowchart TD\nA\nclick A \"https://example.com\"");

        chart.Diagnostics.Should().Contain(d => d.Message.Contains("click"));
        chart.Nodes.Should().ContainSingle("其余内容照常解析");
    }

    #endregion

    #region 宽松

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void Comments_and_blank_lines_are_ignored()
    {
        var chart = MermaidParser.Parse("flowchart TD\n\n%% 说明\nA --> B\n");

        chart.Nodes.Should().HaveCount(2);
        chart.IsClean.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void A_bad_line_produces_a_diagnostic_without_killing_the_rest()
    {
        // 把整份判死会让用户手里一份九成正确的图变成零。
        var chart = MermaidParser.Parse("flowchart TD\nA --> B\n@ 乱七八糟\nC --> D");

        chart.Diagnostics.Should().NotBeEmpty();
        chart.Links.Should().HaveCount(2, "坏掉的那一行前后的内容都要保住");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void Diagnostics_carry_positions()
    {
        var chart = MermaidParser.Parse("flowchart TD\nA --> B\n@");

        var diagnostic = chart.Diagnostics.Should().ContainSingle().Subject;

        diagnostic.Line.Should().Be(3);
        diagnostic.Column.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void A_non_flowchart_is_refused_with_a_named_reason()
    {
        // 硬解析出来的是垃圾，而垃圾比空结果更糟——它看起来像是导入成功了。
        var chart = MermaidParser.Parse("sequenceDiagram\nA->>B: hi");

        chart.Nodes.Should().BeEmpty();
        chart.Diagnostics.Should().ContainSingle()
            .Which.Message.Should().Contain("Sequence");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void A_state_diagram_is_refused_as_not_yet_supported()
    {
        // 状态图在 IR 里有对应类型，只是语法还没做。措辞要与"表达不了"区分开：
        // 前者等一等就好，后者得换个工具。
        var chart = MermaidParser.Parse("stateDiagram-v2\n[*] --> A");

        chart.Diagnostics.Should().ContainSingle()
            .Which.Message.Should().Contain("还没做");
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void An_empty_source_produces_an_empty_chart()
    {
        var chart = MermaidParser.Parse(string.Empty);

        chart.Nodes.Should().BeEmpty();
        chart.Diagnostics.Should().ContainSingle();
    }

    [Fact]
    [Trait("Category", "MermaidParsing")]
    public void A_fenced_source_is_parsed_the_same_as_a_bare_one()
    {
        var fenced = MermaidParser.Parse("```mermaid\nflowchart TD\nA --> B\n```");
        var bare = MermaidParser.Parse("flowchart TD\nA --> B");

        fenced.Nodes.Should().BeEquivalentTo(bare.Nodes);
        fenced.Links.Should().BeEquivalentTo(bare.Links);
    }

    #endregion
}
