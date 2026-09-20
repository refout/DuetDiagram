using System.Text.Json.Serialization;

namespace DuetDiagram.Core.Model;

/// <summary>
/// IR 根对象 —— 唯一事实源。
/// </summary>
/// <remarks>
/// <para>
/// AGENTS.md 约定 1：集合对外只读，修改只能通过 <c>IDiagramCommand</c>。
/// <c>Version</c> / <c>StructuralHash</c> / <c>VisualHash</c> 的 setter 是 <c>internal</c>，
/// 外部程序集无法改写，只有 <c>DiagramCommandBus</c> 能递增。
/// </para>
/// <para>
/// 方案 §4.1 的九个集合（Pages / Layers / Composites / Tags / Actions / Fonts / TextPresets）
/// 与三个子对象（Palette / Layout / Canvas）不在本轮垂直切片内，见 docs/IR-Schema.md。
/// </para>
/// </remarks>
public sealed class DiagramDocument
{
    private readonly List<NodeDef> _nodes = [];
    private readonly List<EdgeDef> _edges = [];

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
    /// 反序列化构造。IR 往返无损由 RoundTrip 测试保证。
    /// </summary>
    /// <remarks>
    /// 参数类型必须与对应属性的类型**完全一致**，否则 System.Text.Json 会以
    /// "constructor parameter must bind to an object property" 拒绝整个类型。
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

    /// <summary>方向的唯一来源。</summary>
    public Direction Direction { get; internal set; }

    /// <summary>版本号。排序一律用 Version，不用时间戳（多进程时钟可能不同步）。</summary>
    public int Version { get; internal set; }

    public string StructuralHash { get; internal set; } = string.Empty;

    public string VisualHash { get; internal set; } = string.Empty;

    public IReadOnlyList<NodeDef> Nodes => _nodes;

    public IReadOnlyList<EdgeDef> Edges => _edges;

    internal List<NodeDef> MutableNodes => _nodes;

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
