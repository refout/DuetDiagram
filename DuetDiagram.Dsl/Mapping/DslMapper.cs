using DuetDiagram.Core.Model;
using DuetDiagram.Dsl.Parsing;

namespace DuetDiagram.Dsl.Mapping;

/// <summary>
/// 把 DSL 语法树映射成 IR。
/// </summary>
/// <remarks>
/// <para>
/// 这一层做的是**形状转换加消歧**，不做语义判断。语法树已经是 IR 的形状
/// （分组成员引用不带类型前缀、端点就是标识字符串），所以大部分字段是一对一直传；
/// 真正要做决定的只有三处，都写在下面各自的注释里：撞名、补节点、缺省值该不该写出来。
/// </para>
/// <para>
/// 产出的文档必须能通过 <c>DiagramValidator</c> 零问题——校验器就是给这种
/// 不经过命令层的外部输入准备的。校验器在 Core，映射层不重复实现它。
/// </para>
/// </remarks>
public static class DslMapper
{
    /// <summary>
    /// 映射一份解析好的 DSL。
    /// </summary>
    /// <param name="document">解析产物。</param>
    /// <param name="options">调用参数。</param>
    public static MappingResult Map(DslDocument document, MappingOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DocumentId);

        return new Session(document, options).Run();
    }

    /// <summary>
    /// 一次映射的中间状态。
    /// </summary>
    /// <remarks>
    /// 做成一个有状态的类而不是一串静态方法：撞名改名要贯穿后面每一步
    /// （节点父级、组合父级、成员表都要用改后的标识），把这些映射表一路传下去
    /// 比收在一个对象里更容易传漏一处。传漏的表现是某个父级指向了不存在的组合，
    /// 而那要等到校验器才报出来。
    /// </remarks>
    private sealed class Session
    {
        private readonly DslDocument _source;
        private readonly MappingOptions _options;

        /// <summary>九个集合共用一个命名空间，生成的标识要在这个整体里查重。</summary>
        private readonly HashSet<string> _taken = new(StringComparer.Ordinal);

        /// <summary>被改名的容器：原标识 → 新标识。只有容器会进这里。</summary>
        private readonly Dictionary<string, string> _renamed = new(StringComparer.Ordinal);

        private readonly List<MappingRename> _renames = [];
        private readonly List<CreatedNode> _created = [];

        public Session(DslDocument source, MappingOptions options)
        {
            _source = source;
            _options = options;

            foreach (var node in source.Nodes)
            {
                _taken.Add(node.Id);
            }

            foreach (var group in source.Groups)
            {
                _taken.Add(group.Id);
            }

            foreach (var edge in source.Edges)
            {
                if (edge.Id is not null)
                {
                    _taken.Add(edge.Id);
                }
            }
        }

        public MappingResult Run()
        {
            // 顺序是有依赖的，不能调换：
            //   1. 改名要在节点与组合都建好之前定下来（父级与成员表都要用改后的标识）。
            //   2. 补节点的标识要先于边的标识定下来——两者共用一个命名空间，
            //      先给边发标识就可能发出一个被补节点占着的名字。
            //   3. 边的标识要先于"补节点记录"定下来，记录里要写清是哪条边引用的。
            RenameCollidingComposites();

            var implicitNodes = CollectImplicitNodes();
            var edgeIds = AssignEdgeIds();

            var nodes = MapNodes(implicitNodes);
            var composites = MapComposites();
            var edges = MapEdges(edgeIds);

            _created.AddRange(
                implicitNodes.Select(created => new CreatedNode(created.Id, edgeIds[created.EdgeIndex])));

            var document = DiagramDocument.CreateFromContent(
                _options.DocumentId,
                _source.Kind,
                _source.Direction,
                nodes: nodes,
                edges: edges,
                composites: composites,
                palette: _options.Palette,
                layout: LayoutHints());

            var report = new MappingReport(_source.Diagnostics, _renames, _created);

            return new MappingResult(document, report);
        }

        // ---- 撞名 ----

        /// <summary>
        /// 容器与节点同名时，**容器让位，节点保留原名**。
        /// </summary>
        /// <remarks>
        /// <para>
        /// IR 的九个集合共用一个命名空间，同名不能共存；而 DSL 允许
        /// <c>lane pay "支付服务"</c> 与节点 <c>pay "完成支付"</c> 同时存在。
        /// 这不是边角情况：冻结语料里真的出现过，模型天然会把「那个阶段」与「那个框」
        /// 取同一个名字。
        /// </para>
        /// <para>
        /// **为什么让容器让位而不是节点。** 节点名出现在边的两端，改名会牵动更多引用，
        /// 而且改错方向时是"连线接错了"——比"框的名字对不上"难发现得多。
        /// 容器名是给人看的标题，改标识不动标签，视觉上几乎无感。
        /// </para>
        /// <para>
        /// **改了名之后，原文里指向这个名字的边仍然指向节点。** 这是刻意的：
        /// 一条边到底想连节点还是连容器，从文本上分不出来（两者同名），
        /// 而节点优先与 IR 的端点解析口径一致（先节点、后组合）。
        /// 改名记录会把这件事写下来，于是"容器连不上了"是查得到的，不是猜的。
        /// </para>
        /// </remarks>
        private void RenameCollidingComposites()
        {
            var nodeIds = _source.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

            foreach (var group in _source.Groups)
            {
                if (!nodeIds.Contains(group.Id))
                {
                    continue;
                }

                var newId = UniqueId(group.Id + Suffix(group.Kind));
                _renamed[group.Id] = newId;

                _renames.Add(new MappingRename(
                    group.Id,
                    newId,
                    $"与节点 {group.Id} 同名。节点保留原名，容器改名，标签不变。"));
            }
        }

        private static string Suffix(DslGroupKind kind) => kind switch
        {
            DslGroupKind.Group => "-group",
            DslGroupKind.Lane => "-lane",
            DslGroupKind.Subflow => "-subflow",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "没有登记这个分组种类"),
        };

        /// <summary>标识改后的样子。没被改过的原样返回。空值原样返回。</summary>
        private string? Mapped(string? id) =>
            id is not null && _renamed.TryGetValue(id, out var renamed) ? renamed : id;

        // ---- 节点 ----

        /// <summary>
        /// 边引用了但没声明的端点，补成节点。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 补出来的节点：形状矩形、显示文本等于标识、不属于任何分组——都是规格定下的
        /// （见 <c>docs/DSL-Syntax.md</c>）。
        /// </para>
        /// <para>
        /// **插在集合末尾，顺序按边表里的首次引用。** 语法树里没有行号
        /// （只有诊断带位置），所以"插到首次引用处"做不到；要做得先给
        /// <c>DslNodeDeclaration</c> 与 <c>DslEdgeDeclaration</c> 都加上位置信息。
        /// 代价是补出来的节点在层内次序上排在最后，而层内次序会影响连线的交叉数量——
        /// 这是规则，不是意外。
        /// </para>
        /// <para>
        /// 端点指向的是已声明的容器时不补节点：那是一条连到分组上的边
        /// （<c>ODS --&gt; DWD</c> 那种写法），IR 容得下它。补了反而会撞名。
        /// </para>
        /// </remarks>
        private List<(string Id, int EdgeIndex)> CollectImplicitNodes()
        {
            var declaredNodes = _source.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            var declaredGroups = _source.Groups.Select(g => Mapped(g.Id)!).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var created = new List<(string Id, int EdgeIndex)>();

            for (var index = 0; index < _source.Edges.Count; index++)
            {
                var edge = _source.Edges[index];

                foreach (var endpoint in (string[])[edge.From, edge.To])
                {
                    if (declaredNodes.Contains(endpoint)
                        || declaredGroups.Contains(endpoint)
                        || !seen.Add(endpoint))
                    {
                        continue;
                    }

                    created.Add((endpoint, index));
                }
            }

            // 先占住名字，后面给边发标识时要让开它们——两者共用一个命名空间。
            foreach (var (id, _) in created)
            {
                _taken.Add(id);
            }

            return created;
        }

        private List<NodeDef> MapNodes(IReadOnlyList<(string Id, int EdgeIndex)> implicitNodes)
        {
            var nodes = new List<NodeDef>(_source.Nodes.Count + implicitNodes.Count);

            foreach (var node in _source.Nodes)
            {
                nodes.Add(new NodeDef
                {
                    Id = node.Id,

                    // 显示文本省略时用标识本身。语法层如实产出"没写"，
                    // 回退是这一层的事——IR 里的 label 是要拿去渲染的，不能是空串。
                    Label = node.Label ?? node.Id,
                    Shape = node.Shape,
                    StyleToken = node.StyleToken,
                    Layer = node.Layer,
                    Desc = node.Description,
                    Parent = Mapped(node.Parent),
                    Ports = MapPorts(node.Ports),
                });
            }

            foreach (var (id, _) in implicitNodes)
            {
                nodes.Add(new NodeDef { Id = id, Label = id });
            }

            return nodes;
        }

        private static List<PortDef> MapPorts(IReadOnlyList<DslPortDeclaration> ports) =>
            [.. ports.Select(port => new PortDef { Name = port.Name, Side = port.Side, IsCustom = true })];

        // ---- 组合 ----

        private List<CompositeDef> MapComposites()
        {
            var composites = new List<CompositeDef>(_source.Groups.Count);

            foreach (var group in _source.Groups)
            {
                var id = Mapped(group.Id)!;

                composites.Add(NewComposite(group, id));
            }

            return composites;
        }

        private CompositeDef NewComposite(DslGroupDeclaration group, string id)
        {
            var members = MembersOf(id);

            return group.Kind switch
            {
                DslGroupKind.Group => new GroupDef { Id = id, Label = group.Label, Parent = Mapped(group.Parent), Members = members },
                DslGroupKind.Lane => new LaneDef { Id = id, Label = group.Label, Parent = Mapped(group.Parent), Members = members },
                DslGroupKind.Subflow => new SubflowDef { Id = id, Label = group.Label, Parent = Mapped(group.Parent), Members = members },
                _ => throw new ArgumentOutOfRangeException(nameof(group), group.Kind, "没有登记这个分组种类"),
            };
        }

        /// <summary>
        /// 成员表：按父级重新推一遍，而不是照抄语法树里那一份再改名。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 撞名时语法树的成员表里会有两个相同的字符串——一个是节点、一个是容器——
        /// 照着改会改错那一个，而且改错了不会报错，只会让成员关系悄悄变样。
        /// 重推没有这个问题：父级字段只可能指向容器，改起来是无歧义的。
        /// </para>
        /// <para>
        /// 顺序与语法层保持一致（先节点、后嵌套分组），所以没撞名时重推的结果
        /// 与语法树里那一份逐项相同。
        /// </para>
        /// </remarks>
        private List<string> MembersOf(string newGroupId) =>
        [
            .. _source.Nodes
                .Where(node => string.Equals(Mapped(node.Parent), newGroupId, StringComparison.Ordinal))
                .Select(node => node.Id),
            .. _source.Groups
                .Where(group => string.Equals(Mapped(group.Parent), newGroupId, StringComparison.Ordinal))
                .Select(group => Mapped(group.Id)!),
        ];

        // ---- 边 ----

        private List<EdgeDef> MapEdges(IReadOnlyList<string> edgeIds)
        {
            var edges = new List<EdgeDef>(_source.Edges.Count);

            for (var index = 0; index < _source.Edges.Count; index++)
            {
                var edge = _source.Edges[index];

                edges.Add(new EdgeDef
                {
                    Id = edgeIds[index],

                    // 端点**不改名**：撞名时容器让位，原文里的引用一律落在节点上。
                    // 见 RenameCollidingComposites 的说明。
                    From = edge.From,
                    To = edge.To,
                    FromPort = edge.FromPort,
                    ToPort = edge.ToPort,
                    Label = edge.Label ?? string.Empty,
                    Style = MapEdgeStyle(edge),
                });
            }

            return edges;
        }

        /// <summary>
        /// 边样式。与缺省值相同的字段**不写出来**。
        /// </summary>
        /// <remarks>
        /// <c>EdgeDef.Line</c> 与 <c>EdgeDef.Arrow</c> 已经会回退到实线、有箭头，
        /// 所以显式写一个同样的值不增加信息，却会让"同一张图的两种文本"产出
        /// 不同的 IR（视觉哈希也就跟着不同）。DSL 的 <c>--</c> 是
        /// <c>arrow=none</c>，与缺省不同，因此照写。
        /// </remarks>
        private static EdgeStyle MapEdgeStyle(DslEdgeDeclaration edge) => new()
        {
            Line = edge.Line == LineStyle.Solid ? null : edge.Line,
            Arrow = edge.Arrow == ArrowStyle.Arrow ? null : edge.Arrow,
            StyleToken = edge.StyleToken,
        };

        // ---- 布局提示 ----

        /// <summary>
        /// 文档级的布局提示。四类约束不在这里（见 P1-17 的后续提交）。
        /// </summary>
        private LayoutHints LayoutHints()
        {
            if (_source.NodeSpacing is null && _source.LayerSpacing is null)
            {
                return LayoutHintsDefaults.Create();
            }

            var defaults = LayoutHintsDefaults.Create();

            return defaults with
            {
                NodeSpacing = _source.NodeSpacing ?? defaults.NodeSpacing,
                LayerSpacing = _source.LayerSpacing ?? defaults.LayerSpacing,
            };
        }

        // ---- 标识生成 ----

        /// <summary>
        /// 给每条边定下标识：原文给了就用原文的，没给就补一个。
        /// </summary>
        /// <remarks>
        /// 语法层如实产出"没写标识"（<see cref="DslEdgeDeclaration.Id"/> 为空），
        /// 补标识是这一层的事——边没有标识就没法被命令层引用，也没法进 sidecar。
        /// </remarks>
        private string[] AssignEdgeIds()
        {
            var ids = new string[_source.Edges.Count];

            for (var index = 0; index < _source.Edges.Count; index++)
            {
                var id = _source.Edges[index].Id ?? NextEdgeId();

                _taken.Add(id);
                ids[index] = id;
            }

            return ids;
        }

        /// <summary>在整体命名空间里找一个没被占用的标识。</summary>
        private string UniqueId(string stem)
        {
            var candidate = stem;
            var counter = 2;

            while (!_taken.Add(candidate))
            {
                candidate = $"{stem}-{counter++}";
            }

            return candidate;
        }

        /// <summary>给未命名的边找一个标识：<c>e1</c>、<c>e2</c>……取第一个空位。</summary>
        private string NextEdgeId()
        {
            var counter = 1;

            while (_taken.Contains($"e{counter}"))
            {
                counter++;
            }

            return $"e{counter}";
        }
    }
}
