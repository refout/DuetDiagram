using DuetDiagram.Dsl.Lexing;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Dsl.Tests;

/// <summary>
/// DSL 的词法分析，以及剥围栏那一步。
/// </summary>
/// <remarks>
/// 剥围栏与词法分析放在同一组测，因为它俩的分界就是"处理一段源码"与"从包装里取出源码"，
/// 而两者的失败表现都是"第一个记号就不认识"，分开放会让人查错地方。
/// </remarks>
public sealed class LexerTests
{
    [Fact]
    [Trait("Category", "DslLexing")]
    public void A_node_line_becomes_words_an_equals_and_a_quoted_text()
    {
        Tokens("start \"开始\" shape=stadium")
            .Should().Equal("Word(start)", "QuotedText(开始)", "Word(shape)", "Equals(=)", "Word(stadium)");
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void Newline_is_a_token_not_whitespace()
    {
        // 行式语法的语句边界全靠它。把换行当空白吞掉的话，
        // 「一行里塞了两句」与「一句写错了」这两种情况将无法区分。
        DslLexer.Tokenize("a\nb")
            .Select(t => t.Kind)
            .Should().Equal(DslTokenKind.Word, DslTokenKind.NewLine, DslTokenKind.Word, DslTokenKind.End);
    }

    [Theory]
    [InlineData("a\r\nb")]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    [Trait("Category", "DslLexing")]
    public void All_three_line_endings_produce_exactly_one_newline(string source)
    {
        // \r\n 是 Windows 上的常态，而模型偶尔只给 \r。
        // 两种都当一次换行——产出两个会让每个空行都多出一条语句边界。
        DslLexer.Tokenize(source).Count(t => t.Kind == DslTokenKind.NewLine).Should().Be(1);
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void A_comment_swallows_the_rest_of_the_line()
    {
        Tokens("a # 这是注释 \"带引号也没关系\"")
            .Should().Equal("Word(a)", "Comment(# 这是注释 \"带引号也没关系\")");
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void A_hash_inside_quoted_text_is_not_a_comment()
    {
        Tokens("a \"价格 #1\"")
            .Should().Equal("Word(a)", "QuotedText(价格 #1)");
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void Quoted_text_decodes_the_three_supported_escapes()
    {
        // 只认 \" \\ \n 三种。换行必须支持，因为标签里的换行是常见需求——
        // Mermaid 语料里 <br/> 是最常见的写法，说明模型确实需要多行标签。
        Tokens("a \"说 \\\"好\\\" 并\\n换行\\\\结束\"")
            .Should().Equal("Word(a)", "QuotedText(说 \"好\" 并\n换行\\结束)");
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void An_unknown_escape_keeps_its_backslash_verbatim()
    {
        // 不报错也不猜测：\t 原样保留成反斜杠加 t。
        // 猜的话，一个写错的转义会静默变成别的东西，而用户看不出发生了什么。
        Tokens("a \"x\\ty\"").Should().Equal("Word(a)", "QuotedText(x\\ty)");
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void An_unterminated_string_becomes_unknown_and_keeps_its_opening_quote()
    {
        // 保留开引号是为了让语法层能说出"字符串没收尾"，
        // 而不是笼统的"有个不认识的字符"。
        var token = DslLexer.Tokenize("a \"abc").Should().ContainSingle(t => t.Kind == DslTokenKind.Unknown).Subject;

        token.Text.Should().Be("\"abc");
        token.Column.Should().Be(3);
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void A_quoted_string_does_not_cross_a_line()
    {
        // 跨行的话，"哪一行坏了"就失去唯一答案。
        var tokens = DslLexer.Tokenize("a \"未收尾\nb");

        tokens.Should().ContainSingle(t => t.Kind == DslTokenKind.Unknown);
        tokens.Should().Contain(t => t.Kind == DslTokenKind.NewLine);
        tokens.Should().Contain(t => t.Kind == DslTokenKind.Word && t.Text == "b");
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void An_arrow_and_an_open_line_are_told_apart()
    {
        DslLexer.Tokenize("a -> b").Should().ContainSingle(t => t.IsArrow).Which.Text.Should().Be("->");
        DslLexer.Tokenize("a -- b").Should().ContainSingle(t => t.IsOpenLine).Which.Text.Should().Be("--");
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void A_hyphenated_identifier_is_not_split_at_the_hyphen()
    {
        // 词法层最容易写错的一处：连字符既是连线的一部分，又是标识的合法字符。
        // 按「遇到连字符就切」会把 my-node-1 切碎。
        Tokens("my-node-1 -> B").Should().Equal("Word(my-node-1)", "Arrow(->)", "Word(B)");
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void A_hyphenated_keyword_is_one_word()
    {
        // same-rank / node-spacing 这些关键字本身带连字符。
        // 切碎的话语法层永远匹配不上它们。
        Tokens("same-rank a, b").Should().Equal("Word(same-rank)", "Word(a)", "Comma(,)", "Word(b)");
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void An_identifier_immediately_followed_by_an_arrow_splits_correctly()
    {
        Tokens("a->b").Should().Equal("Word(a)", "Arrow(->)", "Word(b)");
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void A_negative_number_is_a_word_not_an_arrow()
    {
        // pin 的坐标可以是负数。判定顺序不能换——先认连线再认负号。
        Tokens("pin a at -10, 20")
            .Should().Equal("Word(pin)", "Word(a)", "Word(at)", "Word(-10)", "Comma(,)", "Word(20)");
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void A_lone_hyphen_is_unknown()
    {
        DslLexer.Tokenize("a - b").Should().ContainSingle(t => t.Kind == DslTokenKind.Unknown).Which.Text.Should().Be("-");
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void The_punctuation_set_is_recognized()
    {
        DslLexer.Tokenize("a.b, c: d=e")
            .Select(t => t.Kind)
            .Should().Equal(
                DslTokenKind.Word, DslTokenKind.Dot, DslTokenKind.Word,
                DslTokenKind.Comma, DslTokenKind.Word,
                DslTokenKind.Colon, DslTokenKind.Word,
                DslTokenKind.Equals, DslTokenKind.Word,
                DslTokenKind.End);
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void An_unrecognized_character_is_a_token_not_an_exception()
    {
        // 与 Mermaid 侧同一口径。抛异常的话，一份输入里某几行写坏了，
        // 其余部分本来是好的一并拿不到。
        DslLexer.Tokenize("a % b").Should().ContainSingle(t => t.Kind == DslTokenKind.Unknown).Which.Text.Should().Be("%");
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void Positions_are_one_based_and_survive_across_lines()
    {
        var tokens = DslLexer.Tokenize("a\n  b");

        tokens[0].Should().Match<DslToken>(t => t.Line == 1 && t.Column == 1);
        tokens[2].Should().Match<DslToken>(t => t.Text == "b" && t.Line == 2 && t.Column == 3);
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void Tokenizing_the_same_source_twice_gives_the_same_result()
    {
        const string Source = "dsl 1\ngroup a \"甲\"\n  n \"文本\" shape=diamond\nend\nn -> m \"边\"\n";

        DslLexer.Tokenize(Source).Should().Equal(DslLexer.Tokenize(Source));
    }

    #region 剥围栏

    [Fact]
    [Trait("Category", "DslLexing")]
    public void Text_without_a_fence_is_returned_trimmed()
    {
        DslSource.Extract("\n  a -> b\n\n").Should().Be("a -> b");
    }

    [Theory]
    [InlineData("dsl")]
    [InlineData("flow")]
    [InlineData("text")]
    [InlineData("")]
    [Trait("Category", "DslLexing")]
    public void A_fence_block_is_unwrapped_regardless_of_its_language_tag(string tag)
    {
        // 真实语料里的标签五花八门。按标签匹配会漏掉一半，
        // 所以只能整行跳过。
        DslSource.Extract($"```{tag}\na -> b\n```").Should().Be("a -> b");
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void Prose_before_the_fence_is_discarded()
    {
        DslSource.Extract("下面是流程图：\n\n```dsl\na -> b\n```\n\n希望有帮助。").Should().Be("a -> b");
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void An_unterminated_fence_takes_everything_to_the_end()
    {
        DslSource.Extract("```dsl\na -> b").Should().Be("a -> b");
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void A_bare_fence_marker_yields_an_empty_source()
    {
        DslSource.Extract("```").Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "DslLexing")]
    public void Extracting_twice_is_the_same_as_extracting_once()
    {
        // 解析器内部会剥一次围栏，而调用方可能已经剥过了。
        // 不是恒等操作的话，调用方的"好意"反而会破坏内容。
        const string Fenced = "```dsl\na -> b\n```";

        var once = DslSource.Extract(Fenced);

        DslSource.Extract(once).Should().Be(once);
    }

    private static IEnumerable<string> Tokens(string source) =>
        DslLexer.Tokenize(source)
            .Where(t => t.Kind is not (DslTokenKind.NewLine or DslTokenKind.End))
            .Select(t => $"{t.Kind}({t.Text})");

    #endregion
}
