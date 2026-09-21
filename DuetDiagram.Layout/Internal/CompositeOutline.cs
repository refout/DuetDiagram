namespace DuetDiagram.Layout.Internal;

/// <summary>
/// 组合的包围盒。
/// </summary>
/// <remarks>
/// <para>
/// 端点在组合上的边（分层架构图里 <c>ODS --&gt; DWD</c> 那种写法）要落到组合的边界上，
/// 所以路由之前得先知道每个组合占了哪块地方。这个盒子**不是输入**：
/// 它由成员的最终坐标算出来，而坐标要等求解与让位都做完才有。
/// </para>
/// <para>
/// 盒子只包住成员的可见范围，比真实的分组框小一圈——渲染时分组框还要往外留内边距与标题。
/// 那一圈是渲染的事，布局这里不知道也不该猜：尺寸由渲染定，布局只管连线的落点。
/// 代价是线会稍微扎进分组框一点，而这比按猜测的内边距去算准得多。
/// </para>
/// </remarks>
internal static class CompositeOutline
{
    /// <summary>
    /// 算出每个有成员的组合的包围盒。
    /// </summary>
    /// <remarks>
    /// 嵌套组合要算进外层的范围。递归时用 <paramref name="visiting"/> 挡住成环——
    /// 合法的文档里不会有环，而没有这道挡板，环会让递归直接溢出。
    /// </remarks>
    /// <param name="groups">组合与成员。</param>
    /// <param name="nodes">已经定好坐标的节点。</param>
    public static Dictionary<string, PlacedNode> Compute(
        IReadOnlyList<LayoutGroup> groups,
        PlacedNode[] nodes)
    {
        var placed = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var members = groups.ToDictionary(g => g.Id, g => g.Members, StringComparer.Ordinal);
        var boxes = new Dictionary<string, PlacedNode>(StringComparer.Ordinal);

        foreach (var group in groups)
        {
            Box(group.Id, members, placed, boxes, []);
        }

        return boxes;
    }

    private static PlacedNode? Box(
        string id,
        Dictionary<string, IReadOnlyList<string>> members,
        Dictionary<string, PlacedNode> placed,
        Dictionary<string, PlacedNode> boxes,
        HashSet<string> visiting)
    {
        if (boxes.TryGetValue(id, out var cached))
        {
            return cached;
        }

        if (!members.TryGetValue(id, out var direct) || !visiting.Add(id))
        {
            // 不是组合，或者成员关系成环。两种都不给盒子——调用方据此把边记为"端点解析不出来"，
            // 而不是给它一个编出来的位置。
            return null;
        }

        var left = double.MaxValue;
        var top = double.MaxValue;
        var right = double.MinValue;
        var bottom = double.MinValue;
        var any = false;

        foreach (var member in direct)
        {
            var box = placed.GetValueOrDefault(member) ?? Box(member, members, placed, boxes, visiting);

            if (box is null)
            {
                continue;
            }

            any = true;
            left = Math.Min(left, box.X);
            top = Math.Min(top, box.Y);
            right = Math.Max(right, box.Right);
            bottom = Math.Max(bottom, box.Bottom);
        }

        visiting.Remove(id);

        if (!any)
        {
            // 空组合没有范围可言。给一个零尺寸的盒子会让连线落到一个点上，
            // 而那看起来像是布局算错了；不给则如实记为端点解析不出来。
            return null;
        }

        var result = new PlacedNode(id, left, top, right - left, bottom - top);

        boxes[id] = result;

        return result;
    }
}
