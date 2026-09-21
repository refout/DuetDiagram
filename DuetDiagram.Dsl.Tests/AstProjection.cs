using DuetDiagram.Dsl.Parsing;

namespace DuetDiagram.Dsl.Tests;

/// <summary>
/// 把解析结果投影成可比较的字符串。
/// </summary>
/// <remarks>
/// <para>
/// 不能直接比记录：<see cref="DslNodeDeclaration"/>、<see cref="DslLayoutIntent"/>、
/// <see cref="DslGroupDeclaration"/> 都带列表成员（Ports / Nodes / Members），
/// 而记录的自动相等性对列表成员用**引用**比较。两次解析出来的列表不是同一个实例，
/// 整条记录比会判为不等——而那不是"解析不确定"，是比法不对。
/// </para>
/// <para>
/// 同一个坑在 Core 里踩过两次、Mermaid 侧踩过一次。投影成字符串之后
/// 失败信息也更好读：直接指出是哪个字段不同。
/// </para>
/// </remarks>
internal static class AstProjection
{
    public static string Node(DslNodeDeclaration node) =>
        $"{node.Id}|{node.Label}|{node.Shape}|{node.StyleToken}|{node.Layer}|{node.Description}|{node.Parent}"
        + $"|{string.Join(",", node.Ports.Select(p => $"{p.Name}:{p.Side}"))}";

    public static string Edge(DslEdgeDeclaration edge) =>
        $"{edge.Id}|{edge.From}|{edge.FromPort}|{edge.To}|{edge.ToPort}"
        + $"|{edge.Label}|{edge.Arrow}|{edge.Line}|{edge.StyleToken}";

    public static string Group(DslGroupDeclaration group) =>
        $"{group.Kind}|{group.Id}|{group.Label}|{group.Parent}|{string.Join(",", group.Members)}";

    public static string Intent(DslLayoutIntent intent) =>
        $"{intent.Kind}|{intent.Subject}|{intent.Relation}|{intent.X}|{intent.Y}"
        + $"|{string.Join(",", intent.Nodes)}";

    /// <summary>整个文档的可比较形状，按固定顺序展开。</summary>
    public static IEnumerable<string> All(DslDocument document) =>
    [
        $"header:{document.Version}|{document.Kind}|{document.Direction}",
        $"spacing:{document.NodeSpacing}|{document.LayerSpacing}",
        .. document.Nodes.Select(Node),
        .. document.Edges.Select(Edge),
        .. document.Groups.Select(Group),
        .. document.Layout.Select(Intent),
    ];
}
