using DuetDiagram.Core.Model;
using DuetDiagram.Mermaid.Parsing;

namespace DuetDiagram.Mermaid.Import;

/// <summary>
/// 把 Mermaid 语法树映射成 IR。
/// </summary>
/// <remarks>
/// <para>
/// 与 DSL 那侧的映射同一形状：做形状转换与消歧，不做语义判断。
/// 两边共用同一套消歧规则（分组不因被引用而变成节点、成员表按父级重推），
/// 否则两种格式的对比量到的是实现差异而不是格式差异。
/// </para>
/// <para>
/// 产出的文档必须能通过 <c>DiagramValidator</c> 零问题。校验器就是给这种
/// 不经过命令层的外部输入准备的——命令层那道防线在这里一点都用不上。
/// </para>
/// </remarks>
public static class MermaidImporter
{
    /// <summary>
    /// 导入一份解析好的 Mermaid 流程图。
    /// </summary>
    /// <param name="chart">解析产物。</param>
    /// <param name="options">调用参数。</param>
    public static ImportResult Import(MermaidFlowchart chart, ImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DocumentId);

        return new Session(chart, options).Run();
    }

    /// <summary>一次导入的中间状态。</summary>
    private sealed class Session
    {
        private readonly MermaidFlowchart _chart;
        private readonly ImportOptions _options;

        /// <summary>九个集合共用一个命名空间，生成的标识要在这个整体里查重。</summary>
        private readonly HashSet<string> _taken = new(StringComparer.Ordinal);

        private readonly List<DroppedNode> _dropped = [];
        private readonly List<IgnoredStyleProperty> _ignored = [];

        public Session(MermaidFlowchart chart, ImportOptions options)
        {
            _chart = chart;
            _options = options;

            foreach (var node in chart.Nodes)
            {
                _taken.Add(node.Id);
            }

            foreach (var subgraph in chart.Subgraphs)
            {
                _taken.Add(subgraph.Id);
            }
        }

        public ImportResult Run()
        {
            var subgraphIds = _chart.Subgraphs.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            var styles = ResolveStyles();

            var nodes = MapNodes(subgraphIds, styles);
            var composites = MapComposites(styles);
            var edges = MapEdges();

            var document = DiagramDocument.CreateFromContent(
                _options.DocumentId,
                DiagramKind.Flowchart,
                _chart.Direction,
                nodes: nodes,
                edges: edges,
                composites: composites,
                palette: _options.Palette);

            var report = new MermaidImportReport(_chart.Diagnostics, _dropped, _ignored);

            return new ImportResult(document, report);
        }

        private List<NodeDef> MapNodes(IReadOnlySet<string> subgraphIds, IReadOnlyDictionary<string, NodeStyle> styles)
        {
            var nodes = new List<NodeDef>(_chart.Nodes.Count);

            foreach (var node in _chart.Nodes)
            {
                if (subgraphIds.Contains(node.Id))
                {
                    _dropped.Add(new DroppedNode(node.Id, node.Id));
                    continue;
                }

                nodes.Add(new NodeDef
                {
                    Id = node.Id,

                    // 显示文本省略时用标识本身。解析器如实产出"没写"，
                    // 回退是这一层的事——IR 里的 label 要拿去渲染，不能是空串。
                    Label = node.Label ?? node.Id,
                    Shape = node.Shape,
                    Parent = node.Parent,
                    Style = styles.GetValueOrDefault(node.Id),
                });
            }

            return nodes;
        }

        private List<CompositeDef> MapComposites(IReadOnlyDictionary<string, NodeStyle> styles)
        {
            var composites = new List<CompositeDef>(_chart.Subgraphs.Count);

            foreach (var subgraph in _chart.Subgraphs)
            {
                composites.Add(new GroupDef
                {
                    Id = subgraph.Id,
                    Label = subgraph.Label,
                    Parent = subgraph.Parent,
                    Members = MembersOf(subgraph.Id),
                    Direction = subgraph.Direction,
                    Style = styles.GetValueOrDefault(subgraph.Id),
                });
            }

            return composites;
        }

        /// <summary>
        /// 成员表：按父级重推，节点在前、嵌套分组在后。
        /// </summary>
        /// <remarks>
        /// <para>
        /// **语法树里那份成员表是不全的**：它只装了节点，嵌套子图不在其中
        /// （子图的父级能反向指认出来，但成员表里没有）。而 IR 的约定是
        /// "成员里既有节点也有子组合"，漏掉嵌套子图会让外层组合的成员表与父级字段对不上。
        /// </para>
        /// <para>
        /// 重推而不是"照抄再加嵌套子图"，是为了跟另一侧同一条规则：父级是唯一事实源。
        /// 顺序与那边一致（先节点、后嵌套分组），两边的产物才是可比的。
        /// </para>
        /// </remarks>
        private List<string> MembersOf(string subgraphId) =>
        [
            .. _chart.Nodes
                .Where(node => string.Equals(node.Parent, subgraphId, StringComparison.Ordinal))
                .Select(node => node.Id),
            .. _chart.Subgraphs
                .Where(inner => string.Equals(inner.Parent, subgraphId, StringComparison.Ordinal))
                .Select(inner => inner.Id),
        ];

        private List<EdgeDef> MapEdges()
        {
            var edges = new List<EdgeDef>(_chart.Links.Count);

            foreach (var link in _chart.Links)
            {
                var id = NextEdgeId();

                _taken.Add(id);

                edges.Add(new EdgeDef
                {
                    Id = id,
                    From = link.From,
                    To = link.To,
                    Label = link.Label ?? string.Empty,
                    Style = MapEdgeStyle(link),
                });
            }

            return edges;
        }

        /// <summary>
        /// 边样式。与缺省值相同的字段**不写出来**。
        /// </summary>
        /// <remarks>
        /// <c>EdgeDef.Line</c> 与 <c>EdgeDef.Arrow</c> 已经会回退到实线、有箭头，
        /// 所以显式写一个同样的值不增加信息，却会让"同一张图的两种文本"产出不同的 IR
        /// （视觉哈希也就跟着不同）。
        /// </remarks>
        private static EdgeStyle MapEdgeStyle(MermaidLinkDeclaration link) => new()
        {
            Line = link.Line == LineStyle.Solid ? null : link.Line,
            Arrow = link.Arrow == ArrowStyle.Arrow ? null : link.Arrow,
        };

        #region 样式与类

        /// <summary>
        /// 把 <c>style</c> 与 <c>classDef</c> + <c>class</c> 解析成"标识到样式"的映射。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 两趟：先把所有类定义收下来，再套用。Mermaid 要求类先定义后套用，
        /// 但两趟做没有代价，而只有一趟时"先套用后定义"会**静默丢掉样式**——
        /// 那种丢失在图上几乎看不出来。
        /// </para>
        /// <para>
        /// 同名时后面的覆盖前面的；<c>style</c> 直接写在节点上的优先于类。
        /// 顺序按"类在前、节点自己的 style 在后"处理，于是覆盖关系自然成立。
        /// </para>
        /// </remarks>
        private Dictionary<string, NodeStyle> ResolveStyles()
        {
            var styles = new Dictionary<string, NodeStyle>(StringComparer.Ordinal);
            var classes = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var declaration in _chart.Classes)
            {
                if (declaration.Properties is not null)
                {
                    classes[declaration.Name] = declaration.Properties;
                }
            }

            // 类套用在前，节点自己的 style 在后。
            foreach (var declaration in _chart.Classes)
            {
                if (declaration.Properties is not null || !classes.TryGetValue(declaration.Name, out var properties))
                {
                    continue;
                }

                var style = ParseStyle(declaration.Name, properties);

                if (style is null)
                {
                    continue;
                }

                foreach (var target in declaration.Targets)
                {
                    styles[target] = Merge(styles.GetValueOrDefault(target), style);
                }
            }

            foreach (var declaration in _chart.Styles)
            {
                var style = ParseStyle(declaration.Target, declaration.Properties);

                if (style is not null)
                {
                    styles[declaration.Target] = Merge(styles.GetValueOrDefault(declaration.Target), style);
                }
            }

            return styles;
        }

        /// <summary>后写的字段覆盖先写的，没写的字段留着。</summary>
        private static NodeStyle Merge(NodeStyle? earlier, NodeStyle later) => new()
        {
            Fill = later.Fill ?? earlier?.Fill,
            Stroke = later.Stroke ?? earlier?.Stroke,
            Text = later.Text ?? earlier?.Text,
            Weight = later.Weight ?? earlier?.Weight,
            Border = later.Border ?? earlier?.Border,
            Radius = later.Radius ?? earlier?.Radius,
            Opacity = later.Opacity ?? earlier?.Opacity,
            Badge = later.Badge ?? earlier?.Badge,
        };

        /// <summary>
        /// 把 <c>fill:#f9f,stroke:#333,stroke-width:2px</c> 这样的属性串解析成样式。
        /// </summary>
        /// <remarks>
        /// 认不出的属性记进报告而不是丢掉：丢样式在图上几乎看不出来。
        /// 一个属性都对不上时返回空，免得给节点挂一个全是空字段的样式——
        /// 那会被算进视觉哈希，于是"没有样式"与"空样式"变成两回事。
        /// </remarks>
        private NodeStyle? ParseStyle(string target, string properties)
        {
            string? fill = null;
            string? stroke = null;
            string? text = null;
            double? weight = null;

            foreach (var pair in properties.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var colon = pair.IndexOf(':', StringComparison.Ordinal);

                if (colon <= 0)
                {
                    _ignored.Add(new IgnoredStyleProperty(target, pair, "不是一个 属性:值 的写法。"));
                    continue;
                }

                var key = pair[..colon].Trim();
                var value = pair[(colon + 1)..].Trim();

                switch (key)
                {
                    case "fill":
                        fill = value;
                        break;

                    case "stroke":
                        stroke = value;
                        break;

                    case "color":
                        text = value;
                        break;

                    case "stroke-width":
                        if (TryPixels(value, out var parsed))
                        {
                            weight = parsed;
                        }
                        else
                        {
                            _ignored.Add(new IgnoredStyleProperty(target, pair, "描边粗细要是一个数字。"));
                        }

                        break;

                    default:
                        _ignored.Add(new IgnoredStyleProperty(target, pair, $"IR 的节点样式里没有 {key} 这个字段。"));
                        break;
                }
            }

            return fill is null && stroke is null && text is null && weight is null
                ? null
                : new NodeStyle { Fill = fill, Stroke = stroke, Text = text, Weight = weight };
        }

        /// <summary>读一个长度值。<c>2</c> 与 <c>2px</c> 都认。</summary>
        private static bool TryPixels(string value, out double result)
        {
            var text = value.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? value[..^2] : value;

            return double.TryParse(text.Trim(), System.Globalization.CultureInfo.InvariantCulture, out result);
        }

        #endregion

        #region 标识生成

        /// <summary>
        /// 给未命名的边找一个标识：<c>e1</c>、<c>e2</c>……取第一个空位。
        /// </summary>
        /// <remarks>
        /// Mermaid 的连线没有标识（它的 <c>linkStyle</c> 按序号引用连线，正说明这一点），
        /// 而 IR 的边必须有标识才能被命令层引用、才能进 sidecar。
        /// </remarks>
        private string NextEdgeId()
        {
            var counter = 1;

            while (_taken.Contains($"e{counter}"))
            {
                counter++;
            }

            return $"e{counter}";
        }

        #endregion
    }
}
