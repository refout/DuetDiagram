namespace DuetDiagram.Layout.Internal;

/// <summary>
/// 逐元素对齐：让一组节点在垂直于分层方向的那个轴上取同一个坐标。
/// </summary>
/// <remarks>
/// <para>
/// 对齐轴与让位轴**必须是同一个**。让位沿层内的那个轴推，对齐也沿同一个轴取齐；
/// 两处推错一个，节点就会被推出它所在的层，而那种错误在只看一个方向的测试里看不出来。
/// 所以这里与让位一样，先把坐标换成"层沿纵轴排列"的形态，做完再换回去。
/// </para>
/// <para>
/// 对齐只改层内的那个坐标，不碰分层方向上的坐标。因此它不会把节点挪到别的层去——
/// 这正是"对齐与分层互不干扰"这条判断的落点。
/// </para>
/// <para>
/// 目标坐标取组里**第一个成员**的坐标，不取平均。取平均会让每次重排都把所有成员
/// 挪一点，图上看起来像整组在抖；取其中一个的坐标至少有一个成员不用动。
/// </para>
/// </remarks>
internal static class AlignSolver
{
    private const double Tolerance = 0.01;

    /// <summary>按给定分组取齐。</summary>
    /// <param name="nodes">全部节点。</param>
    /// <param name="groups">每一组是要取齐的节点标识。</param>
    /// <param name="anchored">固定位置的节点标识。它们不能移动。</param>
    /// <param name="ranksAreVertical">层是不是沿纵向排列的。决定对齐哪个轴。</param>
    public static AlignResult Apply(
        PlacedNode[] nodes,
        IReadOnlyList<IReadOnlyList<string>> groups,
        IReadOnlySet<string> anchored,
        bool ranksAreVertical)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(anchored);

        if (groups.Count == 0)
        {
            return new AlignResult(nodes, 0, [], []);
        }

        // 统一到"层沿纵轴排列"的形态，对齐轴因此总是横轴。
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
            var members = group
                .Where(positions.ContainsKey)
                .Select(id => working[positions[id]])
                .ToArray();

            // 只剩一个成员时无事可做。约束里提到不存在的标识不算冲突：
            // 那说明图变了而约束没跟着变，属于陈旧数据，报出来只会淹没真正的矛盾。
            if (members.Length < 2)
            {
                continue;
            }

            var pinned = members.Where(m => anchored.Contains(m.Id)).ToArray();

            if (pinned.Length > 1 && pinned.Select(p => p.X).Distinct().Count() > 1)
            {
                conflicts.Add(
                    $"对齐约束「{Describe(group)}」里有 {pinned.Length} 个固定位置的节点，"
                    + "它们的坐标不同，无法同时取齐。");
                failed.Add(group);
                continue;
            }

            var target = pinned.Length == 1 ? pinned[0].X : members[0].X;

            foreach (var member in members)
            {
                if (anchored.Contains(member.Id) || Math.Abs(member.X - target) <= Tolerance)
                {
                    continue;
                }

                working[positions[member.Id]] = member with { X = target };
                moved++;
            }
        }

        return new AlignResult(
            [.. working.Select(n => ranksAreVertical ? n : Transpose(n))],
            moved,
            conflicts,
            failed);
    }

    /// <summary>横纵对调。宽度与高度跟着换，否则占位判断会错。</summary>
    private static PlacedNode Transpose(PlacedNode node) => new(node.Id, node.Y, node.X, node.Height, node.Width);

    private static string Describe(IReadOnlyList<string> members) => string.Join("、", members);
}

/// <summary>对齐的结果。</summary>
/// <param name="Nodes">取齐之后的节点。</param>
/// <param name="Moved">坐标确实变了的节点数。</param>
/// <param name="Conflicts">没能满足的约束，每一条是一句人话。</param>
/// <param name="FailedGroups">
/// 求解阶段就判定做不了的组。核对最终坐标时要跳过它们——
/// 那些组根本没有被改动过，再报一次会给出一个错误的理由（"被让位推开了"，而实际是压根没动）。
/// </param>
internal sealed record AlignResult(
    PlacedNode[] Nodes,
    int Moved,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<IReadOnlyList<string>> FailedGroups);
