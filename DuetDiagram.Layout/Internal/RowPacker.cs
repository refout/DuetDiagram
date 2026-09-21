namespace DuetDiagram.Layout.Internal;

/// <summary>
/// 把一层里的节点沿一个轴摆开，避开固定节点。
/// </summary>
/// <remarks>
/// <para>
/// 这是"同一层内节点只可能在层内的那个轴上互相干扰"这条判断的落点：
/// 问题在每一层内退化成一条直线上的排布，一次扫描就能算完，不需要迭代。
/// 迭代收敛慢、结果依赖遍历顺序，而且因为要限制轮数而可能半途而废。
/// </para>
/// <para>
/// 两条调用路径共用它：让位与层内次序约束。分开写两遍的话，
/// 它们对"怎样才算摆得下"的判断迟早会不一致，同一条约束在不同路径上就会得到不同结果。
/// </para>
/// <para>
/// 方向适配靠坐标换轴完成，不在这一层做：调用方先把坐标换成"层沿纵轴排列"的形态，
/// 摆完再换回去。换轴是对合操作，换两次就还原，不会引入误差。
/// </para>
/// </remarks>
internal static class RowPacker
{
    /// <summary>
    /// 按给定次序把节点摆开。
    /// </summary>
    /// <param name="members">要摆的节点，**已经按期望次序排好**，坐标也已就位。</param>
    /// <param name="blockers">与这一层有交集的固定节点，按坐标升序。</param>
    /// <param name="gap">节点之间保留的最小间隙。</param>
    /// <remarks>
    /// <para>
    /// 只把节点往一个方向推，所以从左往右扫一遍就够，不需要反复回头检查：
    /// 扫过的固定节点一定已经在当前位置左侧，后面的只会更靠右。
    /// </para>
    /// <para>
    /// 两条调用路径的差别只在于坐标怎么来：让位给的是"各人按当前坐标从左往右"，
    /// 次序约束给的是"各人按用户要的次序坐回原来的槽位"。摆开这一段完全一样。
    /// </para>
    /// </remarks>
    public static PackResult Pack(
        IReadOnlyList<PlacedNode> members,
        IReadOnlyList<PlacedNode> blockers,
        double gap)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(blockers);

        var placed = new PlacedNode[members.Count];
        var cursor = double.NegativeInfinity;
        var moved = 0;

        for (var index = 0; index < members.Count; index++)
        {
            var member = members[index];
            var x = Math.Max(member.X, cursor);

            foreach (var blocker in blockers)
            {
                if (blocker.Right <= x)
                {
                    continue;
                }

                if (blocker.X >= x + member.Width)
                {
                    // 这个固定节点与后面所有固定节点都在右侧且不相交，不必再看。
                    break;
                }

                x = blocker.Right + gap;
            }

            placed[index] = member with { X = x };
            cursor = x + member.Width + gap;

            if (Math.Abs(x - member.X) > 0.01)
            {
                moved++;
            }
        }

        return new PackResult(placed, moved);
    }
}

/// <summary>摆开的结果。</summary>
/// <param name="Nodes">摆好之后的节点。</param>
/// <param name="Moved">坐标确实变了的节点数。</param>
internal sealed record PackResult(PlacedNode[] Nodes, int Moved);
