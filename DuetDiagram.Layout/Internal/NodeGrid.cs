namespace DuetDiagram.Layout.Internal;

/// <summary>
/// 节点的均匀网格索引。
/// </summary>
/// <remarks>
/// <para>
/// 路由一开始写成对每条边遍历全部节点，这在千节点上是平方复杂度：950 条边乘 1000 个节点，
/// 实测 48 毫秒，和布局引擎本身一个量级——补齐逻辑本来应该可以忽略不计。
/// </para>
/// <para>
/// 用网格而不是按层分桶，是因为固定节点的纵坐标是自由的，层结构在它那里是断的，
/// 按层分桶会把锚点归到错误的桶里，查询就会漏。网格只依赖坐标，与层结构无关。
/// </para>
/// <para>
/// 查询结果写进调用方给的缓冲区，去重靠一个代次数组而不是每次新建集合。
/// 这两件事合起来把每条边的多次查询变成零分配——每条边都新建一个哈希集合的话，
/// 千条边就是几千次分配，光垃圾回收就比索引本身省下的时间还多。
/// </para>
/// <para>
/// 格子边长取节点尺寸量级的两倍左右：太小会让每个查询碰上很多空格子，
/// 太大就退化成遍历全部节点。
/// </para>
/// </remarks>
internal sealed class NodeGrid
{
    private const double CellSize = 160;

    private readonly PlacedNode[] _nodes;
    private readonly Dictionary<(int Column, int Row), List<int>> _cells = [];
    private readonly int[] _visited;
    private int _generation;

    public NodeGrid(PlacedNode[] nodes)
    {
        _nodes = nodes;
        _visited = new int[nodes.Length];

        for (var i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i];

            ForEachCell(node.X, node.Y, node.Right, node.Bottom, (column, row) =>
            {
                if (!_cells.TryGetValue((column, row), out var bucket))
                {
                    bucket = [];
                    _cells[(column, row)] = bucket;
                }

                bucket.Add(i);
            });
        }
    }

    /// <summary>
    /// 取与给定矩形相交的候选节点，写进 <paramref name="buffer"/>，返回个数。
    /// 结果是候选集，调用方仍需做精确的相交判定。
    /// </summary>
    /// <remarks>
    /// 格子遍历写成嵌套循环而不是带状态机的迭代器：迭代器每调用一次都会分配一个状态机对象，
    /// 而这个方法每条边要调用多次，千条边就是几千次分配，光回收就抵掉了索引省下的时间。
    /// </remarks>
    public int Query(double x0, double y0, double x1, double y1, List<PlacedNode> buffer)
    {
        buffer.Clear();
        _generation++;

        var minColumn = (int)Math.Floor(Math.Min(x0, x1) / CellSize);
        var maxColumn = (int)Math.Floor(Math.Max(x0, x1) / CellSize);
        var minRow = (int)Math.Floor(Math.Min(y0, y1) / CellSize);
        var maxRow = (int)Math.Floor(Math.Max(y0, y1) / CellSize);

        for (var column = minColumn; column <= maxColumn; column++)
        {
            for (var row = minRow; row <= maxRow; row++)
            {
                if (!_cells.TryGetValue((column, row), out var bucket))
                {
                    continue;
                }

                foreach (var index in bucket)
                {
                    // 一个节点可能横跨多个格子，用代次标记去重，避免重复判定。
                    if (_visited[index] == _generation)
                    {
                        continue;
                    }

                    _visited[index] = _generation;
                    buffer.Add(_nodes[index]);
                }
            }
        }

        return buffer.Count;
    }

    private static void ForEachCell(double x0, double y0, double x1, double y1, Action<int, int> visit)
    {
        var minColumn = (int)Math.Floor(x0 / CellSize);
        var maxColumn = (int)Math.Floor(x1 / CellSize);
        var minRow = (int)Math.Floor(y0 / CellSize);
        var maxRow = (int)Math.Floor(y1 / CellSize);

        for (var column = minColumn; column <= maxColumn; column++)
        {
            for (var row = minRow; row <= maxRow; row++)
            {
                visit(column, row);
            }
        }
    }
}
