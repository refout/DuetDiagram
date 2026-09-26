using System.Globalization;
using System.Text;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Sidecar;
using DuetDiagram.Dsl.Parsing;

namespace DuetDiagram.Dsl.Export;

/// <summary>
/// 把 IR 写成 DSL 文本。
/// </summary>
/// <remarks>
/// <para>
/// **导出方向不许反向影响语法。** 这里只把 IR 里已经能表达的东西按既有语法写出来，
/// 写不出来的记进丢失清单。为了让导出好写而改语法的话，导入方向的对比测试量到的
/// 东西就变了——而那条测试是 DSL 去留判据的输入，动它等于自己改判据。
/// </para>
/// <para>
/// **缺省值不写。** 与导入方向同一条规则：显式写一个与缺省相同的值不增加信息，
/// 却会让同一张图的两种文本产出不同的文档。矩形不写 <c>shape</c>、实线不写 <c>line</c>、
/// 有箭头不写 <c>arrow</c>、标签与标识相同时不写标签、间距等于缺省时不写间距。
/// </para>
/// <para>
/// **<c>pin</c> 从 sidecar 来。** 绝对坐标不在 IR 里——它在人工产物的那份 sidecar 里，
/// 因为坐标属于渲染结果，存进 IR 会让同一份语义在不同机器上产生不同的文档内容。
/// 所以导出器要接一份 sidecar 才能把固定位置写出来；不传就没有 <c>pin</c> 行。
/// </para>
/// </remarks>
public static class DslExporter
{
    /// <summary>
    /// 导出。
    /// </summary>
    /// <param name="document">要导出的文档。</param>
    /// <param name="sidecar">人工产物，提供固定位置。为空表示不写 <c>pin</c>。</param>
    /// <remarks>
    /// 同一个文档与同一份 sidecar 两次调用，产出的文本逐字节相同——输出里没有任何
    /// 依赖系统时钟、哈希顺序或字典枚举顺序的东西。
    /// </remarks>
    public static DslExportResult Export(DiagramDocument document, UserSidecar? sidecar = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new Session(document, sidecar).Run();
    }

    /// <summary>
    /// 一次导出的中间状态。
    /// </summary>
    /// <remarks>
    /// 做成有状态的类而不是一串静态方法：标识改写要贯穿每一处引用
    /// （节点、边、成员表、父级、布局意图、固定位置），把那张映射表一路传下去
    /// 比收在一个对象里更容易传漏一处，而传漏的表现是导出的文本里有一条指向
    /// 不存在标识的引用——那份文本解析得出来，只是连错地方。
    /// </remarks>
    private sealed class Session
    {
        private readonly DiagramDocument _document;
        private readonly UserSidecar? _sidecar;

        private readonly StringBuilder _output = new();

        /// <summary>丢失清单，按类收拢。同一类只出一条，涉及的元素收在那一条的 <c>Ids</c> 里。</summary>
        private readonly List<DroppedFeature> _dropped = [];

        private readonly Dictionary<string, int> _droppedIndex = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _droppedIds = new(StringComparer.Ordinal);

        /// <summary>导出时用到的标识：原标识 → 写进文本的标识。只有被改写的进这里。</summary>
        private readonly Dictionary<string, string> _rewritten = new(StringComparer.Ordinal);

        private readonly HashSet<string> _taken = new(StringComparer.Ordinal);

        public Session(DiagramDocument document, UserSidecar? sidecar)
        {
            _document = document;
            _sidecar = sidecar;
        }

        public DslExportResult Run()
        {
            AssignIdentifiers();

            Header();
            TopLevelNodes();
            TopLevelGroups();
            Edges();
            LayoutIntents();
            Pins();
            Spacing();
            DocumentLevelLosses();

            FlushDropped();

            return new DslExportResult(_output.ToString(), new DslExportReport(_dropped));
        }

        #region 标识

        /// <summary>
        /// 给每一个要写出来的标识定下写法。
        /// </summary>
        /// <remarks>
        /// <para>
        /// DSL 的标识必须以字母或下划线开头、只含字母数字下划线与连字符，且不能出现
        /// 连续的连字符（词法层把它读成无箭头连线）。IR 里没有这条限制——标识是命令层
        /// 与外部调用方给的，可能是任意字符串。
        /// </para>
        /// <para>
        /// 写不出来的改成能写的，并把改写记进报告。**不能原样写出去**：那样导出的文本
        /// 自己解析不了，或者解析出别的意思；也不能悄悄跳过那个元素，那会让图少一块
        /// 而没有任何提示。
        /// </para>
        /// </remarks>
        private void AssignIdentifiers()
        {
            foreach (var id in AllIdentifiers())
            {
                _taken.Add(id);
            }

            foreach (var id in AllIdentifiers())
            {
                if (IsWritable(id))
                {
                    continue;
                }

                var replacement = Unique(Sanitize(id));
                _rewritten[id] = replacement;
                DropIds("标识", [id], $"原文里不是合法的标识，导出时改名为 {replacement}。");
            }
        }

        /// <summary>文档里所有会被写出来的标识，按文档顺序。</summary>
        private IEnumerable<string> AllIdentifiers()
        {
            foreach (var node in _document.Nodes)
            {
                yield return node.Id;
            }

            foreach (var composite in _document.Composites)
            {
                yield return composite.Id;
            }

            foreach (var edge in _document.Edges)
            {
                yield return edge.Id;
            }
        }

        /// <summary>写进文本的标识。</summary>
        private string Written(string id) => _rewritten.TryGetValue(id, out var replacement) ? replacement : id;

        /// <summary>这个标识能不能原样写进文本。</summary>
        private static bool IsWritable(string id)
        {
            if (id.Length == 0 || !(char.IsAsciiLetter(id[0]) || id[0] == '_'))
            {
                return false;
            }

            foreach (var current in id)
            {
                if (!(char.IsAsciiLetterOrDigit(current) || current == '_' || current == '-'))
                {
                    return false;
                }
            }

            // 连续连字符会被词法层读成无箭头连线，标识就断成两截了。
            return !id.Contains("--", StringComparison.Ordinal);
        }

        private static string Sanitize(string id)
        {
            var builder = new StringBuilder(id.Length + 1);

            foreach (var current in id)
            {
                builder.Append(char.IsAsciiLetterOrDigit(current) || current == '_' || current == '-' ? current : '_');
            }

            var text = builder.ToString();

            if (text.Length == 0)
            {
                text = "id";
            }

            if (!(char.IsAsciiLetter(text[0]) || text[0] == '_'))
            {
                text = "id" + text;
            }

            return text.Replace("--", "-", StringComparison.Ordinal);
        }

        private string Unique(string stem)
        {
            var candidate = stem;
            var counter = 2;

            while (_taken.Contains(candidate))
            {
                candidate = $"{stem}-{counter++}";
            }

            _taken.Add(candidate);

            return candidate;
        }

        #endregion

        #region 头部

        private void Header()
        {
            _output.Append("dsl ").Append(DslVersion.Current.ToString(CultureInfo.InvariantCulture)).Append('\n');
            _output.Append("kind ").Append(KindText(_document.Kind)).Append('\n');
            _output.Append("direction ").Append(DirectionText(_document.Direction)).Append('\n');
            _output.Append('\n');
        }

        private static string KindText(DiagramKind kind) => kind switch
        {
            DiagramKind.Flow => "flow",
            DiagramKind.Flowchart => "flowchart",
            DiagramKind.Block => "block",
            DiagramKind.State => "state",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "没有登记这个图类型"),
        };

        private static string DirectionText(Direction direction) => direction switch
        {
            Direction.TB => "TB",
            Direction.LR => "LR",
            Direction.RL => "RL",
            Direction.BT => "BT",
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "没有登记这个方向"),
        };

        #endregion

        #region 节点

        private void TopLevelNodes()
        {
            foreach (var node in _document.Nodes.Where(node => ParentComposite(node.Parent) is null))
            {
                NodeLine(node, indent: 0);
            }
        }

        private void NodeLine(NodeDef node, int indent)
        {
            var line = new StringBuilder();
            line.Append(Indent(indent)).Append(Written(node.Id));

            // 标签与标识相同时不写：导入方向会把省略的标签回退成标识，写出来只是噪声。
            if (!string.Equals(node.Label, node.Id, StringComparison.Ordinal))
            {
                line.Append(' ').Append(Quote(node.Label));
            }

            if (node.Shape != NodeShape.Rect)
            {
                line.Append(" shape=").Append(ShapeText(node.Shape));
            }

            if (!string.IsNullOrEmpty(node.StyleToken))
            {
                line.Append(" style=").Append(Attribute(node.StyleToken));
            }

            if (!string.IsNullOrEmpty(node.Layer))
            {
                line.Append(" layer=").Append(Attribute(node.Layer));
            }

            var ports = WritablePorts(node);

            if (ports.Count > 0)
            {
                line.Append(" ports=").Append(string.Join(',', ports.Select(port => $"{port.Name}:{SideText(port.Side)}")));
            }

            if (!string.IsNullOrEmpty(node.Desc))
            {
                line.Append(" desc=").Append(Quote(node.Desc));
            }

            _output.Append(line).Append('\n');

            NodeLosses(node);
        }

        /// <summary>
        /// 能写出来的端口。写不出来的（名字不合语法）在这里被筛掉并记进报告。
        /// </summary>
        /// <remarks>
        /// 端口名要当成裸记号写，而裸记号只认 ASCII 字母数字下划线与连字符。
        /// 名字里带别的字符时写出去会断成几个记号，解析出别的端口——所以宁可丢掉
        /// 那个端口并说出来，也不要写一份解析得出来但意思不对的文本。
        /// </remarks>
        private List<PortDef> WritablePorts(NodeDef node)
        {
            var writable = new List<PortDef>(node.Ports.Count);
            var rejected = new List<string>();

            foreach (var port in node.Ports)
            {
                if (IsWritable(port.Name))
                {
                    writable.Add(port);
                }
                else
                {
                    rejected.Add($"{node.Id}.{port.Name}");
                }
            }

            DropIds("端口名", rejected, "端口名不是合法的标识，写不进 ports=。");

            return writable;
        }

        private void NodeLosses(NodeDef node)
        {
            DropIds("节点归属的页面", Ids(node.Page is null, node.Id), "DSL 没有页面这个概念，导出的文本只描述一页。");
            DropIds("自定义形状的路径", Ids(node.ShapePath is null, node.Id), "DSL 的 shape 只有八个内置形状，路径数据写不进去。");
            DropIds("富文本内容", Ids(!node.RichText && node.RichLabel is null, node.Id), "DSL 的显示文本是纯文本，分段与行内样式写不进去。");
            DropIds("数学排版模式", Ids(node.MathMode == MathMode.None, node.Id), "DSL 没有数学排版，公式会被当成普通文字。");
            DropIds("节点具体样式", Ids(node.Style is null, node.Id), "DSL 的 style 只认调色板令牌，具体样式记录写不进去。");
            DropIds("节点文本样式", Ids(node.Text is null, node.Id), "DSL 没有文本样式，字号字重这些写不进去。");
            DropIds("宿主附加数据", Ids(node.Meta.Count == 0, node.Id), "DSL 没有承载宿主自定义数据的语法。");
            DropIds("标签里的回车符", Ids(!node.Label.Contains('\r', StringComparison.Ordinal), node.Id), "DSL 只支持 \\n 一种换行转义，回车被写成换行。");

            var drifted = node.Ports
                .Where(port => port.IsCustom == false || !port.Offset.Equals(0.5))
                .Select(port => $"{node.Id}.{port.Name}")
                .ToList();

            DropIds("端口的偏移与来源标记", drifted, "DSL 的端口只写名字与方位，偏移量与自定义标记写不进去。");
        }

        #endregion

        #region 分组

        private void TopLevelGroups()
        {
            foreach (var composite in _document.Composites.Where(item => ParentComposite(item.Parent) is null))
            {
                GroupBlock(composite, indent: 0);
            }
        }

        private void GroupBlock(CompositeDef composite, int indent)
        {
            var keyword = Keyword(composite);

            _output.Append(Indent(indent))
                .Append(keyword)
                .Append(' ')
                .Append(Written(composite.Id))
                .Append(' ')
                .Append(Quote(composite.Label))
                .Append('\n');

            // 成员按文档顺序写：节点在前、嵌套分组在后。这个顺序与导入方向重推成员表
            // 的顺序一致，所以成员次序能原样回来；对不上的那些在下面记进报告。
            foreach (var member in Members(composite).Where(item => item.Node is not null))
            {
                NodeLine(member.Node!, indent + 1);
            }

            foreach (var member in Members(composite).Where(item => item.Composite is not null))
            {
                GroupBlock(member.Composite!, indent + 1);
            }

            _output.Append(Indent(indent)).Append("end").Append('\n');

            CompositeLosses(composite);
        }

        /// <summary>写这个组合用的关键字。</summary>
        /// <remarks>
        /// 组合框没有对应关键字，写成 <c>group</c>：成员、标签与父级都能保住，
        /// 丢的只是"它不参与布局"这个性质——那一类在报告里说明。
        /// </remarks>
        private string Keyword(CompositeDef composite)
        {
            switch (composite)
            {
                case GroupDef:
                    return "group";
                case LaneDef:
                    return "lane";
                case SubflowDef:
                    return "subflow";
                case ComboDef:
                    DropIds("组合框", [composite.Id], "DSL 没有组合框这个关键字，写成 group——成员与标签保住，不参与布局这个性质丢了。");
                    return "group";
                default:
                    throw new ArgumentOutOfRangeException(nameof(composite), composite, "没有登记这个组合种类");
            }
        }

        /// <summary>这个组合的直接成员，按文档顺序、节点在前分组在后。</summary>
        private List<(NodeDef? Node, CompositeDef? Composite)> Members(CompositeDef composite)
        {
            var members = new List<(NodeDef?, CompositeDef?)>(composite.Members.Count);

            foreach (var id in composite.Members)
            {
                if (_document.Nodes.FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.Ordinal)) is { } node)
                {
                    members.Add((node, null));
                }
                else if (_document.Composites.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal)) is { } nested)
                {
                    members.Add((null, nested));
                }
            }

            return members;
        }

        private void CompositeLosses(CompositeDef composite)
        {
            DropIds("组合内部的布局方向", Ids(composite.Direction is null, composite.Id), "DSL 的分组没有方向，内部方向写不进去。");
            DropIds("组合的折叠状态", Ids(!composite.Collapsed, composite.Id), "DSL 没有折叠这个状态。");
            DropIds("组合样式", Ids(composite.Style is null, composite.Id), "DSL 的分组没有样式属性。");
            DropIds("组合内部的布局提示", Ids(composite.LocalLayout is null, composite.Id), "DSL 的分组没有承载局部布局提示的语法。");

            // 成员次序能回来的条件是"节点在前、分组在后，各自按文档顺序"——
            // 导入方向就是这么重推成员表的。对不上时次序回不来，而次序对泳道是有语义的。
            if (!Members(composite).Select(item => item.Node?.Id ?? item.Composite!.Id).SequenceEqual(composite.Members, StringComparer.Ordinal))
            {
                DropIds("成员次序", [composite.Id], "成员不是「节点在前、分组在后、各自按文档顺序」，重排之后次序会变。");
            }
        }

        /// <summary>这个标识指向的组合。指不到（或为空）时返回空。</summary>
        private CompositeDef? ParentComposite(string? id) =>
            id is null ? null : _document.Composites.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

        #endregion

        #region 边

        private void Edges()
        {
            foreach (var edge in _document.Edges)
            {
                EdgeLine(edge);
            }
        }

        private void EdgeLine(EdgeDef edge)
        {
            var line = new StringBuilder();
            line.Append(Written(edge.Id)).Append(": ")
                .Append(Endpoint(edge.From, edge.FromPort))
                .Append(edge.Arrow == ArrowStyle.None ? " -- " : " -> ")
                .Append(Endpoint(edge.To, edge.ToPort));

            if (edge.Label.Length > 0)
            {
                line.Append(' ').Append(Quote(edge.Label));
            }

            if (edge.Line != LineStyle.Solid)
            {
                line.Append(" line=").Append(LineText(edge.Line));
            }

            if (edge.Arrow is not (ArrowStyle.Arrow or ArrowStyle.None))
            {
                line.Append(" arrow=").Append(ArrowText(edge.Arrow));
            }

            if (!string.IsNullOrEmpty(edge.StyleToken))
            {
                line.Append(" style=").Append(Attribute(edge.StyleToken));
            }

            _output.Append(line).Append('\n');

            EdgeLosses(edge);
        }

        private string Endpoint(string id, string? port)
        {
            var text = Written(id);

            // 端口只属于节点。端点是组合时端口必须为空（校验器会报 EDGE_PORT_ON_COMPOSITE），
            // 所以这里只在有端口时拼一段。
            return string.IsNullOrEmpty(port) ? text : $"{text}.{port}";
        }

        private static string LineText(LineStyle line) => line switch
        {
            LineStyle.Dashed => "dashed",
            LineStyle.Dotted => "dotted",
            _ => throw new ArgumentOutOfRangeException(nameof(line), line, "没有登记这个线型"),
        };

        private static string ArrowText(ArrowStyle arrow) => arrow switch
        {
            ArrowStyle.OpenArrow => "open",
            ArrowStyle.Circle => "circle",
            ArrowStyle.Cross => "cross",
            _ => throw new ArgumentOutOfRangeException(nameof(arrow), arrow, "没有登记这个箭头"),
        };

        private void EdgeLosses(EdgeDef edge)
        {
            DropIds("边归属的页面", Ids(edge.Page is null, edge.Id), "DSL 没有页面这个概念，导出的文本只描述一页。");

            var style = edge.Style;
            var extra = new List<string>();

            if (style.Color is not null)
            {
                extra.Add("颜色");
            }

            if (style.Weight is not null)
            {
                extra.Add("粗细");
            }

            if (style.Route is not null)
            {
                extra.Add("走线方式");
            }

            if (style.LabelPosition is not null)
            {
                extra.Add("标签位置");
            }

            DropIds("边样式的其余字段", extra.Count == 0 ? [] : [edge.Id], $"DSL 的边只有线型、箭头与令牌，写不进去：{string.Join('、', extra)}。");
        }

        #endregion

        #region 布局意图

        private void LayoutIntents()
        {
            var layout = _document.Layout;

            foreach (var constraint in layout.SameRank)
            {
                _output.Append("same-rank ").Append(string.Join(", ", constraint.Value.Nodes.Select(Written))).Append('\n');
            }

            foreach (var constraint in layout.Align)
            {
                _output.Append("align ").Append(string.Join(", ", constraint.Value.Nodes.Select(Written))).Append('\n');
            }

            foreach (var constraint in layout.Place)
            {
                _output.Append("place ")
                    .Append(Written(constraint.Value.NodeId))
                    .Append(' ')
                    .Append(RelationText(constraint.Value.Relation))
                    .Append(' ')
                    .Append(Written(constraint.Value.RelativeTo))
                    .Append('\n');
            }

            foreach (var constraint in layout.Order)
            {
                OrderLine(constraint.Value);
            }

            if (layout.SameRank.Count + layout.Align.Count + layout.Place.Count + layout.Order.Count > 0)
            {
                _output.Append('\n');
            }

            OwnerLoss(layout);
        }

        /// <summary>
        /// 把 <c>order</c> 约束还原成文本。
        /// </summary>
        /// <remarks>
        /// IR 的次序存的是**出边的标识**——它的用途是减少连线的交叉，而交叉是边之间的事，
        /// 节点标识区分不了平行边。DSL 写的是**目标节点名**，人就是这么想的
        /// （"校验之后先走通过还是先走不通过"）。所以这里要把标识翻回名字。
        /// 翻不回来的那条边写不出来，记进报告——少一项之后图还是画得出来，
        /// 只是层内次序不是原来那个，而那种偏差在图上完全看不出来。
        /// </remarks>
        private void OrderLine(OrderConstraint constraint)
        {
            var targets = new List<string>();
            var unresolved = new List<string>();

            foreach (var edgeId in constraint.Order)
            {
                if (_document.Edges.FirstOrDefault(edge => string.Equals(edge.Id, edgeId, StringComparison.Ordinal)) is { } edge)
                {
                    targets.Add(Written(edge.To));
                }
                else
                {
                    unresolved.Add(edgeId);
                }
            }

            DropIds("order 约束里的边", unresolved, $"这些边不在文档里，order {constraint.NodeId} 的次序少了几项。");

            if (targets.Count > 0)
            {
                _output.Append("order ")
                    .Append(Written(constraint.NodeId))
                    .Append(": ")
                    .Append(string.Join(", ", targets))
                    .Append('\n');
            }
        }

        private static string RelationText(PlaceRelation relation) => relation switch
        {
            PlaceRelation.RightOf => "right-of",
            PlaceRelation.LeftOf => "left-of",
            PlaceRelation.Above => "above",
            PlaceRelation.Below => "below",
            _ => throw new ArgumentOutOfRangeException(nameof(relation), relation, "没有登记这个相对位置"),
        };

        /// <summary>
        /// 约束的归属方写不进去。
        /// </summary>
        /// <remarks>
        /// DSL 文本里没有归属方这一栏——导入时由调用方声明，因为同一份文本可能由模型生成
        /// 也可能由人手工写回，语法上分不出来。所以文档里若混着不同归属方的约束，
        /// 导出再导入会把它们统一成同一个，而归属方决定冲突时听谁的。
        /// </remarks>
        private void OwnerLoss(LayoutHints layout)
        {
            var owners = layout.SameRank.Select(item => item.Owner)
                .Concat(layout.Order.Select(item => item.Owner))
                .Concat(layout.Align.Select(item => item.Owner))
                .Concat(layout.Place.Select(item => item.Owner))
                .Distinct()
                .ToList();

            DropIds("约束的归属方", owners.Count > 1 ? [.. owners.Select(owner => owner.ToString())] : [], "DSL 没有归属方这一栏，导入时由调用方统一声明。");
        }

        #endregion

        #region 固定位置

        /// <summary>
        /// 把 sidecar 里的固定位置写成 <c>pin</c>。
        /// </summary>
        /// <remarks>
        /// 固定折线与自定义端口写不出来——DSL 只有 <c>pin</c> 一种人工产物的入口。
        /// 它们也记进报告：少了它们，用户拖过的路径与摆过的端口会回到自动结果。
        /// </remarks>
        private void Pins()
        {
            if (_sidecar is null)
            {
                return;
            }

            var pinned = _sidecar.PinnedNodes
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToList();

            foreach (var entry in pinned)
            {
                _output.Append("pin ")
                    .Append(Written(entry.Key))
                    .Append(" at ")
                    .Append(Number(entry.Value.X))
                    .Append(", ")
                    .Append(Number(entry.Value.Y))
                    .Append('\n');
            }

            if (pinned.Count > 0)
            {
                _output.Append('\n');
            }

            var orphans = pinned
                .Where(entry => _document.Nodes.All(node => !string.Equals(node.Id, entry.Key, StringComparison.Ordinal)))
                .Select(entry => entry.Key)
                .ToList();

            DropIds("指向不存在节点的固定位置", orphans, "sidecar 里的这些固定位置指向的节点不在文档里，写出去也落不了地。");

            DropIds("固定折线", [.. _sidecar.PinnedEdges.Keys], "DSL 只有节点的固定位置，边的固定折线写不进去。");
            DropIds("自定义端口", [.. _sidecar.CustomPorts.Keys], "DSL 的端口写在节点声明上，sidecar 里那一份写不进去。");
        }

        private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        #endregion

        #region 间距与文档级丢失

        private void Spacing()
        {
            if (!_document.Layout.NodeSpacing.Equals(LayoutHintsDefaults.NodeSpacing))
            {
                _output.Append("node-spacing ").Append(Number(_document.Layout.NodeSpacing)).Append('\n');
            }

            if (!_document.Layout.LayerSpacing.Equals(LayoutHintsDefaults.LayerSpacing))
            {
                _output.Append("layer-spacing ").Append(Number(_document.Layout.LayerSpacing)).Append('\n');
            }
        }

        /// <summary>
        /// 文档级的丢失：整个集合在 DSL 里都没有落脚处。
        /// </summary>
        /// <remarks>
        /// 这一类不指向具体元素，所以 <c>Ids</c> 为空。**文档标识**也在其中：
        /// DSL 描述的是图而不是文档，文本里没有文档标识这一栏，导入时要由调用方给。
        /// </remarks>
        private void DocumentLevelLosses()
        {
            DropIf(_document.Pages.Count > 0, "页面", "DSL 没有页面这个概念，多页文档导出的文本只描述缺省页。");
            DropIf(_document.Layers.Count > 0, "图层的可见性、锁定与次序", "DSL 能把节点放进某个图层，但图层本身的定义写不进去。");
            DropIf(_document.Fonts.Count > 0, "字体", "DSL 没有字体声明，而字体变化会改变标签宽度与布局结果。");
            DropIf(_document.Tags.Count > 0, "标签", "DSL 没有标签这个集合。");
            DropIf(_document.Actions.Count > 0, "动作", "DSL 没有交互动作的语法。");
            DropIf(_document.TextPresets.Count > 0, "文本样式预设", "DSL 没有具名文本样式预设。");
            DropIf(_document.Palette.Entries.Count > 0, "调色板定义", "DSL 只写令牌名，令牌到颜色的映射由调用方随导入一起给。");
            DropIf(_document.Canvas != new CanvasSettings(), "画布设置", "DSL 没有网格、纸张与背景这些画布设置。");
        }

        /// <summary>记一条文档级的丢失。没有具体元素涉及，但确实丢了。</summary>
        private void DropIf(bool condition, string feature, string reason)
        {
            if (condition)
            {
                Register(feature, reason);
            }
        }

        #endregion

        #region 记录

        /// <summary>
        /// 记一条元素级的丢失。一个元素都没涉及就什么也不记。
        /// </summary>
        /// <remarks>
        /// **按类收拢，不逐个元素一条。** 一次导出里同一类丢失往往涉及几十个元素，
        /// 逐条列出来之后报告会长到没人看，而"丢了端口，涉及 A、B、C"一眼就够。
        /// 所以先按类攒起来，最后一次性产出——顺序按首次出现，换个人跑得到同一份报告。
        /// </remarks>
        private void DropIds(string feature, IReadOnlyList<string> ids, string reason)
        {
            if (ids.Count == 0)
            {
                return;
            }

            Register(feature, reason);
            _droppedIds[feature].AddRange(ids);
        }

        /// <summary>给一类丢失占一个位置。已经在清单里的不动——理由取第一次那条。</summary>
        private void Register(string feature, string reason)
        {
            if (_droppedIndex.ContainsKey(feature))
            {
                return;
            }

            _droppedIndex[feature] = _dropped.Count;
            _droppedIds[feature] = [];
            _dropped.Add(new DroppedFeature(feature, [], reason));
        }

        /// <summary>涉及元素的标识。条件为真表示没有涉及，返回空。</summary>
        private static IReadOnlyList<string> Ids(bool none, string id) => none ? [] : [id];

        /// <summary>把攒起来的标识并进清单。导出结束时调一次。</summary>
        private void FlushDropped()
        {
            for (var index = 0; index < _dropped.Count; index++)
            {
                var item = _dropped[index];

                _dropped[index] = item with { Ids = _droppedIds[item.Feature] };
            }
        }

        #endregion

        #region 写法

        private static string Indent(int level) => new(' ', level * 2);

        /// <summary>把一段文字写成带引号的显示文本。</summary>
        /// <remarks>
        /// 只支持三种转义：引号、反斜杠、换行。这与语法层定的那三种一致——
        /// 其余写法原样保留反斜杠，不猜。
        /// </remarks>
        private static string Quote(string text)
        {
            var builder = new StringBuilder(text.Length + 2);
            builder.Append('"');

            foreach (var current in text)
            {
                switch (current)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                    case '\r':
                        builder.Append("\\n");
                        break;
                    default:
                        builder.Append(current);
                        break;
                }
            }

            builder.Append('"');

            return builder.ToString();
        }

        /// <summary>
        /// 属性值。能当裸记号写的就裸写，写不了的加引号。
        /// </summary>
        /// <remarks>
        /// 语法层允许属性值是引号包裹的文本，所以带空格或非 ASCII 的值有出路。
        /// 裸写优先只是为了让常见情形读起来干净——缺省写法与手写的文本一致。
        /// </remarks>
        private static string Attribute(string value) => IsBare(value) ? value : Quote(value);

        private static bool IsBare(string value)
        {
            if (value.Length == 0)
            {
                return false;
            }

            foreach (var current in value)
            {
                if (!(char.IsAsciiLetterOrDigit(current) || current is '_' or '-' or '.'))
                {
                    return false;
                }
            }

            return !value.Contains("--", StringComparison.Ordinal);
        }

        private static string ShapeText(NodeShape shape) => shape switch
        {
            NodeShape.Rounded => "rounded",
            NodeShape.Stadium => "stadium",
            NodeShape.Diamond => "diamond",
            NodeShape.Circle => "circle",
            NodeShape.Hexagon => "hexagon",
            NodeShape.Parallelogram => "parallelogram",
            NodeShape.Cylinder => "cylinder",
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "没有登记这个形状"),
        };

        private static string SideText(PortSide side) => side switch
        {
            PortSide.Left => "left",
            PortSide.Right => "right",
            PortSide.Top => "top",
            PortSide.Bottom => "bottom",
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, "没有登记这个方位"),
        };

        #endregion
    }
}
