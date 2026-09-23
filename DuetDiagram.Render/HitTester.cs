namespace DuetDiagram.Render;

/// <summary>
/// 按绘制列表回答"这个点上是谁"。
/// </summary>
/// <remarks>
/// <para>
/// **判据从绘制列表来，不从文档来。** 从文档来意味着再算一遍元素的位置，
/// 于是屏幕上的位置与命中的位置各有一套算法，两处迟早差一点点——
/// 而那种偏差的表现是"点不中"，离得越远偏得越多，因为误差随缩放倍数放大。
/// </para>
/// <para>
/// **倒着找。** 列表的顺序就是层叠顺序，后画的在上面，所以第一个命中的就是看得见的那个。
/// 正着找会把压在下面的元素选出来，用户点在最上面的东西上却选中了背后的另一个。
/// </para>
/// <para>
/// **画出来不等于点得中。** 锁定的元素照常画，但不该被点中，所以调用方把那一批标识
/// 传进来（见 <see cref="DrawList.Blocked"/>）。这份名单与指令是同一刻算出来的，
/// 不会出现"画的是这一批、判的是另一批"。不可见的元素本来就没有指令，
/// 传不传都不影响结果。
/// </para>
/// <para>
/// 坐标是文档坐标，不是屏幕坐标。视口变换在外面做，这里只认几何——
/// 这样它不依赖任何视口状态，同一份列表与同一个点永远给出同一个答案。
/// </para>
/// </remarks>
public static class HitTester
{
    /// <summary>
    /// 找出这个点上最上面的元素。
    /// </summary>
    /// <param name="commands">绘制指令，按层叠顺序。</param>
    /// <param name="point">文档坐标下的一个点。</param>
    /// <param name="tolerance">
    /// 折线的命中容差，文档坐标。传零表示必须正好落在线上。
    /// </param>
    /// <param name="blocked">
    /// 画出来但不该被点中的元素标识。传空表示画出来的都能点。
    /// </param>
    /// <returns>元素标识。这一点上没有东西时返回空。</returns>
    public static string? Hit(
        IReadOnlyList<DrawCommand> commands,
        DrawPoint point,
        double tolerance = 0,
        IReadOnlySet<string>? blocked = null)
    {
        ArgumentNullException.ThrowIfNull(commands);

        for (var index = commands.Count - 1; index >= 0; index--)
        {
            var command = commands[index];

            if (blocked is not null && blocked.Contains(command.ElementId))
            {
                continue;
            }

            if (Contains(command, point, tolerance))
            {
                return command.ElementId;
            }
        }

        return null;
    }

    /// <summary>
    /// 找出这个矩形框住了哪些元素。
    /// </summary>
    /// <param name="commands">绘制指令，按层叠顺序。</param>
    /// <param name="area">文档坐标下的一个矩形。</param>
    /// <param name="blocked">画出来但不该被选中的元素标识。传空表示画出来的都能选。</param>
    /// <remarks>
    /// <para>
    /// **判据与 <see cref="Hit"/> 同一条：同一份绘制列表、同样跳过 <paramref name="blocked"/>。**
    /// 两套判据的表现是"框进去的元素点不中、点得中的框不进"——而两处各自都自洽，
    /// 只有把两次结果摆在一起才看得出来。
    /// </para>
    /// <para>
    /// **矩形判据与点命中有一处刻意的不同：折线按它的外接矩形算。**
    /// 点命中逐段算距离，因为外接矩形在转折处会空出一大片；
    /// 而框选本来就是"圈一块区域"，一条斜着穿过的边该被框进来——
    /// 按逐段距离判的话，框住它中间那一段反而不算选中，而用户看不出为什么。
    /// </para>
    /// <para>
    /// 返回的标识按层叠顺序（从头到尾），同一个元素只出现一次。
    /// 顺序确定，于是"框同一块区域两次"得到同一份选中。
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Visible(
        IReadOnlyList<DrawCommand> commands,
        SpatialRect area,
        IReadOnlySet<string>? blocked = null)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var command in commands)
        {
            if (blocked is not null && blocked.Contains(command.ElementId))
            {
                continue;
            }

            if (Box(command) is not { } box || !box.Intersects(area))
            {
                continue;
            }

            if (seen.Add(command.ElementId))
            {
                ids.Add(command.ElementId);
            }
        }

        return ids;
    }

    /// <summary>这条指令画的东西占的那一块。算不出来的指令返回空。</summary>
    private static SpatialRect? Box(DrawCommand command) => command switch
    {
        DrawShape shape => shape.Rect,
        DrawText text => text.Box,
        DrawPolyline polyline => Box(polyline.Points),
        _ => null,
    };

    private static SpatialRect? Box(IReadOnlyList<DrawPoint> points)
    {
        if (points.Count == 0)
        {
            return null;
        }

        var box = new SpatialRect(points[0].X, points[0].Y, 0, 0);

        for (var index = 1; index < points.Count; index++)
        {
            box = box.Union(new SpatialRect(points[index].X, points[index].Y, 0, 0));
        }

        return box;
    }

    /// <summary>
    /// 这条指令画的东西盖住了这个点没有。
    /// </summary>
    /// <remarks>
    /// 形状与文本按外接矩形算，不按实际轮廓。圆形与菱形因此会在四个角上多出一小块：
    /// 点在角上、形状之外，仍然算命中。选元素这件事上宁可放宽一点——
    /// 放宽的代价是"偶尔选到了旁边的"，收紧的代价是"明明点在图上却没反应"，
    /// 后者会让人以为软件坏了。
    /// </remarks>
    private static bool Contains(DrawCommand command, DrawPoint point, double tolerance) => command switch
    {
        DrawShape shape => shape.Rect.Contains(point.X, point.Y),
        DrawText text => text.Box.Contains(point.X, point.Y),
        DrawPolyline polyline => DistanceTo(polyline.Points, point) <= tolerance,
        _ => false,
    };

    /// <summary>
    /// 点到折线的最短距离。
    /// </summary>
    /// <remarks>
    /// 折线的外接矩形在转折处会空出一大片，按外接矩形判会让"点在空白处却选中了那条边"。
    /// 逐段算点到线段的距离没有这个问题，而折线的段数是个位数，代价可以忽略。
    /// </remarks>
    private static double DistanceTo(IReadOnlyList<DrawPoint> points, DrawPoint point)
    {
        if (points.Count == 0)
        {
            return double.PositiveInfinity;
        }

        if (points.Count == 1)
        {
            return Distance(points[0], point);
        }

        var best = double.PositiveInfinity;

        for (var index = 1; index < points.Count; index++)
        {
            best = Math.Min(best, DistanceToSegment(points[index - 1], points[index], point));
        }

        return best;
    }

    private static double DistanceToSegment(DrawPoint from, DrawPoint to, DrawPoint point)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var lengthSquared = (dx * dx) + (dy * dy);

        if (lengthSquared <= 0)
        {
            return Distance(from, point);
        }

        // 把点投影到线段所在的直线上，再把参数夹到 [0,1]——
        // 不夹的话算出来的是"到无限长直线"的距离，线段两端之外的点会得到零距离。
        var t = (((point.X - from.X) * dx) + ((point.Y - from.Y) * dy)) / lengthSquared;
        t = Math.Clamp(t, 0, 1);

        return Distance(new DrawPoint(from.X + (t * dx), from.Y + (t * dy)), point);
    }

    private static double Distance(DrawPoint left, DrawPoint right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;

        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
