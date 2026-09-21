using DuetDiagram.Core.Model;
using DuetDiagram.Mermaid.Parsing;

namespace DuetDiagram.Mermaid.Import;

/// <summary>
/// 一个被当成子图端点的节点声明，它没有进 IR。
/// </summary>
/// <param name="Id">标识。</param>
/// <param name="SubgraphId">同名的子图，也就是这个名字实际指向的东西。</param>
/// <remarks>
/// <para>
/// 写法是 <c>subgraph ODS[...] ... end</c> 之后再写 <c>ODS --&gt; DWD</c>。
/// 解析器读到连线时会顺手给两端各造一个节点（那是它的既有行为，别处也靠它），
/// 于是同一个标识既在子图表里又在节点表里。
/// </para>
/// <para>
/// 导入时**丢掉节点那一份，让标识指向子图**：Mermaid 渲染的就是两个子图框之间的连线，
/// 而且节点与组合共用标识命名空间，两份都留会撞名。
/// </para>
/// <para>
/// 丢掉要留记录。用户看到的是"我明明写了 ODS 这个节点怎么不见了"，
/// 而答案（它被当成子图了）只有这里能回答。
/// </para>
/// </remarks>
public sealed record DroppedNode(string Id, string SubgraphId);

/// <summary>
/// 一条没映射进 IR 的样式属性。
/// </summary>
/// <param name="Target">作用对象，原文里的样子。</param>
/// <param name="Property">属性原文，例如 <c>stroke-dasharray:5 5</c>。</param>
/// <param name="Reason">为什么映射不了。</param>
/// <remarks>
/// 丢掉样式比丢掉节点隐蔽得多：图还是画得出来，只是少了一处颜色，
/// 而"少了一处"在图上几乎看不出来。所以每一条都要留记录。
/// </remarks>
public sealed record IgnoredStyleProperty(string Target, string Property, string Reason);

/// <summary>
/// 一次导入做了什么。
/// </summary>
/// <remarks>
/// 三份列表分别是「解析没看懂的地方」「被丢掉的节点声明」「没映射的样式属性」。
/// 第一份是问题，后两份是**有意的取舍**——不是错误，但都会让图与原文对不上，
/// 必须能查到出处。
/// </remarks>
/// <param name="Diagnostics">解析器记下的诊断，原样带过来。</param>
/// <param name="DroppedNodes">被当成子图端点而丢掉的节点声明。</param>
/// <param name="IgnoredStyleProperties">没映射进 IR 的样式属性。</param>
public sealed record MermaidImportReport(
    IReadOnlyList<MermaidDiagnostic> Diagnostics,
    IReadOnlyList<DroppedNode> DroppedNodes,
    IReadOnlyList<IgnoredStyleProperty> IgnoredStyleProperties);

/// <summary>
/// 导入产物。
/// </summary>
/// <param name="Document">产出文档。版本号为 0，两个哈希已算好。</param>
/// <param name="Report">这次导入做了什么。</param>
public sealed record ImportResult(DiagramDocument Document, MermaidImportReport Report);
