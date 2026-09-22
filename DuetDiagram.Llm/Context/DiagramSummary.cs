using System.Text.Json.Serialization;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Llm.Context;

#region 摘要的各块

/// <summary>
/// 一个节点在最近一次布局里落在第几层。
/// </summary>
/// <remarks>
/// <para>
/// 层号是**量化之后**的位置，不是坐标。布局结果给的是像素，而像素每次重排都会变；
/// 把像素注入上下文，模型会学着照那些数字去微调位置——它学到的是一条不存在的能力。
/// 层号则是稳定的：只要图的形状不变，谁和谁同层就不变。
/// </para>
/// <para>
/// 层号从 0 开始、连续，与分层布局里「第几层」的口径一致。
/// </para>
/// </remarks>
/// <param name="Id">节点标识。</param>
/// <param name="Layer">第几层。</param>
public sealed record NodeRank(string Id, int Layer);

/// <summary>摘要里的一个节点。</summary>
/// <param name="Id">节点标识，改这个节点时要用它。</param>
/// <param name="Label">显示文本。</param>
/// <param name="Shape">形状。</param>
/// <param name="StyleToken">样式令牌名。未设时为空。</param>
public sealed record SummaryNode(string Id, string Label, NodeShape Shape, string? StyleToken);

/// <summary>摘要里的一条边。</summary>
/// <param name="Id">边标识，改这条边时要用它。</param>
/// <param name="From">起点标识。可能是节点，也可能是组合。</param>
/// <param name="To">终点标识。</param>
/// <param name="Label">边上的文字。</param>
/// <param name="Line">线型。</param>
public sealed record SummaryEdge(string Id, string From, string To, string Label, LineStyle Line);

/// <summary>
/// 摘要里的布局一段。
/// </summary>
/// <remarks>
/// <see cref="LayerCount"/> 与 <see cref="SameLayerGroups"/> 只有在宿主给了一份层投影时才有值。
/// 没有的时候留空而**不自己按拓扑算一个**：算出来的层数会与实际画面各按一套算法，
/// 对不上时没有任何东西会报错，而模型会照着一个错的层数去提要求。
/// </remarks>
/// <param name="Direction">主方向。</param>
/// <param name="LayerCount">层数。宿主没给层投影时为空。</param>
/// <param name="SameLayerGroups">同层的节点组，只列成员不少于两个的那些。</param>
public sealed record SummaryLayout(
    Direction Direction,
    int? LayerCount,
    IReadOnlyList<IReadOnlyList<string>> SameLayerGroups);

/// <summary>摘要里的一次字段变更。</summary>
/// <param name="ElementId">被改的元素标识。</param>
/// <param name="Field">字段名。元素级的名字带 <c>@</c> 前缀，表示整个元素被增删。</param>
/// <param name="NewValue">改之后的值。删掉时为空。</param>
/// <param name="Kind">变更种类。</param>
public sealed record SummaryFieldChange(string ElementId, string Field, string? NewValue, ChangeKind Kind);

/// <summary>
/// 摘要里的一条变更记录。
/// </summary>
/// <remarks>
/// <see cref="Fields"/> 为空有两种来历，靠 <see cref="IsBulkChange"/> 区分：
/// 批量变更（规模超限被裁剪过，明细没了，只剩条数）与没有字段明细的其它记录。
/// 不区分的话，前者会显示成「改了一条什么都没改的记录」。
/// </remarks>
/// <param name="Version">这一版对应的版本号。</param>
/// <param name="CommandId">产生这一版的命令标识。</param>
/// <param name="Source">谁改的。</param>
/// <param name="ActorId">操作者标识。未声明时为空。</param>
/// <param name="Timestamp">变更发生的时刻。</param>
/// <param name="Fields">字段级明细。</param>
/// <param name="IsBulkChange">明细是否因为规模过大而被裁剪过。</param>
/// <param name="OriginalChangeCount">裁剪前的变更条数。非批量变更时为 0。</param>
public sealed record SummaryChange(
    int Version,
    string CommandId,
    ChangeSource Source,
    string? ActorId,
    DateTimeOffset Timestamp,
    IReadOnlyList<SummaryFieldChange> Fields,
    bool IsBulkChange,
    int OriginalChangeCount);

/// <summary>
/// 一份归一化的图状态摘要。
/// </summary>
/// <remarks>
/// <para>
/// 它只带决策所需的最小信息。**不含原始坐标、不含人工产物里的折点与固定位置、
/// 不含整份 DSL**——多注入一样，模型就会去用它，而那些东西要么下次重排就变，
/// 要么根本不该由模型来读。
/// </para>
/// <para>
/// 集合都按标识排序过，所以同一份文档无论集合的插入顺序如何，这份对象完全相同。
/// 文本由 <see cref="SummaryFormatter"/> 从这份对象渲染，两者不会各说一套。
/// </para>
/// </remarks>
/// <param name="DocumentId">文档标识。</param>
/// <param name="Kind">图类型。</param>
/// <param name="Version">当前版本号。做版本检查时要用它。</param>
/// <param name="Nodes">节点列表，按标识排序。</param>
/// <param name="Edges">边列表，按标识排序。</param>
/// <param name="Layout">布局摘要。</param>
/// <param name="PinnedNodes">被固定的节点标识，已去重并排序。</param>
/// <param name="StyleTokens">可用样式令牌，按序数字典序排序。</param>
/// <param name="RecentChanges">最近若干条变更，最新的在前。</param>
public sealed record DiagramSummary(
    string DocumentId,
    DiagramKind Kind,
    int Version,
    IReadOnlyList<SummaryNode> Nodes,
    IReadOnlyList<SummaryEdge> Edges,
    SummaryLayout Layout,
    IReadOnlyList<string> PinnedNodes,
    IReadOnlyList<string> StyleTokens,
    IReadOnlyList<SummaryChange> RecentChanges);

/// <summary>
/// 交给调用方的那一份：给人看的文本，加上给程序读的结构。
/// </summary>
/// <remarks>
/// 文本是从 <see cref="Summary"/> 渲染出来的，不是另写一份。
/// 两者各写一份的话，改了一处忘了另一处，模型看到的与程序读到的是两份不同的内容，
/// 而两边各自都自洽。
/// </remarks>
/// <param name="Text">渲染后的文本。</param>
/// <param name="Summary">结构化摘要。</param>
public sealed record SummaryPayload(string Text, DiagramSummary Summary);

#endregion

#region 序列化

/// <summary>
/// 摘要的序列化上下文。
/// </summary>
/// <remarks>
/// 用编译期生成的上下文而不是运行时反射：后者在裁剪与原生编译之后会失败，
/// 而失败发生在真的调用模型那一刻。枚举写成字符串——写成数字的话，
/// 模型看到的是一串不知道对应什么的整数。
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SummaryPayload))]
[JsonSerializable(typeof(DiagramSummary))]
[JsonSerializable(typeof(SummaryLayout))]
[JsonSerializable(typeof(SummaryChange))]
internal sealed partial class SummaryJsonContext : JsonSerializerContext;

#endregion
