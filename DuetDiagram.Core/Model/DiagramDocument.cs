using System.Text.Json.Serialization;
using DuetDiagram.Core.Serialization;

namespace DuetDiagram.Core.Model;

/// <summary>
/// 一份完整快照。
/// </summary>
/// <remarks>
/// <para>
/// 快照是**深拷贝**：所有集合都用数组承接，子对象是不可变记录，
/// 因此拿到快照之后原文档怎么改都不会影响它。这一点是它可用的前提——
/// 如果快照里存的是原集合的引用，那它就只是一张迟早会变的"当时的视图"。
/// </para>
/// <para>
/// 用显式记录而不是序列化后的字符串：字符串形式省事，但每次取样都要走一遍序列化，
/// 而快照最常见的用途恰恰是在频繁的检查点上。显式记录没有这个开销，
/// 代价是字段要跟着模型一起改——这个代价由编译器帮我们盯着，不会漏。
/// </para>
/// </remarks>
public sealed record DiagramSnapshot(
    string Id,
    DiagramKind Kind,
    Direction Direction,
    int Version,
    string StructuralHash,
    string VisualHash,
    IReadOnlyList<PageDef> Pages,
    IReadOnlyList<LayerDef> Layers,
    IReadOnlyList<NodeDef> Nodes,
    IReadOnlyList<EdgeDef> Edges,
    IReadOnlyList<CompositeDef> Composites,
    IReadOnlyList<TagDef> Tags,
    IReadOnlyList<ActionDef> Actions,
    IReadOnlyList<FontDef> Fonts,
    IReadOnlyList<TextStylePreset> TextPresets,
    Palette Palette,
    LayoutHints Layout,
    CanvasSettings Canvas)
{
    /// <summary>
    /// 结构化相等。
    /// </summary>
    /// <remarks>
    /// 必须重写：九个集合成员都是集合，记录自动生成的相等性对它们用引用比较，
    /// 会让"两份内容相同的快照"被判为不等。
    /// </remarks>
    public bool Equals(DiagramSnapshot? other) =>
        other is not null
        && string.Equals(Id, other.Id, StringComparison.Ordinal)
        && Kind == other.Kind
        && Direction == other.Direction
        && Version == other.Version
        && string.Equals(StructuralHash, other.StructuralHash, StringComparison.Ordinal)
        && string.Equals(VisualHash, other.VisualHash, StringComparison.Ordinal)
        && CollectionEquality.List(Pages, other.Pages)
        && CollectionEquality.List(Layers, other.Layers)
        && CollectionEquality.List(Nodes, other.Nodes)
        && CollectionEquality.List(Edges, other.Edges)
        && CollectionEquality.List(Composites, other.Composites)
        && CollectionEquality.List(Tags, other.Tags)
        && CollectionEquality.List(Actions, other.Actions)
        && CollectionEquality.List(Fonts, other.Fonts)
        && CollectionEquality.List(TextPresets, other.TextPresets)
        && Equals(Palette, other.Palette)
        && Equals(Layout, other.Layout)
        && Equals(Canvas, other.Canvas);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Id, StringComparer.Ordinal);
        hash.Add(Kind);
        hash.Add(Direction);
        hash.Add(Version);
        hash.Add(StructuralHash, StringComparer.Ordinal);
        hash.Add(VisualHash, StringComparer.Ordinal);
        hash.Add(CollectionEquality.ListHash(Pages));
        hash.Add(CollectionEquality.ListHash(Layers));
        hash.Add(CollectionEquality.ListHash(Nodes));
        hash.Add(CollectionEquality.ListHash(Edges));
        hash.Add(CollectionEquality.ListHash(Composites));
        hash.Add(CollectionEquality.ListHash(Tags));
        hash.Add(CollectionEquality.ListHash(Actions));
        hash.Add(CollectionEquality.ListHash(Fonts));
        hash.Add(CollectionEquality.ListHash(TextPresets));
        hash.Add(Palette);
        hash.Add(Layout);
        hash.Add(Canvas);

        return hash.ToHashCode();
    }
}

/// <summary>
/// 图的根对象，也是整个系统唯一的事实源：所有入口（界面、AI 助手、外部代理、导入器）
/// 最终都只能改这个对象，没有任何旁路。
/// </summary>
/// <remarks>
/// <para>
/// 不变式一：九个集合对外只读，修改只能通过命令。
/// 它们全部由 <see cref="DefinitionCollection{T}"/> 承载，只以 <see cref="IReadOnlyList{T}"/>
/// 暴露给外部程序集，写入通道只留给同程序集里的命令实现。
/// 这样任何调用方都无法绕过命令层直接改结构。
/// </para>
/// <para>
/// 不变式二：<see cref="Version"/>、<see cref="StructuralHash"/>、<see cref="VisualHash"/>
/// 的 setter 是 <c>internal</c>。它们由命令总线在命令成功之后统一推进，
/// 命令自身无法改写版本号，因此"版本号与实际内容脱节"这类问题在类型层面就不可能发生。
/// 三个子对象的 setter 同样如此——它们也会影响序列化与哈希。
/// </para>
/// <para>
/// 版本号的语义是"状态序列号"而不是"变更次数"：撤销与重做同样会让它 +1。
/// 这样任意两个版本号之间的区间都能对应到一段确定的变更序列，增量同步才有依据。
/// </para>
/// </remarks>
public sealed class DiagramDocument
{
    private readonly DefinitionCollection<PageDef> _pages = new();
    private readonly DefinitionCollection<LayerDef> _layers = new();
    private readonly DefinitionCollection<NodeDef> _nodes = new();
    private readonly DefinitionCollection<EdgeDef> _edges = new();
    private readonly DefinitionCollection<CompositeDef> _composites = new();
    private readonly DefinitionCollection<TagDef> _tags = new();
    private readonly DefinitionCollection<ActionDef> _actions = new();
    private readonly DefinitionCollection<FontDef> _fonts = new();
    private readonly DefinitionCollection<TextStylePreset> _textPresets = new();

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
    /// <para>
    /// 每个参数都必须与同名属性的类型**完全一致**，否则序列化器会在读取时报
    /// "constructor parameter must bind to an object property" 并拒绝整个类型。
    /// 特别是集合参数要写成 <see cref="IReadOnlyList{T}"/>，不能图省事写成 <see cref="List{T}"/>——
    /// 属性是只读接口类型，参数放宽成可变类型就匹配不上了。
    /// </para>
    /// <para>
    /// 集合参数一律可空。旧版本的 JSON 里没有这些字段，反序列化时它们会是空引用，
    /// 按空集合处理即可——这样旧文件仍然打得开，而不是因为缺了新字段就整个读不出来。
    /// 这正是"首行版本声明可选、解析器记提示但不报错"那条约定在数据层的对应做法。
    /// </para>
    /// </remarks>
    [JsonConstructor]
    public DiagramDocument(
        string id,
        DiagramKind kind,
        Direction direction,
        int version,
        string structuralHash,
        string visualHash,
        IReadOnlyList<PageDef>? pages,
        IReadOnlyList<LayerDef>? layers,
        IReadOnlyList<NodeDef>? nodes,
        IReadOnlyList<EdgeDef>? edges,
        IReadOnlyList<CompositeDef>? composites,
        IReadOnlyList<TagDef>? tags,
        IReadOnlyList<ActionDef>? actions,
        IReadOnlyList<FontDef>? fonts,
        IReadOnlyList<TextStylePreset>? textPresets,
        Palette? palette,
        LayoutHints? layout,
        CanvasSettings? canvas)
        : this(id, kind, direction)
    {
        Version = version;
        StructuralHash = structuralHash;
        VisualHash = visualHash;

        _pages.Replace(pages);
        _layers.Replace(layers);
        _nodes.Replace(nodes);
        _edges.Replace(edges);
        _composites.Replace(composites);
        _tags.Replace(tags);
        _actions.Replace(actions);
        _fonts.Replace(fonts);
        _textPresets.Replace(textPresets);

        Palette = palette ?? new Palette();
        Layout = layout ?? LayoutHintsDefaults.Create();
        Canvas = canvas ?? new CanvasSettings();
    }

    /// <summary>
    /// 按内容新建文档，并把两个哈希一并算好。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是**外部程序集唯一一条能造出内容完整的文档的路**。另两条都不行：
    /// <see cref="DiagramDocument(string, DiagramKind, Direction)"/> 只造空文档；
    /// 反序列化构造要求把版本号与哈希一并传入，而哈希要先有文档实例才算得出来，
    /// setter 又是 internal——外部算得出、赋不进去。
    /// </para>
    /// <para>
    /// **它只造新实例，不提供改已有文档的路。** 哈希的计算权仍然只有两处：
    /// 命令总线（每次成功变更后重算）与这里（构造时算一次）。
    /// 这里算完之后文档对外就是只读的，要改仍然只能走命令。
    /// 没有这条限定，这个方法会变成一个绕过命令层直接改内容的后门。
    /// </para>
    /// <para>
    /// **版本号取 0。** <see cref="Version"/> 是变更计数，构造不是变更——
    /// "新建时是 0、首次成功变更后是 1"这条规则不为导入破例。
    /// </para>
    /// <para>
    /// **不做标识唯一性检查。** 九个集合共用一个命名空间，重复标识是可能出现的，
    /// 而它是**可诊断**的：<c>DiagramValidator</c> 报 <c>DUPLICATE_ID</c>。
    /// 在这里抛异常会让"造一份带重复标识的文档去测校验器"变得做不到，
    /// 也会让这条路径与反序列化路径行为不一致（后者同样不检查）。
    /// 内容合不合法由校验器回答，不由构造入口回答。
    /// </para>
    /// </remarks>
    /// <param name="id">文档标识。</param>
    /// <param name="kind">图类型。</param>
    /// <param name="direction">主方向。</param>
    /// <param name="pages">页面集合。</param>
    /// <param name="layers">图层集合。</param>
    /// <param name="nodes">节点集合。顺序有意义：位置是层内次序的依据。</param>
    /// <param name="edges">边集合。顺序有意义：删除后撤销要按原索引插回。</param>
    /// <param name="composites">组合集合。</param>
    /// <param name="tags">标签集合。</param>
    /// <param name="actions">动作集合。</param>
    /// <param name="fonts">字体集合。</param>
    /// <param name="textPresets">文本预设集合。</param>
    /// <param name="palette">调色板。为空表示空表，由渲染层用兜底外观。</param>
    /// <param name="layout">布局提示。为空表示取缺省间距、无约束。</param>
    /// <param name="canvas">画布设置。</param>
    public static DiagramDocument CreateFromContent(
        string id,
        DiagramKind kind = DiagramKind.Flowchart,
        Direction direction = Direction.TB,
        IReadOnlyList<PageDef>? pages = null,
        IReadOnlyList<LayerDef>? layers = null,
        IReadOnlyList<NodeDef>? nodes = null,
        IReadOnlyList<EdgeDef>? edges = null,
        IReadOnlyList<CompositeDef>? composites = null,
        IReadOnlyList<TagDef>? tags = null,
        IReadOnlyList<ActionDef>? actions = null,
        IReadOnlyList<FontDef>? fonts = null,
        IReadOnlyList<TextStylePreset>? textPresets = null,
        Palette? palette = null,
        LayoutHints? layout = null,
        CanvasSettings? canvas = null)
    {
        var document = new DiagramDocument(
            id,
            kind,
            direction,
            version: 0,
            structuralHash: string.Empty,
            visualHash: string.Empty,
            pages,
            layers,
            nodes,
            edges,
            composites,
            tags,
            actions,
            fonts,
            textPresets,
            palette,
            layout,
            canvas);

        document.StructuralHash = DiagramHashing.ComputeStructuralHash(document);
        document.VisualHash = DiagramHashing.ComputeVisualHash(document);

        return document;
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
    /// 结构哈希，回答"要不要重新求解布局"。
    /// </summary>
    /// <remarks>
    /// 它的判据不是"图形形状变了吗"，而是"现有坐标还有效吗"。因此除了连接关系与父子归属，
    /// 它还覆盖端口、布局提示与字体——这三者都不改变图形拓扑，却都会让已算出的坐标失效。
    /// 名称保留为"结构"是历史原因，读的时候按"要不要重排"来理解。
    /// </remarks>
    public string StructuralHash { get; internal set; } = string.Empty;

    /// <summary>
    /// 视觉哈希，覆盖标签、形状、样式令牌等只影响外观的内容。
    /// 结构没变而它变了，说明只需要重绘，不需要重布局。
    /// </summary>
    public string VisualHash { get; internal set; } = string.Empty;

    // ---- 九个集合 ----

    /// <summary>页面集合。顺序有意义。</summary>
    public IReadOnlyList<PageDef> Pages => _pages.ReadOnly;

    /// <summary>图层集合。顺序有意义：排列次序决定叠放次序。</summary>
    public IReadOnlyList<LayerDef> Layers => _layers.ReadOnly;

    /// <summary>节点集合。顺序有意义：节点在集合中的位置就是它的层内次序依据。</summary>
    public IReadOnlyList<NodeDef> Nodes => _nodes.ReadOnly;

    /// <summary>边集合。顺序有意义：删除节点后撤销时按原索引插回，才能还原成删除前的样子。</summary>
    public IReadOnlyList<EdgeDef> Edges => _edges.ReadOnly;

    /// <summary>组合集合。顺序有意义：泳道成员的先后就是条带顺序。</summary>
    public IReadOnlyList<CompositeDef> Composites => _composites.ReadOnly;

    /// <summary>标签集合。</summary>
    public IReadOnlyList<TagDef> Tags => _tags.ReadOnly;

    /// <summary>动作集合。不进任何哈希——它只影响交互，不影响外观与布局。</summary>
    public IReadOnlyList<ActionDef> Actions => _actions.ReadOnly;

    /// <summary>字体集合。计入结构哈希——字体变化会改变标签宽度，进而改变布局。</summary>
    public IReadOnlyList<FontDef> Fonts => _fonts.ReadOnly;

    /// <summary>文本样式预设集合。</summary>
    public IReadOnlyList<TextStylePreset> TextPresets => _textPresets.ReadOnly;

    // ---- 三个子对象 ----

    /// <summary>调色板。计入视觉哈希。</summary>
    public Palette Palette { get; internal set; } = new();

    /// <summary>布局提示。计入结构哈希——改了间距就必须重排。</summary>
    public LayoutHints Layout { get; internal set; } = LayoutHintsDefaults.Create();

    /// <summary>画布设置。计入视觉哈希。</summary>
    public CanvasSettings Canvas { get; internal set; } = new();

    // ---- 快照 ----

    /// <summary>
    /// 取一份完整快照。
    /// </summary>
    /// <remarks>
    /// 深拷贝：所有集合复制成新数组，子对象本身是不可变记录，因此快照取到之后不会被打扰。
    /// 版本号与两个哈希一并带走，这样快照能回答"这份内容是哪个版本、对应什么结构"。
    /// </remarks>
    public DiagramSnapshot TakeFullSnapshot() => new(
        Id,
        Kind,
        Direction,
        Version,
        StructuralHash,
        VisualHash,
        [.. _pages.ReadOnly],
        [.. _layers.ReadOnly],
        [.. _nodes.ReadOnly],
        [.. _edges.ReadOnly],
        [.. _composites.ReadOnly],
        [.. _tags.ReadOnly],
        [.. _actions.ReadOnly],
        [.. _fonts.ReadOnly],
        [.. _textPresets.ReadOnly],
        Palette,
        Layout,
        Canvas);

    /// <summary>
    /// 用快照整体替换文档内容。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 版本号与哈希一并恢复，而不是留给命令总线推进。原因是这个方法的用途是
    /// "把文档退回某个已知状态"，版本号如果被重新推进，退回去的那份内容就再也无法
    /// 与历史上的版本号对上，增量同步会失去参照。
    /// </para>
    /// <para>
    /// 文档标识不参与替换。跨文档套用快照会得到一个自相矛盾的对象：
    /// 标识是甲的，内容是乙的。要打开另一份文档应当新建实例——
    /// 这与"历史栈清空只用于同一文档重新加载"是同一条约定的两面。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">快照来自另一份文档。</exception>
    public void RestoreFromSnapshot(DiagramSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!string.Equals(snapshot.Id, Id, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"快照属于文档 {snapshot.Id}，不能套用到 {Id} 上。打开另一份文档应当新建实例。",
                nameof(snapshot));
        }

        Kind = snapshot.Kind;
        Direction = snapshot.Direction;
        Version = snapshot.Version;
        StructuralHash = snapshot.StructuralHash;
        VisualHash = snapshot.VisualHash;

        _pages.Replace(snapshot.Pages);
        _layers.Replace(snapshot.Layers);
        _nodes.Replace(snapshot.Nodes);
        _edges.Replace(snapshot.Edges);
        _composites.Replace(snapshot.Composites);
        _tags.Replace(snapshot.Tags);
        _actions.Replace(snapshot.Actions);
        _fonts.Replace(snapshot.Fonts);
        _textPresets.Replace(snapshot.TextPresets);

        Palette = snapshot.Palette;
        Layout = snapshot.Layout;
        Canvas = snapshot.Canvas;
    }

    // ---- 仅命令实现可用的可变视图 ----

    internal List<PageDef> MutablePages => _pages.Mutable;

    internal List<LayerDef> MutableLayers => _layers.Mutable;

    internal List<NodeDef> MutableNodes => _nodes.Mutable;

    internal List<EdgeDef> MutableEdges => _edges.Mutable;

    internal List<CompositeDef> MutableComposites => _composites.Mutable;

    internal List<TagDef> MutableTags => _tags.Mutable;

    internal List<ActionDef> MutableActions => _actions.Mutable;

    internal List<FontDef> MutableFonts => _fonts.Mutable;

    internal List<TextStylePreset> MutableTextPresets => _textPresets.Mutable;

    // ---- 查找 ----

    internal bool HasNode(string id) => _nodes.Has(id);

    internal bool HasEdge(string id) => _edges.Has(id);

    internal bool HasComposite(string id) => _composites.Has(id);

    internal NodeDef? FindNode(string id) => _nodes.Find(id);

    internal CompositeDef? FindComposite(string id) => _composites.Find(id);

    internal EdgeDef? FindEdge(string id) => _edges.Find(id);

    /// <summary>
    /// 标识能不能当作边的端点。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 节点可以，组合也可以。分层架构图里 <c>ODS --&gt; DWD</c> 是拿分组当端点用的，
    /// 表达的是"这一层流向那一层"——这是外部格式里很常见、也很自然的写法，
    /// 语料里真的出现了（见 <c>reports/compare-blind/parse-report.md</c> 的"端点是分组的边"一列）。
    /// </para>
    /// <para>
    /// 判定顺序是先节点后组合，与组合成员的解析口径一致（见 <c>CheckCompositeMembership</c>）。
    /// 九个集合共用一个命名空间，所以同一个标识不会既是节点又是组合，
    /// 先查哪个都不影响结果——顺序统一只是为了让两处读起来是同一件事。
    /// </para>
    /// <para>
    /// **端口仍然只属于节点。** 组合没有端口，端点落在组合上时不能指定端口，
    /// 见 <c>DiagramValidator.CheckPort</c>。
    /// </para>
    /// </remarks>
    internal bool HasEndpoint(string id) => HasNode(id) || HasComposite(id);

    internal int IndexOfNode(string id) => _nodes.IndexOf(id);

    internal int IndexOfEdge(string id) => _edges.IndexOf(id);

    /// <summary>
    /// 标识在九个集合中是否已被占用。
    /// </summary>
    /// <remarks>
    /// 九个集合共用一个命名空间：成员列表里的标识不区分它是节点还是组合，
    /// 引用关系（边的两端、标签的成员、动作的目标）也都不带类型前缀。
    /// 因此新增任何定义之前都要用这个整体检查，而不是只查自己那一个集合。
    /// </remarks>
    internal bool IsIdTaken(string id, IDefinition? except = null) =>
        IsTaken(_pages, id, except)
        || IsTaken(_layers, id, except)
        || IsTaken(_nodes, id, except)
        || IsTaken(_edges, id, except)
        || IsTaken(_composites, id, except)
        || IsTaken(_tags, id, except)
        || IsTaken(_actions, id, except)
        || IsTaken(_fonts, id, except)
        || IsTaken(_textPresets, id, except);

    private static bool IsTaken<T>(DefinitionCollection<T> collection, string id, IDefinition? except)
        where T : IDefinition
    {
        var found = collection.Find(id);

        return found is not null && !ReferenceEquals(found, except);
    }
}
