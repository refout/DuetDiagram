using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Shapes;

/// <summary>
/// 八个内置形状。
/// </summary>
/// <remarks>
/// <para>
/// **这是形状库的第一个实现，也是"加一个形状要改几处"的答案。** 在此之前，
/// 加一个形状要同时改三处：枚举、渲染层那个按枚举分发的 switch、工具与属性面板的取值表。
/// 搬到提供者之后，加一个形状只在这里加一条——其余三处都读这份表。
/// </para>
/// <para>
/// 枚举仍然要加一个成员：它写进文档与结构哈希，没法凭空长出来。这是唯一躲不掉的那一处，
/// 也是"枚举不许删"那条约束的代价。
/// </para>
/// <para>
/// 顶点按"从上到下、从左到右"的顺时针顺序给。顺序本身不参与判定，但它决定了
/// 描边的走法，而两条边重叠处的画法在顺逆时针下不一样。
/// </para>
/// <para>
/// **表里的次序与枚举的声明次序一致。** 属性面板的下拉按表里的次序排，改了次序
/// 就会改用户看到的那一排顺序——那是一次没有理由的界面变动。
/// </para>
/// </remarks>
public sealed class BuiltinShapeProvider : IShapeProvider
{
    /// <summary>唯一一份。它是无状态的纯表，建多份没有意义。</summary>
    public static BuiltinShapeProvider Instance { get; } = new();

    private BuiltinShapeProvider()
    {
    }

    /// <inheritdoc/>
    public IReadOnlyList<ShapeDefinition> Shapes { get; } =
    [
        new(NodeShape.Rect, new RoundedRectOutline(CornerRadiusMode.None)),
        new(NodeShape.Rounded, new RoundedRectOutline(CornerRadiusMode.FromStyle)),
        new(NodeShape.Stadium, new RoundedRectOutline(CornerRadiusMode.HalfMinSide)),
        new(NodeShape.Diamond, new PolygonOutline(
        [
            new ShapePoint(0.5, 0),
            new ShapePoint(1, 0.5),
            new ShapePoint(0.5, 1),
            new ShapePoint(0, 0.5),
        ])),
        new(NodeShape.Circle, new EllipseOutline()),
        new(NodeShape.Hexagon, new PolygonOutline(
        [
            new ShapePoint(0.25, 0),
            new ShapePoint(0.75, 0),
            new ShapePoint(1, 0.5),
            new ShapePoint(0.75, 1),
            new ShapePoint(0.25, 1),
            new ShapePoint(0, 0.5),
        ])),
        new(NodeShape.Parallelogram, new PolygonOutline(
        [
            new ShapePoint(0.25, 0),
            new ShapePoint(1, 0),
            new ShapePoint(0.75, 1),
            new ShapePoint(0, 1),
        ])),

        // 圆柱：顶面与底面的前半圈各一段弧，中间两条竖边。弧高取高度的四分之一——
        // 再高就成了一段管子，再低看不出是圆柱。弧的横向半径取宽度的一半，
        // 所以顶面正好从左边拱到右边。
        // 写成一段闭合路径而不是"两个椭圆加一个矩形"：三块图形各自的描边会在接缝处
        // 叠出一道深色线。
        new(NodeShape.Cylinder, new PathOutline(
            new ShapePoint(0, 0.25),
            [
                new PathArc(new ShapePoint(1, 0.25), 0.5, 0.25, Clockwise: true),
                new PathLine(new ShapePoint(1, 0.75)),
                new PathArc(new ShapePoint(0, 0.75), 0.5, 0.25, Clockwise: true),
            ])),
    ];
}
