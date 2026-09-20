namespace DuetDiagram.Poc.LayoutConstraints;

/// <summary>
/// 把用户声明的坐标放回布局结果。
/// </summary>
/// <remarks>
/// <para>
/// 这一步不做任何避让，只负责把坐标改回目标值。避让交给后面的行内让位，
/// 两件事分开的好处是：偏差检查可以独立进行，只要这一步之后偏差不为零，
/// 就说明回填本身写错了，与让位逻辑无关。
/// </para>
/// <para>
/// 固定节点之间互相压住的情况在这里不处理，也无从处理——两个都不可动，
/// 任何一方让步都等于违背用户意图。这种输入矛盾只能报出来。
/// </para>
/// </remarks>
internal static class AnchorRestore
{
    public static PlacedNode[] Apply(PlacedNode[] nodes, ConstraintGraph graph)
    {
        var anchors = graph.Nodes
            .Where(n => n.Anchor is not null)
            .ToDictionary(n => n.Id, n => n.Anchor!, StringComparer.Ordinal);

        if (anchors.Count == 0)
        {
            return nodes;
        }

        return
        [
            .. nodes.Select(node => anchors.TryGetValue(node.Id, out var anchor)
                ? node with { X = anchor.X, Y = anchor.Y }
                : node),
        ];
    }

    /// <summary>回填之后固定节点与目标坐标的最大偏差。这条必须恒为零。</summary>
    public static int MaxDeviation(PlacedNode[] nodes, ConstraintGraph graph)
    {
        var max = 0.0;

        foreach (var node in graph.Nodes.Where(n => n.Anchor is not null))
        {
            var placed = nodes.FirstOrDefault(p => p.Id == node.Id);

            if (placed is null)
            {
                continue;
            }

            max = Math.Max(max, Math.Abs(placed.X - node.Anchor!.X));
            max = Math.Max(max, Math.Abs(placed.Y - node.Anchor!.Y));
        }

        return (int)Math.Ceiling(max);
    }
}
