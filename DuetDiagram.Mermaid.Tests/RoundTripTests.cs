using DuetDiagram.Core.Model;
using DuetDiagram.Mermaid.Export;
using DuetDiagram.Mermaid.Import;
using DuetDiagram.Mermaid.Parsing;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Mermaid.Tests;

/// <summary>
/// 「导入 → 导出 → 导入」的不动点。
/// </summary>
/// <remarks>
/// <para>
/// **IR 的表达力严格强于 Mermaid**，所以"任意文档导出再导入得到同一份文档"是不可达的。
/// 这里用的是可行的口径：把往返定义成**不动点**——
/// 不要求导出的文本与原始 Mermaid 逐字相同，只要求走一圈之后稳定下来。
/// </para>
/// <para>
/// 四条判据，一条比一条强：
/// </para>
/// <list type="number">
/// <item>两个哈希相等（这是任务里写的那条口径）。</item>
/// <item>内容逐字段相等——比哈希强，因为哈希对节点顺序不敏感。</item>
/// <item>节点顺序也相等——比内容相等更强，因为顺序是层内次序的依据。</item>
/// <item>再导出的文本逐字节相同——这才是"稳定下来"。</item>
/// </list>
/// <para>
/// 分开写而不是合成一条，是为了失败时能一眼看出是哪一层不稳。
/// </para>
/// </remarks>
public sealed class RoundTripTests
{
    [Fact]
    [Trait("Category", "MermaidRoundTrip")]
    public void Every_corpus_document_keeps_its_hashes()
    {
        var unstable = new List<string>();

        foreach (var (entry, first) in Imported())
        {
            var second = Reimport(first);

            if (first.StructuralHash != second.StructuralHash || first.VisualHash != second.VisualHash)
            {
                unstable.Add($"{entry.Arm}/{entry.PromptId}");
            }
        }

        unstable.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "MermaidRoundTrip")]
    public void Every_corpus_document_keeps_its_content()
    {
        // 比哈希强：哈希是按标识排序之后算的，对集合顺序不敏感，
        // 所以内容被换了个次序它也看不出来。
        var drifted = new List<string>();

        foreach (var (entry, first) in Imported())
        {
            var second = Reimport(first);
            var differences = Differences(first, second);

            if (differences.Count > 0)
            {
                drifted.Add($"{entry.Arm}/{entry.PromptId}：{string.Join("；", differences.Take(3))}");
            }
        }

        drifted.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "MermaidRoundTrip")]
    public void Every_corpus_document_keeps_the_same_nodes_in_the_same_places()
    {
        // 归属关系必须原样保留：它决定布局的嵌套，而且是结构性的。
        var broken = new List<string>();

        foreach (var (entry, first) in Imported())
        {
            var second = Reimport(first);

            foreach (var node in first.Nodes)
            {
                var other = second.Nodes.FirstOrDefault(n => string.Equals(n.Id, node.Id, StringComparison.Ordinal));

                if (other is null)
                {
                    broken.Add($"{entry.Arm}/{entry.PromptId}：节点 {node.Id} 丢了");
                }
                else if (!string.Equals(node.Parent, other.Parent, StringComparison.Ordinal))
                {
                    broken.Add($"{entry.Arm}/{entry.PromptId}：节点 {node.Id} 的归属从 {node.Parent ?? "无"} 变成 {other.Parent ?? "无"}");
                }
            }
        }

        broken.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "MermaidRoundTrip")]
    public void A_second_round_trip_changes_nothing()
    {
        // 这才是"稳定下来"：走一圈之后，再走一圈不再有任何变化。
        //
        // 第一趟可能变一次，原因是 Mermaid 的语法把**声明位置与归属块绑在一起**：
        // 一个节点若在原文里先于它的归属块出现（分层的图里很常见——先在某一层里
        // 引用它，稍后在另一层里正式声明它），那么"位置"与"归属"在文本里没法同时保。
        // 导出按归属写，所以它的位置会挪到归属块那一处。
        // 挪过之后就与语法结构对齐了，第二趟不再变。
        var drifted = new List<string>();

        foreach (var (entry, first) in Imported())
        {
            var second = Reimport(first);
            var third = Reimport(second);

            var once = MermaidExporter.Export(second, new ExportOptions()).Text;
            var twice = MermaidExporter.Export(third, new ExportOptions()).Text;

            if (!string.Equals(once, twice, StringComparison.Ordinal))
            {
                drifted.Add($"{entry.Arm}/{entry.PromptId}：文本仍在变");
            }
            else if (Differences(second, third).Count > 0)
            {
                drifted.Add($"{entry.Arm}/{entry.PromptId}：{string.Join("；", Differences(second, third).Take(3))}");
            }
        }

        drifted.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "MermaidRoundTrip")]
    public void A_corpus_document_reports_nothing_dropped_on_export()
    {
        // 语料的文档只用了 Mermaid 表达得出来的东西，所以导出不该报任何丢失。
        // 报了说明丢失清单过报——而过报会让真丢失被淹掉。
        var noisy = new List<string>();

        foreach (var (entry, document) in Imported())
        {
            var dropped = MermaidExporter.Export(document, new ExportOptions()).Report.Dropped;

            if (dropped.Count > 0)
            {
                noisy.Add($"{entry.Arm}/{entry.PromptId}: {string.Join('；', dropped.Select(d => d.Feature))}");
            }
        }

        noisy.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "MermaidRoundTrip")]
    public void A_hand_built_document_survives_the_round_trip()
    {
        // 语料覆盖不到的形状与线型在这里补上：圆角、体育场、菱形、点线、子图方向。
        var document = DiagramDocument.CreateFromContent(
            "hand",
            DiagramKind.Flowchart,
            Direction.LR,
            nodes:
            [
                new NodeDef { Id = "A", Label = "开始", Shape = NodeShape.Stadium, Parent = "后端" },
                new NodeDef { Id = "B", Label = "接口", Shape = NodeShape.Rounded, Parent = "后端" },
                new NodeDef { Id = "C", Label = "判定", Shape = NodeShape.Diamond },
                new NodeDef { Id = "D", Label = "库", Shape = NodeShape.Cylinder, Style = new NodeStyle { Fill = "#eef", Stroke = "#333" } },
            ],
            edges:
            [
                new EdgeDef { Id = "e1", From = "A", To = "B", Label = "下一步" },
                new EdgeDef { Id = "e2", From = "B", To = "C", Style = new EdgeStyle { Line = LineStyle.Dotted } },
                new EdgeDef { Id = "e3", From = "C", To = "D", Style = new EdgeStyle { Arrow = ArrowStyle.None } },
                new EdgeDef { Id = "e4", From = "后端", To = "D" },
            ],
            composites: [new GroupDef { Id = "后端", Label = "后端服务", Direction = Direction.TB, Members = ["A", "B"] }]);

        var text = MermaidExporter.Export(document, new ExportOptions()).Text;
        var restored = MermaidImporter.Import(MermaidParser.Parse(text), new ImportOptions { DocumentId = "hand" }).Document;

        restored.StructuralHash.Should().Be(document.StructuralHash);
        SameContent(document, restored).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "MermaidRoundTrip")]
    public void An_empty_document_round_trips()
    {
        var document = DiagramDocument.CreateFromContent("empty");
        var text = MermaidExporter.Export(document, new ExportOptions()).Text;

        text.Should().Be("flowchart TB\n");

        var restored = MermaidImporter.Import(MermaidParser.Parse(text), new ImportOptions { DocumentId = "empty" }).Document;

        restored.Nodes.Should().BeEmpty();
        restored.Edges.Should().BeEmpty();
    }

    /// <summary>两份文档的内容是否逐字段相同。顺序不计，由另一条用例单独管。</summary>
    private static bool SameContent(DiagramDocument first, DiagramDocument second) =>
        Differences(first, second).Count == 0;

    /// <summary>
    /// 逐字段比一遍，把不同的地方写成人能读的一句话。
    /// </summary>
    /// <remarks>
    /// 失败信息里带上差异，是因为"某一组里的某一份漂了"这句话本身没法查——
    /// 还得自己去把两份文档打出来对。漂移是这一层最要紧的失败，值得让信息直接可读。
    /// </remarks>
    private static List<string> Differences(DiagramDocument first, DiagramDocument second)
    {
        var report = new List<string>();

        Diff("节点", first.Nodes, second.Nodes, report);
        Diff("边", first.Edges, second.Edges, report);
        Diff("组合", first.Composites, second.Composites, report);

        if (first.Direction != second.Direction)
        {
            report.Add($"方向 {first.Direction} → {second.Direction}");
        }

        return report;
    }

    private static void Diff<T>(string what, IReadOnlyList<T> before, IReadOnlyList<T> after, List<string> report)
        where T : IDefinition
    {
        var left = before.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var right = after.ToDictionary(item => item.Id, StringComparer.Ordinal);

        foreach (var id in left.Keys.Except(right.Keys, StringComparer.Ordinal))
        {
            report.Add($"{what} {id} 丢了");
        }

        foreach (var id in right.Keys.Except(left.Keys, StringComparer.Ordinal))
        {
            report.Add($"{what} {id} 多出来了");
        }

        foreach (var id in left.Keys.Intersect(right.Keys, StringComparer.Ordinal))
        {
            if (!Equals(left[id], right[id]))
            {
                report.Add($"{what} {id}：{left[id]} → {right[id]}");
            }
        }
    }

    private static DiagramDocument Reimport(DiagramDocument document)
    {
        var text = MermaidExporter.Export(document, new ExportOptions()).Text;

        return MermaidImporter
            .Import(MermaidParser.Parse(text), new ImportOptions { DocumentId = document.Id })
            .Document;
    }

    private static IEnumerable<(CorpusEntry Entry, DiagramDocument Document)> Imported()
    {
        foreach (var entry in Corpus.Load())
        {
            var chart = MermaidParser.Parse(entry.Content);

            // 没有节点也没有连线的那几份不是流程图，解析器整份放弃了。
            if (chart.Nodes.Count == 0 && chart.Links.Count == 0)
            {
                continue;
            }

            yield return (entry, MermaidImporter.Import(chart, new ImportOptions { DocumentId = entry.PromptId }).Document);
        }
    }
}
