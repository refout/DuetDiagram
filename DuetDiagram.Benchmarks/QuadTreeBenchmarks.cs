using BenchmarkDotNet.Attributes;
using DuetDiagram.Render;

namespace DuetDiagram.Benchmarks;

/// <summary>
/// 四叉树视口索引的耗时基线。
/// </summary>
/// <remarks>
/// 指标是"一千节点查询低于一毫秒"。那个数用秒表读一次是读不出来的——
/// 单次查询在微秒量级，秒表的分辨率与系统噪声都能把结论淹掉。
/// 因此精确数字在这里用统计方法测，单元测试里只留一条很宽的上限，
/// 盯的是"有没有退化成逐个遍历"。
/// </remarks>
[MemoryDiagnoser]
[ShortRunJob]
public class QuadTreeBenchmarks
{
    private QuadTree _tree = null!;
    private List<string> _buffer = null!;

    /// <summary>查询面积约占全部内容的四十分之一，接近真实视口的比例。</summary>
    private SpatialRect _viewport;

    [GlobalSetup]
    public void Setup()
    {
        var items = new (string Id, SpatialRect Rect)[1000];

        for (var i = 0; i < items.Length; i++)
        {
            items[i] = ($"n{i}", new SpatialRect((i % 40) * 100, (i / 40) * 100, 80, 40));
        }

        _tree = QuadTree.Build(items);
        _buffer = new List<string>(128);
        _viewport = CullingPolicy.Default.VisibleArea(new SpatialRect(0, 0, 500, 500));
    }

    /// <summary>单次视口查询。</summary>
    [Benchmark]
    public int Query() => _tree.Query(_viewport, _buffer);

    /// <summary>建树。内容整体变化时需要重建。</summary>
    [Benchmark]
    public int Build()
    {
        var items = new (string Id, SpatialRect Rect)[1000];

        for (var i = 0; i < items.Length; i++)
        {
            items[i] = ($"n{i}", new SpatialRect((i % 40) * 100, (i / 40) * 100, 80, 40));
        }

        return QuadTree.Build(items).Count;
    }

    /// <summary>插入一千次。用来确认增删确实是按深度而不是按数量的。</summary>
    [Benchmark]
    public int Insert1000()
    {
        var tree = new QuadTree(new SpatialRect(0, 0, 4000, 2500));

        for (var i = 0; i < 1000; i++)
        {
            tree.Insert($"n{i}", new SpatialRect((i % 40) * 100, (i / 40) * 100, 80, 40));
        }

        return tree.Count;
    }
}
