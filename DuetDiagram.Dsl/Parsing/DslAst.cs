using DuetDiagram.Core.Model;

namespace DuetDiagram.Dsl.Parsing;

/// <summary>一条诊断。</summary>
/// <param name="Message">说明。</param>
/// <param name="Line">行号。</param>
/// <param name="Column">列号。</param>
/// <remarks>
/// 与 Mermaid 侧同一条理由：认不出来的内容记成诊断而不是抛异常。
/// 一份输入里某几行写坏了，其余部分往往是好的。
/// </remarks>
public sealed record DslDiagnostic(string Message, int Line, int Column);

/// <summary>端口声明。</summary>
/// <param name="Name">端口名。</param>
/// <param name="Side">所在方位。</param>
public sealed record DslPortDeclaration(string Name, PortSide Side);

/// <summary>节点声明。</summary>
/// <param name="Id">标识。</param>
/// <param name="Label">显示文本。未声明时为空，由消费方回退到标识。</param>
/// <param name="Shape">形状。</param>
/// <param name="StyleToken">调色板令牌名。</param>
/// <param name="Layer">图层名。</param>
/// <param name="Description">说明，不参与渲染。</param>
/// <param name="Ports">端口列表。</param>
/// <param name="Parent">所属分组。不在分组里时为空。</param>
public sealed record DslNodeDeclaration(
    string Id,
    string? Label,
    NodeShape Shape,
    string? StyleToken,
    string? Layer,
    string? Description,
    IReadOnlyList<DslPortDeclaration> Ports,
    string? Parent);

/// <summary>边声明。</summary>
/// <param name="Id">标识。原样未给时为空，由消费方生成。</param>
/// <param name="From">起点标识。</param>
/// <param name="FromPort">起点端口。未指定时为空。</param>
/// <param name="To">终点标识。</param>
/// <param name="ToPort">终点端口。未指定时为空。</param>
/// <param name="Label">边上的文字。</param>
/// <param name="Arrow">箭头样式。</param>
/// <param name="Line">线型。</param>
/// <param name="StyleToken">调色板令牌名。</param>
public sealed record DslEdgeDeclaration(
    string? Id,
    string From,
    string? FromPort,
    string To,
    string? ToPort,
    string? Label,
    ArrowStyle Arrow,
    LineStyle Line,
    string? StyleToken);

/// <summary>分组种类。</summary>
public enum DslGroupKind
{
    /// <summary><c>group</c>：把若干节点圈在一起。</summary>
    Group,

    /// <summary><c>lane</c>：按责任方划分的条带。</summary>
    Lane,

    /// <summary><c>subflow</c>：可以折叠的嵌套流程。</summary>
    Subflow,
}

/// <summary>
/// 分组声明。
/// </summary>
/// <param name="Kind">种类。</param>
/// <param name="Id">标识。</param>
/// <param name="Label">显示文本。</param>
/// <param name="Members">直接成员。**既有节点也有嵌套分组**，引用不带类型前缀。</param>
/// <param name="Parent">外层分组的标识。</param>
/// <remarks>
/// 成员里既放节点也放分组，是照着 IR 的约定来的：Core 的校验器先按节点解析成员、
/// 解析不到再按组合解析。这里保持一致，映射阶段就不需要再转换一次形状。
/// </remarks>
public sealed record DslGroupDeclaration(
    DslGroupKind Kind,
    string Id,
    string Label,
    IReadOnlyList<string> Members,
    string? Parent);

/// <summary>布局意图的种类。</summary>
/// <remarks>
/// 这五条是 DSL 存在的主要理由：Mermaid 一个都表达不了，
/// 而它们正是「人工拖动作为约束反馈」的实现基础。
/// </remarks>
public enum DslLayoutIntentKind
{
    /// <summary><c>same-rank a, b</c>：强制同处一层。</summary>
    SameRank,

    /// <summary><c>order check: pass, fail</c>：判定节点出边的层内次序。</summary>
    Order,

    /// <summary><c>align a, b</c>：让这几个节点对齐。</summary>
    Align,

    /// <summary><c>place fail right-of pass</c>：相对位置。</summary>
    Place,

    /// <summary><c>pin fail at 640, 320</c>：固定坐标。</summary>
    Pin,
}

/// <summary>
/// 一条布局意图。
/// </summary>
/// <param name="Kind">种类。</param>
/// <param name="Nodes">涉及的节点。<c>Order</c> 是出边次序，<c>Place</c> 是参照物，<c>Pin</c> 为空。</param>
/// <param name="Subject">主语节点。<c>Order</c>、<c>Place</c>、<c>Pin</c> 用；<c>SameRank</c> 与 <c>Align</c> 为空。</param>
/// <param name="Relation"><c>Place</c> 的相对关系。</param>
/// <param name="X"><c>Pin</c> 的横坐标。</param>
/// <param name="Y"><c>Pin</c> 的纵坐标。</param>
/// <remarks>
/// 五种意图收在一条记录里而不是各建一个类型，是因为它们在语法层形状相近
/// （都是一行关键字加若干标识），而真正有差别的部分是映射阶段的事。
/// 语义映射再按 Kind 分派到 IR 的四类约束上。
/// </remarks>
public sealed record DslLayoutIntent(
    DslLayoutIntentKind Kind,
    IReadOnlyList<string> Nodes,
    string? Subject = null,
    PlaceRelation? Relation = null,
    double? X = null,
    double? Y = null);

/// <summary>
/// 解析出的 DSL 文档。
/// </summary>
/// <remarks>
/// 各列表按**首次出现顺序**排列。顺序有意义：它决定节点在集合里的位置，
/// 而那个位置是层内次序的依据。
/// </remarks>
public sealed record DslDocument
{
    /// <summary>版本声明的值。缺省视为当前版本。</summary>
    public int Version { get; init; } = DslVersion.Current;

    public DiagramKind Kind { get; init; } = DiagramKind.Flowchart;

    public Direction Direction { get; init; } = Direction.TB;

    public IReadOnlyList<DslNodeDeclaration> Nodes { get; init; } = [];

    public IReadOnlyList<DslEdgeDeclaration> Edges { get; init; } = [];

    public IReadOnlyList<DslGroupDeclaration> Groups { get; init; } = [];

    public IReadOnlyList<DslLayoutIntent> Layout { get; init; } = [];

    /// <summary>同层节点间距。未声明时为空，由布局默认值兜底。</summary>
    public double? NodeSpacing { get; init; }

    /// <summary>层间距。未声明时为空。</summary>
    public double? LayerSpacing { get; init; }

    public IReadOnlyList<DslDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>解析是否完全干净。</summary>
    public bool IsClean => Diagnostics.Count == 0;
}

/// <summary>版本。</summary>
public static class DslVersion
{
    /// <summary>当前版本。</summary>
    public const int Current = 1;
}
