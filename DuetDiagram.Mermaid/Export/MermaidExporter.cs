using System.Globalization;
using System.Text;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Shapes;
using DuetDiagram.Mermaid.Lexing;

namespace DuetDiagram.Mermaid.Export;

/// <summary>
/// 把 IR 写成 Mermaid 文本。
/// </summary>
/// <remarks>
/// <para>
/// 输出是**确定性的**：同一份文档两次导出逐字节相同，换一台机器也一样。
/// 行尾一律用 <c>\n</c>，不跟平台走——跟着走的话同一份文档在两台机器上导出会得到
/// 不同的文件，而那会让"逐字节相同"这条判据在跨平台时失效。
/// </para>
/// <para>
/// **导出必然有损。** IR 的表达力严格强于 Mermaid，写不出来的东西进
/// <see cref="ExportReport"/>。判据是：报告为空不等于无损，只等于没有东西落进已知的丢失清单——
/// 那个清单是照着 IR 逐字段对出来的，新加字段时要跟着补。
/// </para>
/// </remarks>
public static class MermaidExporter
{
    /// <summary>
    /// 导出一份文档。
    /// </summary>
    /// <param name="document">文档。</param>
    /// <param name="options">调用参数。</param>
    public static ExportResult Export(DiagramDocument document, ExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        return new Session(document, options).Run();
    }

    private sealed class Session
    {
        private readonly DiagramDocument _document;
        private readonly ExportOptions _options;

        private readonly List<string> _lines = [];
        private readonly List<DroppedFeature> _dropped = [];

        /// <summary>被改写的标识：原来写不出来，换了一个。只有标识进这里。</summary>
        private readonly Dictionary<string, string> _renamed = new(StringComparer.Ordinal);

        /// <summary>九个集合共用一个命名空间，改出来的标识要在这个整体里查重。</summary>
        private readonly HashSet<string> _taken = new(StringComparer.Ordinal);

        /// <summary>已经写出去的节点与组合。末尾靠它把剩下的补上。</summary>
        private readonly HashSet<string> _written = new(StringComparer.Ordinal);

        public Session(DiagramDocument document, ExportOptions options)
        {
            _document = document;
            _options = options;

            foreach (var node in document.Nodes)
            {
                _taken.Add(node.Id);
            }

            foreach (var composite in document.Composites)
            {
                _taken.Add(composite.Id);
            }
        }

        public ExportResult Run()
        {
            RenameUnwritableIds();

            _lines.Add($"flowchart {_document.Direction}");

            EmitComposites();
            EmitLeftovers();
            EmitEdges();
            EmitStyles();
            AuditLosses();

            return new ExportResult(string.Join('\n', _lines) + "\n", new ExportReport(_dropped));
        }

        #region 标识

        /// <summary>
        /// 写不出来的标识换一个。
        /// </summary>
        /// <remarks>
        /// <para>
        /// Mermaid 的标识只能是标识字符，没有"带引号的标识"这种写法——引号是给显示文本用的。
        /// 所以标识里有空格或标点时，要么换一个，要么这个节点根本写不出来。
        /// </para>
        /// <para>
        /// 换名要贯穿到引用处（边的两端、组合成员、节点父级），漏一处就是断掉的图。
        /// 改名进报告：用户看到的是"导出的文件里标识怎么变了"，答案只有报告能回答。
        /// </para>
        /// </remarks>
        private void RenameUnwritableIds()
        {
            var nodes = _document.Nodes.Where(n => !IsWritable(n.Id)).Select(n => n.Id);
            var composites = _document.Composites.Where(c => !IsWritable(c.Id)).Select(c => c.Id);

            // 先节点后组合，与导入侧"节点优先"的口径一致。
            var offenders = nodes.Concat(composites).ToArray();

            foreach (var id in offenders)
            {
                var replacement = UniqueId(Sanitize(id));

                _renamed[id] = replacement;
                _dropped.Add(new DroppedFeature("标识", [id], $"原文里不是合法的标识，导出时改名为 {replacement}。"));
            }
        }

        private static bool IsWritable(string id) => MermaidLexer.IsPlainIdentifier(id);

        /// <summary>把不能用的字符换成下划线，并清掉藏在里面的连线记号。</summary>
        private static string Sanitize(string id)
        {
            var builder = new StringBuilder(id.Length);

            foreach (var c in id)
            {
                builder.Append(MermaidLexer.IsWordChar(c) ? c : '_');
            }

            if (builder.Length == 0)
            {
                return "id";
            }

            // 全是标识字符也可能藏着连线记号：`a--b` 读出来是 `a`、连线、`b` 三段。
            return IsWritable(builder.ToString()) ? builder.ToString() : builder.ToString().Replace('-', '_');
        }

        private string UniqueId(string stem)
        {
            var candidate = stem;
            var counter = 2;

            while (!_taken.Add(candidate))
            {
                candidate = $"{stem}_{counter++}";
            }

            return candidate;
        }

        /// <summary>标识改后的样子。没被改过的原样返回。</summary>
        private string Mapped(string id) => _renamed.GetValueOrDefault(id, id);

        #endregion

        #region 结构

        /// <summary>
        /// 从没有被任何组合收进去的那些开始，把整棵组合树写出来。
        /// </summary>
        /// <remarks>
        /// 顶层判据是"父级为空**或者父级指向一个不存在的组合**"。后一种在文档合法时不会出现，
        /// 而不这样判它就会一个都不写——写不出来的组合里的节点也跟着消失。
        /// 导出可以产出有问题的文本，但不能产出**少了东西**的文本：
        /// 前者一眼能看出来，后者看不出来。
        /// </remarks>
        private void EmitComposites()
        {
            var ids = _document.Composites.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            var roots = _document.Composites
                .Where(c => c.Parent is null || !ids.Contains(c.Parent))
                .ToArray();

            foreach (var root in roots)
            {
                Blank();
                EmitComposite(root, 0);
            }
        }

        private void EmitComposite(CompositeDef composite, int depth)
        {
            var id = Mapped(composite.Id);
            var label = WriteLabel(composite.Label);

            _written.Add(composite.Id);

            _lines.Add(Pad(depth) + (label.Length == 0 || label == id
                ? $"subgraph {id}"
                : $"subgraph {id}[\"{label}\"]"));

            if (composite.Direction is { } direction)
            {
                _lines.Add(Pad(depth + 1) + $"direction {direction}");
            }

            // 成员关系只从成员表读。节点的父级是冗余索引，从它反推会在两者不一致时
            // 悄悄按另一份事实走，而"按哪一份"没有依据。
            foreach (var member in composite.Members)
            {
                if (_document.Nodes.FirstOrDefault(n => string.Equals(n.Id, member, StringComparison.Ordinal)) is { } node)
                {
                    _written.Add(node.Id);
                    _lines.Add(Pad(depth + 1) + NodeDeclaration(node));
                }
            }

            foreach (var member in composite.Members)
            {
                if (_document.Composites.FirstOrDefault(c => string.Equals(c.Id, member, StringComparison.Ordinal)) is { } inner)
                {
                    // 已经写过的不再写第二遍，否则同一个组合会出两块。
                    if (_written.Add(inner.Id))
                    {
                        EmitComposite(inner, depth + 1);
                    }
                }
            }

            _lines.Add(Pad(depth) + "end");
        }

        /// <summary>
        /// 补齐还没写出去的节点与组合。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 判据是"写过了没有"，而不是"父级为不为空"或"在不在某张成员表里"。
        /// 后两种判据都依赖文档本身自洽——父级悬空、成员表与父级对不上、
        /// 组合的成员表里写着一个不存在的标识，任一情形都会让某个元素**既不进组合也不被补齐**。
        /// </para>
        /// <para>
        /// 这样漏掉的元素不会报错，只会从图里消失，而"少了一个节点"极难归因到导出这一层。
        /// 按"写过了没有"补齐之后，任何元素至少会出现在文本里一次；实在写不明白的
        /// 会以裸标识的形式出现，读回去是一个矩形节点——形状不对，但东西还在。
        /// </para>
        /// </remarks>
        private void EmitLeftovers()
        {
            var nodes = _document.Nodes.Where(n => !_written.Contains(n.Id)).ToArray();
            var composites = _document.Composites.Where(c => !_written.Contains(c.Id)).ToArray();

            if (nodes.Length == 0 && composites.Length == 0)
            {
                return;
            }

            Blank();

            foreach (var composite in composites)
            {
                // 前面按根遍历时没能走到它，说明它的父级链是断的。按顶层写出来。
                EmitComposite(composite, 0);
            }

            foreach (var node in nodes)
            {
                _written.Add(node.Id);
                _lines.Add(NodeDeclaration(node));
            }
        }

        private void EmitEdges()
        {
            if (_document.Edges.Count == 0)
            {
                return;
            }

            Blank();

            foreach (var edge in _document.Edges)
            {
                var token = ArrowToken(edge);
                var label = WriteLabel(edge.Label);

                // 标签一律加引号。裸标签里出现竖线就会把标签切开，而"什么时候会出问题"
                // 要去翻 Mermaid 的语法才说得清；统一加引号没有这个心智负担，读起来也清楚。
                var text = label.Length == 0 ? string.Empty : $"|\"{label}\"|";

                _lines.Add($"{Mapped(edge.From)}{token}{text}{Mapped(edge.To)}");
            }
        }

        private void EmitStyles()
        {
            var assignments = new List<string>();

            foreach (var node in _document.Nodes)
            {
                if (StyleOf(node.Style, Mapped(node.Id)) is { Length: > 0 } style)
                {
                    assignments.Add(style);
                }
            }

            foreach (var composite in _document.Composites)
            {
                if (StyleOf(composite.Style, Mapped(composite.Id)) is { Length: > 0 } style)
                {
                    assignments.Add(style);
                }
            }

            if (assignments.Count == 0)
            {
                return;
            }

            Blank();
            _lines.AddRange(assignments);
        }

        private string NodeDeclaration(NodeDef node)
        {
            var id = Mapped(node.Id);
            var label = WriteLabel(node.Label);

            // 标签与标识相同、形状又是矩形时省掉方括号。这不只是省字：
            // 真实语料里大半的节点是这么写的，省掉之后导出的文本更接近人写的样子。
            if (label == id && node.Shape == NodeShape.Rect)
            {
                return id;
            }

            var (open, close) = ShapeDelimiters(node.Shape, id);

            return $"{id}{open}\"{label}\"{close}";
        }

        #endregion

        #region 词汇表

        /// <summary>
        /// 形状定界符。写不出来的形状退回矩形，并记进报告。
        /// </summary>
        /// <remarks>
        /// 平行四边形的定界符在解析器的形状表里根本没有——那是它认得出来的形状的一个子集。
        /// 找不到对应写法时退回矩形而不是报错：退回之后图还是对的，只是形状变了，
        /// 而报错会让整份导出失败，那比形状不对严重得多。
        /// </remarks>
        private (string Open, string Close) ShapeDelimiters(NodeShape shape, string id)
        {
            (string Open, string Close)? delimiters = shape switch
            {
                NodeShape.Rect => ("[", "]"),
                NodeShape.Rounded => ("(", ")"),
                NodeShape.Stadium => ("([", "])"),
                NodeShape.Diamond => ("{", "}"),
                NodeShape.Circle => ("((", "))"),
                NodeShape.Hexagon => ("{{", "}}"),
                NodeShape.Cylinder => ("[(", ")]"),
                _ => null,
            };

            if (delimiters is { } found)
            {
                return found;
            }

            _dropped.Add(new DroppedFeature("节点形状", [id], $"{shape} 在 Mermaid 里没有对应写法，导出成矩形。"));

            return ("[", "]");
        }

        /// <summary>
        /// 连线的记号。写不出来的线型或箭头退回实线箭头，并记进报告。
        /// </summary>
        private string ArrowToken(EdgeDef edge)
        {
            var line = edge.Line;
            var arrow = edge.Arrow;

            if (line == LineStyle.Dashed)
            {
                _dropped.Add(new DroppedFeature("边线型", [edge.Id], "Mermaid 没有虚线，导出成实线。"));
                line = LineStyle.Solid;
            }

            if (arrow is not (ArrowStyle.Arrow or ArrowStyle.None))
            {
                _dropped.Add(new DroppedFeature("边箭头", [edge.Id], $"Mermaid 没有 {arrow} 这种箭头，导出成普通箭头。"));
                arrow = ArrowStyle.Arrow;
            }

            var token = (line, arrow) switch
            {
                (LineStyle.Dotted, ArrowStyle.Arrow) => "-.->",
                (LineStyle.Dotted, _) => "-.-",
                (_, ArrowStyle.Arrow) => "-->",
                _ => "---",
            };

            return token;
        }

        /// <summary>
        /// 一条 <c>style</c> 语句。没有可写的字段时返回空。
        /// </summary>
        /// <remarks>
        /// 只有四个字段写得出来：填充、描边、文字色、描边粗细。
        /// 其余（边框线型、圆角、不透明度、角标）Mermaid 的 <c>style</c> 认不出来，
        /// 记进报告——写一个它不认的键进去，那一条会被静默忽略，而图上"少了一处样式"
        /// 几乎看不出来。
        /// </remarks>
        private string? StyleOf(NodeStyle? style, string id)
        {
            if (style is null)
            {
                return null;
            }

            var properties = new List<string>();

            if (style.Fill is { Length: > 0 } fill)
            {
                properties.Add($"fill:{fill}");
            }

            if (style.Stroke is { Length: > 0 } stroke)
            {
                properties.Add($"stroke:{stroke}");
            }

            if (style.Text is { Length: > 0 } text)
            {
                properties.Add($"color:{text}");
            }

            if (style.Weight is { } weight)
            {
                properties.Add($"stroke-width:{weight.ToString("G", CultureInfo.InvariantCulture)}px");
            }

            var lost = new List<string>();

            if (style.Border is not null)
            {
                lost.Add("border");
            }

            if (style.Radius is not null)
            {
                lost.Add("radius");
            }

            if (style.Opacity is not null)
            {
                lost.Add("opacity");
            }

            if (style.Badge is not null)
            {
                lost.Add("badge");
            }

            if (lost.Count > 0)
            {
                _dropped.Add(new DroppedFeature("样式字段", [id], $"Mermaid 的 style 认不出 {string.Join('、', lost)}。"));
            }

            return properties.Count == 0 ? null : $"style {id} {string.Join(',', properties)}";
        }

        /// <summary>
        /// 把显示文本写成能安全放进双引号里的样子。
        /// </summary>
        /// <remarks>
        /// Mermaid 用实体码转义双引号（<c>#quot;</c>），没有反斜杠转义。
        /// 换行也写不出来：标签里的换行要写成 <c>&lt;br/&gt;</c>，而那是个标记符，
        /// 与文本里的字面量 <c>&lt;br/&gt;</c> 分不清——所以换行按丢失处理，换成空格。
        /// </remarks>
        private string WriteLabel(string label)
        {
            var escaped = label
                .Replace("\r\n", " ", StringComparison.Ordinal)
                .Replace('\n', ' ')
                .Replace('\r', ' ')
                .Replace("\"", "#quot;", StringComparison.Ordinal);

            return escaped;
        }

        private string Pad(int depth) => string.Concat(Enumerable.Repeat(_options.Indent, depth));

        private void Blank()
        {
            if (_lines.Count > 0 && _lines[^1].Length > 0)
            {
                _lines.Add(string.Empty);
            }
        }

        #endregion

        #region 丢失清单

        /// <summary>
        /// 照着 IR 逐字段对一遍，把写不出来的东西记下来。
        /// </summary>
        /// <remarks>
        /// 新加 IR 字段时要在这里跟着补一句。不补不会报错，只会从报告里悄悄消失——
        /// 而报告是用户判断"Mermaid 文件是不是全部内容"的唯一依据。
        /// </remarks>
        private void AuditLosses()
        {
            if (_document.Kind != DiagramKind.Flowchart)
            {
                Drop("图类型", $"Mermaid 只有流程图一种图类型，{_document.Kind} 导出成 flowchart。");
            }

            var constraints = _document.Layout.SameRank.Count
                + _document.Layout.Order.Count
                + _document.Layout.Align.Count
                + _document.Layout.Place.Count;

            if (constraints > 0)
            {
                Drop("布局约束", $"Mermaid 没有同层、层内次序、对齐与相对位置的语法，{constraints} 条约束丢失。");
            }

            if (_document.Layout.NodeSpacing != LayoutHintsDefaults.NodeSpacing
                || _document.Layout.LayerSpacing != LayoutHintsDefaults.LayerSpacing)
            {
                Drop("间距设置", "Mermaid 的间距只能靠初始化指令调，没有图内语法。");
            }

            if (_document.Palette.Entries.Count > 0)
            {
                Drop("调色板", $"Mermaid 没有调色板，颜色只能逐条写死，{_document.Palette.Entries.Count} 项调色板丢失。");
            }
            DropIds("页面", [.. _document.Pages.Select(p => p.Id)], "Mermaid 没有多页。");
            DropIds("图层", [.. _document.Layers.Select(l => l.Id)], "Mermaid 没有图层。");
            DropIds("标签", [.. _document.Tags.Select(t => t.Id)], "Mermaid 的 class 是样式分组，不是跨集合的标记。");
            DropIds("动作", [.. _document.Actions.Select(a => a.Id)], "Mermaid 的 click 只能挂链接与回调，本导出不写。");
            DropIds("字体", [.. _document.Fonts.Select(f => f.Id)], "Mermaid 的字体由主题统一决定。");
            DropIds("文本预设", [.. _document.TextPresets.Select(t => t.Id)], "Mermaid 没有具名文本预设。");

            DropIds("节点自定义形状", Ids(n => PathShape.IsCustom(n) ? n.Id : null, _document.Nodes), "Mermaid 没有自定义形状的语法，导出成该节点的内置形状。");
            DropIds("节点所属图层", Ids(n => n.Layer, _document.Nodes), "Mermaid 没有图层。");
            DropIds("节点富文本标记", Ids(n => n.RichText ? n.Id : null, _document.Nodes), "Mermaid 的方括号标签是纯文本，标记符会原样显示。");
            DropIds("节点数学排版", Ids(n => n.MathMode == MathMode.None ? null : n.Id, _document.Nodes), "Mermaid 的标签不做数学排版。");
            DropIds("节点文本样式", Ids(n => n.Text is null ? null : n.Id, _document.Nodes), "Mermaid 的字号与对齐由主题统一决定。");
            DropIds("节点说明", Ids(n => n.Desc, _document.Nodes), "Mermaid 没有不渲染的说明字段。");
            DropIds("节点端口", Ids(n => n.Ports.Count == 0 ? null : n.Id, _document.Nodes), "Mermaid 没有端口语法，连线只能落在节点的边中点附近。");
            DropIds("节点附加数据", Ids(n => n.Meta.Count == 0 ? null : n.Id, _document.Nodes), "Mermaid 没有宿主自定义数据的落点。");
            DropIds("节点样式令牌", Ids(n => n.StyleToken, _document.Nodes), "Mermaid 的颜色要写死，令牌没有对应写法。");

            DropIds("边端口", Ids(e => e.FromPort is not null || e.ToPort is not null ? e.Id : null, _document.Edges), "Mermaid 没有端口语法。");
            DropIds("边走线方式", Ids(e => e.Style.Route?.ToString(), _document.Edges), "Mermaid 的走线由布局决定。");
            DropIds("边标签位置", Ids(e => e.Style.LabelPosition?.ToString(), _document.Edges), "Mermaid 的标签固定在连线中点。");
            DropIds("边颜色与粗细", Ids(e => e.Style.Color is not null || e.Style.Weight is not null ? e.Id : null, _document.Edges), "Mermaid 的连线样式要按序号引用，本导出不写。");
            DropIds("边样式令牌", Ids(e => e.Style.StyleToken, _document.Edges), "Mermaid 的颜色要写死，令牌没有对应写法。");

            DropIds("组合折叠状态", Ids(c => c.Collapsed ? c.Id : null, _document.Composites), "Mermaid 的子图不能折叠。");
            DropIds("组合内部布局", Ids(c => c.LocalLayout is not null ? c.Id : null, _document.Composites), "Mermaid 的子图只有方向一种设置。");
        }

        private static IReadOnlyList<string> Ids<T>(Func<T, string?> pick, IEnumerable<T> items) =>
            [.. items.Select(pick).OfType<string>()];

        /// <summary>记一条文档级的丢失。没有具体元素涉及，但确实丢了。</summary>
        private void Drop(string feature, string reason) =>
            _dropped.Add(new DroppedFeature(feature, [], reason));

        /// <summary>记一条元素级的丢失。一个元素都没涉及就什么也不记。</summary>
        private void DropIds(string feature, IReadOnlyList<string> ids, string reason)
        {
            if (ids.Count > 0)
            {
                _dropped.Add(new DroppedFeature(feature, ids, reason));
            }
        }

        #endregion
    }
}
