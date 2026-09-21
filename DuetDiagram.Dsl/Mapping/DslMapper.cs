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
        private readonly List<UnresolvedIntent> _unresolved = [];

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

            var nodes = MapNodes(implicitNodes.Ids);
            var composites = MapComposites();
            var edges = MapEdges(edgeIds);

            _created.AddRange(
                implicitNodes.FromEdges.Select(created => new CreatedNode(created.Id, $"边 {edgeIds[created.EdgeIndex]}")));

            _created.AddRange(
                implicitNodes.FromIntents.Select(created => new CreatedNode(created.Id, created.Intent)));

            var document = DiagramDocument.CreateFromContent(
                _options.DocumentId,
                _source.Kind,
                _source.Direction,
                nodes: nodes,
                edges: edges,
                composites: composites,
                palette: _options.Palette,
                layout: LayoutHints(edgeIds));

            var report = new MappingReport(_source.Diagnostics, _renames, _created, _unresolved);

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
        /// <para>
        /// **布局意图里的引用也算引用。** 规格的设计原则写着"未声明即隐式创建：
        /// 节点在首次被引用时自动出现"，而 <c>pin fail at 640, 320</c> 里的
        /// <c>fail</c> 就是一次引用——不补的话那条约束会指向一个不存在的节点，
        /// 而校验器目前不检查布局约束的节点引用（见 P1-17 的 findings），
        /// 于是它会一路安静地传下去。
        /// </para>
        /// <para>
        /// **例外是 <c>order</c> 的次序项。** 那些名字指的是主语节点的出边终点，
        /// 不是新节点；补出来只会多一个谁也不连的方块。
        /// </para>
        /// </remarks>
        private (List<string> Ids, List<(string Id, int EdgeIndex)> FromEdges, List<(string Id, string Intent)> FromIntents)
            CollectImplicitNodes()
        {
            var declaredNodes = _source.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            var declaredGroups = _source.Groups.Select(g => Mapped(g.Id)!).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            var ids = new List<string>();
            var fromEdges = new List<(string Id, int EdgeIndex)>();
            var fromIntents = new List<(string Id, string Intent)>();

            void Consider(string endpoint, Action<string> record)
            {
                if (declaredNodes.Contains(endpoint) || declaredGroups.Contains(endpoint) || !seen.Add(endpoint))
                {
                    return;
                }

                ids.Add(endpoint);
                record(endpoint);
            }

            for (var index = 0; index < _source.Edges.Count; index++)
            {
                var edgeIndex = index;

                foreach (var endpoint in (string[])[_source.Edges[index].From, _source.Edges[index].To])
                {
                    Consider(endpoint, id => fromEdges.Add((id, edgeIndex)));
                }
            }

            foreach (var intent in _source.Layout)
            {
                var text = IntentText(intent);

                foreach (var reference in NodeReferences(intent))
                {
                    Consider(reference, id => fromIntents.Add((id, text)));
                }
            }

            // 先占住名字，后面给边发标识时要让开它们——两者共用一个命名空间。
            _taken.UnionWith(ids);

            return (ids, fromEdges, fromIntents);
        }

        /// <summary>
        /// 一条布局意图里算作"引用了一个节点"的名字。
        /// </summary>
        /// <remarks>
        /// <c>order</c> 只算主语：它的次序项是出边的终点，不是节点声明。
        /// 其余四种（含 <c>pin</c>）的主语与列表都是节点引用。
        /// </remarks>
        private static IEnumerable<string> NodeReferences(DslLayoutIntent intent)
        {
            if (intent.Kind == DslLayoutIntentKind.Order)
            {
                return intent.Subject is null ? [] : [intent.Subject];
            }

            return intent.Subject is null
                ? intent.Nodes
                : [intent.Subject, .. intent.Nodes];
        }

        private List<NodeDef> MapNodes(IReadOnlyList<string> implicitNodes)
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

            foreach (var id in implicitNodes)
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
        /// 把五类布局意图落到 IR 的四类约束上。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 四类约束都带归属方与创建时间。归属方由调用方声明（见 <see cref="MappingOptions.Owner"/>），
        /// 创建时间走注入的时钟——它虽然不进哈希，但会出现在产物里，
        /// 读系统时钟的话"同一份文本映射两次得到同一份产物"就不成立了。
        /// </para>
        /// <para>
        /// <c>pin</c> 不在这里：<see cref="LayoutHints"/> 的四个列表都是相对约束，
        /// 没有绝对坐标，而布局输入本来就从 sidecar 收固定坐标。它走另一条路。
        /// </para>
        /// </remarks>
        private LayoutHints LayoutHints(IReadOnlyList<string> edgeIds)
        {
            var defaults = LayoutHintsDefaults.Create();
            var owner = _options.Owner;
            var at = _options.Time.UtcNow;

            var sameRank = new List<Constraint<SameRankConstraint>>();
            var order = new List<Constraint<OrderConstraint>>();
            var align = new List<Constraint<AlignConstraint>>();
            var place = new List<Constraint<PlaceConstraint>>();

            foreach (var intent in _source.Layout)
            {
                switch (intent.Kind)
                {
                    case DslLayoutIntentKind.SameRank:
                        sameRank.Add(new Constraint<SameRankConstraint>(new SameRankConstraint(intent.Nodes), owner, at));
                        break;

                    case DslLayoutIntentKind.Align:
                        align.Add(new Constraint<AlignConstraint>(new AlignConstraint(intent.Nodes), owner, at));
                        break;

                    case DslLayoutIntentKind.Place:
                        place.Add(new Constraint<PlaceConstraint>(
                            new PlaceConstraint(intent.Subject!, intent.Nodes[0], intent.Relation!.Value),
                            owner,
                            at));
                        break;

                    case DslLayoutIntentKind.Order:
                        var outgoing = ResolveOutgoingEdges(intent, edgeIds);

                        if (outgoing.Count > 0)
                        {
                            order.Add(new Constraint<OrderConstraint>(
                                new OrderConstraint(intent.Subject!, outgoing),
                                owner,
                                at));
                        }

                        break;

                    case DslLayoutIntentKind.Pin:
                        // 绝对坐标不进 IR，见上面的说明。
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(intent), intent.Kind, "没有登记这个布局意图");
                }
            }

            return defaults with
            {
                NodeSpacing = _source.NodeSpacing ?? defaults.NodeSpacing,
                LayerSpacing = _source.LayerSpacing ?? defaults.LayerSpacing,
                SameRank = sameRank,
                Order = order,
                Align = align,
                Place = place,
            };
        }

        /// <summary>
        /// 把 <c>order</c> 的次序项从"目标节点"解析成"出边标识"。
        /// </summary>
        /// <remarks>
        /// <para>
        /// **两处对同一个概念的定义不一致，转换在这一层做。**
        /// DSL 写的是 <c>order check: pass, fail</c>——名字是节点名，人就是这么想的
        /// （"校验之后先走通过还是先走不通过"）。而 IR 的 <c>OrderConstraint</c>
        /// 存的是**出边的标识**：它的用途是减少连线的交叉，而交叉是边之间的事，
        /// 节点标识区分不了平行边（同一个终点可以有多条边）。
        /// </para>
        /// <para>
        /// 一个名字可能对应多条边（平行边）。全部按边在文档里的先后收进来——
        /// 收一条、丢几条会让约束的表达与文本不符，而且不报错。
        /// </para>
        /// <para>
        /// 找不到那条边时这一项落不了地，记进报告。整个意图一项都落不了地时不产出约束：
        /// 一条空次序的约束没有意义，而 <c>OrderConstraint</c> 的次序为空会让下游
        /// 分不清"没有约束"与"约束是空的"。
        /// </para>
        /// </remarks>
        private List<string> ResolveOutgoingEdges(DslLayoutIntent intent, IReadOnlyList<string> edgeIds)
        {
            var resolved = new List<string>();

            foreach (var target in intent.Nodes)
            {
                var found = false;

                for (var index = 0; index < _source.Edges.Count; index++)
                {
                    var edge = _source.Edges[index];

                    if (string.Equals(edge.From, intent.Subject, StringComparison.Ordinal)
                        && string.Equals(edge.To, target, StringComparison.Ordinal))
                    {
                        resolved.Add(edgeIds[index]);
                        found = true;
                    }
                }

                if (!found)
                {
                    _unresolved.Add(new UnresolvedIntent(
                        IntentText(intent),
                        target,
                        $"节点 {intent.Subject} 没有通往 {target} 的边，而 order 的次序项指的是出边的终点。"));
                }
            }

            return resolved;
        }

        /// <summary>把一条意图还原成接近原文的样子，用于报告。</summary>
        private static string IntentText(DslLayoutIntent intent) => intent.Kind switch
        {
            DslLayoutIntentKind.SameRank => $"same-rank {string.Join(", ", intent.Nodes)}",
            DslLayoutIntentKind.Align => $"align {string.Join(", ", intent.Nodes)}",
            DslLayoutIntentKind.Order => $"order {intent.Subject}: {string.Join(", ", intent.Nodes)}",
            DslLayoutIntentKind.Place =>
                $"place {intent.Subject} {RelationText(intent.Relation)} {intent.Nodes[0]}",
            DslLayoutIntentKind.Pin => $"pin {intent.Subject} at {intent.X}, {intent.Y}",
            _ => throw new ArgumentOutOfRangeException(nameof(intent), intent.Kind, "没有登记这个布局意图"),
        };

        private static string RelationText(PlaceRelation? relation) => relation switch
        {
            PlaceRelation.RightOf => "right-of",
            PlaceRelation.LeftOf => "left-of",
            PlaceRelation.Above => "above",
            PlaceRelation.Below => "below",
            _ => "?",
        };

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
