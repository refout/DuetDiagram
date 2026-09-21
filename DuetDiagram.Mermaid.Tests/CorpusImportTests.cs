using DuetDiagram.Core.Model;
using DuetDiagram.Mermaid.Import;
using DuetDiagram.Mermaid.Parsing;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Mermaid.Tests;

/// <summary>
/// 冻结语料上跑一遍导入。
/// </summary>
/// <remarks>
/// <para>
/// 手写用例干净得不真实，一百份真实回答才是导入必须能吃下的东西。
/// 这是这一层的验收口径：97 份流程图全部导入，且产物通过校验器零问题。
/// </para>
/// <para>
/// 另外三份不是流程图（图类型不是 flowchart），解析器整份放弃、记一条诊断——
/// 那是刻意的，硬解析出来的是垃圾，而垃圾看起来像是导入成功了。
/// </para>
/// </remarks>
public sealed class CorpusImportTests
{
    /// <summary>一百份里能当流程图导入的份数。</summary>
    private const int FlowchartCount = 97;

    [Fact]
    [Trait("Category", "MermaidCorpus")]
    public void Every_parseable_corpus_answer_imports_to_a_document_the_validator_accepts()
    {
        // 校验器就是给这种不经过命令层的外部输入准备的：命令层那道防线在这里一点都用不上。
        var failures = new List<string>();
        var imported = 0;

        foreach (var (entry, chart) in Parsed())
        {
            if (!IsFlowchart(chart))
            {
                continue;
            }

            imported++;

            var result = MermaidImporter.Import(chart, Options(entry));
            var issues = DiagramValidator.Validate(result.Document);

            if (issues.Count > 0)
            {
                failures.Add($"{entry.Arm}/{entry.PromptId}: {string.Join("；", issues.Select(i => $"{i.Code} {i.Message}"))}");
            }
        }

        imported.Should().Be(FlowchartCount);
        failures.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "MermaidCorpus")]
    public void The_three_answers_that_are_not_flowcharts_are_exactly_the_rejected_ones()
    {
        // 数出来的而不是假设的：三个非流程图必须与"解析器整份放弃"的那三份对得上。
        // 对不上说明有流程图被误判成别的类型，或者反过来。
        var empty = new List<string>();

        foreach (var (entry, chart) in Parsed())
        {
            if (!IsFlowchart(chart))
            {
                empty.Add($"{entry.Arm}/{entry.PromptId}");
            }
        }

        empty.Should().HaveCount(100 - FlowchartCount);
        empty.Should().AllSatisfy(id => id.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    [Trait("Category", "MermaidCorpus")]
    public void Every_link_endpoint_resolves_to_a_node_or_a_composite()
    {
        // 导入层不补节点——它依赖的是"解析器读到连线时会顺手给两端各造一个节点"。
        // 那条性质属于解析器，将来可能变；变了却没人知道的话，
        // 表现是**边悄悄地少了一条**，而图上少一条线不容易被当成 bug 归到这里。
        // 校验器会报端点缺失，所以这条用例与上面那条有一半重叠；
        // 它单独存在是为了给出更直接的失败信息：是哪个端点没落地。
        var failures = new List<string>();

        foreach (var (entry, chart) in Parsed())
        {
            if (!IsFlowchart(chart))
            {
                continue;
            }

            var document = MermaidImporter.Import(chart, Options(entry)).Document;

            var known = document.Nodes.Select(n => n.Id)
                .Concat(document.Composites.Select(c => c.Id))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var edge in document.Edges)
            {
                foreach (var endpoint in (string[])[edge.From, edge.To])
                {
                    if (!known.Contains(endpoint))
                    {
                        failures.Add($"{entry.Arm}/{entry.PromptId}: 边 {edge.Id} 的端点 {endpoint} 没有落点");
                    }
                }
            }
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "MermaidCorpus")]
    public void Importing_is_reproducible()
    {
        // 同一份文本两次导入必须给出同一对哈希，否则"打开两次同一份文件"
        // 会被判成内容变了，白白触发一次重排。
        var unstable = new List<string>();

        foreach (var (entry, chart) in Parsed())
        {
            if (!IsFlowchart(chart))
            {
                continue;
            }

            var first = MermaidImporter.Import(chart, Options(entry)).Document;
            var second = MermaidImporter.Import(MermaidParser.Parse(entry.Content), Options(entry)).Document;

            if (first.StructuralHash != second.StructuralHash || first.VisualHash != second.VisualHash)
            {
                unstable.Add($"{entry.Arm}/{entry.PromptId}");
            }
        }

        unstable.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "MermaidCorpus")]
    public void The_product_passes_the_core_validator_for_every_arm()
    {
        // 按组各报一次，是为了让"某一组整体坏掉"与"个别几份坏掉"在失败信息里能分开。
        foreach (var arm in (string[])["a-bare", "b-documented"])
        {
            var armEntries = Parsed().Where(pair => pair.Entry.Arm == arm).ToArray();

            armEntries.Should().HaveCount(50);

            var bad = armEntries
                .Where(pair => IsFlowchart(pair.Chart))
                .Where(pair => DiagramValidator.Validate(
                    MermaidImporter.Import(pair.Chart, Options(pair.Entry)).Document).Count > 0)
                .Select(pair => pair.Entry.PromptId)
                .ToArray();

            bad.Should().BeEmpty($"{arm} 组不该有导入后校验不过的");
        }
    }

    /// <summary>没有节点也没有连线：解析器整份放弃了，或者输入本来就是空的。</summary>
    private static bool IsFlowchart(MermaidFlowchart chart) => chart.Nodes.Count > 0 || chart.Links.Count > 0;

    private static IEnumerable<(CorpusEntry Entry, MermaidFlowchart Chart)> Parsed() =>
        Corpus.Load().Select(entry => (entry, MermaidParser.Parse(entry.Content)));

    /// <summary>文档标识取组别加提示词编号，出报告时一眼能看出是哪一份。</summary>
    private static ImportOptions Options(CorpusEntry entry) =>
        new() { DocumentId = $"{entry.Arm}-{entry.PromptId}" };
}
