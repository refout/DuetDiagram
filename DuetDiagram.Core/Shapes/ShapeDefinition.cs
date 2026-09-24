using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Shapes;

/// <summary>
/// 一个形状的定义：它是哪个枚举值，以及它的几何。
/// </summary>
/// <remarks>
/// <para>
/// **枚举值是身份，几何是外观。** 枚举写进文档、写进序列化、进结构哈希，
/// 所以它不能随几何调整而变；几何可以被提供者改（换个顶点、换段弧），
/// 改完文档里的形状名还是同一个。
/// </para>
/// <para>
/// 名字直接取枚举成员名。两处各写一个名字字段的话，迟早会对不上——
/// 而界面上、工具取值表里用的都是这个名字，对不上就是"填进去说不认识"。
/// </para>
/// </remarks>
/// <param name="Shape">形状的枚举值。</param>
/// <param name="Geometry">这个形状的几何。</param>
public sealed record ShapeDefinition(NodeShape Shape, ShapeGeometry Geometry)
{
    /// <summary>形状名。取值表、界面与错误信息都用它。</summary>
    public string Name => Shape.ToString();
}
