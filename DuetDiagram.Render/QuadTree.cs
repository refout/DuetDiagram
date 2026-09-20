namespace DuetDiagram.Render;

/// <summary>
/// 四叉树空间索引。
/// </summary>
/// <remarks>
/// <para>
/// 用途是视口裁剪：画一帧时只需要画视口里那几个元素，而不是全部。
/// 千节点量级上图里绝大多数元素在视口外，逐个判一遍"在不在视口里"，
/// 本身就比画出来便宜不了多少，而查询的开销随节点数增长。
/// </para>
/// <para>
/// **跨界的对象存在能完整容纳它的最小节点上。** 这是本类唯一需要小心的地方：
/// 一个横跨两个象限的矩形如果被塞进其中一个，另一半的查询就找不到它，
/// 表现是"拖动时某些元素一半画出来一半没有"。放不进任何子象限的对象留在当前节点，
/// 而查询会检查途经节点的全部条目，因此它一定被查得到。
/// </para>
/// <para>
/// 插入与删除都是 O(深度)。深度被 <see cref="MaxDepth"/> 封顶，
/// 因此退化输入（例如全部元素落在同一个点上）的代价是有界的，
/// 不会因为反复细分而变成线性扫描。
/// </para>
/// <para>
/// 根的范围在构造时固定。落在范围外的对象会被存在根上——查询仍然找得到它们
/// （正确性不受影响），但它们享受不到细分带来的加速。内容整体变大之后应当重建，
/// 见 <see cref="Build"/>。
/// </para>
/// </remarks>
public sealed class QuadTree
{
    /// <summary>一个节点容纳多少条目后开始细分。</summary>
    public const int DefaultCapacity = 8;

    /// <summary>最大深度。封住退化输入的最坏情况。</summary>
    public const int DefaultMaxDepth = 8;

    private readonly int _capacity;
    private readonly int _maxDepth;
    private readonly Node _root;

    /// <summary>标识到矩形的映射。删除时靠它不必重新遍历整棵树去找对象在哪。</summary>
    private readonly Dictionary<string, SpatialRect> _known = new(StringComparer.Ordinal);

    public QuadTree(SpatialRect bounds, int capacity = DefaultCapacity, int maxDepth = DefaultMaxDepth)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "容量至少要为一。");
        }

        if (maxDepth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth), maxDepth, "深度至少要为一。");
        }

        Bounds = bounds;
        _capacity = capacity;
        _maxDepth = maxDepth;
        _root = new Node(bounds, depth: 0);
    }

    /// <summary>索引覆盖的范围。</summary>
    public SpatialRect Bounds { get; }

    /// <summary>索引里的对象数。</summary>
    public int Count => _known.Count;

    /// <summary>树的实际深度。诊断用，也用来验证细分确实发生了。</summary>
    public int Depth => MeasureDepth(_root);

    /// <summary>
    /// 按一批对象建树，范围取它们的并集。
    /// </summary>
    /// <remarks>
    /// 内容整体变大之后应当用这个方法重建，而不是往旧树里继续塞：
    /// 塞进去的对象会落在根上，查询仍然正确但会退化成接近逐个遍历。
    /// </remarks>
    public static QuadTree Build(
        IEnumerable<(string Id, SpatialRect Rect)> items,
        int capacity = DefaultCapacity,
        int maxDepth = DefaultMaxDepth)
    {
        ArgumentNullException.ThrowIfNull(items);

        var materialized = items as IReadOnlyCollection<(string Id, SpatialRect Rect)> ?? [.. items];

        var tree = new QuadTree(UnionOf(materialized), capacity, maxDepth);

        foreach (var (id, rect) in materialized)
        {
            tree.Insert(id, rect);
        }

        return tree;
    }

    /// <summary>
    /// 放入或更新一个对象。
    /// </summary>
    /// <remarks>
    /// 同一个标识重复放入按更新处理：先把旧的摘掉再放新的。
    /// 不这么做的话同一个标识会在树里留下多处副本，删除时只能清掉一处，
    /// 剩下的那些会永远被查询返回——而它们指向的元素早就挪走了。
    /// </remarks>
    public void Insert(string id, SpatialRect rect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (_known.TryGetValue(id, out var previous))
        {
            RemoveFrom(_root, id, previous);
        }

        _known[id] = rect;
        InsertInto(_root, id, rect);
    }

    /// <summary>移出一个对象。不存在时返回假。</summary>
    public bool Remove(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (!_known.TryGetValue(id, out var rect))
        {
            return false;
        }

        _known.Remove(id);

        return RemoveFrom(_root, id, rect);
    }

    /// <summary>清空全部内容，树结构一并重置。</summary>
    public void Clear()
    {
        _known.Clear();
        _root.Reset();
    }

    /// <summary>
    /// 查询与给定范围相交的对象，写进调用方给的缓冲区，返回个数。
    /// </summary>
    /// <remarks>
    /// 缓冲区由调用方复用。视口查询每帧都会发生，每次都新建一个列表
    /// 会让垃圾回收成为这一层的固定开销。需要一份独立结果的场合用
    /// <see cref="Query(SpatialRect)"/>。
    /// </remarks>
    public int Query(SpatialRect area, List<string> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        results.Clear();
        Visit(_root, area, results);

        return results.Count;
    }

    /// <summary>查询与给定范围相交的对象。</summary>
    public IReadOnlyList<string> Query(SpatialRect area)
    {
        var results = new List<string>();
        Query(area, results);

        return results;
    }

    /// <summary>查询覆盖某个点的对象。</summary>
    public int QueryPoint(double x, double y, List<string> results) =>
        Query(new SpatialRect(x, y, 0, 0), results);

    private static SpatialRect UnionOf(IReadOnlyCollection<(string Id, SpatialRect Rect)> items)
    {
        if (items.Count == 0)
        {
            // 空集给一个可用的最小范围。用零宽零高会让后续的细分除出无穷大。
            return new SpatialRect(0, 0, 1, 1);
        }

        var bounds = items.First().Rect;

        foreach (var (_, rect) in items)
        {
            bounds = bounds.Union(rect);
        }

        // 内容退化成一条线或一个点时，同样给一点厚度，理由同上。
        return new SpatialRect(
            bounds.X,
            bounds.Y,
            Math.Max(bounds.Width, 1),
            Math.Max(bounds.Height, 1));
    }

    private void InsertInto(Node node, string id, SpatialRect rect)
    {
        if (node.Children is not null)
        {
            foreach (var child in node.Children)
            {
                // 只有能被某个子象限完整容纳时才往下放。
                // 放不下就是跨界的，留在当前节点——这是本类正确性的关键。
                if (child.Bounds.Contains(rect))
                {
                    InsertInto(child, id, rect);
                    return;
                }
            }

            node.Entries.Add(new Entry(id, rect));
            return;
        }

        node.Entries.Add(new Entry(id, rect));

        if (node.Entries.Count > _capacity && node.Depth < _maxDepth)
        {
            Subdivide(node);
        }
    }

    private void Subdivide(Node node)
    {
        var halfWidth = node.Bounds.Width / 2;
        var halfHeight = node.Bounds.Height / 2;
        var depth = node.Depth + 1;

        node.Children =
        [
            new Node(new SpatialRect(node.Bounds.X, node.Bounds.Y, halfWidth, halfHeight), depth),
            new Node(new SpatialRect(node.Bounds.X + halfWidth, node.Bounds.Y, halfWidth, halfHeight), depth),
            new Node(new SpatialRect(node.Bounds.X, node.Bounds.Y + halfHeight, halfWidth, halfHeight), depth),
            new Node(new SpatialRect(node.Bounds.X + halfWidth, node.Bounds.Y + halfHeight, halfWidth, halfHeight), depth),
        ];

        // 把能放进子象限的条目挪下去，跨界的留在原地。
        var stay = new List<Entry>(node.Entries.Count);

        foreach (var entry in node.Entries)
        {
            var moved = false;

            foreach (var child in node.Children)
            {
                if (!child.Bounds.Contains(entry.Rect))
                {
                    continue;
                }

                child.Entries.Add(entry);
                moved = true;
                break;
            }

            if (!moved)
            {
                stay.Add(entry);
            }
        }

        node.Entries.Clear();
        node.Entries.AddRange(stay);
    }

    private static bool RemoveFrom(Node node, string id, SpatialRect rect)
    {
        for (var i = 0; i < node.Entries.Count; i++)
        {
            if (string.Equals(node.Entries[i].Id, id, StringComparison.Ordinal))
            {
                node.Entries.RemoveAt(i);
                node.CollapseIfEmptyChildren();

                return true;
            }
        }

        if (node.Children is null)
        {
            return false;
        }

        foreach (var child in node.Children)
        {
            // 对象一定被存进了能完整容纳它的那个子节点，所以按包含关系往下找。
            if (child.Bounds.Contains(rect) && RemoveFrom(child, id, rect))
            {
                node.CollapseIfEmptyChildren();

                return true;
            }
        }

        return false;
    }

    private static void Visit(Node node, SpatialRect area, List<string> results)
    {
        foreach (var entry in node.Entries)
        {
            // 逐条判相交而不是整批收下：根上可能压着一批越界对象，
            // 而它们与当前视口往往毫不相干。
            if (entry.Rect.Intersects(area))
            {
                results.Add(entry.Id);
            }
        }

        if (node.Children is null)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            if (child.Bounds.Intersects(area))
            {
                Visit(child, area, results);
            }
        }
    }

    private static int MeasureDepth(Node node)
    {
        if (node.Children is null)
        {
            return node.Depth;
        }

        var deepest = node.Depth;

        foreach (var child in node.Children)
        {
            deepest = Math.Max(deepest, MeasureDepth(child));
        }

        return deepest;
    }

    private sealed record Entry(string Id, SpatialRect Rect);

    private sealed class Node(SpatialRect bounds, int depth)
    {
        public SpatialRect Bounds { get; } = bounds;

        public int Depth { get; } = depth;

        public List<Entry> Entries { get; } = [];

        public Node[]? Children { get; set; }

        /// <summary>
        /// 子节点全空时把它们收回，变回叶节点。
        /// </summary>
        /// <remarks>
        /// 不做这一步的话，反复增删之后树里会积下大量空节点：
        /// 每次查询都要多走几层空壳，而空壳的数量只增不减。
        /// 收回之后再次插入会重新细分，代价是摊还的。
        /// </remarks>
        public void CollapseIfEmptyChildren()
        {
            if (Children is null)
            {
                return;
            }

            foreach (var child in Children)
            {
                if (!child.IsEmpty())
                {
                    return;
                }
            }

            Children = null;
        }

        private bool IsEmpty()
        {
            if (Entries.Count > 0)
            {
                return false;
            }

            if (Children is null)
            {
                return true;
            }

            foreach (var child in Children)
            {
                if (!child.IsEmpty())
                {
                    return false;
                }
            }

            return true;
        }

        public void Reset()
        {
            Entries.Clear();
            Children = null;
        }
    }
}
