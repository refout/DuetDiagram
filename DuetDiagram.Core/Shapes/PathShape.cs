using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Shapes;

/// <summary>
/// 自定义形状：节点自带的一段路径。
/// </summary>
/// <remarks>
/// <para>
/// **它是形状库的第二条来路，与内置形状汇到同一个出口。** 内置形状的几何从
/// <see cref="ShapeRegistry"/> 里查，自定义形状的几何由这里把节点上的路径文本解析出来，
/// 两者都是 <see cref="ShapeGeometry"/>，最终交给同一个绘制方去画。
/// 自定义形状因此没有"另一套画法"——边距与标签位置天然与内置形状一致。
/// </para>
/// <para>
/// **形状名与几何是分开的。** 节点上的 <see cref="NodeDef.Shape"/> 仍是那八个固定值之一，
/// 它写进序列化与哈希，是节点的身份；<see cref="NodeDef.ShapePath"/> 是外观。
/// 两者同时存在时以路径为准——路径是节点自己声明"我要长这样"，比枚举更具体。
/// </para>
/// <para>
/// **路径只认单位框坐标（0 到 1）**，语法见 <see cref="PathParser"/>。用绝对像素的话，
/// 同一个形状画在不同尺寸的节点上会走形，而看起来像"形状没做对"。
/// </para>
/// </remarks>
public static class PathShape
{
    /// <summary>这个节点用的是不是自定义路径。</summary>
    public static bool IsCustom(NodeDef node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return !string.IsNullOrWhiteSpace(node.ShapePath);
    }

    /// <summary>
    /// 一个节点最终画哪份几何。
    /// </summary>
    /// <remarks>
    /// 这是内置与自定义两条路合流的地方，绘制方只认这里的返回值，
    /// 所以它不必知道"这个形状是表里的还是节点自己写的"。
    /// </remarks>
    /// <param name="node">节点。</param>
    /// <param name="registry">内置形状表。</param>
    /// <exception cref="PathSyntaxException">节点带了路径，但路径解析不出来。</exception>
    public static ShapeGeometry GeometryOf(NodeDef node, ShapeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(registry);

        return IsCustom(node) ? Parse(node.ShapePath!) : registry.Find(node.Shape).Geometry;
    }

    /// <summary>
    /// 解析一段路径文本。
    /// </summary>
    /// <remarks>
    /// 解析本身在 <see cref="PathParser"/>；这里只是把"自定义形状"这一层的意思说出来，
    /// 让调用点读到的是"解析一个自定义形状"，而不是"解析一段文本"。
    /// </remarks>
    /// <param name="pathData">路径文本。</param>
    /// <exception cref="PathSyntaxException">语法不合法。</exception>
    public static PathOutline Parse(string pathData) => PathParser.Parse(pathData);
}
