using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Render;

/// <summary>
/// 一种变更高亮手段。
/// </summary>
/// <remarks>
/// 三种手段都是**形状或运动**，颜色只是辅助线索。只靠颜色的话，色觉障碍用户看到的
/// 是一张没标过的图，而这一点无法事后补救——所以角标是一个点、轮廓是虚线、脉冲是动，
/// 各自靠形状与位置就能认出来，颜色只用来区分"谁改的"。
/// </remarks>
public enum HighlightKind
{
    /// <summary>元素边框脉冲。只动边框，不改填充色。</summary>
    Pulse,

    /// <summary>元素右上角小圆点。</summary>
    Badge,

    /// <summary>元素外部叠加虚线轮廓。</summary>
    Outline,
}

/// <summary>
/// 一个元素上的变更高亮。
/// </summary>
/// <param name="ElementId">被标记的元素。</param>
/// <param name="Source">
/// 变更来源。撤销/重做时它继承**原命令**的来源，而不是 <see cref="ChangeSource.Undo"/>，
/// 这样用户看到的还是"谁改的"，只是多了一个撤销符号。
/// </param>
/// <param name="Kinds">这个元素上开着哪几种手段。</param>
/// <param name="IsUndo">这次标记来自一次撤销，画 ↶。</param>
/// <param name="IsRedo">这次标记来自一次重做，画 ↷。</param>
public sealed record ElementHighlight(
    string ElementId,
    ChangeSource Source,
    IReadOnlySet<HighlightKind> Kinds,
    bool IsUndo = false,
    bool IsRedo = false);

/// <summary>
/// 变更高亮的叠加层构建。
/// </summary>
/// <remarks>
/// <para>
/// **高亮不进绘制列表的几何。** 它是一层叠在正式列表之上的临时图形：混进列表之后，
/// 动画的时间戳会污染快照测试，而那些快照本该是"同一份输入永远同一份输出"。
/// </para>
/// <para>
/// 这个类只做纯计算：给一批高亮与一份基准列表，算出这一帧要叠哪些指令。
/// 它不读时钟、不读文档——脉冲的相位由调用方算好传进来，于是同一相位永远得到同一份输出。
/// </para>
/// </remarks>
public static class Highlight
{
    /// <summary>角标指令的元素标识前缀。它不进命中测试，只是让指令可被辨认。</summary>
    public const string BadgePrefix = "__hl_badge__:";

    /// <summary>虚线轮廓指令的元素标识前缀。</summary>
    public const string OutlinePrefix = "__hl_outline__:";

    /// <summary>脉冲指令的元素标识前缀。</summary>
    public const string PulsePrefix = "__hl_pulse__:";

    /// <summary>撤销/重做符号指令的元素标识前缀。</summary>
    public const string SymbolPrefix = "__hl_symbol__:";

    /// <summary>
    /// 来源对应的颜色。
    /// </summary>
    /// <remarks>
    /// 人工用蓝、LLM 用紫，其余来源各给一个一眼分得开的颜色。这里没有"冲突"这一档——
    /// 冲突是并发检测的结果，不是一次命令的来源，<see cref="ChangeSource"/> 里也没有它。
    /// 颜色本身只是辅助线索，认手段靠形状。
    /// </remarks>
    public static string SourceColor(ChangeSource source) => source switch
    {
        ChangeSource.Human => "#1f6feb",
        ChangeSource.Llm => "#8250df",
        ChangeSource.Import => "#0d9488",
        ChangeSource.Mcp => "#15803d",
        _ => "#6b7280",
    };

    /// <summary>
    /// 算出这一帧的高亮叠加指令。
    /// </summary>
    /// <param name="highlights">要标记的元素。</param>
    /// <param name="baseCommands">基准绘制列表。用它找到每个元素的外接框，高亮才贴得住元素。</param>
    /// <param name="theme">外观查表。脉冲的时长、次数、角标大小都从它取。</param>
    /// <param name="pulsePhase">
    /// 脉冲在它那一轮里的相位，取值 0 到 1；不在脉冲中时传负数，脉冲那一条就不画。
    /// </param>
    /// <remarks>
    /// 找不到外接框的元素（已经被删掉、或者这一份列表压根是别的文档）直接跳过，
    /// 不猜一个位置画上去——那会留下一个漂在空处的标记。
    /// </remarks>
    public static IReadOnlyList<DrawCommand> Build(
        IReadOnlyList<ElementHighlight> highlights,
        IReadOnlyList<DrawCommand> baseCommands,
        Theme theme,
        double pulsePhase)
    {
        ArgumentNullException.ThrowIfNull(highlights);
        ArgumentNullException.ThrowIfNull(baseCommands);
        ArgumentNullException.ThrowIfNull(theme);

        var commands = new List<DrawCommand>(highlights.Count * 3);

        foreach (var highlight in highlights)
        {
            if (BoundsOf(baseCommands, highlight.ElementId) is not { } bounds)
            {
                continue;
            }

            var color = SourceColor(highlight.Source);

            if (highlight.Kinds.Contains(HighlightKind.Outline))
            {
                commands.Add(Outline(highlight.ElementId, bounds, color, theme));
            }

            if (highlight.Kinds.Contains(HighlightKind.Pulse) && pulsePhase >= 0)
            {
                commands.Add(Pulse(highlight.ElementId, bounds, color, theme, pulsePhase));
            }

            if (highlight.Kinds.Contains(HighlightKind.Badge))
            {
                var badge = Badge(highlight.ElementId, bounds, color, theme);

                commands.Add(badge);

                if (highlight.IsUndo || highlight.IsRedo)
                {
                    commands.Add(Symbol(highlight.ElementId, badge.Rect, color, theme, highlight.IsUndo));
                }
            }
        }

        return commands;
    }

    /// <summary>虚线轮廓：往外撑一圈，不与元素自己的描边抢那条线。</summary>
    private static DrawShape Outline(string elementId, SpatialRect bounds, string color, Theme theme) =>
        new(
            OutlinePrefix + elementId,
            NodeShape.Rounded,
            bounds.Inflate(theme.HighlightOutlineInset),
            "transparent",
            color,
            1.5,
            LineStyle.Dashed,
            6,
            1);

    /// <summary>
    /// 脉冲：只动边框，不改填充色。
    /// </summary>
    /// <remarks>
    /// 往外撑两像素而不是贴着元素外沿：贴上去的话脉冲会压在元素自己的描边上，
    /// 那圈描边看起来在忽粗忽细。填充一律透明——改填充色会与样式面板里用户自己选的颜色打架。
    /// </remarks>
    private static DrawShape Pulse(
        string elementId,
        SpatialRect bounds,
        string color,
        Theme theme,
        double phase)
    {
        var strength = Strength(phase, theme.HighlightPulseCount);

        return new DrawShape(
            PulsePrefix + elementId,
            NodeShape.Rounded,
            bounds.Inflate(2),
            "transparent",
            color,
            1.5 + (3 * strength),
            LineStyle.Solid,
            4,
            0.35 + (0.65 * strength));
    }

    /// <summary>角标：右上角一个圆点，落在轮廓之外。</summary>
    /// <remarks>
    /// 沿对角线往外让开轮廓一圈再留一点缝。只往上挪一点点的话，圆点会压在轮廓或脉冲的
    /// 上边框上——三种手段叠在一起时，那一下就变成"互相盖住"了。
    /// </remarks>
    private static DrawShape Badge(string elementId, SpatialRect bounds, string color, Theme theme)
    {
        var size = theme.HighlightBadgeSize;
        var inset = theme.HighlightOutlineInset;
        const double gap = 2;

        var centerX = bounds.Right + inset + gap + (size / 2);
        var centerY = bounds.Y - inset - gap - (size / 2);

        return new DrawShape(
            BadgePrefix + elementId,
            NodeShape.Circle,
            new SpatialRect(centerX - (size / 2), centerY - (size / 2), size, size),
            color,
            "transparent",
            0,
            LineStyle.Solid,
            size / 2,
            1);
    }

    /// <summary>撤销/重做符号：贴在角标左侧，颜色沿用来源色。</summary>
    private static DrawText Symbol(
        string elementId,
        SpatialRect badge,
        string color,
        Theme theme,
        bool undo)
    {
        var size = badge.Height + 3;

        return new DrawText(
            SymbolPrefix + elementId,
            undo ? "↶" : "↷",
            new SpatialRect(badge.X - size - 2, badge.Y, size, size),
            color,
            theme.FontFamily,
            size,
            FontWeight.Bold);
    }

    /// <summary>
    /// 相位对应的脉冲强度，取值 0 到 1。
    /// </summary>
    /// <remarks>
    /// 余弦而不是锯齿：锯齿在每一轮边界处会从最亮直接跳回最暗，看起来像闪了一下，
    /// 而脉冲要的是"渐亮渐暗"。次数由主题给，于是"一秒半脉冲三次"这件事只有一处定义。
    /// </remarks>
    public static double Strength(double phase, int pulseCount)
    {
        var cycles = Math.Max(1, pulseCount);
        var angle = 2 * Math.PI * cycles * phase;

        return 0.5 + (0.5 * Math.Cos(angle));
    }

    /// <summary>一个元素在基准列表里的外接框，取它全部指令的并集。</summary>
    private static SpatialRect? BoundsOf(IReadOnlyList<DrawCommand> commands, string elementId)
    {
        SpatialRect? bounds = null;

        foreach (var command in commands)
        {
            if (!string.Equals(command.ElementId, elementId, StringComparison.Ordinal))
            {
                continue;
            }

            if (Box(command) is not { } box)
            {
                continue;
            }

            bounds = bounds is { } current ? current.Union(box) : box;
        }

        return bounds;
    }

    private static SpatialRect? Box(DrawCommand command) => command switch
    {
        DrawShape shape => shape.Rect,
        DrawText text => text.Box,
        DrawPolyline polyline => PolylineBounds(polyline.Points),
        _ => null,
    };

    private static SpatialRect? PolylineBounds(IReadOnlyList<DrawPoint> points)
    {
        if (points.Count == 0)
        {
            return null;
        }

        var left = points[0].X;
        var top = points[0].Y;
        var right = left;
        var bottom = top;

        foreach (var point in points)
        {
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }

        return new SpatialRect(left, top, right - left, bottom - top);
    }
}
