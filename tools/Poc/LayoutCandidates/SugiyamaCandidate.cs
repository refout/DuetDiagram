using System.Reflection;
using Sugiyama;

namespace DuetDiagram.Poc.LayoutCandidates;

/// <summary>
/// 一款候选引擎的适配器：把归一化输入翻译成该引擎的输入。
/// </summary>
internal sealed class SugiyamaCandidate : ILayoutCandidate
{
    public string Name => "Sugiyama 0.12.2";

    public LayoutInputFacts InputFacts { get; } = ProbeInputFacts();

    public CandidateResult Compute(CandidateGraph graph, LayoutKnobs knobs)
    {
        var nodes = graph.Nodes.Select(n => new LayoutNode(n.Id, (float)n.Width, (float)n.Height)).ToArray();
        var edges = graph.Edges.Select(e => new LayoutEdge(e.From, e.To)).ToArray();

        var subgraphs = graph.Groups.Select(ToSubgraph).ToArray();

        var input = new LayoutGraph(ToDirection(knobs.Direction), nodes, edges, subgraphs);

        var options = new LayoutOptions
        {
            NodeSpacing = (float)knobs.NodeSpacing,
            LayerSpacing = (float)knobs.LayerSpacing,
        };

        var result = SugiyamaLayout.Compute(input, options);

        return new CandidateResult(
            [.. result.Nodes.Select(n => new CandidateBox(n.Id, n.X, n.Y, n.Width, n.Height))],
            [.. result.Groups.Select(g => new CandidateBox(g.Id, g.X, g.Y, g.Width, g.Height))],
            result.Width,
            result.Height);
    }

    /// <summary>
    /// 输入模型里没有任何坐标字段，所以不存在"往哪儿固定"这件事，返回未尝试。
    /// </summary>
    public PinProbeResult ProbePinned(CandidateGraph graph, LayoutKnobs knobs) =>
        new(false, false, "节点输入类型没有坐标或固定标记，调用方无法表达固定位置");

    /// <summary>
    /// 用成对同层约束把两个本来不同层的节点拉到同一层。
    /// </summary>
    /// <remarks>
    /// 这个引擎的输入模型里确实有同层约束字段，所以可以直接表达。
    /// 构造的图形是 a → b → c 再加 a → d，不加约束时 c 比 d 低一层。
    /// </remarks>
    public SameRankProbeResult ProbeSameRank(CandidateGraph graph, LayoutKnobs knobs)
    {
        LayoutNode[] nodes =
        [
            new("a", 80, 40),
            new("b", 80, 40),
            new("c", 80, 40),
            new("d", 80, 40),
        ];

        LayoutEdge[] edges =
        [
            new("a", "b"),
            new("b", "c"),
            new("a", "d"),
        ];

        var options = new LayoutOptions
        {
            NodeSpacing = (float)knobs.NodeSpacing,
            LayerSpacing = (float)knobs.LayerSpacing,
        };

        var plain = SugiyamaLayout.Compute(new LayoutGraph(LayoutDirection.TD, nodes, edges, []), options);

        var constrained = SugiyamaLayout.Compute(
            new LayoutGraph(LayoutDirection.TD, nodes, edges, []) with { SameRankConstraints = [("c", "d")] },
            options);

        var plainDelta = Math.Abs(plain.Nodes.First(n => n.Id == "c").Y - plain.Nodes.First(n => n.Id == "d").Y);
        var constrainedDelta = Math.Abs(
            constrained.Nodes.First(n => n.Id == "c").Y - constrained.Nodes.First(n => n.Id == "d").Y);

        var honored = constrainedDelta < 0.5 && plainDelta > 0.5;

        return new SameRankProbeResult(
            true,
            honored,
            $"未加约束时 c 与 d 纵坐标相差 {plainDelta:0}，加同层约束之后相差 {constrainedDelta:0}");
    }

    private static LayoutSubgraph ToSubgraph(CandidateGroup group) =>
        new(group.Id, group.Label, group.Members, [.. group.Children.Select(ToSubgraph)]);

    private static LayoutDirection ToDirection(string direction) => direction switch
    {
        "TD" => LayoutDirection.TD,
        "BT" => LayoutDirection.BT,
        "LR" => LayoutDirection.LR,
        "RL" => LayoutDirection.RL,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "未知方向"),
    };

    private static LayoutInputFacts ProbeInputFacts()
    {
        var nodeMembers = typeof(LayoutNode).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToArray();
        var layoutMembers = typeof(LayoutGraph).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name)
            .Concat(typeof(LayoutOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name))
            .ToArray();

        return new LayoutInputFacts(
            HasPinnedField: ContainsAny(nodeMembers, "X", "Y", "Position", "Pin", "Fixed"),
            HasOrderOrAlignOrPlaceField: ContainsAny(nodeMembers.Concat(layoutMembers), "Order", "Align", "Place", "Nudge"),
            NodeMembers: string.Join(", ", nodeMembers),
            LayoutMembers: string.Join(", ", layoutMembers));
    }

    private static bool ContainsAny(IEnumerable<string> members, params string[] names) =>
        members.Any(member => names.Any(name => member.Contains(name, StringComparison.OrdinalIgnoreCase)));
}
