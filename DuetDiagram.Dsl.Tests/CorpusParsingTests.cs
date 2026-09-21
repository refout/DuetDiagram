using DuetDiagram.Core.Model;
using DuetDiagram.Dsl.Lexing;
using DuetDiagram.Dsl.Parsing;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Dsl.Tests;

/// <summary>
/// 拿冻结语料跑语法分析。
/// </summary>
/// <remarks>
/// <para>
/// 与 Mermaid 侧同一套理由：数据驱动。手写的示例干净得不真实，
/// 而这五十份是模型**在真实提示词下**产出的，正是解析器必须能吃下的东西。
/// </para>
/// <para>
/// 分成词法/语法两步，加上这一组，是为了让失败一眼能定位到哪一层。
/// </para>
/// </remarks>
public sealed class CorpusParsingTests
{
    private static IReadOnlyList<(CorpusEntry Entry, DslDocument Document)> Parsed()
    {
        return [.. Corpus.Load().Select(entry => (entry, DslParser.Parse(entry.Content)))];
    }

    [Fact]
    [Trait("Category", "DslCorpus")]
    public void Every_response_parses_cleanly()
    {
        var checkedCount = 0;
        var offenders = new List<string>();

        foreach (var (entry, document) in Parsed())
        {
            checkedCount++;

            if (!document.IsClean)
            {
                offenders.Add($"{entry.Arm}/{entry.PromptId}: "
                    + string.Join("；", document.Diagnostics.Select(d => $"第 {d.Line} 行第 {d.Column} 列：{d.Message}")));
            }
        }

        // 先确认确实跑了这么多条，否则空转也能通过。
        checkedCount.Should().Be(50, "c-dsl 组是五十条");

        offenders.Should().BeEmpty($"全部真实回答都应当解析干净。有问题的：{string.Join(" | ", offenders)}");
    }

    [Fact]
    [Trait("Category", "DslCorpus")]
    public void Every_response_declares_the_flowchart_kind()
    {
        // 这一组提示词要的就是流程图。出现别的类型说明语料生成时跑偏了，
        // 而那种情况下"解析干净"就不再是有效证据。
        Parsed().Should().OnlyContain(pair => pair.Document.Kind == DiagramKind.Flowchart);
    }

    [Fact]
    [Trait("Category", "DslCorpus")]
    public void The_scale_the_parser_actually_handles_is_pinned()
    {
        // 把真实规模固定下来。它决定后续布局与渲染要按多大的图来设计，
        // 而"多大"这件事必须来自数据而不是猜测。
        var documents = Parsed().Select(pair => pair.Document).ToArray();

        documents.Sum(d => d.Nodes.Count).Should().Be(451);
        documents.Sum(d => d.Edges.Count).Should().Be(491);
        documents.Sum(d => d.Groups.Count).Should().Be(48);

        // 分组不是边角特性，近四成文件用到。
        documents.Count(d => d.Groups.Count > 0).Should().Be(18);

        // 单份最大规模。语料是模型产出的"教科书式"图，不是压力测试，
        // 所以这个数字小是正常的——千节点的用例在 Benchmarks 里。
        // 也比 Mermaid 组小：DSL 每行更长，同样长度的回答装不下同样多的节点。
        documents.Max(d => d.Nodes.Count).Should().Be(18);
        documents.Max(d => d.Edges.Count).Should().Be(22);
    }

    [Fact]
    [Trait("Category", "DslCorpus")]
    public void The_layout_intents_that_actually_occur_are_pinned()
    {
        // DSL 存在的主要理由是这几条布局意图，所以"语料里到底用没用"
        // 是一个有信息量的数字。
        //
        // align / place / pin 是零。这三条不是没人要，而是**这一批提示词
        // 没有要求人工微调**——它们是拖动之后才写回的。语料覆盖不到它们，
        // 所以那三条只能靠手写用例保证，不能宣称"语料验过"。
        var intents = Parsed()
            .SelectMany(pair => pair.Document.Layout)
            .GroupBy(i => i.Kind)
            .ToDictionary(g => g.Key, g => g.Count());

        intents.GetValueOrDefault(DslLayoutIntentKind.SameRank).Should().Be(32);
        intents.GetValueOrDefault(DslLayoutIntentKind.Order).Should().Be(64);
        intents.GetValueOrDefault(DslLayoutIntentKind.Align).Should().Be(0);
        intents.GetValueOrDefault(DslLayoutIntentKind.Place).Should().Be(0);
        intents.GetValueOrDefault(DslLayoutIntentKind.Pin).Should().Be(0);
    }

    [Fact]
    [Trait("Category", "DslCorpus")]
    public void Every_edge_endpoint_is_a_declared_node()
    {
        // 边连到不存在的节点，布局阶段会拿到一张有悬空引用的图。
        // 在这里挡下来，比在布局里报一个与真正原因无关的错要好。
        //
        // 注意这条不变量靠**语料本身**满足，不是解析器补的：
        // 隐式创建留给映射阶段，与 Mermaid 侧一致。
        var offenders = new List<string>();

        foreach (var (entry, document) in Parsed())
        {
            var declared = document.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

            foreach (var edge in document.Edges)
            {
                if (!declared.Contains(edge.From))
                {
                    offenders.Add($"{entry.Arm}/{entry.PromptId}: 起点 {edge.From}");
                }

                if (!declared.Contains(edge.To))
                {
                    offenders.Add($"{entry.Arm}/{entry.PromptId}: 终点 {edge.To}");
                }
            }
        }

        offenders.Should().BeEmpty($"每条边的两端都应当有节点声明。悬空的：{string.Join("、", offenders)}");
    }

    [Fact]
    [Trait("Category", "DslCorpus")]
    public void Group_membership_agrees_with_node_parents_across_the_whole_corpus()
    {
        // 这一条是 Mermaid 侧修过的那个缺陷的对应门禁。
        // 成员表与父级曾经是各记各的两份记录，在真实语料里有六处对不上。
        // DSL 侧从一开始就只记父级、成员由父级推出来，这条测试防止有人改回去。
        var offenders = new List<string>();

        foreach (var (entry, document) in Parsed())
        {
            foreach (var group in document.Groups)
            {
                var expected = document.Nodes
                    .Where(n => string.Equals(n.Parent, group.Id, StringComparison.Ordinal))
                    .Select(n => n.Id)
                    .Concat(document.Groups
                        .Where(inner => string.Equals(inner.Parent, group.Id, StringComparison.Ordinal))
                        .Select(inner => inner.Id));

                if (!group.Members.SequenceEqual(expected, StringComparer.Ordinal))
                {
                    offenders.Add(
                        $"{entry.Arm}/{entry.PromptId}: 分组 {group.Id} 的成员是 "
                        + $"[{string.Join(",", group.Members)}]，"
                        + $"按父级应当是 [{string.Join(",", expected)}]");
                }
            }
        }

        offenders.Should().BeEmpty(string.Join(" | ", offenders));
    }

    [Fact]
    [Trait("Category", "DslCorpus")]
    public void Every_parent_reference_points_at_something_that_exists()
    {
        var offenders = new List<string>();

        foreach (var (entry, document) in Parsed())
        {
            var groupIds = document.Groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);

            foreach (var node in document.Nodes.Where(n => n.Parent is not null))
            {
                if (!groupIds.Contains(node.Parent!))
                {
                    offenders.Add($"{entry.Arm}/{entry.PromptId}: 节点 {node.Id} 的父级 {node.Parent} 不是分组");
                }
            }

            foreach (var group in document.Groups.Where(g => g.Parent is not null))
            {
                if (!groupIds.Contains(group.Parent!))
                {
                    offenders.Add($"{entry.Arm}/{entry.PromptId}: 分组 {group.Id} 的外层 {group.Parent} 不存在");
                }
            }
        }

        offenders.Should().BeEmpty(string.Join(" | ", offenders));
    }

    [Fact]
    [Trait("Category", "DslCorpus")]
    public void A_group_and_a_node_may_share_a_name_and_that_is_pinned()
    {
        // C02 里 `pay` 既是一个节点（"完成支付"，在 lane user 里）
        // 又是一个泳道（lane pay "支付服务"）。
        //
        // 这不是解析错误——两个声明各自合法，DSL 也允许。但 IR 的约定是
        // **节点与组合共用一个标识命名空间**，所以映射阶段（P1-17）必须合并或改名，
        // 否则导入出来的图上会有一个与泳道同名的方块。
        //
        // Mermaid 侧有一模一样的现象：那边是子图被当成边的端点用，
        // 语料里出现七次。两个格式都会撞，说明这不是边角情况——
        // 模型天然会把"那个阶段"和"那个框"取同一个名字。
        //
        // 这里先把事实钉住：数量与位置变了要能立刻看见。
        var collisions = new List<string>();

        foreach (var (entry, document) in Parsed())
        {
            var groupIds = document.Groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);

            foreach (var shared in document.Nodes
                .Select(n => n.Id)
                .Where(groupIds.Contains)
                .OrderBy(id => id, StringComparer.Ordinal))
            {
                collisions.Add($"{entry.Arm}/{entry.PromptId}: {shared}");
            }
        }

        collisions.Should().Equal(
            ["c-dsl/C02: pay"],
            $"这是同名的节点与泳道，映射阶段要处理。实际出现：{string.Join(" | ", collisions)}");
    }

    [Fact]
    [Trait("Category", "DslCorpus")]
    public void No_response_declares_the_same_node_twice()
    {
        // 重复声明在映射阶段会产生两条命令，而 IR 的集合不允许同 id 两项。
        var offenders = new List<string>();

        foreach (var (entry, document) in Parsed())
        {
            foreach (var id in document.Nodes.GroupBy(n => n.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                offenders.Add($"{entry.Arm}/{entry.PromptId}: {id.Key} × {id.Count()}");
            }
        }

        offenders.Should().BeEmpty(string.Join(" | ", offenders));
    }

    [Fact]
    [Trait("Category", "DslCorpus")]
    public void Fenced_responses_are_unwrapped()
    {
        // 五十份里有十五份带围栏（30%），标签还各不相同。
        // 这个数字钉住是因为它是"剥围栏必需"的直接证据：
        // 哪天它掉到零，也不该把剥围栏这一步删掉——模型行为会变，
        // 而这一步对没有围栏的文本是恒等操作。
        var fenced = Corpus.Load()
            .Count(entry => entry.Content.Contains("```", StringComparison.Ordinal));

        fenced.Should().Be(15);

        // 而且剥完必须真的能解析：围栏本身不该留下任何诊断。
        Parsed().Should().OnlyContain(pair => pair.Document.IsClean);
    }

    [Fact]
    [Trait("Category", "DslCorpus")]
    public void Unwrapping_actually_changes_something_for_the_fenced_ones()
    {
        // 防止"剥围栏"退化成恒等操作而测试仍然全绿：
        // 对有围栏的那十五份，剥过与没剥过的解析结果必须不同。
        var fenced = Corpus.Load().Where(entry => entry.Content.Contains("```", StringComparison.Ordinal)).ToArray();

        fenced.Should().HaveCount(15);

        foreach (var entry in fenced)
        {
            var extracted = DslSource.Extract(entry.Content);

            extracted.Should().NotBe(entry.Content.Trim());
            extracted.Should().NotContain("```");
        }
    }

    [Fact]
    [Trait("Category", "DslCorpus")]
    public void Parsing_the_same_source_twice_gives_the_same_result()
    {
        // 解析必须是纯函数。用集合而不是列表会让顺序随哈希漂移，
        // 表现为"同一份输入两次导入出不同的图"，而这种问题最难查。
        foreach (var entry in Corpus.Load())
        {
            var first = DslParser.Parse(entry.Content);
            var second = DslParser.Parse(entry.Content);

            // 投影成字符串再比，理由见 AstProjection：三条 AST 记录都带列表成员，
            // 记录的自动相等性对它们用引用比较。
            AstProjection.All(second).Should().Equal(AstProjection.All(first));
        }
    }
}
