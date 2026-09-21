using DuetDiagram.Core.Model;
using DuetDiagram.Core.Sidecar;
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
/// <param name="Reason">谁引用了它，例如"边 e1""布局意图 same-rank"。</param>
/// <remarks>
/// 语法层不替边端点补节点（见 <c>docs/DSL-Syntax.md</c> 的「两条实现层面的约定」），
/// 补节点是这一层的事。补出来的节点在图上是真实存在的方块，所以也要留下记录——
/// 否则用户会看到"图里凭空多了一个节点"而找不到它是哪来的。
/// </remarks>
public sealed record CreatedNode(string Id, string Reason);

/// <summary>
/// 一条布局意图里没能落地的引用。
/// </summary>
/// <param name="Intent">意图的样子，例如"order check: pass, fail"。</param>
/// <param name="Reference">没落地的那个名字。</param>
/// <param name="Reason">为什么落不了地。</param>
/// <remarks>
/// <para>
/// 布局意图目前只有一种落不了地的方式：<c>order</c> 的次序项写的是**目标节点**，
/// 而 IR 的 <c>OrderConstraint</c> 存的是**出边的标识**，所以映射层要按
/// "该节点的出边、终点是这个名字"去解析。找不到那条边时这一项就落不了地。
/// </para>
/// <para>
/// 落不了地必须留记录：约束少了一项之后图还是画得出来，只是层内次序不是你写的那个，
/// 而那种偏差在图上完全看不出来。
/// </para>
/// </remarks>
public sealed record UnresolvedIntent(string Intent, string Reference, string Reason);

/// <summary>
/// 一次映射做了什么。
/// </summary>
/// <remarks>
/// <para>
/// 四份列表分别是「解析没看懂的地方」「改了名的地方」「补出来的东西」「意图里没落地的引用」。
/// 第一份与最后一份是问题，中间两份是**正常结果**——不是错误，
/// 但都是文本与图之间对不上的地方，必须能查到出处。
/// </para>
/// <para>
/// 这里刻意不提供 <c>IsClean</c> 之类的单一布尔值：四件事的处置方式完全不同，
/// 合成一个真假值之后调用方就只能全丢或全留。
/// </para>
/// </remarks>
/// <param name="Diagnostics">解析器记下的诊断，原样带过来。</param>
/// <param name="Renames">容器改名记录。</param>
/// <param name="CreatedNodes">补出来的节点。</param>
/// <param name="UnresolvedIntents">布局意图里没能落地的引用。</param>
public sealed record MappingReport(
    IReadOnlyList<DslDiagnostic> Diagnostics,
    IReadOnlyList<MappingRename> Renames,
    IReadOnlyList<CreatedNode> CreatedNodes,
    IReadOnlyList<UnresolvedIntent> UnresolvedIntents);

/// <summary>
/// 映射产物。
/// </summary>
/// <remarks>
/// 是**两件套**：文档加 sidecar。DSL 里有一类意图（<c>pin</c>）落不到 IR 上——
/// 绝对坐标在 <see cref="LayoutHints"/> 里没有位置，而布局输入本来就从 sidecar
/// 收固定坐标。硬塞进 IR 会让"同一份语义在不同机器上产出不同的文档内容"。
/// </remarks>
/// <param name="Document">产出文档。版本号为 0，两个哈希已算好。</param>
/// <param name="Sidecar">这份文本里的固定位置。没有 <c>pin</c> 时是一份空的。</param>
/// <param name="Report">这次映射做了什么。</param>
public sealed record MappingResult(DiagramDocument Document, UserSidecar Sidecar, MappingReport Report);
