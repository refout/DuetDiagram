using DuetDiagram.Core.Model;
using DuetDiagram.Layout;

namespace DuetDiagram.Render;

/// <summary>
/// 把一份文档与它的布局结果转成绘制列表。
/// </summary>
/// <remarks>
/// <para>
/// **纯函数。** 同样的文档、同样的布局结果、同样的主题与度量器，永远给出同一份列表。
/// 没有缓存，也不看"上次画的是什么"——要不要重建由上游按结构哈希与视觉哈希决定，
/// 这里只负责重建得对。
/// </para>
/// <para>
/// **层内次序**：组合在最下，然后连线，最后节点。连线画在节点之下，是因为
/// 折线的两端落在节点边界上，压在节点上会把边界盖住，看上去像线穿进了框里。
/// 组合在连线之下，是因为成员节点就画在组合框里面。
/// </para>
/// <para>
/// **层与层之间按图层次序。** 每一档内部仍然按上面那个次序出笔，
/// 图层只决定档与档的先后（见 <see cref="LayerPlan"/>）。没有图层的文档只有一档，
/// 出笔次序与从前逐字节相同。连线与组合没有图层归属，画在缺省层里；
/// **端点落在被藏起来的图层上的连线不画**——一条线连着看不见的东西，
/// 画出来是一根悬空的线。
/// </para>
/// <para>
/// 选中、高亮、拖动预览这些都不在这里。它们是画布的状态而不是文档的状态，
/// 由画布在这份列表之上叠加。
/// </para>
/// </remarks>
public static class SceneBuilder
{
    /// <summary>
    /// 量一个节点的框尺寸。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **布局输入里的尺寸必须用这个方法量。** 绘制列表把标签在框里居中排，
    /// 而居中的前提是框的尺寸就是"标签块加留白"。两处用不同的算法量，
    /// 表现是文字贴着边框或者偏在一边，而那种偏差看起来像对齐算错了。
    /// </para>
    /// <para>
    /// 留白加在这里而不是加在绘制那一步：尺寸是布局的输入，必须在布局之前定下来，
    /// 而绘制那一步只能拿到定好的尺寸。
    /// </para>
    /// </remarks>
    public static Size MeasureNode(NodeDef node, Theme theme, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(measurer);

        var appearance = theme.Node(node);
        var text = theme.Text(node.Text, appearance.Text);
        var block = TextLayout.MeasureBlock(measurer, TextLayout.SplitLines(node.Label), text);

        return new Size(
            Math.Max(block.Width + (theme.NodePaddingX * 2), theme.MinNodeWidth),
            Math.Max(block.Height + (theme.NodePaddingY * 2), theme.MinNodeHeight));
    }

    /// <summary>
    /// 量一个组合标题的尺寸。
    /// </summary>
    /// <remarks>
    /// 只量标题，不量整个组合框：框的尺寸由成员的最终坐标决定，
    /// 而坐标要等布局算完才有。这个方法给的是布局**输入**里要用的那部分。
    /// </remarks>
    public static Size MeasureComposite(CompositeDef composite, Theme theme, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(composite);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(measurer);

        var appearance = theme.Composite(composite);
        var text = theme.Text(null, appearance.Text);
        var block = TextLayout.MeasureBlock(measurer, TextLayout.SplitLines(composite.Label), text);

        return new Size(block.Width, Math.Max(block.Height, theme.CompositeHeader));
    }

    /// <summary>
    /// 构建绘制列表。
    /// </summary>
    /// <param name="document">文档。元素的声明顺序就是绘制顺序。</param>
    /// <param name="layout">布局结果。提供坐标、折线与内容范围。</param>
    /// <param name="theme">外观查表。</param>
    /// <param name="measurer">文本度量。</param>
    public static DrawList Build(
        DiagramDocument document,
        EngineLayoutResult layout,
        Theme theme,
        ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(measurer);

        var placed = new Dictionary<string, PlacedNode>(StringComparer.Ordinal);

        foreach (var node in layout.Nodes)
        {
            placed[node.Id] = node;
        }

        var commands = new List<DrawCommand>();
        var plan = LayerPlan.Of(document);

        // 缺省层在最底下：组合框、连线，以及没有图层归属的节点都在这一档。
        AppendComposites(document, CompositeBoxes(document, placed, theme, plan), theme, measurer, commands);
        AppendEdges(document, layout, theme, measurer, commands, plan);
        AppendNodes(document, placed, theme, measurer, commands, plan, layer: null);

        // 已声明的图层按次序一档一档往上画。
        foreach (var layer in plan.PaintOrder.Skip(1))
        {
            AppendNodes(document, placed, theme, measurer, commands, plan, layer);
        }

        return new DrawList(commands, layout.Width, layout.Height, theme.Background)
        {
            Blocked = plan.BlockedNodes,
        };
    }

    #region 组合

    /// <summary>
    /// 画组合框与标题，外层在前。
    /// </summary>
    /// <remarks>
    /// 外层必须先画：内层框叠在外层框之上，反过来会把外层的标题盖掉。
    /// 同一深度内按声明顺序，与节点一致。
    /// </remarks>
    private static void AppendComposites(
        DiagramDocument document,
        IReadOnlyDictionary<string, SpatialRect> boxes,
        Theme theme,
        ITextMeasurer measurer,
        List<DrawCommand> commands)
    {
        if (document.Composites.Count == 0)
        {
            return;
        }

        var parents = document.Composites.ToDictionary(c => c.Id, c => c.Parent, StringComparer.Ordinal);

        var ordered = document.Composites
            .Select((composite, index) => (composite, index, depth: Depth(composite.Id, parents)))
            .OrderBy(item => item.depth)
            .ThenBy(item => item.index);

        foreach (var (composite, _, _) in ordered)
        {
            if (!boxes.TryGetValue(composite.Id, out var box))
            {
                continue;
            }

            var appearance = theme.Composite(composite);

            commands.Add(new DrawShape(
                composite.Id,
                NodeShape.Rounded,
                box,
                appearance.Fill,
                appearance.Stroke,
                appearance.Weight,
                appearance.Border,
                appearance.Radius,
                appearance.Opacity));

            var text = theme.Text(null, appearance.Text);
            var band = new SpatialRect(
                box.X + theme.CompositePadding,
                box.Y,
                Math.Max(box.Width - (theme.CompositePadding * 2), 0),
                theme.CompositeHeader);

            AppendBlock(commands, composite.Id, composite.Label, band, text, measurer);
        }
    }

    /// <summary>
    /// 算出每个有成员的组合占哪块地方。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 框只包住成员，再往外留内边距与标题的高度。布局层自己也算过一份包围盒，
    /// 但那份只包住成员的可见范围、用来定连线的落点，它明确不管渲染要留的那一圈。
    /// 两处都要算是因为要的东西不同：布局要的是线落在哪，渲染要的是框画到哪。
    /// </para>
    /// <para>
    /// 没有成员的组合不出现在结果里——它没有地方可画。画一个空框出来，
    /// 用户会以为那是个可以往里放东西的位置，而它其实只是没填成员。
    /// </para>
    /// <para>
    /// **被藏起来的成员不算进框里。** 成员全被藏起来的组合因此算不出框，也就不画。
    /// 让框仍然包着看不见的成员的话，用户会看到一个空荡荡的大框，
    /// 而看不出它是为了谁留的。
    /// </para>
    /// </remarks>
    private static Dictionary<string, SpatialRect> CompositeBoxes(
        DiagramDocument document,
        IReadOnlyDictionary<string, PlacedNode> placed,
        Theme theme,
        LayerPlan plan)
    {
        var members = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var composite in document.Composites)
        {
            members[composite.Id] = composite.Members;
        }

        var boxes = new Dictionary<string, SpatialRect>(StringComparer.Ordinal);

        foreach (var composite in document.Composites)
        {
            Box(composite.Id, members, placed, theme, plan, boxes, []);
        }

        return boxes;
    }

    private static SpatialRect? Box(
        string id,
        IReadOnlyDictionary<string, IReadOnlyList<string>> members,
        IReadOnlyDictionary<string, PlacedNode> placed,
        Theme theme,
        LayerPlan plan,
        Dictionary<string, SpatialRect> boxes,
        HashSet<string> visiting)
    {
        if (boxes.TryGetValue(id, out var cached))
        {
            return cached;
        }

        if (!members.TryGetValue(id, out var children) || !visiting.Add(id))
        {
            return null;
        }

        SpatialRect? union = null;

        foreach (var child in children)
        {
            if (plan.HiddenNodes.Contains(child))
            {
                continue;
            }

            var childBox = placed.TryGetValue(child, out var node)
                ? new SpatialRect(node.X, node.Y, node.Width, node.Height)
                : Box(child, members, placed, theme, plan, boxes, visiting);

            if (childBox is null)
            {
                continue;
            }

            union = union is null ? childBox : union.Value.Union(childBox.Value);
        }

        visiting.Remove(id);

        if (union is null)
        {
            return null;
        }

        var inner = union.Value;
        var outline = new SpatialRect(
            inner.X - theme.CompositePadding,
            inner.Y - theme.CompositePadding - theme.CompositeHeader,
            inner.Width + (theme.CompositePadding * 2),
            inner.Height + (theme.CompositePadding * 2) + theme.CompositeHeader);

        boxes[id] = outline;

        return outline;
    }

    /// <summary>组合的嵌套深度。顶层为零。</summary>
    /// <remarks>
    /// 用访问过的集合挡住成环。合法的文档里不会有环，而画一张有环的图不该把程序拖死——
    /// 校验器会报这件事，渲染只需要别崩。
    /// </remarks>
    private static int Depth(string id, IReadOnlyDictionary<string, string?> parents)
    {
        var depth = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal) { id };
        var current = parents.TryGetValue(id, out var parent) ? parent : null;

        while (current is not null && seen.Add(current))
        {
            depth++;
            current = parents.TryGetValue(current, out var next) ? next : null;
        }

        return depth;
    }

    #endregion

    #region 连线

    private static void AppendEdges(
        DiagramDocument document,
        EngineLayoutResult layout,
        Theme theme,
        ITextMeasurer measurer,
        List<DrawCommand> commands,
        LayerPlan plan)
    {
        var routed = new Dictionary<string, RoutedEdge>(StringComparer.Ordinal);

        foreach (var edge in layout.Edges)
        {
            routed[edge.Id] = edge;
        }

        foreach (var edge in document.Edges)
        {
            if (!routed.TryGetValue(edge.Id, out var route) || route.Points.Length < 2)
            {
                continue;
            }

            // 端点在被藏起来的图层上就不画：一条线连着看不见的东西，
            // 画出来是一根悬空的线，而用户会去找它另一头在哪。
            if (plan.HiddenNodes.Contains(edge.From) || plan.HiddenNodes.Contains(edge.To))
            {
                continue;
            }

            var appearance = theme.Edge(edge);
            var points = new DrawPoint[route.Points.Length];

            for (var index = 0; index < route.Points.Length; index++)
            {
                points[index] = new DrawPoint(route.Points[index].X, route.Points[index].Y);
            }

            commands.Add(new DrawPolyline(
                edge.Id,
                points,
                appearance.Color,
                appearance.Weight,
                appearance.Line,
                appearance.Arrow));

            if (edge.Label.Length == 0)
            {
                continue;
            }

            var text = theme.Text(null, theme.EdgeText);
            var anchor = Along(points, LabelFactor(edge.Style.LabelPosition));

            AppendBlockAt(commands, edge.Id, edge.Label, anchor, text, measurer);
        }
    }

    /// <summary>
    /// 标签落在折线的哪个位置。
    /// </summary>
    /// <remarks>
    /// 起点与终点各让开百分之十五的线长，而不是取两端的端点：端点就在节点的边界上，
    /// 标签压在那里会同时盖住节点边框与箭头，两个都看不清。
    /// </remarks>
    private static double LabelFactor(LabelPosition? position) => position switch
    {
        LabelPosition.Start => 0.15,
        LabelPosition.End => 0.85,
        _ => 0.5,
    };

    /// <summary>折线上按总长比例取一点。</summary>
    private static DrawPoint Along(IReadOnlyList<DrawPoint> points, double factor)
    {
        if (points.Count == 0)
        {
            return default;
        }

        if (points.Count == 1)
        {
            return points[0];
        }

        var total = 0.0;

        for (var index = 1; index < points.Count; index++)
        {
            total += Distance(points[index - 1], points[index]);
        }

        if (total <= 0)
        {
            return points[0];
        }

        var target = Math.Clamp(factor, 0, 1) * total;
        var walked = 0.0;

        for (var index = 1; index < points.Count; index++)
        {
            var segment = Distance(points[index - 1], points[index]);

            if (walked + segment >= target)
            {
                var local = segment <= 0 ? 0 : (target - walked) / segment;

                return new DrawPoint(
                    points[index - 1].X + ((points[index].X - points[index - 1].X) * local),
                    points[index - 1].Y + ((points[index].Y - points[index - 1].Y) * local));
            }

            walked += segment;
        }

        return points[^1];
    }

    private static double Distance(DrawPoint left, DrawPoint right)
    {
        var dx = right.X - left.X;
        var dy = right.Y - left.Y;

        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    #endregion

    #region 节点

    /// <summary>
    /// 出一档图层的节点指令。
    /// </summary>
    /// <param name="layer">要出的那一档。空表示缺省层。</param>
    /// <remarks>
    /// 按档遍历而不是给每个节点算一个档位再排序：排序要遍历两遍、还要为并列定规则，
    /// 而档的个数就是图层的个数，逐档筛一遍足够，也更容易看出"每一档内部保持声明顺序"。
    /// </remarks>
    private static void AppendNodes(
        DiagramDocument document,
        IReadOnlyDictionary<string, PlacedNode> placed,
        Theme theme,
        ITextMeasurer measurer,
        List<DrawCommand> commands,
        LayerPlan plan,
        string? layer)
    {
        foreach (var node in document.Nodes)
        {
            if (!string.Equals(plan.EffectiveLayerId(node), layer, StringComparison.Ordinal)
                || plan.HiddenNodes.Contains(node.Id))
            {
                continue;
            }

            if (!placed.TryGetValue(node.Id, out var box))
            {
                continue;
            }

            var appearance = theme.Node(node);
            var rect = new SpatialRect(box.X, box.Y, box.Width, box.Height);

            commands.Add(new DrawShape(
                node.Id,
                node.Shape,
                rect,
                appearance.Fill,
                appearance.Stroke,
                appearance.Weight,
                appearance.Border,
                appearance.Radius,
                appearance.Opacity));

            var text = theme.Text(node.Text, appearance.Text);

            AppendBlock(commands, node.Id, node.Label, rect, text, measurer);
        }
    }

    #endregion

    #region 文本

    /// <summary>
    /// 把一段标签排在容器里。
    /// </summary>
    /// <remarks>
    /// 两段定位：先按对齐方式把整块放进容器，再按同一个对齐方式把每一行放进块里。
    /// 看似重复，其实是两件事——块宽取最长的那一行，短行要靠行内对齐才能跟长行对齐。
    /// 只做一段的话，居中的标签会变成左边对齐。
    /// </remarks>
    private static void AppendBlock(
        List<DrawCommand> commands,
        string elementId,
        string label,
        SpatialRect container,
        TextAppearance text,
        ITextMeasurer measurer)
    {
        var lines = TextLayout.SplitLines(label);

        if (lines.Count == 0)
        {
            return;
        }

        var block = TextLayout.MeasureBlock(measurer, lines, text);
        var left = text.Align switch
        {
            TextAlign.Start => container.X,
            TextAlign.End => container.Right - block.Width,
            _ => container.X + ((container.Width - block.Width) / 2),
        };

        var top = text.Vertical switch
        {
            VerticalAlign.Start => container.Y,
            VerticalAlign.End => container.Bottom - block.Height,
            _ => container.Y + ((container.Height - block.Height) / 2),
        };

        EmitLines(commands, elementId, lines, new DrawPoint(left, top), block.Width, text, measurer);
    }

    /// <summary>把一段标签排在某个点周围。连线标签用它。</summary>
    private static void AppendBlockAt(
        List<DrawCommand> commands,
        string elementId,
        string label,
        DrawPoint center,
        TextAppearance text,
        ITextMeasurer measurer)
    {
        var lines = TextLayout.SplitLines(label);

        if (lines.Count == 0)
        {
            return;
        }

        var block = TextLayout.MeasureBlock(measurer, lines, text);

        EmitLines(
            commands,
            elementId,
            lines,
            new DrawPoint(center.X - (block.Width / 2), center.Y - (block.Height / 2)),
            block.Width,
            text,
            measurer);
    }

    /// <summary>
    /// 逐行出指令。
    /// </summary>
    /// <remarks>
    /// 空行只占高度，不出指令：一条宽度为零的文本指令画不出东西，
    /// 却会让快照里多出一堆没有意义的行。
    /// </remarks>
    private static void EmitLines(
        List<DrawCommand> commands,
        string elementId,
        IReadOnlyList<string> lines,
        DrawPoint origin,
        double blockWidth,
        TextAppearance text,
        ITextMeasurer measurer)
    {
        var lineHeight = TextLayout.LineHeight(text);

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];

            if (line.Length == 0)
            {
                continue;
            }

            var width = measurer.Measure(line, text.FontFamily, text.FontSize, text.Weight).Width;
            var left = text.Align switch
            {
                TextAlign.Start => origin.X,
                TextAlign.End => origin.X + blockWidth - width,
                _ => origin.X + ((blockWidth - width) / 2),
            };

            commands.Add(new DrawText(
                elementId,
                line,
                new SpatialRect(left, origin.Y + (index * lineHeight), width, lineHeight),
                text.Color,
                text.FontFamily,
                text.FontSize,
                text.Weight));
        }
    }

    #endregion
}

