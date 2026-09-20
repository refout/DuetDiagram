using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 四叉树空间索引。
/// </summary>
public sealed class QuadTreeTests
{
    [Fact]
    [Trait("Category", "QuadTree")]
    public void An_empty_tree_returns_nothing()
    {
        var tree = new QuadTree(new SpatialRect(0, 0, 100, 100));

        tree.Count.Should().Be(0);
        tree.Query(new SpatialRect(0, 0, 100, 100)).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "QuadTree")]
    public void Objects_are_found_by_a_query_covering_them()
    {
        var tree = new QuadTree(new SpatialRect(0, 0, 100, 100));

        tree.Insert("a", new SpatialRect(10, 10, 10, 10));
        tree.Insert("b", new SpatialRect(80, 80, 10, 10));

        tree.Query(new SpatialRect(0, 0, 30, 30)).Should().Equal("a");
        tree.Query(new SpatialRect(70, 70, 30, 30)).Should().Equal("b");
        tree.Query(new SpatialRect(0, 0, 100, 100)).Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    [Trait("Category", "QuadTree")]
    public void Objects_outside_the_query_are_not_returned()
    {
        var tree = new QuadTree(new SpatialRect(0, 0, 1000, 1000));

        for (var i = 0; i < 100; i++)
        {
            tree.Insert($"n{i}", new SpatialRect(i * 10, 0, 5, 5));
        }

        // 视口只覆盖最左边一小块，右侧的都不该被返回。
        tree.Query(new SpatialRect(0, 0, 20, 20)).Should().BeEquivalentTo("n0", "n1", "n2");
    }

    // ---- 跨边界：本块最容易出错的地方 ----

    [Fact]
    [Trait("Category", "QuadTree")]
    public void An_object_spanning_a_quadrant_boundary_is_found_from_both_sides()
    {
        // 容量设成一，逼它在第二个对象进来时就细分，象限边界因此落在 50,50。
        var tree = new QuadTree(new SpatialRect(0, 0, 100, 100), capacity: 1, maxDepth: 4);

        // 这个矩形横跨纵向的象限分界。
        tree.Insert("跨越", new SpatialRect(45, 10, 10, 10));

        // 两侧的查询都必须找得到它。只存在一侧的话，另一半就查不到——
        // 表现是"拖动时某些元素一半画出来一半没有"。
        tree.Query(new SpatialRect(0, 0, 50, 50)).Should().Contain("跨越");
        tree.Query(new SpatialRect(50, 0, 50, 50)).Should().Contain("跨越");
    }

    [Fact]
    [Trait("Category", "QuadTree")]
    public void An_object_spanning_all_four_quadrants_is_found_everywhere()
    {
        var tree = new QuadTree(new SpatialRect(0, 0, 100, 100), capacity: 1, maxDepth: 4);

        tree.Insert("正中", new SpatialRect(45, 45, 10, 10));
        tree.Insert("铺满", new SpatialRect(0, 0, 100, 100));

        foreach (var (x, y) in new[] { (0, 0), (50, 0), (0, 50), (50, 50) })
        {
            tree.Query(new SpatialRect(x, y, 50, 50))
                .Should().Contain("铺满", $"它覆盖了整个区域，({x},{y}) 那一块也该看得到");
        }
    }

    [Fact]
    [Trait("Category", "QuadTree")]
    public void An_object_larger_than_a_quadrant_is_kept_at_the_parent()
    {
        var tree = new QuadTree(new SpatialRect(0, 0, 100, 100), capacity: 1, maxDepth: 4);

        tree.Insert("大的", new SpatialRect(10, 10, 80, 80));

        // 它放不进任何子象限，因此留在某个上层节点上。
        // 四个子区域都应当能查到它。
        tree.Query(new SpatialRect(0, 0, 25, 25)).Should().Contain("大的");
        tree.Query(new SpatialRect(75, 75, 25, 25)).Should().Contain("大的");
    }

    [Fact]
    [Trait("Category", "QuadTree")]
    public void Objects_outside_the_root_bounds_are_still_found()
    {
        // 根的范围在构造时固定。落在范围外的对象存在根上，查询仍然找得到——
        // 正确性不受影响，只是它们享受不到细分带来的加速。
        var tree = new QuadTree(new SpatialRect(0, 0, 100, 100));

        tree.Insert("越界", new SpatialRect(500, 500, 10, 10));

        tree.Query(new SpatialRect(490, 490, 30, 30)).Should().Contain("越界");
        tree.Query(new SpatialRect(0, 0, 10, 10)).Should().BeEmpty();
    }

    // ---- 增删 ----

    [Fact]
    [Trait("Category", "QuadTree")]
    public void Inserting_the_same_id_twice_updates_rather_than_duplicates()
    {
        var tree = new QuadTree(new SpatialRect(0, 0, 100, 100));

        tree.Insert("a", new SpatialRect(0, 0, 10, 10));
        tree.Insert("a", new SpatialRect(80, 80, 10, 10));

        tree.Count.Should().Be(1);

        // 旧位置不该再查到它。留副本的话，那些副本会永远被返回，
        // 而它们指向的元素早就挪走了。
        tree.Query(new SpatialRect(0, 0, 20, 20)).Should().BeEmpty();
        tree.Query(new SpatialRect(70, 70, 20, 20)).Should().Equal("a");
    }

    [Fact]
    [Trait("Category", "QuadTree")]
    public void Remove_takes_the_object_out_of_queries()
    {
        var tree = new QuadTree(new SpatialRect(0, 0, 100, 100));

        tree.Insert("a", new SpatialRect(10, 10, 10, 10));
        tree.Insert("b", new SpatialRect(20, 20, 10, 10));

        tree.Remove("a").Should().BeTrue();
        tree.Count.Should().Be(1);

        tree.Query(new SpatialRect(0, 0, 100, 100)).Should().Equal("b");
    }

    [Fact]
    [Trait("Category", "QuadTree")]
    public void Removing_an_unknown_id_reports_failure()
    {
        var tree = new QuadTree(new SpatialRect(0, 0, 100, 100));

        tree.Remove("从来没有过").Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "QuadTree")]
    public void Clear_empties_the_tree_and_its_structure()
    {
        var tree = new QuadTree(new SpatialRect(0, 0, 100, 100), capacity: 1);

        for (var i = 0; i < 50; i++)
        {
            tree.Insert($"n{i}", new SpatialRect(i, i, 2, 2));
        }

        tree.Depth.Should().BeGreaterThan(0, "先确认它确实细分过");

        tree.Clear();

        tree.Count.Should().Be(0);
        tree.Depth.Should().Be(0, "结构应当一并重置");
        tree.Query(new SpatialRect(0, 0, 100, 100)).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "QuadTree")]
    public void Empty_children_are_collapsed_after_removal()
    {
        // 不收回的话，反复增删之后树里会积下大量空节点，
        // 每次查询都要多走几层空壳，而空壳只增不减。
        var tree = new QuadTree(new SpatialRect(0, 0, 100, 100), capacity: 1, maxDepth: 4);

        for (var i = 0; i < 50; i++)
        {
            tree.Insert($"n{i}", new SpatialRect(i % 50, i / 50, 1, 1));
        }

        tree.Depth.Should().BeGreaterThan(0);

        for (var i = 0; i < 50; i++)
        {
            tree.Remove($"n{i}");
        }

        tree.Count.Should().Be(0);
        tree.Depth.Should().Be(0);
    }

    // ---- 细分与深度 ----

    [Fact]
    [Trait("Category", "QuadTree")]
    public void Subdivision_keeps_the_depth_bounded_for_a_shared_position()
    {
        // 全部元素落在同一个点上是最坏的输入：只要不封顶，它会一直细分下去。
        var tree = new QuadTree(new SpatialRect(0, 0, 1000, 1000), capacity: 1, maxDepth: 6);

        for (var i = 0; i < 200; i++)
        {
            tree.Insert($"n{i}", new SpatialRect(500, 500, 1, 1));
        }

        tree.Count.Should().Be(200);
        tree.Depth.Should().BeLessThanOrEqualTo(6, "深度封顶保证最坏情况有界");

        // 有界之外还要正确：全部找得到。
        tree.Query(new SpatialRect(499, 499, 3, 3)).Should().HaveCount(200);
    }

    [Fact]
    [Trait("Category", "QuadTree")]
    public void Capacity_and_depth_must_be_positive()
    {
        var badCapacity = () => new QuadTree(new SpatialRect(0, 0, 10, 10), capacity: 0);
        badCapacity.Should().Throw<ArgumentOutOfRangeException>();

        var badDepth = () => new QuadTree(new SpatialRect(0, 0, 10, 10), maxDepth: 0);
        badDepth.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    [Trait("Category", "QuadTree")]
    public void Build_sizes_the_tree_from_the_content()
    {
        var items = new (string, SpatialRect)[]
        {
            ("a", new SpatialRect(-500, -300, 10, 10)),
            ("b", new SpatialRect(900, 700, 10, 10)),
        };

        var tree = QuadTree.Build(items);

        tree.Bounds.Contains(items[0].Item2).Should().BeTrue();
        tree.Bounds.Contains(items[1].Item2).Should().BeTrue();
        tree.Query(new SpatialRect(-500, -300, 10, 10)).Should().Equal("a");
    }

    [Fact]
    [Trait("Category", "QuadTree")]
    public void Build_handles_an_empty_set()
    {
        var tree = QuadTree.Build([]);

        tree.Count.Should().Be(0);
        tree.Bounds.Width.Should().BeGreaterThan(0, "零宽的根会让细分除出无穷大");
    }

    // ---- 查询接口 ----

    [Fact]
    [Trait("Category", "QuadTree")]
    public void Query_writes_into_the_provided_buffer()
    {
        // 视口查询每帧都会发生，缓冲区复用是为了让这一层不产生固定开销。
        var tree = new QuadTree(new SpatialRect(0, 0, 100, 100));

        tree.Insert("a", new SpatialRect(10, 10, 5, 5));

        var buffer = new List<string> { "上次留下的" };

        var count = tree.Query(new SpatialRect(0, 0, 50, 50), buffer);

        count.Should().Be(1);

        // 缓冲区先被清空，不留上次的结果。
        buffer.Should().Equal("a");
    }

    [Fact]
    [Trait("Category", "QuadTree")]
    public void QueryPoint_finds_objects_under_a_point()
    {
        var tree = new QuadTree(new SpatialRect(0, 0, 100, 100));

        tree.Insert("a", new SpatialRect(10, 10, 10, 10));

        var buffer = new List<string>();

        tree.QueryPoint(15, 15, buffer).Should().Be(1);
        tree.QueryPoint(50, 50, buffer).Should().Be(0);
    }

    [Fact]
    [Trait("Category", "QuadTree")]
    public void An_empty_id_is_rejected()
    {
        var tree = new QuadTree(new SpatialRect(0, 0, 100, 100));

        var act = () => tree.Insert("  ", new SpatialRect(0, 0, 1, 1));

        act.Should().Throw<ArgumentException>();
    }

    // ---- 裁剪效果 ----

    [Fact]
    [Trait("Category", "QuadTree")]
    public void Culling_keeps_the_draw_count_far_below_the_node_count()
    {
        // 这是四叉树存在的理由：不裁剪的话每帧要判一千个元素，
        // 裁剪之后只判视口里那几个。方案的 Phase 2 目标是剔除率高于八成。
        var tree = QuadTree.Build(Grid(count: 1000, columns: 40, spacing: 100));

        var viewport = new SpatialRect(0, 0, 400, 400);
        var visible = tree.Query(ViewportCulling.WithPrefetch(viewport));

        visible.Count.Should().BeLessThan(1000 / 2, "视口只覆盖四十分之一左右的面积");

        var culled = 1.0 - ((double)visible.Count / tree.Count);
        culled.Should().BeGreaterThan(0.8, "剔除率应当高于八成");
    }

    // ---- 规模 ----

    [Fact]
    [Trait("Category", "QuadTree")]
    public void A_thousand_nodes_build_and_query_quickly()
    {
        var items = Grid(count: 1000, columns: 40, spacing: 100);

        var tree = QuadTree.Build(items);
        tree.Count.Should().Be(1000);

        var buffer = new List<string>();
        var viewport = new SpatialRect(0, 0, 500, 500);

        // 先跑几遍预热，再看时间。
        for (var i = 0; i < 10; i++)
        {
            tree.Query(viewport, buffer);
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();

        for (var i = 0; i < 100; i++)
        {
            tree.Query(viewport, buffer);
        }

        watch.Stop();

        // 上限给得很宽：这条断言盯的是"有没有退化成逐个遍历"，不是精确耗时。
        // 精确的耗时基线在基准工程里，那里有统计方法，秒表读数在这里没有意义。
        var perQuery = watch.Elapsed.TotalMicroseconds / 100;

        perQuery.Should().BeLessThan(1000, "方案要求单次查询低于一毫秒");
    }

    // ---- 策略参数 ----

    [Fact]
    [Trait("Category", "QuadTree")]
    public void Policy_values_are_the_documented_ones()
    {
        ViewportCulling.PrefetchMargin.Should().Be(100);
        ViewportCulling.VirtualizationThreshold.Should().Be(500);

        ViewportCulling.ShouldVirtualize(499).Should().BeFalse();
        ViewportCulling.ShouldVirtualize(500).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "QuadTree")]
    public void Prefetch_expands_the_viewport_on_all_sides()
    {
        var viewport = new SpatialRect(100, 100, 50, 50);

        var expanded = ViewportCulling.WithPrefetch(viewport);

        expanded.X.Should().Be(0);
        expanded.Y.Should().Be(0);
        expanded.Width.Should().Be(250);
        expanded.Height.Should().Be(250);
    }

    // ---- 几何 ----

    [Fact]
    [Trait("Category", "QuadTree")]
    public void Rectangles_that_touch_are_considered_intersecting()
    {
        // 相接算相交而不是不算：视口裁剪宁可多画一个贴边的元素，也不能漏画。
        // 漏画的表现是"拖动时边缘的元素一闪一闪"。
        var a = new SpatialRect(0, 0, 10, 10);
        var b = new SpatialRect(10, 0, 10, 10);

        a.Intersects(b).Should().BeTrue();
        a.Contains(b).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "QuadTree")]
    public void Union_covers_both_rectangles()
    {
        var a = new SpatialRect(0, 0, 10, 10);
        var b = new SpatialRect(20, 30, 10, 10);

        var union = a.Union(b);

        union.Contains(a).Should().BeTrue();
        union.Contains(b).Should().BeTrue();
        union.Should().Be(new SpatialRect(0, 0, 30, 40));
    }

    private static IEnumerable<(string Id, SpatialRect Rect)> Grid(int count, int columns, double spacing)
    {
        for (var i = 0; i < count; i++)
        {
            yield return ($"n{i}", new SpatialRect((i % columns) * spacing, (i / columns) * spacing, 80, 40));
        }
    }
}
