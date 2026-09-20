using DuetDiagram.Mermaid.Lexing;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Mermaid.Tests;

/// <summary>
/// 拿冻结语料跑词法分析。
/// </summary>
/// <remarks>
/// <para>
/// 这一组用例的价值在于**它是数据驱动的**：语料是模型在真实提示词下产出的响应，
/// 而不是我挑出来给解析器过的例子。挑例子的话，人会不自觉地挑好解析的，
/// 于是测试全绿而真实输入照样失败。
/// </para>
/// <para>
/// 词法层在这里只验一件事：**没有不认识的字符**。结构是否正确由语法层的用例负责。
/// 分成两步报错，是为了让失败一眼能定位到哪一层。
/// </para>
/// </remarks>
public sealed class CorpusLexingTests
{
    [Fact]
    [Trait("Category", "MermaidCorpus")]
    public void The_corpus_is_present_and_complete()
    {
        // 先确认语料本身到位。它缺席时后面每一条都会失败得莫名其妙。
        var entries = Corpus.Load();

        entries.Should().HaveCount(100, "两个 Mermaid 组各五十条");
        entries.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.Content));
    }

    [Fact]
    [Trait("Category", "MermaidCorpus")]
    public void The_corpus_composition_is_pinned()
    {
        // 把语料实际由什么构成固定下来。它决定了词法与语法要覆盖的范围，
        // 而"范围"这件事必须来自数据而不是猜测。
        var kinds = Corpus.Load()
            .GroupBy(e => MermaidDiagramKindDetector.Detect(MermaidSource.Extract(e.Content)))
            .ToDictionary(g => g.Key, g => g.Count());

        kinds[MermaidDiagramKind.Flowchart].Should().Be(97);

        // 这两条是模型面对状态机提示词时的合理选择，不是错误。
        kinds[MermaidDiagramKind.State].Should().Be(2);

        // 这一条是模型面对「下单全链路」选了时序图。属表达不了的类别。
        kinds[MermaidDiagramKind.Sequence].Should().Be(1);
    }

    [Fact]
    [Trait("Category", "MermaidCorpus")]
    public void Every_flowchart_response_lexes_without_unrecognized_characters()
    {
        var checkedCount = 0;
        var characters = new SortedSet<string>(StringComparer.Ordinal);
        var files = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var entry in Corpus.Load())
        {
            var source = MermaidSource.Extract(entry.Content);

            // 非流程图的语法不同，它们的词法覆盖由各自的用例负责。
            if (MermaidDiagramKindDetector.Detect(source) != MermaidDiagramKind.Flowchart)
            {
                continue;
            }

            checkedCount++;

            var unknown = MermaidLexer.Tokenize(source)
                .Where(t => t.Kind == MermaidTokenKind.Unknown)
                .ToArray();

            if (unknown.Length == 0)
            {
                continue;
            }

            files.Add($"{entry.Arm}/{entry.PromptId}");

            foreach (var token in unknown)
            {
                characters.Add(token.Text);
            }
        }

        checkedCount.Should().Be(97, "先确认确实跑了这么多条，否则空转也能通过");

        // 报出全部不认识的字符而不是第一个：修一个跑一次太慢，
        // 而且字符集本身就是要补进词法表的东西。
        characters.Should().BeEmpty(
            $"词法层应当能吃下全部真实流程图输出，不认识的字符：{string.Join("、", characters)}"
            + $"（出现在 {string.Join("、", files)}）");
    }

    [Fact]
    [Trait("Category", "MermaidCorpus")]
    public void Every_flowchart_response_starts_with_a_recognizable_header()
    {
        // 剥围栏之后第一行必须是一个可认的图头。认不出来说明剥错了，
        // 而剥错的表现是"解析在第一个记号就失败"，与被测的东西毫无关系。
        var offenders = new List<string>();

        foreach (var entry in Corpus.Load())
        {
            var source = MermaidSource.Extract(entry.Content);
            var kind = MermaidDiagramKindDetector.Detect(source);

            if (kind == MermaidDiagramKind.Flowchart)
            {
                continue;
            }

            if (kind == MermaidDiagramKind.Unknown)
            {
                offenders.Add($"{entry.Arm}/{entry.PromptId}: 认不出图类型");
            }
        }

        offenders.Should().BeEmpty("每条都应当能被认出属于某个图类型");
    }

    [Fact]
    [Trait("Category", "MermaidCorpus")]
    public void Non_flowchart_kinds_are_representable_or_explicitly_not()
    {
        // 状态图在 IR 里有对应类型，只是语法解析还没做；
        // 时序图连类型都没有。两者对调用方的含义不同：前者等一等就好，后者得换个工具。
        MermaidDiagramKindDetector.IsRepresentable(MermaidDiagramKind.Flowchart).Should().BeTrue();
        MermaidDiagramKindDetector.IsRepresentable(MermaidDiagramKind.State).Should().BeTrue();
        MermaidDiagramKindDetector.IsRepresentable(MermaidDiagramKind.Sequence).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "MermaidCorpus")]
    public void The_constructs_the_corpus_uses_are_the_ones_the_lexer_knows()
    {
        // 把语料实际用到的构造列出来，作为词法与语法范围的数据依据。
        // 这一条不判对错，只把事实固定下来——范围变了应当能从这里看出来。
        var entries = Corpus.Load();
        var withFences = entries.Count(e => e.Content.Contains("```", StringComparison.Ordinal));

        var shapes = new Dictionary<string, int>(StringComparer.Ordinal);
        var arrows = new Dictionary<MermaidArrowKind, int>();

        foreach (var entry in entries)
        {
            var source = MermaidSource.Extract(entry.Content);

            if (MermaidDiagramKindDetector.Detect(source) != MermaidDiagramKind.Flowchart)
            {
                continue;
            }

            var tokens = MermaidLexer.Tokenize(source);

            foreach (var shape in tokens.Where(t => t.IsShapeOpen).Select(t => t.Text).Distinct(StringComparer.Ordinal))
            {
                shapes[shape] = shapes.GetValueOrDefault(shape) + 1;
            }

            foreach (var arrow in tokens.Where(t => t.Arrow is not null).Select(t => t.Arrow!.Value).Distinct())
            {
                arrows[arrow] = arrows.GetValueOrDefault(arrow) + 1;
            }
        }

        // 这几条是实测得到的下限，写死下来防止范围悄悄缩水。
        withFences.Should().BeGreaterThan(50, "多数回答带代码围栏，剥围栏是必需的前置步骤");
        shapes.Should().ContainKey("[", "矩形是最常用的形状");
        shapes.Should().ContainKey("{", "判定节点用菱形");
        shapes.Should().ContainKey("([", "起止节点用胶囊形");
        arrows.Should().ContainKey(MermaidArrowKind.Arrow);
    }
}
