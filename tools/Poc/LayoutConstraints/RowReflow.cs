namespace DuetDiagram.Poc.LayoutConstraints;

/// <summary>
/// 行内让位：把被锚点占位的自由节点在同一层内重新排列。
/// </summary>
/// <remarks>
/// <para>
/// 这是锚点回填之后唯一的避让手段。之所以不用"发现重叠就互相推开"的迭代办法，
/// 是因为在分层布局里，同一层的节点只可能在横向上互相干扰——
/// 层与层的纵坐标本来就不同，跨层重叠只可能由固定节点的自由纵坐标引起，
/// 而那种情况把固定节点当作该层的障碍物处理即可。于是问题在每一层内退化成一条直线上的排布，
/// 一次扫描就能算完，不需要迭代。
/// </para>
/// <para>
/// 迭代之所以不好：它收敛慢、结果依赖遍历顺序，而且因为要限制轮数而可能半途而废，
/// 留下一个"差不多分开了"的状态。直线上的排布是精确可解的，用蛮力去逼近它是把简单问题做复杂。
/// </para>
/// <para>
/// 节点在层内的相对次序必须保持。次序本身是有语义的——它决定了连线的交叉情况和视觉上的阅读顺序，
/// 打乱它比留下一点空隙更糟。所以这里的做法是保持次序、只把节点往右推，
/// 需要的话整层会变宽。
/// </para>
/// </remarks>
internal static class RowReflow
{
    /// <summary>层内节点之间保留的横向间隙。</summary>
    private const double Gap = 36;

    public static ReflowResult Apply(PlacedNode[] nodes, IReadOnlySet<string> anchored)
    {
        var free = nodes.Where(n => !anchored.Contains(n.Id)).ToArray();
        var obstacles = nodes.Where(n => anchored.Contains(n.Id)).ToArray();

        // 分层布局里同一层的节点纵坐标相同，所以按纵坐标分组就是按层分组。
        var rows = free
            .GroupBy(n => Math.Round(n.Y, 3))
            .OrderBy(g => g.Key)
            .ToArray();

        var placed = new List<PlacedNode>(nodes.Length);
        placed.AddRange(obstacles);

        var reflowed = 0;

        foreach (var row in rows)
        {
            var rowTop = row.Min(n => n.Y);
            var rowBottom = row.Max(n => n.Bottom);

            // 与该层纵向有交集的固定节点都是障碍物，哪怕它其实属于另一层。
            // 固定节点的纵坐标是用户给的自由值，可能正好卡在两层之间，
            // 忽略它的话，这一层的节点会被推过去压在它身上。
            var blockers = obstacles
                .Where(o => o.Bottom > rowTop + 0.01 && o.Y < rowBottom - 0.01)
                .OrderBy(o => o.X)
                .ToArray();

            // 保持原有的横向次序。
            var members = row.OrderBy(n => n.X).ToArray();

            var cursor = double.NegativeInfinity;

            foreach (var member in members)
            {
                var x = Math.Max(member.X, cursor);

                // 障碍物已经按横坐标升序排好，而让位只会把节点往右推，
                // 所以从左往右扫一遍就够，不需要反复回头检查：
                // 扫过的障碍物一定已经在当前位置左侧，后面的障碍物只会更靠右。
                foreach (var blocker in blockers)
                {
                    if (blocker.Right <= x)
                    {
                        continue;
                    }

                    if (blocker.X >= x + member.Width)
                    {
                        // 这一层与后面所有障碍物都在右侧且不相交，不必再看。
                        break;
                    }

                    x = blocker.Right + Gap;
                }

                placed.Add(member with { X = x });
                cursor = x + member.Width + Gap;

                if (Math.Abs(x - member.X) > 0.01)
                {
                    reflowed++;
                }
            }
        }

        return new ReflowResult([.. placed], reflowed);
    }

    /// <summary>统计确实相交的节点对数量。相接不算相交。</summary>
    public static int CountOverlaps(IReadOnlyList<PlacedNode> nodes)    {
        var ordered = nodes.OrderBy(n => n.Id, StringComparer.Ordinal).ToArray();
        var count = 0;

        for (var i = 0; i < ordered.Length; i++)
        {
            for (var j = i + 1; j < ordered.Length; j++)
            {
                if (ordered[i].Overlaps(ordered[j]))
                {
                    count++;
                }
            }
        }

        return count;
    }
}

/// <summary>
/// 让位结果。
/// </summary>
/// <remarks>
/// 这里刻意不带重叠统计。统计是给验证用的，本身是两两比较的平方复杂度，
/// 放在让位耗时里会让人误以为算法很慢。生产实现里也不该每帧做一次全量两两比较。
/// </remarks>
internal sealed record ReflowResult(PlacedNode[] Nodes, int ReflowedCount);
