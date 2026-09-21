namespace DuetDiagram.Layout.Internal;

/// <summary>
/// 层内次序：让一组节点在同一层里按给定次序从左到右排列。
/// </summary>
/// <remarks>
/// <para>
/// **它是"保持次序"，不是"按给定次序排"。** 约束只说了组内这几个谁在谁左边，
/// 没说组外的节点该去哪。做法因此是**换位**：把这几个成员当前占的槽位取出来，
/// 按给定次序重新填进去，其余节点一个都不动。
/// 重排整层的写法会顺手打乱组外的节点，而次序是有语义的——它决定连线的交叉情况与阅读顺序，
/// 打乱它比整层变宽更糟：变宽只是占地方，乱序会让人看不懂图。
/// </para>
/// <para>
/// 层内次序只在**同一层之内**有意义。成员落在不同的层上时这条约束无解，
/// 如实报出来而不是硬把节点拉到一层去——拉过去会连带改变分层，而分层是另一条约束管的事。
/// </para>
/// <para>
/// 固定位置的节点不能移动。组里混着固定节点时次序仍然可能成立：
/// 固定节点占住它自己的槽位，自由成员依次填进剩下的槽位；
/// 填完之后整体次序必须递增，否则这条约束与固定位置互相矛盾，报出来。
/// </para>
/// </remarks>
internal static class OrderSolver
{
    /// <summary>按给定次序换位。</summary>
    /// <param name="nodes">全部节点。</param>
    /// <param name="groups">每一组是要排次序的节点标识，按期望的先后给出。</param>
    /// <param name="anchored">固定位置的节点标识。它们不能移动。</param>
    /// <param name="ranksAreVertical">层是不是沿纵向排列的。决定往哪个轴排。</param>
    /// <param name="gap">节点之间保留的最小间隙。</param>
    public static OrderResult Apply(
        PlacedNode[] nodes,
        IReadOnlyList<IReadOnlyList<string>> groups,
        IReadOnlySet<string> anchored,
        bool ranksAreVertical,
        double gap)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(anchored);

        if (groups.Count == 0)
        {
            return new OrderResult(nodes, 0, [], []);
        }

        var working = nodes.Select(n => ranksAreVertical ? n : Transpose(n)).ToArray();
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = 0; index < working.Length; index++)
        {
            positions[working[index].Id] = index;
        }

        var conflicts = new List<string>();
        var failed = new List<IReadOnlyList<string>>();
        var moved = 0;

        foreach (var group in groups)
        {
            var present = working.Where(n => group.Contains(n.Id, StringComparer.Ordinal)).ToArray();

            // 少于两个成员时无所谓次序。约束里提到不存在的标识不算冲突：
            // 那说明图变了而约束没跟着变，报出来只会淹没真正的矛盾。
            if (present.Length < 2)
            {
                continue;
            }

            // 同一层的节点纵坐标相同，所以按纵坐标分组就是按层分组。
            var ranks = present.Select(n => Math.Round(n.Y, 3)).Distinct().ToArray();

            if (ranks.Length > 1)
            {
                conflicts.Add(
                    $"层内次序约束「{Describe(group)}」要求这些节点在同一层，实际分布在 {ranks.Length} 层上。");
                failed.Add(group);
                continue;
            }

            var row = working.Where(n => Math.Abs(Math.Round(n.Y, 3) - ranks[0]) <= 0.001).ToArray();
            var rowTop = row.Min(n => n.Y);
            var rowBottom = row.Max(n => n.Bottom);

            // 这一层里所有节点的当前次序，含固定节点。槽位就是它里面的下标。
            var rowOrder = row.OrderBy(n => n.X).ThenBy(n => n.Id, StringComparer.Ordinal).ToArray();
            var slotOf = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var index = 0; index < rowOrder.Length; index++)
            {
                slotOf[rowOrder[index].Id] = index;
            }

            var desired = group.Where(slotOf.ContainsKey).ToArray();

            if (desired.Length < 2)
            {
                continue;
            }

            // 自由成员依次填进它们自己那些槽位，固定成员占住自己的槽位。
            // 填完之后整体次序必须递增，否则这条约束做不到。
            var freeSlots = desired
                .Where(id => !anchored.Contains(id))
                .Select(id => slotOf[id])
                .OrderBy(slot => slot)
                .ToArray();

            var target = new int[desired.Length];
            var cursor = 0;
            var satisfiable = true;

            for (var index = 0; index < desired.Length; index++)
            {
                target[index] = anchored.Contains(desired[index]) ? slotOf[desired[index]] : freeSlots[cursor++];

                if (index > 0 && target[index] <= target[index - 1])
                {
                    satisfiable = false;
                }
            }

            if (!satisfiable)
            {
                conflicts.Add(
                    $"层内次序约束「{Describe(group)}」与固定位置冲突："
                    + "固定节点已经占住的次序挡在了要求的位置上，两者无法同时满足。");
                failed.Add(group);
                continue;
            }

            var reordered = rowOrder.Select(n => n.Id).ToArray();

            for (var index = 0; index < desired.Length; index++)
            {
                reordered[target[index]] = desired[index];
            }

            var rank = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var index = 0; index < reordered.Length; index++)
            {
                rank[reordered[index]] = index;
            }

            // 成员搬到它该占的那个槽位上去，槽位的坐标取自原来占着它的那个节点。
            // 只把次序换掉、坐标不动的话，摆开那一步会按各人**原来的**坐标往后推，
            // 结果次序虽然对了，整层却被撑宽——而用户只是想换两个人的左右关系。
            // 槽位是同一批坐标的一个排列，按槽位坐回去之后整层的跨度一点没变。
            var seat = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var index = 0; index < desired.Length; index++)
            {
                seat[desired[index]] = target[index];
            }

            var free = row
                .Where(n => !anchored.Contains(n.Id))
                .OrderBy(n => rank[n.Id])
                .Select(n => seat.TryGetValue(n.Id, out var slot) ? n with { X = rowOrder[slot].X } : n)
                .ToArray();

            if (free.Length == 0)
            {
                continue;
            }

            var blockers = working
                .Where(n => anchored.Contains(n.Id) && n.Bottom > rowTop + 0.01 && n.Y < rowBottom - 0.01)
                .OrderBy(n => n.X)
                .ToArray();

            var packed = RowPacker.Pack(free, blockers, gap);

            foreach (var node in packed.Nodes)
            {
                working[positions[node.Id]] = node;
            }

            moved += packed.Moved;
        }

        return new OrderResult(
            [.. working.Select(n => ranksAreVertical ? n : Transpose(n))],
            moved,
            conflicts,
            failed);
    }

    /// <summary>横纵对调。宽度与高度跟着换，否则占位判断会错。</summary>
    private static PlacedNode Transpose(PlacedNode node) => new(node.Id, node.Y, node.X, node.Height, node.Width);

    private static string Describe(IReadOnlyList<string> members) => string.Join("、", members);
}

/// <summary>层内次序的结果。</summary>
/// <param name="Nodes">换位之后的节点。</param>
/// <param name="Moved">坐标确实变了的节点数。</param>
/// <param name="Conflicts">没能满足的约束，每一条是一句人话。</param>
/// <param name="FailedGroups">
/// 求解阶段就判定做不了的组。核对最终坐标时要跳过它们——
/// 那些组根本没有被改动过，再报一次会给出一个错误的理由（"被让位推回了原次序"，而实际是压根没动）。
/// </param>
internal sealed record OrderResult(
    PlacedNode[] Nodes,
    int Moved,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<IReadOnlyList<string>> FailedGroups);
