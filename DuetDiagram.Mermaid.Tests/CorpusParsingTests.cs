using DuetDiagram.Mermaid.Lexing;
using DuetDiagram.Mermaid.Parsing;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Mermaid.Tests;

/// <summary>
/// 拿冻结语料跑语法分析。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="CorpusLexingTests"/> 同一套语料、同一个理由：数据驱动。
/// 词法层只验「没有不认识的字符」，结构是否正确由这一组负责。
/// 分成两步是为了让失败一眼能定位到哪一层。
/// </para>
/// <para>
/// 这一组还负责钉住一条**不变量**：子图的成员表与节点的父级必须互相一致。
/// 两者曾经是各记各的两份记录，在真实语料里有六处对不上——而两份记录分叉之后，
/// 每一份单看都自洽，测试全绿，直到有人真的去用它。
/// </para>
/// </remarks>
public sealed class CorpusParsingTests
{
    /// <summary>语料里唯一的已知诊断。</summary>
    /// <remarks>
    /// <c>click</c> 是交互指令，IR 里有对应位置但尚未接线，解析器如实报出「已跳过」。
    /// 报出来而不是静默丢弃，是因为静默丢弃会让人以为它生效了。
    /// </remarks>
    private const string KnownSkippedDirective = "暂不支持";

    private static IEnumerable<(CorpusEntry Entry, MermaidFlowchart Chart)> Flowcharts()
    {
        foreach (var entry in Corpus.Load())
        {
            if (MermaidDiagramKindDetector.Detect(MermaidSource.Extract(entry.Content))
                != MermaidDiagramKind.Flowchart)
            {
                continue;
            }

            yield return (entry, MermaidParser.Parse(entry.Content));
        }
    }

    [Fact]
    [Trait("Category", "MermaidCorpus")]
    public void Every_flowchart_response_parses()
    {
        var checkedCount = 0;
        var offenders = new List<string>();

        foreach (var (entry, chart) in Flowcharts())
        {
            checkedCount++;

            // 唯一允许的诊断是「明确说了不支持、已跳过」这一类。
            // 其他任何诊断都意味着解析器没吃下真实输入。
            var unexpected = chart.Diagnostics
                .Where(d => !d.Message.Contains(KnownSkippedDirective, StringComparison.Ordinal))
                .Select(d => $"第 {d.Line} 行第 {d.Column} 列：{d.Message}")
                .ToArray();

            if (unexpected.Length > 0)
            {
                offenders.Add($"{entry.Arm}/{entry.PromptId}: {string.Join("；", unexpected)}");
            }
        }

        checkedCount.Should().Be(97, "先确认确实跑了这么多条，否则空转也能通过");

        offenders.Should().BeEmpty(
            $"全部真实流程图都应当解析干净。有问题的：{string.Join(" | ", offenders)}");
    }

    [Fact]
    [Trait("Category", "MermaidCorpus")]
    public void The_only_diagnostics_are_explicitly_skipped_directives()
    {
        // 把已知的那几条固定下来。数字变了说明语料用到了新构造，
        // 那是有信息量的变化，应当被看见而不是被"非空即可"的断言吞掉。
        //
        // **2026-09-21：从一条降为零条。** 原先那一条是 b-documented/S04 的
        // "暂不支持 click 指令，已跳过"——而它根本不是什么指令：那一行是
        // `click --> setpwd[设置新密码]`，click 是节点名，只是撞上了指令关键字。
        // 判据修好之后（行首的关键字后面若跟着连线或形状定界符，它就是节点），
        // 流程图语料里再没有需要跳过的内容了。
        var diagnostics = Flowcharts()
            .SelectMany(pair => pair.Chart.Diagnostics.Select(d => (pair.Entry, Diagnostic: d)))
            .ToArray();

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "MermaidCorpus")]
    public void Every_link_endpoint_is_a_declared_node()
    {
        // 边连到不存在的节点，布局阶段会拿到一张有悬空引用的图。
        // 在这里挡下来，比在布局里报一个与真正原因无关的错要好。
        var offenders = new List<string>();

        foreach (var (entry, chart) in Flowcharts())
        {
            var declared = chart.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

            foreach (var link in chart.Links)
            {
                if (!declared.Contains(link.From) || !declared.Contains(link.To))
                {
                    offenders.Add($"{entry.Arm}/{entry.PromptId}: {link.From} --> {link.To}");
                }
            }
        }

        offenders.Should().BeEmpty(
            $"每条边的两端都应当有节点声明。悬空的：{string.Join("、", offenders)}");
    }

    [Fact]
    [Trait("Category", "MermaidCorpus")]
    public void Subgraph_membership_agrees_with_node_parents_across_the_whole_corpus()
    {
        // 这一条是这次修掉的那个缺陷的回归门禁。
        var offenders = new List<string>();

        foreach (var (entry, chart) in Flowcharts())
        {
            foreach (var subgraph in chart.Subgraphs)
            {
                var expected = chart.Nodes
                    .Where(n => string.Equals(n.Parent, subgraph.Id, StringComparison.Ordinal))
                    .Select(n => n.Id);

                if (!subgraph.Members.SequenceEqual(expected, StringComparer.Ordinal))
                {
                    offenders.Add(
                        $"{entry.Arm}/{entry.PromptId}: 子图 {subgraph.Id} 的成员是 "
                        + $"[{string.Join(",", subgraph.Members)}]，"
                        + $"按节点父级应当是 [{string.Join(",", expected)}]");
                }
            }
        }

        offenders.Should().BeEmpty(string.Join(" | ", offenders));
    }

    [Fact]
    [Trait("Category", "MermaidCorpus")]
    public void Every_parent_reference_points_at_something_that_exists()
    {
        var offenders = new List<string>();

        foreach (var (entry, chart) in Flowcharts())
        {
            var subgraphIds = chart.Subgraphs.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

            foreach (var node in chart.Nodes.Where(n => n.Parent is not null))
            {
                if (!subgraphIds.Contains(node.Parent!))
                {
                    offenders.Add($"{entry.Arm}/{entry.PromptId}: 节点 {node.Id} 的父级 {node.Parent} 不是子图");
                }
            }

            foreach (var subgraph in chart.Subgraphs.Where(s => s.Parent is not null))
            {
                if (!subgraphIds.Contains(subgraph.Parent!))
                {
                    offenders.Add($"{entry.Arm}/{entry.PromptId}: 子图 {subgraph.Id} 的外层 {subgraph.Parent} 不存在");
                }
            }
        }

        offenders.Should().BeEmpty(string.Join(" | ", offenders));
    }

    [Fact]
    [Trait("Category", "MermaidCorpus")]
    public void A_subgraph_used_as_an_edge_endpoint_also_becomes_a_node()
    {
        // 分层架构图里 `ODS --> DWD` 这种连线是拿子图当端点用的——Mermaid 允许，
        // 子图在这里就是个复合节点。解析器因此会同时产出同名的子图与节点。
        //
        // 这不是解析错误，但**导入时必须合并**，否则图上会多出一个与子图重叠的方块。
        // 合并规则取决于 IR 怎么表达复合节点，属接线阶段的事，这里先把事实钉住：
        // 出现了多少个这样的标识、分布在哪些文件，改坏了要能立刻看出来。
        var collisions = new List<string>();

        foreach (var (entry, chart) in Flowcharts())
        {
            var subgraphIds = chart.Subgraphs.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            var nodeIds = chart.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            var shared = nodeIds.Intersect(subgraphIds, StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();

            if (shared.Length > 0)
            {
                collisions.Add($"{entry.Arm}/{entry.PromptId}: {string.Join(",", shared)}");
            }
        }

        collisions.Should().HaveCount(
            7,
            $"这是分层架构图的常见写法，不是边角情况。实际出现：{string.Join(" | ", collisions)}");
    }

    [Fact]
    [Trait("Category", "MermaidCorpus")]
    public void The_scale_the_parser_actually_handles_is_pinned()
    {
        // 把真实规模固定下来。它决定后续布局与渲染要按多大的图来设计，
        // 而"多大"这件事必须来自数据而不是猜测。
        var charts = Flowcharts().Select(pair => pair.Chart).ToArray();

        charts.Sum(c => c.Nodes.Count).Should().Be(993);

        // **2026-09-21：从 1059 加到 1060。** S04 里 `click --> setpwd[设置新密码]`
        // 那一行原先被当成 click 指令跳过了，边跟着一起丢；判据修好之后它回来了。
        // 加一而不是加多，说明只丢了这一条。
        charts.Sum(c => c.Links.Count).Should().Be(1060);
        charts.Sum(c => c.Subgraphs.Count).Should().Be(77);

        // 有子图的文件占了近四分之一，子图不是边角特性。
        charts.Count(c => c.Subgraphs.Count > 0).Should().Be(23);

        // 单份最大规模。语料是模型产出的"教科书式"图，不是压力测试，
        // 所以这个数字小是正常的——千节点的用例在 Benchmarks 里，不在这里。
        charts.Max(c => c.Nodes.Count).Should().Be(31);
        charts.Max(c => c.Links.Count).Should().Be(32);
    }

    [Fact]
    [Trait("Category", "MermaidCorpus")]
    public void Nested_subgraphs_occur_in_the_corpus()
    {
        // 嵌套子图不是假想的需求：分层架构图外面再套一个总览框是模型的自然写法。
        // 没有这一条，丢掉嵌套这件事在测试里看不出来。
        var nested = new List<string>();

        foreach (var (entry, chart) in Flowcharts())
        {
            foreach (var inner in chart.Subgraphs.Where(s => s.Parent is not null))
            {
                var outer = chart.Subgraphs.SingleOrDefault(s => s.Id == inner.Parent);

                if (outer is null)
                {
                    continue;
                }

                // 内层是外层的成员子图，不该出现在外层的成员节点里。
                outer.Members.Should().NotContain(inner.Id, "成员表只放节点，不放子图");

                nested.Add($"{entry.Arm}/{entry.PromptId}: {inner.Id} ⊂ {outer.Id}");
            }
        }

        nested.Should().HaveCount(6, $"语料里实际出现的嵌套：{string.Join("、", nested)}");
    }

    [Fact]
    [Trait("Category", "MermaidCorpus")]
    public void Parsing_the_same_source_twice_gives_the_same_result()
    {
        // 解析必须是纯函数。用集合而不是列表会让顺序随哈希漂移，
        // 表现为"同一份输入两次导入出不同的图"，而这种问题最难查。
        foreach (var entry in Corpus.Load())
        {
            var first = MermaidParser.Parse(entry.Content);
            var second = MermaidParser.Parse(entry.Content);

            second.Nodes.Should().Equal(first.Nodes);
            second.Links.Should().Equal(first.Links);

            // 子图逐字段比而不是整条记录比：记录的相等性对**集合成员**用引用比较
            // （Members 是列表），两次解析出来的列表不是同一个实例，
            // 整条记录比会判为不等，而那不是"解析不确定"。
            // 同一个坑在 Core 里踩过两次，见 CollectionEquality。
            second.Subgraphs.Should().HaveCount(first.Subgraphs.Count);

            for (var i = 0; i < first.Subgraphs.Count; i++)
            {
                second.Subgraphs[i].Id.Should().Be(first.Subgraphs[i].Id);
                second.Subgraphs[i].Label.Should().Be(first.Subgraphs[i].Label);
                second.Subgraphs[i].Direction.Should().Be(first.Subgraphs[i].Direction);
                second.Subgraphs[i].Parent.Should().Be(first.Subgraphs[i].Parent);
                second.Subgraphs[i].Members.Should().Equal(first.Subgraphs[i].Members);
            }
        }
    }
}
