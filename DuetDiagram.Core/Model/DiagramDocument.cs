using System.Text.Json.Serialization;

namespace DuetDiagram.Core.Model;

/// <summary>
/// 图的根对象，也是整个系统唯一的事实源：所有入口（界面、AI 助手、外部代理、导入器）
/// 最终都只能改这个对象，没有任何旁路。
/// </summary>
/// <remarks>
/// <para>
/// 不变式一：<see cref="Nodes"/> 与 <see cref="Edges"/> 对外只读。
/// 内部虽然用 <see cref="List{T}"/> 持有，但只以 <see cref="IReadOnlyList{T}"/> 暴露给外部程序集，
/// 写入通道只留给同程序集里的命令实现。这样任何调用方都无法绕过命令层直接改结构。
/// </para>
/// <para>
/// 不变式二：<see cref="Version"/>、<see cref="StructuralHash"/>、<see cref="VisualHash"/>
/// 的 setter 是 <c>internal</c>。它们由命令总线在命令成功之后统一推进，
/// 命令自身无法改写版本号，因此"版本号与实际内容脱节"这类问题在类型层面就不可能发生。
/// </para>
/// <para>
/// 版本号的语义是"状态序列号"而不是"变更次数"：撤销与重做同样会让它 +1。
/// 这样任意两个版本号之间的区间都能对应到一段确定的变更序列，增量同步才有依据。
/// </para>
/// </remarks>
public sealed class DiagramDocument
{
    private readonly List<NodeDef> _nodes = [];
    private readonly List<EdgeDef> _edges = [];

    /// <summary>新建一个空文档。<see cref="Version"/> 从 0 开始，首次成功变更后变为 1。</summary>
    public DiagramDocument(
        string id,
        DiagramKind kind = DiagramKind.Flowchart,
        Direction direction = Direction.TB)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        Kind = kind;
        Direction = direction;
    }

    /// <summary>
    /// 反序列化专用构造。
    /// </summary>
    /// <remarks>
    /// 每个参数都必须与同名属性的类型**完全一致**，否则序列化器会在读取时报
    /// "constructor parameter must bind to an object property" 并拒绝整个类型。
    /// 特别是集合参数要写成 <see cref="IReadOnlyList{T}"/>，不能图省事写成 <see cref="List{T}"/>——
    /// 属性是只读接口类型，参数放宽成可变类型就匹配不上了。
    /// </remarks>
    [JsonConstructor]
    public DiagramDocument(
        string id,
        DiagramKind kind,
        Direction direction,
        int version,
        string structuralHash,
        string visualHash,
        IReadOnlyList<NodeDef> nodes,
        IReadOnlyList<EdgeDef> edges)
        : this(id, kind, direction)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        Version = version;
        StructuralHash = structuralHash;
        VisualHash = visualHash;
        _nodes.AddRange(nodes);
        _edges.AddRange(edges);
    }

    public string Id { get; }

    public DiagramKind Kind { get; internal set; }

    /// <summary>主方向。它是方向的唯一来源，布局引擎只从这里读取方向。</summary>
    public Direction Direction { get; internal set; }

    /// <summary>
    /// 单调递增的状态序列号，初始为 0。
    /// 多进程场景下各机器时钟可能不同步，所以排序一律用这个字段，绝不用时间戳。
    /// </summary>
    public int Version { get; internal set; }

    /// <summary>
    /// 结构哈希，覆盖"谁和谁相连"以及父子归属关系。
    /// 它只回答一个问题：连接关系变了吗？变了才需要重新跑布局。
    /// </summary>
    public string StructuralHash { get; internal set; } = string.Empty;

    /// <summary>
    /// 视觉哈希，覆盖标签、形状、样式令牌等只影响外观的内容。
    /// 结构没变而它变了，说明只需要重绘，不需要重布局。
    /// </summary>
    public string VisualHash { get; internal set; } = string.Empty;

    /// <summary>节点集合。顺序有意义：节点在集合中的位置就是它的层内次序依据。</summary>
    public IReadOnlyList<NodeDef> Nodes => _nodes;

    /// <summary>边集合。顺序有意义：删除节点后撤销时按原索引插回，才能还原成删除前的样子。</summary>
    public IReadOnlyList<EdgeDef> Edges => _edges;

    /// <summary>仅命令实现可用的可变视图。命令在同一程序集内，所以用 internal 而不是公开。</summary>
    internal List<NodeDef> MutableNodes => _nodes;

    /// <inheritdoc cref="MutableNodes"/>
    internal List<EdgeDef> MutableEdges => _edges;

    internal bool HasNode(string id) => _nodes.Any(n => string.Equals(n.Id, id, StringComparison.Ordinal));

    internal bool HasEdge(string id) => _edges.Any(e => string.Equals(e.Id, id, StringComparison.Ordinal));

    internal NodeDef? FindNode(string id) =>
        _nodes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.Ordinal));

    internal EdgeDef? FindEdge(string id) =>
        _edges.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));

    internal int IndexOfNode(string id) => _nodes.FindIndex(n => string.Equals(n.Id, id, StringComparison.Ordinal));

    internal int IndexOfEdge(string id) => _edges.FindIndex(e => string.Equals(e.Id, id, StringComparison.Ordinal));
}
