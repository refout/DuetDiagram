namespace DuetDiagram.Render;

/// <summary>
/// 一份绘制列表的空间索引。
/// </summary>
/// <remarks>
/// <para>
/// **按元素建索引，按指令取结果。** 索引里的对象是元素（节点、连线、组合），
/// 因为一个元素可能对应好几条指令——一个节点至少一个形状加若干行文本，
/// 按指令建索引会把同一个位置重复存好几遍，查询结果还要再去重。
/// </para>
/// <para>
/// **取结果时按绘制列表的原顺序走一遍。** 顺序就是层叠顺序，剔除只是少画几条，
/// 不能把留下来的那几条换个先后——那会让连线跑到节点上面去。
/// 查询结果因此是列表的一个子序列，而不是"命中的那些"按索引给出的顺序。
/// </para>
/// <para>
/// 索引建好之后只读，可以跨帧复用。绘制列表换了要重建一份：
/// 元素挪过位置之后，旧的包围盒会让它在新位置查不到，而表现是"拖到某些地方就看不见了"。
/// </para>
/// </remarks>
public sealed class CullingIndex
{
    private readonly QuadTree _tree;
    private readonly List<string> _hits = [];
    private readonly HashSet<string> _visible = new(StringComparer.Ordinal);
    private readonly List<DrawCommand> _kept = [];

    /// <summary>给一份绘制列表建索引。</summary>
    public CullingIndex(DrawList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        Source = list;

        var bounds = new Dictionary<string, SpatialRect>(StringComparer.Ordinal);

        foreach (var command in list.Commands)
        {
            if (Box(command) is not { } box)
            {
                continue;
            }

            bounds[command.ElementId] = bounds.TryGetValue(command.ElementId, out var existing)
                ? existing.Union(box)
                : box;
        }

        _tree = QuadTree.Build(bounds.Select(pair => (pair.Key, pair.Value)));

        ElementCount = bounds.Count;
        VisibleCommands = list.Commands.Count;
    }

    /// <summary>建索引用的那份列表。</summary>
    public DrawList Source { get; }

    /// <summary>列表里有多少个不同的元素。</summary>
    public int ElementCount { get; }

    /// <summary>最近一次查询保留下来的指令数。</summary>
    public int VisibleCommands { get; private set; }

    /// <summary>最近一次查询剔掉的指令数。</summary>
    public int CulledCommands => Source.Commands.Count - VisibleCommands;

    /// <summary>最近一次查询剔掉的比例，取值 0 到 1。</summary>
    public double CullRate =>
        Source.Commands.Count == 0 ? 0 : (double)CulledCommands / Source.Commands.Count;

    /// <summary>
    /// 取出与区域相交的那些指令，按原顺序。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 区域应当用 <see cref="CullingPolicy.VisibleArea"/> 从视口扩出来，
    /// 不要直接把视口传进来——贴边的元素会在平移时一闪一闪。
    /// </para>
    /// <para>
    /// **返回的是内部复用的缓冲。** 下一次查询会把它清掉，所以拿到之后要么立刻用完，
    /// 要么自己复制一份。每帧都新建一个列表的话，千节点图上光分配就够抵消剔除省下的开销。
    /// </para>
    /// </remarks>
    public IReadOnlyList<DrawCommand> Visible(SpatialRect area)
    {
        _tree.Query(area, _hits);

        _visible.Clear();

        foreach (var id in _hits)
        {
            _visible.Add(id);
        }

        _kept.Clear();

        foreach (var command in Source.Commands)
        {
            if (_visible.Contains(command.ElementId))
            {
                _kept.Add(command);
            }
        }

        VisibleCommands = _kept.Count;

        return _kept;
    }

    /// <summary>
    /// 一条指令占的地方。
    /// </summary>
    /// <remarks>
    /// 折线取点序的包围盒，而不是"沿线的一条细带"：细带要算每段的法向偏移，
    /// 而包围盒只会多取一点，多取的部分画出来也不显眼。少取则会漏画，
    /// 那是"拖动时某些元素一半画出来一半没有"。
    /// </remarks>
    private static SpatialRect? Box(DrawCommand command) => command switch
    {
        DrawShape shape => shape.Rect,
        DrawText text => text.Box,
        DrawPolyline line => PolylineBox(line.Points),
        _ => null,
    };

    private static SpatialRect? PolylineBox(IReadOnlyList<DrawPoint> points)
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
}
