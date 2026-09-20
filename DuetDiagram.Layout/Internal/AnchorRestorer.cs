namespace DuetDiagram.Layout.Internal;

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
internal static class AnchorRestorer
{
    public static PlacedNode[] Apply(PlacedNode[] nodes, LayoutNode[] inputs)
    {
        var anchors = inputs
            .Where(n => n.Pinned is not null)
            .ToDictionary(n => n.Id, n => n.Pinned!, StringComparer.Ordinal);

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

    /// <summary>
    /// 回填之后固定节点与目标坐标的最大偏差。
    /// </summary>
    /// <remarks>
    /// 这一项**必须恒为零**。它不为零意味着用户的显式意图被无声地改动了，
    /// 而那比任何布局瑕疵都严重——用户会以为自己的拖动没生效，然后反复重试。
    /// </remarks>
    public static double MaxDeviation(PlacedNode[] nodes, LayoutNode[] inputs)
    {
        var byId = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var max = 0.0;

        foreach (var input in inputs.Where(n => n.Pinned is not null))
        {
            if (!byId.TryGetValue(input.Id, out var placed))
            {
                continue;
            }

            max = Math.Max(max, Math.Abs(placed.X - input.Pinned!.X));
            max = Math.Max(max, Math.Abs(placed.Y - input.Pinned!.Y));
        }

        return max;
    }
}
