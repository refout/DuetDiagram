using DuetDiagram.Core.Model;

namespace DuetDiagram.Render;

/// <summary>
/// 一个组合在画面上长什么样。
/// </summary>
/// <remarks>
/// <para>
/// **四种形态要一眼分得开，而且不靠颜色。** 与变更高亮同一条口径：颜色只是辅助线索，
/// 色觉障碍、黑白打印、深浅主题都会把它抹掉，而"这是分组还是泳道"是结构信息。
/// 所以四种各有一套几何上的记号：
/// </para>
/// <list type="bullet">
/// <item>分组：圆角实线框 + 顶上一条标题带。</item>
/// <item>泳道：**直角**框 + 顺着流向那一侧的池头带 + 带上一个指向流向的箭头。</item>
/// <item>子流程：圆角框 + 内缩一圈的第二条线（双线） + 右上角的展开/折叠标记。</item>
/// <item>组合框：**虚线**框 + 标题带（它是纯标注，不参与布局）。</item>
/// </list>
/// <para>
/// **框画在成员下面。** 画在上面的话，成员被一个半透明的框罩住，颜色全部偏一层——
/// 而用户会以为是自己配的色不对。这一条由 <see cref="SceneBuilder"/> 的绘制顺序保证，
/// 这里只负责画框本身。
/// </para>
/// <para>
/// **框的位置不是组合的坐标。** 组合没有坐标字段，框由成员的最终位置算出来
/// （见 <c>SceneBuilder</c> 里算包围盒那一处）。所以"拖组合"实际改的是全部成员的固定位置，
/// 而不是某个不存在的组合坐标。
/// </para>
/// </remarks>
internal static class CompositeFrame
{
    /// <summary>展开／折叠标记的边长。</summary>
    private const double MarkerSize = 14;

    /// <summary>标记与框边的间距。</summary>
    private const double MarkerMargin = 5;

    /// <summary>子流程那条内线的内缩量。</summary>
    private const double DoubleLineInset = 4;

    /// <summary>
    /// 画一个组合的框。
    /// </summary>
    /// <param name="composite">这个组合。</param>
    /// <param name="box">框占的那一块。</param>
    /// <param name="theme">外观查表。</param>
    /// <param name="fallback">组合自己没声明方向时按哪个方向画（取文档的主方向）。</param>
    /// <param name="band">标题该画在哪一条带上。</param>
    public static IReadOnlyList<DrawCommand> Commands(
        CompositeDef composite,
        SpatialRect box,
        Theme theme,
        Direction fallback,
        out SpatialRect band)
    {
        ArgumentNullException.ThrowIfNull(composite);
        ArgumentNullException.ThrowIfNull(theme);

        var appearance = theme.Composite(composite);
        var commands = new List<DrawCommand>(4);
        var horizontal = IsHorizontal(composite.Direction ?? fallback);

        switch (composite)
        {
            case LaneDef:
                band = Lane(commands, composite, box, theme, appearance, horizontal);
                break;

            case SubflowDef:
                band = Subflow(commands, composite, box, theme, appearance);
                break;

            case ComboDef:
                band = Annotation(commands, composite, box, theme, appearance);
                break;

            default:
                band = Group(commands, composite, box, theme, appearance);
                break;
        }

        return commands;
    }

    #region 四种形态

    /// <summary>分组：圆角实线框加一条标题带。这是最"安静"的一种，别的形态都在它之上加记号。</summary>
    private static SpatialRect Group(
        List<DrawCommand> commands,
        CompositeDef composite,
        SpatialRect box,
        Theme theme,
        NodeAppearance appearance)
    {
        commands.Add(Frame(composite.Id, NodeShape.Rounded, box, appearance, appearance.Border));

        return TopBand(box, theme);
    }

    /// <summary>
    /// 泳道：直角框，池头带在流向的一侧。
    /// </summary>
    /// <remarks>
    /// 头带放在哪一侧由流向决定：横向流的池头在左边（道是上下叠的），
    /// 纵向流的池头在上边（道是左右排的）。这不是画着好看的——它是"按责任方划分的条带"
    /// 在画面上的读法，位置放错的话读者会把道序看反。
    /// </remarks>
    private static SpatialRect Lane(
        List<DrawCommand> commands,
        CompositeDef composite,
        SpatialRect box,
        Theme theme,
        NodeAppearance appearance,
        bool horizontal)
    {
        // 直角：泳道是一条一条并排的条带，圆角会让相邻两条的角上出现缝。
        var squared = appearance with { Radius = 0 };

        commands.Add(Frame(composite.Id, NodeShape.Rect, box, squared, appearance.Border));

        var thickness = Math.Min(theme.CompositeHeader, horizontal ? box.Width : box.Height);
        var band = horizontal
            ? new SpatialRect(box.X, box.Y, thickness, box.Height)
            : new SpatialRect(box.X, box.Y, box.Width, thickness);

        Arrow(commands, composite.Id, band, theme, horizontal, composite.Direction ?? Direction.LR);

        return band;
    }

    /// <summary>
    /// 子流程：圆角框加内缩一圈的第二条线，右上角带展开／折叠标记。
    /// </summary>
    /// <remarks>
    /// 第二条线是"这里可以折起来"的记号：子流程折叠之后按一个节点参与布局，
    /// 而双线框在画面上一眼就与分组分得开。标记的形状（加号／减号）说清现在折没折——
    /// 这一条只画出来，展开折叠的交互还没有。
    /// </remarks>
    private static SpatialRect Subflow(
        List<DrawCommand> commands,
        CompositeDef composite,
        SpatialRect box,
        Theme theme,
        NodeAppearance appearance)
    {
        commands.Add(Frame(composite.Id, NodeShape.Rounded, box, appearance, appearance.Border));

        // 内线用折线画：它是装饰，不是这个组合的另一个"形状"，
        // 而画成 DrawShape 会让它在命中测试里与框叠成两条指令。
        var inset = new SpatialRect(
            box.X + DoubleLineInset,
            box.Y + DoubleLineInset,
            Math.Max(box.Width - (DoubleLineInset * 2), 0),
            Math.Max(box.Height - (DoubleLineInset * 2), 0));

        commands.Add(Outline(composite.Id, inset, appearance));

        Toggle(commands, composite.Id, box, appearance, composite.Collapsed);

        // 标题带在右上角那个标记之前收住，免得字压在标记上。
        var band = TopBand(box, theme);

        return band with { Width = Math.Max(band.Width - ((MarkerSize + MarkerMargin) * 2), 0) };
    }

    /// <summary>
    /// 组合框：虚线框。
    /// </summary>
    /// <remarks>
    /// **与约束里那句"按普通节点画"不同，这里画的是虚线。** 按普通节点画（实心填充）
    /// 会与分组几乎分不开，而同一段约束的第一句正是"四种形态要一眼分得开，且不靠颜色"——
    /// 两句打架时按第一句办。虚线框还顺带说清了它的语义：它是纯标注，不参与布局。
    /// </remarks>
    private static SpatialRect Annotation(
        List<DrawCommand> commands,
        CompositeDef composite,
        SpatialRect box,
        Theme theme,
        NodeAppearance appearance)
    {
        commands.Add(Frame(composite.Id, NodeShape.Rounded, box, appearance, LineStyle.Dashed));

        return TopBand(box, theme);
    }

    #endregion

    #region 记号

    /// <summary>流向箭头：画在池头带里的一个折线尖括号，指向流向。</summary>
    private static void Arrow(
        List<DrawCommand> commands,
        string elementId,
        SpatialRect band,
        Theme theme,
        bool horizontal,
        Direction direction)
    {
        var size = Math.Min(MarkerSize, Math.Min(band.Width, band.Height) / 2);

        if (size <= 2)
        {
            return;
        }

        var centerX = band.CenterX;
        var centerY = band.CenterY;
        var forward = direction switch
        {
            Direction.BT or Direction.RL => -1,
            _ => 1,
        };

        // 三个点围成一个尖角，尖的一头指向前进方向。
        var points = horizontal
            ? (IReadOnlyList<DrawPoint>)
            [
                new DrawPoint(centerX - (forward * size / 2), centerY - size),
                new DrawPoint(centerX + (forward * size / 2), centerY),
                new DrawPoint(centerX - (forward * size / 2), centerY + size),
            ]
            :
            [
                new DrawPoint(centerX - size, centerY - (forward * size / 2)),
                new DrawPoint(centerX, centerY + (forward * size / 2)),
                new DrawPoint(centerX + size, centerY - (forward * size / 2)),
            ];

        commands.Add(new DrawPolyline(
            elementId,
            points,
            theme.CompositeStroke,
            theme.StrokeWeight,
            LineStyle.Solid,
            Core.Model.ArrowStyle.None));
    }

    /// <summary>展开／折叠标记：右上角一个小方块，里面是加号或减号。</summary>
    private static void Toggle(
        List<DrawCommand> commands,
        string elementId,
        SpatialRect box,
        NodeAppearance appearance,
        bool collapsed)
    {
        var marker = new SpatialRect(
            box.Right - MarkerSize - MarkerMargin,
            box.Y + MarkerMargin,
            MarkerSize,
            MarkerSize);

        if (marker.X < box.X || marker.Bottom > box.Bottom)
        {
            // 框太小，放不下标记。宁可不画也不画到框外面去——那会与相邻的元素叠在一起。
            return;
        }

        commands.Add(Frame(elementId, NodeShape.Rect, marker, appearance with { Opacity = 1 }, LineStyle.Solid));

        var half = MarkerSize / 4;
        var centerX = marker.CenterX;
        var centerY = marker.CenterY;

        commands.Add(Bar(elementId, centerX - half, centerY, centerX + half, centerY, appearance));

        if (collapsed)
        {
            // 折起来时是加号（点一下能展开），展开时是减号。
            commands.Add(Bar(elementId, centerX, centerY - half, centerX, centerY + half, appearance));
        }
    }

    private static DrawPolyline Bar(
        string elementId,
        double x0,
        double y0,
        double x1,
        double y1,
        NodeAppearance appearance) =>
        new(
            elementId,
            [new DrawPoint(x0, y0), new DrawPoint(x1, y1)],
            appearance.Stroke,
            appearance.Weight,
            LineStyle.Solid,
            Core.Model.ArrowStyle.None);

    #endregion

    #region 几何

    private static DrawShape Frame(
        string elementId,
        NodeShape shape,
        SpatialRect box,
        NodeAppearance appearance,
        LineStyle border) =>
        new(
            elementId,
            shape,
            box,
            appearance.Fill,
            appearance.Stroke,
            appearance.Weight,
            border,
            appearance.Radius,
            appearance.Opacity);

    /// <summary>一条闭合的矩形折线。</summary>
    private static DrawPolyline Outline(string elementId, SpatialRect box, NodeAppearance appearance) =>
        new(
            elementId,
            [
                new DrawPoint(box.X, box.Y),
                new DrawPoint(box.Right, box.Y),
                new DrawPoint(box.Right, box.Bottom),
                new DrawPoint(box.X, box.Bottom),
                new DrawPoint(box.X, box.Y),
            ],
            appearance.Stroke,
            appearance.Weight,
            LineStyle.Solid,
            Core.Model.ArrowStyle.None);

    /// <summary>顶上那条标题带。</summary>
    private static SpatialRect TopBand(SpatialRect box, Theme theme) =>
        new(
            box.X + theme.CompositePadding,
            box.Y,
            Math.Max(box.Width - (theme.CompositePadding * 2), 0),
            theme.CompositeHeader);

    /// <summary>横向的流向（左右）还是纵向的（上下）。</summary>
    private static bool IsHorizontal(Direction direction) =>
        direction is Direction.LR or Direction.RL;

    #endregion
}
