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
}
