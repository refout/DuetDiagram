using DuetDiagram.Core.Model;
using DuetDiagram.Layout;

namespace DuetDiagram.Layout.Tests;

/// <summary>
/// 测试用的图形装配。
/// </summary>
internal static class Graphs
{
    public const double NodeWidth = 80;

    public const double NodeHeight = 40;

    public static LayoutNode Node(
        string id,
        double? x = null,
        double? y = null,
        IReadOnlyList<LayoutPort>? ports = null,
        string? parent = null) =>
        new(id, NodeWidth, NodeHeight, x is null || y is null ? null : new LayoutPoint(x.Value, y.Value), ports, parent);

    /// <summary>菱形：一个起点分出两条路，再汇到一个终点。</summary>
    public static LayoutRequest Diamond(
        Direction direction = Direction.TB,
        IReadOnlyList<LayoutNode>? extra = null,
        IReadOnlyList<IReadOnlyList<string>>? sameRank = null) => new(
        [
            Node("start"),
            Node("left"),
            Node("right"),
            Node("end"),
            .. extra ?? [],
        ],
        [
            new LayoutEdge("e1", "start", "left"),
            new LayoutEdge("e2", "start", "right"),
            new LayoutEdge("e3", "left", "end"),
            new LayoutEdge("e4", "right", "end"),
        ],
        new LayoutOptions(direction, SameRankGroups: sameRank));

    /// <summary>
    /// 一个节点分出三个并列的分支。
    /// </summary>
    /// <remarks>
    /// 三个分支天然落在同一层，所以它同时是"层内次序"与"同层"两类约束的现成素材：
    /// 层内次序要的正是同一层里几个节点的左右关系，而层内的左右关系只有在这种形状上才看得出来。
    /// </remarks>
    /// <param name="direction">主方向。</param>
    /// <param name="nodes">替换掉默认的四个节点。需要固定坐标或改尺寸时用它。</param>
    /// <param name="order">层内次序约束，每一组是一批要按给定先后排列的节点。</param>
    /// <param name="align">对齐约束，每一组是一批要在层内轴上取齐的节点。</param>
    public static LayoutRequest Fork(
        Direction direction = Direction.TB,
        IReadOnlyList<LayoutNode>? nodes = null,
        IReadOnlyList<IReadOnlyList<string>>? order = null,
        IReadOnlyList<IReadOnlyList<string>>? align = null)
    {
        var members = nodes ?? new LayoutNode[] { Node("p"), Node("a"), Node("b"), Node("c") };

        return new LayoutRequest(
            [.. members],
            [
                new LayoutEdge("e1", "p", "a"),
                new LayoutEdge("e2", "p", "b"),
                new LayoutEdge("e3", "p", "c"),
            ],
            new LayoutOptions(direction, OrderGroups: order, AlignGroups: align));
    }

    /// <summary>多层图：每层若干并列节点，层间连接同序号节点。</summary>
    public static LayoutRequest Layered(int depth, int breadth, Direction direction = Direction.TB)
    {
        var nodes = new List<LayoutNode>(depth * breadth);
        var edges = new List<LayoutEdge>();

        for (var layer = 0; layer < depth; layer++)
        {
            for (var index = 0; index < breadth; index++)
            {
                nodes.Add(Node($"n{layer}-{index}"));
            }
        }

        for (var layer = 0; layer < depth - 1; layer++)
        {
            for (var index = 0; index < breadth; index++)
            {
                edges.Add(new LayoutEdge($"e{layer}-{index}", $"n{layer}-{index}", $"n{layer + 1}-{index}"));
            }
        }

        return new LayoutRequest([.. nodes], [.. edges], new LayoutOptions(direction));
    }

    /// <summary>
    /// 首尾相接的环：<c>c0 -&gt; c1 -&gt; ... -&gt; c(n-1) -&gt; c0</c>。
    /// </summary>
    /// <remarks>
    /// 环是分层布局里唯一必须违背输入的形状：层序本身要求无环，所以引擎必然要把其中一条边反过来。
    /// 这一族用例量的不是"好不好看"，而是那条被反过来的边有没有把整张图带崩，
    /// 以及环上各节点是否仍然落在互不相同的层上。
    /// </remarks>
    /// <param name="count">环上的节点数。为 1 时退化成一条自环。</param>
    /// <param name="direction">主方向。</param>
    public static LayoutRequest Cycle(int count, Direction direction = Direction.TB)
    {
        var nodes = new List<LayoutNode>(count);
        var edges = new List<LayoutEdge>(count);

        for (var index = 0; index < count; index++)
        {
            nodes.Add(Node($"c{index}"));
        }

        for (var index = 0; index < count; index++)
        {
            edges.Add(new LayoutEdge($"e{index}", $"c{index}", $"c{(index + 1) % count}"));
        }

        return new LayoutRequest([.. nodes], [.. edges], new LayoutOptions(direction));
    }
}
