using DuetDiagram.Core.Model;
using DuetDiagram.Dsl.Parsing;

namespace DuetDiagram.Dsl.Mapping;

/// <summary>
/// 一次容器改名。
/// </summary>
/// <param name="OriginalId">原文里的标识。</param>
/// <param name="NewId">映射之后的标识。</param>
/// <param name="Reason">为什么改。</param>
/// <remarks>
/// 改名必须留下记录。IR 的九个集合共用一个命名空间，而 DSL 允许节点与分组同名，
/// 于是撞名时总有一方要让位——让位之后图上的标识就与文本里的对不上了。
/// 静默改名会让"为什么图上的标识和文本对不上"变成一个查不出来的问题。
/// </remarks>
public sealed record MappingRename(string OriginalId, string NewId, string Reason);

/// <summary>
/// 补出来的一个节点。
/// </summary>
/// <param name="Id">补出来的标识，取自被引用的名字。</param>
/// <param name="ReferencedBy">第一次引用它的那条边的标识。</param>
/// <remarks>
/// 语法层不替边端点补节点（见 <c>docs/DSL-Syntax.md</c> 的「两条实现层面的约定」），
/// 补节点是这一层的事。补出来的节点在图上是真实存在的方块，所以也要留下记录——
/// 否则用户会看到"图里凭空多了一个节点"而找不到它是哪来的。
/// </remarks>
public sealed record CreatedNode(string Id, string ReferencedBy);

/// <summary>
/// 一次映射做了什么。
/// </summary>
/// <remarks>
/// <para>
/// 三份列表分别是「解析没看懂的地方」「改了名的地方」「补出来的东西」。
/// 前一份是问题，后两份是**正常结果**——不是错误，但都是文本与图之间对不上的地方，
/// 必须能查到出处。
/// </para>
/// <para>
/// 这里刻意不提供 <c>IsClean</c> 之类的单一布尔值：诊断、改名、补节点三件事的
/// 处置方式完全不同，合成一个真假值之后调用方就只能全丢或全留。
/// </para>
/// </remarks>
/// <param name="Diagnostics">解析器记下的诊断，原样带过来。</param>
/// <param name="Renames">容器改名记录。</param>
/// <param name="CreatedNodes">补出来的节点。</param>
public sealed record MappingReport(
    IReadOnlyList<DslDiagnostic> Diagnostics,
    IReadOnlyList<MappingRename> Renames,
    IReadOnlyList<CreatedNode> CreatedNodes);

/// <summary>
/// 映射产物。
/// </summary>
/// <param name="Document">产出文档。版本号为 0，两个哈希已算好。</param>
/// <param name="Report">这次映射做了什么。</param>
public sealed record MappingResult(DiagramDocument Document, MappingReport Report);
