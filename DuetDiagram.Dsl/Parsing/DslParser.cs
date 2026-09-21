using System.Globalization;
using DuetDiagram.Core.Model;
using DuetDiagram.Dsl.Lexing;

namespace DuetDiagram.Dsl.Parsing;

/// <summary>
/// DSL 的语法分析。
/// </summary>
/// <remarks>
/// <para>
/// **认不出来的内容记成诊断，不抛异常。** 与 Mermaid 侧同一口径：
/// 两份解析器对坏输入的处理必须一致，否则对比测试量到的是口径差异而不是格式差异。
/// </para>
/// <para>
/// 语句以行为单位。缩进只为了可读，边界由 <c>end</c> 决定——
/// 依赖缩进的话，一段缩进错位的文本会解析出一棵静默错误的树。
/// </para>
/// </remarks>
public static class DslParser
{
    /// <summary>解析一份 DSL 源码。</summary>
    /// <remarks>
    /// 内部会先剥代码围栏。传入已剥过的内容也没问题——剥围栏对没有围栏的文本是恒等操作。
    /// </remarks>
    public static DslDocument Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var text = DslSource.Extract(source);
        var cursor = new Cursor(DslLexer.Tokenize(text));
        var builder = new Builder();

        while (!cursor.AtEnd)
        {
            cursor.SkipBlank();

            if (cursor.AtEnd)
            {
                break;
            }

            builder.Statement(cursor);
        }

        return builder.Build();
    }

    /// <summary>记号游标。</summary>
    private sealed class Cursor(IReadOnlyList<DslToken> tokens)
    {
        private int _index;

        public bool AtEnd => tokens[_index].Kind == DslTokenKind.End;

        public DslToken Peek() => tokens[_index];

        public DslToken PeekAt(int offset) =>
            _index + offset < tokens.Count ? tokens[_index + offset] : tokens[^1];

        public DslToken Next() => tokens[_index++];

        public bool AtLineEnd => Peek().Kind is DslTokenKind.NewLine or DslTokenKind.End;

        public void SkipBlank()
        {
            while (Peek().Kind is DslTokenKind.NewLine or DslTokenKind.Comment)
            {
                Next();
            }
        }

        public void SkipToNextLine()
        {
            while (!AtLineEnd)
            {
                Next();
            }
        }

        /// <summary>跳过行尾注释。</summary>
        /// <remarks>
        /// 注释吃掉的是一整行剩下的部分，所以「注释 + 换行」与「换行」是同一件事。
        /// 不跳的话，任何一行行尾带注释的声明都会被当成"有多余内容"。
        /// </remarks>
        public void SkipTrailingComment()
        {
            while (Peek().Kind == DslTokenKind.Comment)
            {
                Next();
            }
        }
    }

    /// <summary>逐条语句地装配结果。</summary>
    private sealed class Builder
    {
        private readonly List<DslDiagnostic> _diagnostics = [];
        private readonly List<DslNodeDeclaration> _nodes = [];
        private readonly List<DslEdgeDeclaration> _edges = [];
        private readonly List<DslGroupDeclaration> _groups = [];
        private readonly List<DslLayoutIntent> _layout = [];

        /// <summary>当前所在的分组。支持嵌套，用栈。</summary>
        private readonly Stack<string> _openGroups = new();

        private int _version = DslVersion.Current;
        private DiagramKind _kind = DiagramKind.Flowchart;
        private Direction _direction = Direction.TB;
        private double? _nodeSpacing;
        private double? _layerSpacing;

        public DslDocument Build() => new()
        {
            Version = _version,
            Kind = _kind,
            Direction = _direction,

            // 成员是从子项的外层字段推出来的，不是一边解析一边登记的第二份记录。
            // 两份记录各记各的必然分叉——同一个教训在 Mermaid 侧已经吃过一次，
            // 六份真实文件里节点父级与成员表对不上。
            // 成员里既有节点也有嵌套分组，与 IR 的约定一致。
            Groups =
            [
                .. _groups.Select(group => group with
                {
                    Members =
                    [
                        .. _nodes
                            .Where(node => string.Equals(node.Parent, group.Id, StringComparison.Ordinal))
                            .Select(node => node.Id),
                        .. _groups
                            .Where(inner => string.Equals(inner.Parent, group.Id, StringComparison.Ordinal))
                            .Select(inner => inner.Id),
                    ],
                }),
            ],

            Nodes = _nodes,
            Edges = _edges,
            Layout = _layout,
            NodeSpacing = _nodeSpacing,
            LayerSpacing = _layerSpacing,
            Diagnostics = _diagnostics,
        };

        public void Statement(Cursor cursor)
        {
            var first = cursor.Peek();

            if (first.Kind == DslTokenKind.Comment)
            {
                cursor.SkipToNextLine();
                return;
            }

            if (first.Kind != DslTokenKind.Word)
            {
                Diagnose($"认不出这一行开头的内容：{first.Text}", first);
                cursor.SkipToNextLine();
                return;
            }

            // 关键字在行首是保留的，因此不能拿它们当节点标识。
            // 这是行式语法省不掉的代价：没有别的信号能区分
            // 「一个叫 order 的节点」与「一条 order 语句」。
            //
            // 唯一的例外是 end：它只在有分组打开时才是关键字。
            // 见下面那个分支的说明——真实语料里 end 是终点的常用名。
            switch (first.Text)
            {
                case "dsl":
                    Version(cursor);
                    return;
                case "kind":
                    KindStatement(cursor);
                    return;
                case "direction":
                    DirectionStatement(cursor);
                    return;
                case "group":
                    Group(cursor, DslGroupKind.Group);
                    return;
                case "lane":
                    Group(cursor, DslGroupKind.Lane);
                    return;
                case "subflow":
                    Group(cursor, DslGroupKind.Subflow);
                    return;
                case "end":
                    // end 只在「有分组打开」且「这一行只有 end」时才是关键字。
                    //
                    // 两个条件都是被真实语料逼出来的。S07 里 end 是终点的名字
                    // （`end "结束" shape=stadium`，后面还有三条边连到它）：
                    // 无条件当关键字的话，那一行被吞掉、节点没声明，
                    // 引用它的边与 order 全部指向不存在的节点——整份图错位，
                    // 而诊断只有一条"没有对应分组的 end"，指向的原因完全不对。
                    //
                    // 反过来，带内容的 end 即使在分组里也当标识：把它当收尾
                    // 会静默丢掉后面那些内容，而静默丢弃是最难查的一类问题。
                    if (cursor.PeekAt(1).Kind is not (DslTokenKind.NewLine or DslTokenKind.End))
                    {
                        NodeOrEdge(cursor);
                        return;
                    }

                    cursor.Next();

                    // 到这儿 end 是光杆一个。没有分组可收尾就是写错了，
                    // 报出来而不是当成一个没有显示文本、名叫 end 的节点——
                    // 那种节点在图上是个说不清来历的方块。
                    if (_openGroups.Count == 0)
                    {
                        Diagnose("出现了没有对应分组的 end。", first);
                        return;
                    }

                    _openGroups.Pop();
                    return;
                case "same-rank":
                    SameRank(cursor);
                    return;
                case "order":
                    Order(cursor);
                    return;
                case "align":
                    Align(cursor);
                    return;
                case "place":
                    Place(cursor);
                    return;
                case "pin":
                    Pin(cursor);
                    return;
                case "node-spacing":
                    Spacing(cursor, isNodeSpacing: true);
                    return;
                case "layer-spacing":
                    Spacing(cursor, isNodeSpacing: false);
                    return;
                default:
                    NodeOrEdge(cursor);
                    return;
            }
        }

        // ---- 头部声明 ----

        private void Version(Cursor cursor)
        {
            var keyword = cursor.Next();
            var value = cursor.Peek();

            if (value.Kind != DslTokenKind.Word || !int.TryParse(value.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
            {
                Diagnose("dsl 语句后面要跟一个版本号。", keyword);
                cursor.SkipToNextLine();
                return;
            }

            cursor.Next();
            _version = version;

            // 版本不认识时记一条诊断但**不报错**：旧文件在新版本里仍要能打开，
            // 给出可用的结果比整份拒绝有用。这与"认不出的内容记诊断"是同一条。
            if (version != DslVersion.Current)
            {
                Diagnose(
                    $"版本声明是 {version}，当前解析器认识的是 {DslVersion.Current}，按当前版本解析。",
                    value);
            }

            Justify(cursor);
        }

        private void KindStatement(Cursor cursor)
        {
            var keyword = cursor.Next();
            var value = cursor.Peek();

            if (value.Kind != DslTokenKind.Word)
            {
                Diagnose("kind 语句后面要跟一个图类型。", keyword);
                cursor.SkipToNextLine();
                return;
            }

            cursor.Next();

            _kind = value.Text switch
            {
                "flow" => DiagramKind.Flow,
                "flowchart" => DiagramKind.Flowchart,
                "block" => DiagramKind.Block,
                "state" => DiagramKind.State,
                _ => UnknownKind(value),
            };

            Justify(cursor);
        }

        private DiagramKind UnknownKind(DslToken token)
        {
            Diagnose($"认不出图类型 {token.Text}，按 flowchart 处理。", token);
            return DiagramKind.Flowchart;
        }

        private void DirectionStatement(Cursor cursor)
        {
            var keyword = cursor.Next();
            var value = cursor.Peek();

            if (value.Kind != DslTokenKind.Word || !TryParseDirection(value.Text, out var direction))
            {
                Diagnose("direction 语句缺少可识别的方向。", keyword);
                cursor.SkipToNextLine();
                return;
            }

            cursor.Next();
            _direction = direction;
            Justify(cursor);
        }

        /// <summary>把方向词翻成 IR 的方向。</summary>
        /// <remarks>
        /// <c>TD</c> 与 <c>TB</c> 同义，而 IR 只保留 <c>TB</c>。
        /// 与 Mermaid 侧同一处理：两种都认，只往外给一个。
        /// </remarks>
        private static bool TryParseDirection(string text, out Direction direction)
        {
            switch (text)
            {
                case "TD" or "TB":
                    direction = Direction.TB;
                    return true;
                case "BT":
                    direction = Direction.BT;
                    return true;
                case "LR":
                    direction = Direction.LR;
                    return true;
                case "RL":
                    direction = Direction.RL;
                    return true;
                default:
                    direction = Direction.TB;
                    return false;
            }
        }

        // ---- 分组 ----

        private void Group(Cursor cursor, DslGroupKind kind)
        {
            var keyword = cursor.Next();
            var id = string.Empty;
            var label = string.Empty;

            if (cursor.Peek().Kind == DslTokenKind.Word)
            {
                id = cursor.Next().Text;
                label = id;
            }

            if (cursor.Peek().Kind == DslTokenKind.QuotedText)
            {
                label = cursor.Next().Text;
            }

            if (id.Length == 0)
            {
                id = $"group{_groups.Count + 1}";
                Diagnose($"分组没有标识，按位置取名为 {id}。", keyword);
            }

            Justify(cursor);

            // 外层要在压栈之前取，压完栈取到的就是自己。
            var parent = _openGroups.Count > 0 ? _openGroups.Peek() : null;

            _groups.Add(new DslGroupDeclaration(kind, id, label, [], parent));
            _openGroups.Push(id);
        }

        // ---- 布局意图 ----

        private void SameRank(Cursor cursor)
        {
            var keyword = cursor.Next();
            var nodes = IdentifierList(cursor);

            if (nodes.Count < 2)
            {
                Diagnose("same-rank 至少需要两个节点。", keyword);
            }
            else
            {
                _layout.Add(new DslLayoutIntent(DslLayoutIntentKind.SameRank, nodes));
            }

            Justify(cursor);
        }

        private void Order(Cursor cursor)
        {
            var keyword = cursor.Next();

            if (cursor.Peek().Kind != DslTokenKind.Word)
            {
                Diagnose("order 语句需要「节点: 出边次序」。", keyword);
                cursor.SkipToNextLine();
                return;
            }

            var subject = cursor.Next().Text;

            if (cursor.Peek().Kind != DslTokenKind.Colon)
            {
                Diagnose("order 语句的节点后面要跟冒号。", cursor.Peek());
                cursor.SkipToNextLine();
                return;
            }

            cursor.Next();
            var order = IdentifierList(cursor);

            if (order.Count == 0)
            {
                Diagnose("order 语句没有给出次序。", keyword);
            }
            else
            {
                _layout.Add(new DslLayoutIntent(DslLayoutIntentKind.Order, order, subject));
            }

            Justify(cursor);
        }

        private void Align(Cursor cursor)
        {
            var keyword = cursor.Next();
            var nodes = IdentifierList(cursor);

            if (nodes.Count < 2)
            {
                Diagnose("align 至少需要两个节点。", keyword);
            }
            else
            {
                _layout.Add(new DslLayoutIntent(DslLayoutIntentKind.Align, nodes));
            }

            Justify(cursor);
        }

        private void Place(Cursor cursor)
        {
            var keyword = cursor.Next();

            if (cursor.Peek().Kind != DslTokenKind.Word)
            {
                Diagnose("place 语句需要「节点 关系 参照节点」。", keyword);
                cursor.SkipToNextLine();
                return;
            }

            var subject = cursor.Next().Text;
            var relationToken = cursor.Peek();

            if (relationToken.Kind != DslTokenKind.Word || !TryParseRelation(relationToken.Text, out var relation))
            {
                Diagnose("place 语句缺少可识别的位置关系。", keyword);
                cursor.SkipToNextLine();
                return;
            }

            cursor.Next();

            if (cursor.Peek().Kind != DslTokenKind.Word)
            {
                Diagnose("place 语句缺少参照节点。", keyword);
                cursor.SkipToNextLine();
                return;
            }

            var reference = cursor.Next().Text;
            _layout.Add(new DslLayoutIntent(DslLayoutIntentKind.Place, [reference], subject, relation));
            Justify(cursor);
        }

        private void Pin(Cursor cursor)
        {
            var keyword = cursor.Next();

            if (cursor.Peek().Kind != DslTokenKind.Word)
            {
                Diagnose("pin 语句需要「节点 at 横坐标, 纵坐标」。", keyword);
                cursor.SkipToNextLine();
                return;
            }

            var subject = cursor.Next().Text;

            if (cursor.Peek().Kind != DslTokenKind.Word || cursor.Peek().Text != "at")
            {
                Diagnose("pin 语句缺少 at。", cursor.Peek());
                cursor.SkipToNextLine();
                return;
            }

            cursor.Next();

            if (!TryNumber(cursor, out var x) || cursor.Peek().Kind != DslTokenKind.Comma)
            {
                Diagnose("pin 语句需要「横坐标, 纵坐标」。", keyword);
                cursor.SkipToNextLine();
                return;
            }

            cursor.Next();

            if (!TryNumber(cursor, out var y))
            {
                Diagnose("pin 语句缺少纵坐标。", keyword);
                cursor.SkipToNextLine();
                return;
            }

            _layout.Add(new DslLayoutIntent(DslLayoutIntentKind.Pin, [], subject, X: x, Y: y));
            Justify(cursor);
        }

        private void Spacing(Cursor cursor, bool isNodeSpacing)
        {
            var keyword = cursor.Next();

            if (!TryNumber(cursor, out var value) || value < 0)
            {
                Diagnose($"{keyword.Text} 后面要跟一个非负数。", keyword);
                cursor.SkipToNextLine();
                return;
            }

            if (isNodeSpacing)
            {
                _nodeSpacing = value;
            }
            else
            {
                _layerSpacing = value;
            }

            Justify(cursor);
        }

        private static bool TryNumber(Cursor cursor, out double value)
        {
            if (cursor.Peek().Kind == DslTokenKind.Word
                && double.TryParse(cursor.Peek().Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                cursor.Next();
                return true;
            }

            value = 0;
            return false;
        }

        private static bool TryParseRelation(string text, out PlaceRelation relation)
        {
            switch (text)
            {
                case "right-of":
                    relation = PlaceRelation.RightOf;
                    return true;
                case "left-of":
                    relation = PlaceRelation.LeftOf;
                    return true;
                case "above":
                    relation = PlaceRelation.Above;
                    return true;
                case "below":
                    relation = PlaceRelation.Below;
                    return true;
                default:
                    relation = PlaceRelation.RightOf;
                    return false;
            }
        }

        /// <summary>读一串用逗号分隔的标识。</summary>
        private static List<string> IdentifierList(Cursor cursor)
        {
            var result = new List<string>();

            while (cursor.Peek().Kind == DslTokenKind.Word)
            {
                result.Add(cursor.Next().Text);

                if (cursor.Peek().Kind != DslTokenKind.Comma)
                {
                    break;
                }

                cursor.Next();
            }

            return result;
        }

        // ---- 节点与边 ----

        /// <summary>
        /// 一条节点声明或一条边。
        /// </summary>
        /// <remarks>
        /// 两者共用同一种开头（一个标识），靠后面跟不跟连线区分：
        /// <c>a "文本"</c> 是声明，<c>a -> b</c> 是连线，
        /// <c>e1: a -> b</c> 是带标识的连线，<c>a.req -> b</c> 是起点带端口的连线。
        /// </remarks>
        private void NodeOrEdge(Cursor cursor)
        {
            var first = cursor.Next();

            if (cursor.Peek().Kind == DslTokenKind.Colon)
            {
                var colon = cursor.Next();

                if (cursor.Peek().Kind != DslTokenKind.Word)
                {
                    Diagnose("边标识后面要跟起点节点。", colon);
                    cursor.SkipToNextLine();
                    return;
                }

                Edge(cursor, first.Text, cursor.Next());
                return;
            }

            // 起点带端口：a.req -> b。
            //
            // 这一条必须靠前瞻来认，因为 `a.req` 与「节点 a 后面跟了个多余的 .req」
            // 在读到第二个记号之前长得一样。判据是再往后一个记号是不是连线：
            // 是连线就当边，否则当节点声明并由 Node 报出那个多余的端口。
            //
            // 少了这个分支的后果很隐蔽：整条边会被 Justify 静默跳过，
            // 图上少一条线而没有任何提示。
            if (cursor.Peek().Kind == DslTokenKind.Dot
                && cursor.PeekAt(1).Kind == DslTokenKind.Word
                && cursor.PeekAt(2).Kind == DslTokenKind.Arrow)
            {
                Edge(cursor, null, first);
                return;
            }

            if (cursor.Peek().Kind == DslTokenKind.Arrow)
            {
                Edge(cursor, null, first);
                return;
            }

            Node(cursor, first);
        }

        private void Node(Cursor cursor, DslToken id)
        {
            string? label = null;
            var shape = NodeShape.Rect;
            string? style = null;
            string? layer = null;
            string? description = null;
            var ports = new List<DslPortDeclaration>();

            if (cursor.Peek().Kind == DslTokenKind.QuotedText)
            {
                label = cursor.Next().Text;
            }

            while (cursor.Peek().Kind == DslTokenKind.Word && cursor.PeekAt(1).Kind == DslTokenKind.Equals)
            {
                var key = cursor.Next();
                cursor.Next();

                // ports 的值是一串记号，不能走下面"取一个记号当值"那条路——
                // 词法层把 : 与 , 切成了独立记号，那条路只会拿到第一个端口名。
                if (key.Text == "ports")
                {
                    ports.AddRange(ParsePorts(ReadPortList(cursor), key));
                    continue;
                }

                var valueToken = cursor.Peek();

                if (valueToken.Kind is not (DslTokenKind.Word or DslTokenKind.QuotedText))
                {
                    Diagnose($"属性 {key.Text} 后面要跟一个值。", key);
                    cursor.SkipToNextLine();
                    return;
                }

                cursor.Next();

                switch (key.Text)
                {
                    case "shape":
                        shape = ParseShape(valueToken);
                        break;
                    case "style":
                        style = valueToken.Text;
                        break;
                    case "layer":
                        layer = valueToken.Text;
                        break;
                    case "desc":
                        description = valueToken.Text;
                        break;
                    default:
                        Diagnose($"认不出属性 {key.Text}，已忽略。", key);
                        break;
                }
            }

            cursor.SkipTrailingComment();

            // 端口只属于边。节点声明里出现 .端口 是写错了，
            // 不报的话这个记号会被 Justify 静默跳过——用户看不出自己写错了。
            if (cursor.Peek().Kind == DslTokenKind.Dot)
            {
                Diagnose("节点声明里出现了 .端口，端口只用在边上。", cursor.Peek());
            }
            else if (!cursor.AtLineEnd && cursor.Peek().Kind != DslTokenKind.Unknown)
            {
                // 属性读完了却还有剩，说明这行写坏了（比如 `a b` 想写一条边却漏了连线）。
                // 不报的话剩下的内容被 Justify 静默吃掉，图上看不出少了什么。
                //
                // Unknown 排除在外，是因为它由 Justify 负责，而 Justify 能给出
                // 更具体的说法（比如"字符串没有收尾引号"）。两边都报会出两条诊断。
                Diagnose($"节点声明里出现了多余的内容：{cursor.Peek().Text}", cursor.Peek());
            }

            Justify(cursor);

            _nodes.Add(new DslNodeDeclaration(
                id.Text,
                label,
                shape,
                style,
                layer,
                description,
                ports,
                _openGroups.Count > 0 ? _openGroups.Peek() : null));
        }

        private NodeShape ParseShape(DslToken token) => token.Text switch
        {
            "rect" => NodeShape.Rect,
            "rounded" => NodeShape.Rounded,
            "stadium" => NodeShape.Stadium,
            "diamond" => NodeShape.Diamond,
            "circle" => NodeShape.Circle,
            "hexagon" => NodeShape.Hexagon,
            "parallelogram" => NodeShape.Parallelogram,
            "cylinder" => NodeShape.Cylinder,
            _ => UnknownShape(token),
        };

        private NodeShape UnknownShape(DslToken token)
        {
            Diagnose($"认不出形状 {token.Text}，按矩形处理。", token);
            return NodeShape.Rect;
        }

        /// <summary>
        /// 读一串端口声明，直到行尾或下一个属性开始。
        /// </summary>
        /// <remarks>
        /// 停止条件是「下一个属性开始了」而不是「遇到逗号」：逗号是端口之间的分隔符，
        /// 遇到它就停会只拿到第一个端口。
        /// 端口名与方位都不含空格，所以把记号原文直接接起来是安全的。
        /// </remarks>
        private static string ReadPortList(Cursor cursor)
        {
            var text = new System.Text.StringBuilder();

            while (!cursor.AtLineEnd)
            {
                if (cursor.Peek().Kind == DslTokenKind.Word && cursor.PeekAt(1).Kind == DslTokenKind.Equals)
                {
                    break;
                }

                var token = cursor.Next();

                // 整串加引号的写法也认：ports="req:left,resp:right"。
                if (token.Kind == DslTokenKind.QuotedText)
                {
                    return token.Text;
                }

                text.Append(token.Text);
            }

            return text.ToString();
        }

        /// <summary>
        /// 解析端口列表 <c>req:left,resp:right</c>。
        /// </summary>
        /// <remarks>
        /// 属性值不加引号，也不含空格——这是属性值的统一约定。
        /// 需要空格的只有 <c>desc</c>，它走引号形式。
        /// </remarks>
        private List<DslPortDeclaration> ParsePorts(string text, DslToken at)
        {
            var ports = new List<DslPortDeclaration>();

            foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = part.IndexOf(':', StringComparison.Ordinal);

                if (separator <= 0 || separator == part.Length - 1)
                {
                    Diagnose($"端口声明 {part} 的格式应当是「名字:方位」。", at);
                    continue;
                }

                var name = part[..separator];
                var side = part[(separator + 1)..];

                if (!TryParseSide(side, out var parsed))
                {
                    Diagnose($"认不出端口方位 {side}。", at);
                    continue;
                }

                ports.Add(new DslPortDeclaration(name, parsed));
            }

            return ports;
        }

        private static bool TryParseSide(string text, out PortSide side)
        {
            switch (text)
            {
                case "left":
                    side = PortSide.Left;
                    return true;
                case "right":
                    side = PortSide.Right;
                    return true;
                case "top":
                    side = PortSide.Top;
                    return true;
                case "bottom":
                    side = PortSide.Bottom;
                    return true;
                default:
                    side = PortSide.Right;
                    return false;
            }
        }

        private void Edge(Cursor cursor, string? id, DslToken fromToken)
        {
            var fromPort = OptionalPort(cursor);
            var arrowToken = cursor.Peek();

            // 箭头的校验放在这里而不是调用点：起点有端口与没端口两条路
            // 都会走到这儿，放在调用点就得写两遍，而漏写一遍的表现是
            // 把 `e1: a b` 里的 b 当成箭头吃掉，报出一句对不上原因的错。
            if (arrowToken.Kind != DslTokenKind.Arrow)
            {
                Diagnose("连线缺少 -> 或 --。", arrowToken);
                cursor.SkipToNextLine();
                return;
            }

            cursor.Next();
            var toToken = cursor.Peek();

            if (toToken.Kind != DslTokenKind.Word)
            {
                Diagnose("连线后面没有可识别的终点。", arrowToken);
                cursor.SkipToNextLine();
                return;
            }

            cursor.Next();
            var toPort = OptionalPort(cursor);

            string? label = null;

            if (cursor.Peek().Kind == DslTokenKind.QuotedText)
            {
                label = cursor.Next().Text;
            }

            var arrow = arrowToken.HasArrow ? ArrowStyle.Arrow : ArrowStyle.None;
            var line = LineStyle.Solid;
            string? style = null;

            while (cursor.Peek().Kind == DslTokenKind.Word && cursor.PeekAt(1).Kind == DslTokenKind.Equals)
            {
                var key = cursor.Next();
                cursor.Next();

                var valueToken = cursor.Peek();

                if (valueToken.Kind is not (DslTokenKind.Word or DslTokenKind.QuotedText))
                {
                    Diagnose($"属性 {key.Text} 后面要跟一个值。", key);
                    cursor.SkipToNextLine();
                    return;
                }

                cursor.Next();

                switch (key.Text)
                {
                    case "line":
                        line = ParseLine(valueToken, line);
                        break;
                    case "arrow":
                        arrow = ParseArrow(valueToken, arrow);
                        break;
                    case "style":
                        style = valueToken.Text;
                        break;
                    default:
                        Diagnose($"认不出属性 {key.Text}，已忽略。", key);
                        break;
                }
            }

            cursor.SkipTrailingComment();

            // 与节点声明同一条理由：读完属性还有剩就是写坏了，
            // 不报的话剩下的内容被 Justify 静默吃掉。
            if (!cursor.AtLineEnd && cursor.Peek().Kind != DslTokenKind.Unknown)
            {
                Diagnose($"边声明里出现了多余的内容：{cursor.Peek().Text}", cursor.Peek());
            }

            Justify(cursor);

            _edges.Add(new DslEdgeDeclaration(
                id,
                fromToken.Text,
                fromPort,
                toToken.Text,
                toPort,
                label,
                arrow,
                line,
                style));
        }

        private LineStyle ParseLine(DslToken token, LineStyle fallback) => token.Text switch
        {
            "solid" => LineStyle.Solid,
            "dashed" => LineStyle.Dashed,
            "dotted" => LineStyle.Dotted,
            _ => UnknownLine(token, fallback),
        };

        private LineStyle UnknownLine(DslToken token, LineStyle fallback)
        {
            Diagnose($"认不出线型 {token.Text}，按 solid 处理。", token);
            return fallback;
        }

        private ArrowStyle ParseArrow(DslToken token, ArrowStyle fallback) => token.Text switch
        {
            "none" => ArrowStyle.None,
            "arrow" => ArrowStyle.Arrow,
            "open" => ArrowStyle.OpenArrow,
            "circle" => ArrowStyle.Circle,
            "cross" => ArrowStyle.Cross,
            _ => UnknownArrow(token, fallback),
        };

        private ArrowStyle UnknownArrow(DslToken token, ArrowStyle fallback)
        {
            Diagnose($"认不出箭头样式 {token.Text}，按 arrow 处理。", token);
            return fallback;
        }

        /// <summary>读一个可选的 <c>.端口</c>。</summary>
        private static string? OptionalPort(Cursor cursor)
        {
            if (cursor.Peek().Kind != DslTokenKind.Dot)
            {
                return null;
            }

            cursor.Next();

            return cursor.Peek().Kind == DslTokenKind.Word ? cursor.Next().Text : null;
        }

        /// <summary>把这一行剩下的内容检查一遍，记下不认识的记号。</summary>
        private void Justify(Cursor cursor)
        {
            while (!cursor.AtLineEnd)
            {
                var token = cursor.Next();

                if (token.Kind == DslTokenKind.Unknown)
                {
                    // 未收尾的字符串也走这里。它产出的 Unknown 记号带着原文（含开引号），
                    // 所以能给出比"不认识的字符"更准的说法。
                    Diagnose(
                        token.Text.StartsWith('"')
                            ? "字符串没有收尾引号。"
                            : $"不认识的字符：{token.Text}",
                        token);
                }
            }
        }

        private void Diagnose(string message, DslToken at) =>
            _diagnostics.Add(new DslDiagnostic(message, at.Line, at.Column));
    }
}
