using DuetDiagram.Mermaid.Lexing;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Mermaid.Tests;

/// <summary>
/// Mermaid 的词法分析。
/// </summary>
public sealed class LexerTests
{
    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void A_header_produces_a_word_and_a_direction()
    {
        Words("flowchart TD").Should().Equal("flowchart", "TD");
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void Graph_is_accepted_as_an_alias_for_flowchart()
    {
        // 两种写法都在真实输出里出现，词法层不区分，交给语法层。
        Words("graph LR").Should().Equal("graph", "LR");
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void Arrows_are_recognized_in_all_documented_forms()
    {
        AssertArrow("A --> B", MermaidArrowKind.Arrow);
        AssertArrow("A --- B", MermaidArrowKind.Open);
        AssertArrow("A -.-> B", MermaidArrowKind.Dotted);
        AssertArrow("A -.- B", MermaidArrowKind.DottedOpen);
        AssertArrow("A ==> B", MermaidArrowKind.Thick);
        AssertArrow("A === B", MermaidArrowKind.ThickOpen);
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void A_hyphenated_identifier_is_not_split_at_the_hyphen()
    {
        // 这是词法层最容易写错的一处：连字符既是连线的一部分，
        // 又是标识的合法字符。按「遇到连字符就切」会把 my-node 切碎。
        Words("my-node-1 --> B").Should().Equal("my-node-1", "B");
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void An_identifier_followed_immediately_by_an_arrow_splits_correctly()
    {
        Words("a-->b").Should().Equal("a", "b");

        MermaidLexer.Tokenize("a-->b")
            .Should().ContainSingle(t => t.Kind == MermaidTokenKind.Arrow);
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void Nodes_separated_by_spaces_are_distinct_words()
    {
        // Mermaid 允许一条语句里连着写多个节点，节点之间没有分隔符。
        Words("A --> B --> C").Should().Equal("A", "B", "C");
    }

    // ---- 形状与标签 ----

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void A_shape_label_is_one_opaque_token()
    {
        // 定界符之内是自由文本。标签里可以出现几乎任何字符，
        // 按标识去切的话它们全会变成"不认识的东西"。
        var tokens = MermaidLexer.Tokenize("A[开始，校验（第一步）?]");

        tokens.Should().Contain(t => t.Kind == MermaidTokenKind.ShapeOpen && t.Text == "[");
        tokens.Should().Contain(t => t.Kind == MermaidTokenKind.ShapeClose && t.Text == "]");
        tokens.Should().ContainSingle(t => t.Kind == MermaidTokenKind.Text)
            .Which.Text.Should().Be("开始，校验（第一步）?");
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void Markup_inside_a_label_does_not_produce_unknown_characters()
    {
        // <br/> 是真实输出里最常见的标签内容。尖括号与斜杠在这里只是普通字符。
        var tokens = MermaidLexer.Tokenize("A[第一行<br/>第二行]");

        tokens.Should().NotContain(t => t.Kind == MermaidTokenKind.Unknown);
        tokens.Single(t => t.Kind == MermaidTokenKind.Text).Text.Should().Be("第一行<br/>第二行");
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void Style_values_do_not_produce_unknown_characters()
    {
        // style 行的尾巴是一段属性列表而不是若干标识：十六进制颜色带井号，
        // 属性之间用逗号分隔。整段收成一个文本记号。
        var tokens = MermaidLexer.Tokenize("style A fill:#f9f,stroke:#333,stroke-width:4px");

        tokens.Should().NotContain(t => t.Kind == MermaidTokenKind.Unknown);
        tokens.First(t => t.Kind == MermaidTokenKind.Word).Text.Should().Be("style");
        tokens.Single(t => t.Kind == MermaidTokenKind.Text).Text.Should().Be("A fill:#f9f,stroke:#333,stroke-width:4px");
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void A_class_directive_keeps_its_comma_separated_list_in_one_token()
    {
        // class LB,GW ingress 是一条指令，逗号是节点之间的分隔，
        // 不是"不认识的字符"。
        var tokens = MermaidLexer.Tokenize("class LB,GW ingress");

        tokens.Should().NotContain(t => t.Kind == MermaidTokenKind.Unknown);
        tokens.Single(t => t.Kind == MermaidTokenKind.Text).Text.Should().Be("LB,GW ingress");
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void An_invisible_link_is_recognized()
    {
        // ~~~ 是 Mermaid 的隐形连线：只表示层级关系，不画出来。
        var tokens = MermaidLexer.Tokenize("L1 ~~~ L2");

        tokens.Should().ContainSingle(t => t.Kind == MermaidTokenKind.Arrow);
        tokens.Should().NotContain(t => t.Kind == MermaidTokenKind.Unknown);
    }

    [Theory]
    [Trait("Category", "MermaidLexing")]
    [InlineData("A <-.-> B")]
    [InlineData("A <--> B")]
    [InlineData("A <==> B")]
    public void Left_pointing_links_are_recognized(string source)
    {
        // 左向写法只是在连线前面多一个尖括号。统一按前缀处理，
        // 比把每一种都列进连线表可靠——后者漏掉一个就变成"不认识的字符"。
        //
        // 裸的 `<-` 不在此列：单个连字符不能当连线，否则 my-node-1 这样的标识
        // 会在第一个连字符处被切碎。它本来也不是 Mermaid 的合法写法。
        var tokens = MermaidLexer.Tokenize(source);

        tokens.Should().Contain(t => t.Kind == MermaidTokenKind.Arrow);
        tokens.Should().NotContain(t => t.Kind == MermaidTokenKind.Unknown);
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void Shape_delimiters_come_out_as_open_and_close_tokens()
    {
        AssertShape("A[矩形]", "[", "]");
        AssertShape("A(圆角)", "(", ")");
        AssertShape("A([胶囊])", "([", "])");
        AssertShape("A[[子程序]]", "[[", "]]");
        AssertShape("A[(数据库)]", "[(", ")]");
        AssertShape("A((圆形))", "((", "))");
        AssertShape("A{判定}", "{", "}");
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void A_shape_without_a_closing_delimiter_takes_the_rest_of_the_line()
    {
        // 少一个括号是模型输出里的常见错误。就地放弃会让整行白解析，
        // 而它其余部分往往是好的。
        var tokens = MermaidLexer.Tokenize("A[没有收尾\nB --> C");

        tokens.Single(t => t.Kind == MermaidTokenKind.Text).Text.Should().Be("没有收尾");
        tokens.Should().NotContain(t => t.Kind == MermaidTokenKind.ShapeClose);

        // 下一行照常解析。
        Words("A[没有收尾\nB --> C").Should().Contain("C");
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void Quoted_label_keeps_its_quotes_for_the_parser_to_strip()
    {
        // 词法层原样收下，剥引号是语法层的事——它才知道这个位置的引号是不是可选的。
        var tokens = MermaidLexer.Tokenize("A[\"开始\"]");

        tokens.Single(t => t.Kind == MermaidTokenKind.Text).Text.Should().Be("\"开始\"");
    }

    // ---- 边标签 ----

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void An_edge_label_between_pipes_is_a_text_token()
    {
        var tokens = MermaidLexer.Tokenize("A -->|是| B");

        tokens.Count(t => t.Kind == MermaidTokenKind.Pipe).Should().Be(2);
        tokens.Single(t => t.Kind == MermaidTokenKind.Text).Text.Should().Be("是");
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void An_edge_label_without_a_closing_pipe_takes_the_rest_of_the_line()
    {
        var tokens = MermaidLexer.Tokenize("A -->|没有收尾 B");

        tokens.Single(t => t.Kind == MermaidTokenKind.Text).Text.Should().Be("没有收尾 B");
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void An_edge_label_between_dashes_is_kept_as_words()
    {
        // A -- 是 --> B 这种写法把标签夹在两条连线记号之间。
        Words("A -- 是 --> B").Should().Equal("A", "是", "B");
    }

    // ---- 其它 ----

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void Comments_run_to_the_end_of_the_line()
    {
        var tokens = MermaidLexer.Tokenize("A --> B %% 这是注释\nC --> D");

        tokens.Single(t => t.Kind == MermaidTokenKind.Comment).Text.Should().Be("%% 这是注释");
        Words("A --> B %% 这是注释\nC --> D").Should().Equal("A", "B", "C", "D");
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void Newlines_are_significant_tokens()
    {
        MermaidLexer.Tokenize("A --> B\nC --> D")
            .Count(t => t.Kind == MermaidTokenKind.NewLine).Should().Be(1);
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void Carriage_returns_are_normalized_to_one_newline()
    {
        MermaidLexer.Tokenize("A\r\nB")
            .Count(t => t.Kind == MermaidTokenKind.NewLine).Should().Be(1);
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void An_unknown_character_becomes_a_token_not_an_exception()
    {
        var tokens = MermaidLexer.Tokenize("A --> B @ C");

        tokens.Should().Contain(t => t.Kind == MermaidTokenKind.Unknown && t.Text == "@");
        tokens.Should().Contain(t => t.Kind == MermaidTokenKind.End);
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void Position_information_points_at_the_right_place()
    {
        var tokens = MermaidLexer.Tokenize("flowchart TD\nA --> B");

        var a = tokens.First(t => t.Kind == MermaidTokenKind.Word && t.Text == "A");
        var b = tokens.First(t => t.Kind == MermaidTokenKind.Word && t.Text == "B");

        a.Line.Should().Be(2);
        a.Column.Should().Be(1);
        b.Line.Should().Be(2);
        b.Column.Should().Be(7);
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void Ampersands_and_semicolons_are_their_own_tokens()
    {
        var tokens = MermaidLexer.Tokenize("A & B --> C; D --> E");

        tokens.Should().Contain(t => t.Kind == MermaidTokenKind.Ampersand);
        tokens.Should().Contain(t => t.Kind == MermaidTokenKind.Semicolon);
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void An_empty_input_produces_only_the_end_token()
    {
        MermaidLexer.Tokenize(string.Empty).Should().ContainSingle()
            .Which.Kind.Should().Be(MermaidTokenKind.End);
    }

    // ---- 图类型 ----

    [Theory]
    [Trait("Category", "MermaidLexing")]
    [InlineData("flowchart TD", MermaidDiagramKind.Flowchart)]
    [InlineData("graph LR", MermaidDiagramKind.Flowchart)]
    [InlineData("stateDiagram-v2", MermaidDiagramKind.State)]
    [InlineData("stateDiagram", MermaidDiagramKind.State)]
    [InlineData("sequenceDiagram", MermaidDiagramKind.Sequence)]
    [InlineData("classDiagram", MermaidDiagramKind.Class)]
    [InlineData("erDiagram", MermaidDiagramKind.EntityRelationship)]
    [InlineData("gantt", MermaidDiagramKind.Other)]
    public void Diagram_kinds_are_recognized(string source, MermaidDiagramKind expected)
    {
        MermaidDiagramKindDetector.Detect(source).Should().Be(expected);
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void Comments_and_blank_lines_before_the_header_are_skipped()
    {
        MermaidDiagramKindDetector.Detect("%% 说明\n\nflowchart LR\nA --> B")
            .Should().Be(MermaidDiagramKind.Flowchart);
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void An_unrecognized_header_is_unknown()
    {
        MermaidDiagramKindDetector.Detect("随便写点什么").Should().Be(MermaidDiagramKind.Unknown);
        MermaidDiagramKindDetector.Detect(string.Empty).Should().Be(MermaidDiagramKind.Unknown);
    }

    // ---- 剥围栏 ----

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void A_fenced_block_is_unwrapped()
    {
        // 冻结语料里一百份 Mermaid 回答有八十五份带围栏，
        // 而剩下十五份没带——同一批提示词。不剥的话那八十五份会在第一个记号处就失败。
        MermaidSource.Extract("```mermaid\nflowchart TD\n    A --> B\n```")
            .Should().Be("flowchart TD\n    A --> B");
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void Unfenced_text_is_returned_as_is()
    {
        MermaidSource.Extract("  flowchart TD\n A --> B  ").Should().Be("flowchart TD\n A --> B");
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void Only_the_first_block_is_taken()
    {
        // 多块属于调用方没想清楚要解析哪一块。与其猜，不如取第一个并让它自己决定要不要拆。
        MermaidSource.Extract("```mermaid\nfirst\n```\n\n```mermaid\nsecond\n```").Should().Be("first");
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void An_unterminated_fence_takes_the_rest()
    {
        MermaidSource.Extract("```mermaid\nflowchart TD\nA --> B").Should().Be("flowchart TD\nA --> B");
    }

    [Fact]
    [Trait("Category", "MermaidLexing")]
    public void A_bare_fence_marker_yields_an_empty_source()
    {
        MermaidSource.Extract("```").Should().BeEmpty();
    }

    private static void AssertArrow(string source, MermaidArrowKind expected)
    {
        MermaidLexer.Tokenize(source).Single(t => t.Kind == MermaidTokenKind.Arrow)
            .Arrow.Should().Be(expected);
    }

    private static void AssertShape(string source, string open, string close)
    {
        var tokens = MermaidLexer.Tokenize(source);

        tokens.Should().Contain(t => t.Kind == MermaidTokenKind.ShapeOpen && t.Text == open);
        tokens.Should().Contain(t => t.Kind == MermaidTokenKind.ShapeClose && t.Text == close);
    }

    /// <summary>取出全部有内容的记号，按出现顺序。断言里只看内容更清楚。</summary>
    private static IEnumerable<string> Words(string source) =>
        MermaidLexer.Tokenize(source)
            .Where(t => t.Kind is MermaidTokenKind.Word or MermaidTokenKind.Text or MermaidTokenKind.QuotedText)
            .Select(t => t.Text);
}
