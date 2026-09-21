using DuetDiagram.Core.Model;
using DuetDiagram.Dsl.Parsing;
using DuetDiagram.Mermaid.Lexing;
using DuetDiagram.Mermaid.Parsing;

namespace DuetDiagram.Tools.CompareHarness;

/// <summary>解析的结局。</summary>
internal enum ParseOutcome
{
    /// <summary>解析干净，没有诊断，也拿到了结构。</summary>
    Clean,

    /// <summary>拿到了结构，但报了诊断。有内容丢了。</summary>
    Partial,

    /// <summary>解析器拒绝：一条结构都没拿到。</summary>
    Rejected,

    /// <summary>图类型不是流程图，整份放弃。</summary>
    Unsupported,
}

/// <summary>清单里的一个节点。</summary>
internal sealed record ListedNode(string Id, string? Label, NodeShape Shape);

/// <summary>清单里的一个分组。</summary>
/// <param name="Id">标识。</param>
/// <param name="Label">显示文本。</param>
/// <param name="Members">直接成员，节点与嵌套分组都在里面。</param>
/// <param name="Parent">外层分组。只在报告里用来排缩进。</param>
internal sealed record ListedGroup(
    string Id,
    string Label,
    IReadOnlyList<string> Members,
    string? Parent);

/// <summary>清单里的一条边。</summary>
internal sealed record ListedEdge(string From, string To, string? Label);

/// <summary>
/// 一份响应的结构清单，已经抹掉格式特征。
/// </summary>
/// <param name="Outcome">解析结局。</param>
/// <param name="Direction">整体方向。</param>
/// <param name="Nodes">节点。已按同一口径补齐隐式创建的节点。</param>
/// <param name="Groups">分组。成员已按同一口径重新推导。</param>
/// <param name="Edges">边。</param>
/// <param name="Diagnostics">诊断原文。<b>只进不盲的报告，不进清单。</b></param>
/// <param name="GroupEndpoints">端点是分组的边，形如 <c>ingress -&gt; api</c>。</param>
internal sealed record StructureListing(
    ParseOutcome Outcome,
    string Direction,
    IReadOnlyList<ListedNode> Nodes,
    IReadOnlyList<ListedGroup> Groups,
    IReadOnlyList<ListedEdge> Edges,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> GroupEndpoints);

/// <summary>
/// 把两种格式的解析结果投影成同一种结构清单。
/// </summary>
/// <remarks>
/// <para>
/// 投影必须**真的中立**，否则双盲就是假的。两种格式的抽象语法树有三处形状不同，
/// 三处都要抹平，而且抹平的方向要一致：
/// </para>
/// <list type="number">
/// <item>
/// <b>隐式创建的节点。</b>Mermaid 的解析器在读 <c>A --&gt; B</c> 时会顺手把 A、B 记成节点；
/// DSL 的解析器只记边，不记节点。同一份图因此会给出两个不同的节点数。
/// 统一按"边端点也算节点"补齐，两边才可比。
/// </item>
/// <item>
/// <b>分组里的嵌套分组。</b>DSL 的成员表含嵌套分组，Mermaid 的成员表只有节点。
/// 统一按"父级等于本组的一律算成员"重新推导。
/// </item>
/// <item>
/// <b>边指向分组。</b>这是真问题——分层架构图里拿分组当端点很常见，
/// 而 IR 到底容不容得下它曾经是个待定项。但两边表现不同：
/// Mermaid 会顺手给这个分组造一个同名节点，DSL 不会。
/// 统一成"分组不因此变成节点"，并把这种边单独记下来。
/// </item>
/// </list>
/// <para>
/// 形状与标签本来就已经同源：两个解析器都吐 <see cref="NodeShape"/>，标签都是显示文本。
/// 方向同理。
/// </para>
/// <para>
/// <b>清单里不含端口、样式、布局意图。</b>前三样只有一种格式能表达，放进去等于在清单上盖了
/// 组别的戳。代价是清单看不见 DSL 的布局表达能力——这一点在报告里如实写明。
/// </para>
/// </remarks>
internal static class Structure
{
    /// <summary>组标识 → 用哪个解析器。新增组必须在这里登记。</summary>
    private static readonly Dictionary<string, string> FormatByArm = new(StringComparer.Ordinal)
    {
        ["a-bare"] = "mermaid",
        ["b-documented"] = "mermaid",
        ["c-dsl"] = "dsl",
    };

    public static StructureListing Parse(string arm, string content)
    {
        if (!FormatByArm.TryGetValue(arm, out var format))
        {
            // 猜错格式会静默产出一份错清单，而错清单看起来和好清单一模一样。
            throw new ArgumentOutOfRangeException(
                nameof(arm), arm, "这个组没有登记用哪个解析器，先在 Structure.FormatByArm 里补上。");
        }

        return format == "mermaid" ? FromMermaid(content) : FromDsl(content);
    }

    /// <summary>这份清单算不算"解析器拒绝了"。</summary>
    public static bool IsRejected(ParseOutcome outcome) =>
        outcome is ParseOutcome.Rejected or ParseOutcome.Unsupported;

    private static StructureListing FromMermaid(string content)
    {
        // 图类型不是流程图时解析器返回一份空结果加一条诊断。单独判一次，
        // 是为了把"模型画错了图"与"模型写坏了语法"分开报——前者要改提示词，后者要改语法。
        var wrongKind = MermaidDiagramKindDetector.Detect(MermaidSource.Extract(content))
            != MermaidDiagramKind.Flowchart;

        var flowchart = MermaidParser.Parse(content);

        var groups = flowchart.Subgraphs
            .Select(subgraph => (subgraph.Id, subgraph.Label, subgraph.Parent))
            .ToList();

        var groupIds = groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);

        // Mermaid 会把边端点补成节点。补出来的那些在 DSL 侧根本不存在，
        // 留着会让节点数差出一截。判据是"它是分组标识，且本身没带任何信息"。
        var declared = flowchart.Nodes
            .Where(node => !(groupIds.Contains(node.Id) && node.Label is null && node.Shape == NodeShape.Rect))
            .Select(node => new ListedNode(node.Id, node.Label, node.Shape))
            .ToList();

        var edges = flowchart.Links
            .Select(link => new ListedEdge(link.From, link.To, link.Label))
            .ToList();

        var (nodes, groupEndpoints) = Normalize(declared, groupIds, edges);

        return new StructureListing(
            OutcomeOf(wrongKind, flowchart.IsClean, nodes.Count, edges.Count),
            flowchart.Direction.ToString(),
            nodes,
            BuildGroups(
                [.. groups.Select(g => (g.Id, g.Label))],
                groups.ToDictionary(g => g.Id, g => g.Parent, StringComparer.Ordinal),
                flowchart.Nodes.ToDictionary(n => n.Id, n => n.Parent, StringComparer.Ordinal)),
            edges,
            [.. flowchart.Diagnostics.Select(d => $"{d.Line}:{d.Column} {d.Message}")],
            groupEndpoints);
    }

    private static StructureListing FromDsl(string content)
    {
        var document = DslParser.Parse(content);

        var groups = document.Groups
            .Select(group => (group.Id, group.Label, group.Parent))
            .ToList();

        var edges = document.Edges
            .Select(edge => new ListedEdge(edge.From, edge.To, edge.Label))
            .ToList();

        var (nodes, groupEndpoints) = Normalize(
            [.. document.Nodes.Select(node => new ListedNode(node.Id, node.Label, node.Shape))],
            groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal),
            edges);

        return new StructureListing(
            OutcomeOf(false, document.IsClean, nodes.Count, edges.Count),
            document.Direction.ToString(),
            nodes,
            BuildGroups(
                [.. groups.Select(g => (g.Id, g.Label))],
                groups.ToDictionary(g => g.Id, g => g.Parent, StringComparer.Ordinal),
                document.Nodes.ToDictionary(n => n.Id, n => n.Parent, StringComparer.Ordinal)),
            edges,
            [.. document.Diagnostics.Select(d => $"{d.Line}:{d.Column} {d.Message}")],
            groupEndpoints);
    }

    /// <summary>
    /// 把两种格式的差异抹平：边端点一律算节点，除非它是分组。
    /// </summary>
    private static (List<ListedNode> Nodes, List<string> GroupEndpoints) Normalize(
        List<ListedNode> declared,
        IReadOnlySet<string> groupIds,
        IReadOnlyList<ListedEdge> edges)
    {
        var known = declared.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var nodes = new List<ListedNode>(declared);
        var groupEndpoints = new List<string>();

        foreach (var edge in edges)
        {
            foreach (var endpoint in (string[])[edge.From, edge.To])
            {
                if (groupIds.Contains(endpoint))
                {
                    var text = $"{edge.From} -> {edge.To}";

                    if (!groupEndpoints.Contains(text, StringComparer.Ordinal))
                    {
                        groupEndpoints.Add(text);
                    }

                    continue;
                }

                if (known.Add(endpoint))
                {
                    nodes.Add(new ListedNode(endpoint, null, NodeShape.Rect));
                }
            }
        }

        return (nodes, groupEndpoints);
    }

    /// <summary>
    /// 按"父级等于本组"重新推导成员。
    /// </summary>
    /// <remarks>
    /// 不直接用解析器给的成员表：DSL 的表里有嵌套分组、Mermaid 的表里没有。
    /// 两边的父级字段含义一致，按它推一遍才得到同一个口径。
    /// </remarks>
    private static List<ListedGroup> BuildGroups(
        IReadOnlyList<(string Id, string Label)> groups,
        IReadOnlyDictionary<string, string?> groupParents,
        IReadOnlyDictionary<string, string?> nodeParents)
    {
        var result = new List<ListedGroup>(groups.Count);

        foreach (var (id, label) in groups)
        {
            var members = new List<string>();

            foreach (var (nodeId, parent) in nodeParents)
            {
                if (string.Equals(parent, id, StringComparison.Ordinal))
                {
                    members.Add(nodeId);
                }
            }

            foreach (var (innerId, parent) in groupParents)
            {
                if (string.Equals(parent, id, StringComparison.Ordinal))
                {
                    members.Add(innerId);
                }
            }

            result.Add(new ListedGroup(id, label, members, groupParents[id]));
        }

        return result;
    }

    private static ParseOutcome OutcomeOf(bool wrongKind, bool clean, int nodeCount, int edgeCount)
    {
        if (wrongKind)
        {
            return ParseOutcome.Unsupported;
        }

        // 空结果不算解析成功，哪怕一条诊断都没报：一份空白图当"通过"会把
        // 端到端准确率的分母悄悄做大。DSL 侧解析空输入正好是这种情况。
        if (nodeCount == 0 && edgeCount == 0)
        {
            return ParseOutcome.Rejected;
        }

        return clean ? ParseOutcome.Clean : ParseOutcome.Partial;
    }
}
