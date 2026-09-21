using DuetDiagram.Core.Model;

namespace DuetDiagram.Mermaid.Parsing;

/// <summary>一条诊断。</summary>
/// <param name="Message">说明。</param>
/// <param name="Line">行号。</param>
/// <param name="Column">列号。</param>
/// <remarks>
/// 解析结果里带着诊断而不是直接抛异常：一部分内容认不出来时，
/// 其余内容往往是好的。把整份输入判死会让用户手里一份九成正确的图变成零。
/// 宽松模式（P1-09）就是在这条路上继续走。
/// </remarks>
public sealed record MermaidDiagnostic(string Message, int Line, int Column);

/// <summary>节点声明。</summary>
/// <param name="Id">标识。</param>
/// <param name="Label">显示文本。未声明时为空。</param>
/// <param name="Shape">形状。</param>
/// <param name="Parent">所属子图的标识。不在子图里时为空。</param>
public sealed record MermaidNodeDeclaration(string Id, string? Label, NodeShape Shape, string? Parent);

/// <summary>连线声明。</summary>
/// <param name="From">起点。</param>
/// <param name="To">终点。</param>
/// <param name="Label">边上的文字。</param>
/// <param name="Arrow">箭头样式。</param>
/// <param name="Line">线型。</param>
public sealed record MermaidLinkDeclaration(string From, string To, string? Label, ArrowStyle Arrow, LineStyle Line);

/// <summary>子图声明。</summary>
/// <param name="Id">标识。</param>
/// <param name="Label">显示文本。</param>
/// <param name="Direction">子图内部的方向。</param>
/// <param name="Members">直接成员。嵌套的子图不在其中，它们由 <paramref name="Parent"/> 反向指认。</param>
/// <param name="Parent">外层子图的标识。不在别的子图里时为空。</param>
public sealed record MermaidSubgraphDeclaration(
    string Id,
    string Label,
    Direction? Direction,
    IReadOnlyList<string> Members,
    string? Parent);

/// <summary>样式声明。<c>style 节点 属性列表</c>。</summary>
/// <param name="Target">作用对象。</param>
/// <param name="Properties">属性列表原文。</param>
public sealed record MermaidStyleDeclaration(string Target, string Properties);

/// <summary>类定义与套用。</summary>
/// <param name="Name">类名。</param>
/// <param name="Properties">定义时的属性列表原文。套用时为空。</param>
/// <param name="Targets">套用到哪些节点。定义时为空。</param>
public sealed record MermaidClassDeclaration(string Name, string? Properties, IReadOnlyList<string> Targets);

/// <summary>
/// 解析出的流程图。
/// </summary>
/// <remarks>
/// 各列表按**首次出现顺序**排列。顺序有意义：它决定了节点在集合里的位置，
/// 而那个位置是层内次序的依据。用集合并掉顺序会让同一份输入两次解析出不同的图。
/// </remarks>
public sealed record MermaidFlowchart
{
    public Direction Direction { get; init; } = Direction.TB;

    public IReadOnlyList<MermaidNodeDeclaration> Nodes { get; init; } = [];

    public IReadOnlyList<MermaidLinkDeclaration> Links { get; init; } = [];

    public IReadOnlyList<MermaidSubgraphDeclaration> Subgraphs { get; init; } = [];

    public IReadOnlyList<MermaidStyleDeclaration> Styles { get; init; } = [];

    public IReadOnlyList<MermaidClassDeclaration> Classes { get; init; } = [];

    public IReadOnlyList<MermaidDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>解析是否完全干净。</summary>
    public bool IsClean => Diagnostics.Count == 0;
}
