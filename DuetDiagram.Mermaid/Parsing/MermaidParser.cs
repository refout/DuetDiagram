using DuetDiagram.Core.Model;
using DuetDiagram.Mermaid.Lexing;

namespace DuetDiagram.Mermaid.Parsing;

/// <summary>
/// Mermaid 流程图的语法分析。
/// </summary>
/// <remarks>
/// <para>
/// **认不出来的内容记成诊断，不抛异常。** 一份输入里某几条语句写坏了，
/// 其余部分往往是好的。把整份判死会让用户手里一份九成正确的图变成零，
/// 而他要的只是把能认的那部分导进来。导入层会在这条路上继续走：
/// 认不出的留诊断，能认的照常映射成 IR。
/// </para>
/// <para>
/// 只在一种情况下整份放弃：**图类型不是流程图**。那时候连语法都不一样，
/// 硬解析出来的是垃圾，而垃圾比空结果更糟——它看起来像是导入成功了。
/// </para>
/// <para>
/// 语句以行为单位。虽然 Mermaid 允许用分号分隔，但真实输出里几乎不用，
/// 而按行处理让"哪一行坏了"这个问题有唯一答案。
/// </para>
/// </remarks>
public static class MermaidParser
{
    /// <summary>解析一份 Mermaid 源码。</summary>
    /// <remarks>
    /// 内部会先剥代码围栏。传入已剥过的内容也没问题——剥围栏对没有围栏的文本是恒等操作。
    /// </remarks>
    public static MermaidFlowchart Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var text = MermaidSource.Extract(source);
        var kind = MermaidDiagramKindDetector.Detect(text);

        if (kind != MermaidDiagramKind.Flowchart)
        {
            return Unsupported(kind);
        }

        var cursor = new Cursor(MermaidLexer.Tokenize(text));

        // 图头：flowchart / graph，后面可以跟方向。
        cursor.Next();

        var direction = Direction.TB;

        if (cursor.Peek().Kind == MermaidTokenKind.Word && TryParseDirection(cursor.Peek().Text, out var declared))
        {
            direction = declared;
            cursor.Next();
        }

        cursor.SkipToNextLine();

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

        return builder.Build(direction);
    }

    /// <summary>
    /// 图类型不是流程图时的结果。
    /// </summary>
    /// <remarks>
    /// 诊断里点名是哪种图，以及它属于"IR 表达不了"还是"还没做"。
    /// 这两者对调用方的含义不同：前者得换个工具，后者等一等就好。
    /// </remarks>
    private static MermaidFlowchart Unsupported(MermaidDiagramKind kind) => new()
    {
        Diagnostics =
        [
            new MermaidDiagnostic(
                kind switch
                {
                    MermaidDiagramKind.Unknown => "认不出这是什么图类型。",
                    _ => MermaidDiagramKindDetector.IsRepresentable(kind)
                        ? $"暂不支持解析 {kind}，IR 里有对应的图类型，只是语法还没做。"
                        : $"{kind} 无法用当前的数据模型表达。",
                },
                1,
                1),
        ],
    };

    /// <summary>把 Mermaid 的方向词翻成 IR 的方向。</summary>
    /// <remarks>
    /// Mermaid 里 <c>TD</c> 与 <c>TB</c> 是同义的，而 IR 只保留 <c>TB</c>。
    /// 两者都认，但只往外给一个——保留两种写法会让"同一份图两次解析出不同的结果"。
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

    /// <summary>
    /// 记号游标。
    /// </summary>
    /// <remarks>
    /// 带保存与回退：<c>A -- 标签 --&gt; B</c> 这种写法要先把标签收下才知道它是什么，
    /// 收错了得退回去重来。
    /// </remarks>
    private sealed class Cursor(IReadOnlyList<MermaidToken> tokens)
    {
        private int _index;

        public bool AtEnd => tokens[_index].Kind == MermaidTokenKind.End;

        public MermaidToken Peek() => tokens[_index];

        public MermaidToken PeekAt(int offset) =>
            _index + offset < tokens.Count ? tokens[_index + offset] : tokens[^1];

        public MermaidToken Next() => tokens[_index++];

        public int Position
        {
            get => _index;
            set => _index = value;
        }

        /// <summary>本行是不是已经到头。</summary>
        public bool AtLineEnd => Peek().Kind is MermaidTokenKind.NewLine or MermaidTokenKind.End;

        /// <summary>跳过连续的换行与注释。</summary>
        public void SkipBlank()
        {
            while (Peek().Kind is MermaidTokenKind.NewLine or MermaidTokenKind.Comment)
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
    }

    /// <summary>逐条语句地装配结果。</summary>
    private sealed class Builder
    {
        private readonly List<MermaidDiagnostic> _diagnostics = [];

        /// <summary>节点按首次出现顺序。后来的声明只在带标签或形状时覆盖前面的。</summary>
        private readonly List<MermaidNodeDeclaration> _nodes = [];

        private readonly List<MermaidLinkDeclaration> _links = [];
        private readonly List<MermaidSubgraphDeclaration> _subgraphs = [];
        private readonly List<MermaidStyleDeclaration> _styles = [];
        private readonly List<MermaidClassDeclaration> _classes = [];

        /// <summary>当前所在的子图。支持嵌套，用栈。</summary>
        private readonly Stack<string> _openSubgraphs = new();

        public MermaidFlowchart Build(Direction direction) => new()
        {
            Direction = direction,
            Nodes = _nodes,

            // 子图成员是**从节点父级推出来的**，不是一边解析一边登记的第二份记录。
            // 两份记录各记各的必然分叉：节点在子图外先被声明、之后在子图里被引用时，
            // 父级停在"无"而成员表却把它算进子图。真实语料里这种写法很常见
            // （先画连线、再用 subgraph 划分层次），六份文件都踩到了。
            // 只留一份事实源，分叉就不可能发生。
            Subgraphs =
            [
                .. _subgraphs.Select(subgraph => subgraph with
                {
                    Members =
                    [
                        .. _nodes
                            .Where(node => string.Equals(node.Parent, subgraph.Id, StringComparison.Ordinal))
                            .Select(node => node.Id),
                    ],
                }),
            ],

            Links = _links,
            Styles = _styles,
            Classes = _classes,
            Diagnostics = _diagnostics,
        };

        public void Statement(Cursor cursor)
        {
            var first = cursor.Peek();

            if (first.Kind == MermaidTokenKind.Comment)
            {
                cursor.SkipToNextLine();
                return;
            }

            if (first.Kind != MermaidTokenKind.Word)
            {
                Diagnose($"认不出这一行开头的内容：{first.Text}", first);
                cursor.SkipToNextLine();
                return;
            }

            switch (first.Text)
            {
                case "subgraph":
                    Subgraph(cursor);
                    return;
                case "end":
                    cursor.Next();
                    EndSubgraph(first);
                    Justify(cursor);
                    return;
                case "direction":
                    InnerDirection(cursor);
                    return;
                case "style":
                    Style(cursor);
                    return;
                case "classDef":
                    ClassDef(cursor);
                    return;
                case "class":
                    ClassAssignment(cursor);
                    return;
                case "linkStyle" or "click":
                    // 这两条影响交互或单条连线的外观，IR 里有对应位置但尚未接线。
                    // 记一条诊断而不是静默丢弃：静默丢弃会让人以为它们生效了。
                    Diagnose($"暂不支持 {first.Text} 指令，已跳过。", first);
                    cursor.SkipToNextLine();
                    return;
                default:
                    NodeOrLink(cursor);
                    return;
            }
        }

        private void Subgraph(Cursor cursor)
        {
            var keyword = cursor.Next();
            var id = string.Empty;
            var label = string.Empty;

            if (cursor.Peek().Kind == MermaidTokenKind.Word)
            {
                id = cursor.Next().Text;
                label = id;
            }

            // subgraph id["显示文本"] 这种写法自带引号，剥掉交给显示层。
            if (cursor.Peek().Kind == MermaidTokenKind.ShapeOpen)
            {
                cursor.Next();

                if (cursor.Peek().Kind == MermaidTokenKind.Text)
                {
                    label = Unquote(cursor.Next().Text);
                }

                if (cursor.Peek().IsShapeClose)
                {
                    cursor.Next();
                }
            }

            if (string.IsNullOrEmpty(id))
            {
                id = $"subgraph{_subgraphs.Count + 1}";
                Diagnose($"子图没有标识，按位置取名为 {id}。", keyword);
            }

            Justify(cursor);

            // 外层子图要在压栈之前取，压完栈取到的就是自己。
            // 真实语料里确实有嵌套（分层架构图外面再套一个总览框），
            // 丢掉这一层会让内层子图在外层里凭空消失。
            var parent = _openSubgraphs.Count > 0 ? _openSubgraphs.Peek() : null;

            _subgraphs.Add(new MermaidSubgraphDeclaration(id, label, null, [], parent));
            _openSubgraphs.Push(id);
        }

        private void EndSubgraph(MermaidToken token)
        {
            if (_openSubgraphs.Count == 0)
            {
                Diagnose("出现了没有对应 subgraph 的 end。", token);
                return;
            }

            _openSubgraphs.Pop();
        }

        /// <summary>子图内部的 direction 语句。</summary>
        private void InnerDirection(Cursor cursor)
        {
            var keyword = cursor.Next();

            if (cursor.Peek().Kind == MermaidTokenKind.Word
                && TryParseDirection(cursor.Peek().Text, out var direction)
                && _openSubgraphs.Count > 0)
            {
                cursor.Next();

                var index = _subgraphs.FindIndex(s => string.Equals(s.Id, _openSubgraphs.Peek(), StringComparison.Ordinal));

                if (index >= 0)
                {
                    _subgraphs[index] = _subgraphs[index] with { Direction = direction };
                }
            }
            else
            {
                Diagnose("direction 语句缺少可识别的方向。", keyword);
            }

            Justify(cursor);
        }

        private void Style(Cursor cursor)
        {
            var keyword = cursor.Next();
            var tail = Tail(cursor);

            // 尾部整段收在一个记号里，作用对象与属性在这里才分开。
            // 词法层不拆是因为它不知道每条指令的形状，而这里知道。
            var (target, properties) = SplitFirst(tail);

            if (target.Length == 0)
            {
                Diagnose("style 语句缺少作用对象。", keyword);
                return;
            }

            if (properties.Length == 0)
            {
                Diagnose("style 语句没有属性。", keyword);
            }

            _styles.Add(new MermaidStyleDeclaration(target, properties));
        }

        private void ClassDef(Cursor cursor)
        {
            var keyword = cursor.Next();
            var (name, properties) = SplitFirst(Tail(cursor));

            if (name.Length == 0)
            {
                Diagnose("classDef 语句缺少类名。", keyword);
                return;
            }

            _classes.Add(new MermaidClassDeclaration(name, properties, []));
        }

        /// <summary>把类套用到一批节点。<c>class A,B,C 类名</c>。</summary>
        private void ClassAssignment(Cursor cursor)
        {
            var keyword = cursor.Next();
            var (targets, name) = SplitLast(Tail(cursor));

            if (targets.Length == 0 || name.Length == 0)
            {
                Diagnose("class 语句需要「节点列表 类名」。", keyword);
                return;
            }

            _classes.Add(new MermaidClassDeclaration(
                name,
                null,
                [.. targets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]));
        }

        /// <summary>取这一行剩下的自由文本。</summary>
        private static string Tail(Cursor cursor) =>
            cursor.Peek().Kind == MermaidTokenKind.Text ? cursor.Next().Text : string.Empty;

        /// <summary>按第一个空白切成两段。</summary>
        private static (string Head, string Remainder) SplitFirst(string text)
        {
            var trimmed = text.Trim();
            var index = trimmed.IndexOf(' ', StringComparison.Ordinal);

            return index < 0 ? (trimmed, string.Empty) : (trimmed[..index], trimmed[(index + 1)..].Trim());
        }

        /// <summary>
        /// 按最后一个空白切成两段。
        /// </summary>
        /// <remarks>
        /// 节点列表那一侧可能含逗号，逗号不是空白，所以从后面切是安全的。
        /// 从前面切的话，"class A,B,C done" 会把 "A,B,C" 与 "done" 之外的东西也带进来。
        /// </remarks>
        private static (string Head, string Remainder) SplitLast(string text)
        {
            var trimmed = text.Trim();
            var index = trimmed.LastIndexOf(' ');

            return index < 0 ? (string.Empty, trimmed) : (trimmed[..index].Trim(), trimmed[(index + 1)..].Trim());
        }

        /// <summary>
        /// 一条节点声明或一条连线链条。
        /// </summary>
        /// <remarks>
        /// 两者共用同一种开头，靠"后面跟不跟连线"区分：
        /// <c>A[文本]</c> 是声明，<c>A[文本] --&gt; B</c> 是连线。
        /// </remarks>
        private void NodeOrLink(Cursor cursor)
        {
            var sources = NodeGroup(cursor);
            var chained = false;

            while (!cursor.AtLineEnd && cursor.Peek().Kind == MermaidTokenKind.Arrow)
            {
                chained = true;

                var (arrow, line, label) = ArrowWithLabel(cursor);

                if (cursor.AtLineEnd)
                {
                    Diagnose("连线后面没有终点。", cursor.Peek());
                    break;
                }

                var targets = NodeGroup(cursor);

                if (targets.Count == 0)
                {
                    Diagnose("连线后面没有可识别的终点。", cursor.Peek());
                    break;
                }

                foreach (var from in sources)
                {
                    foreach (var to in targets)
                    {
                        _links.Add(new MermaidLinkDeclaration(from.Id, to.Id, label, arrow, line));
                    }
                }

                sources = targets;
            }

            if (!chained)
            {
                foreach (var node in sources)
                {
                    Declare(node);
                }
            }

            Justify(cursor);
        }

        /// <summary>
        /// 读一组节点，允许用与号并列。
        /// </summary>
        /// <remarks>
        /// <c>A &amp; B --&gt; C</c> 表示 A 和 B 都连到 C。
        /// </remarks>
        private List<MermaidNodeDeclaration> NodeGroup(Cursor cursor)
        {
            var nodes = new List<MermaidNodeDeclaration>();

            while (true)
            {
                var node = Node(cursor);

                if (node is not null)
                {
                    nodes.Add(node);
                    Declare(node);
                }

                if (cursor.Peek().Kind != MermaidTokenKind.Ampersand)
                {
                    return nodes;
                }

                cursor.Next();
            }
        }

        private MermaidNodeDeclaration? Node(Cursor cursor)
        {
            if (cursor.Peek().Kind != MermaidTokenKind.Word)
            {
                return null;
            }

            var token = cursor.Next();
            var shape = NodeShape.Rect;
            string? label = null;

            if (cursor.Peek().IsShapeOpen)
            {
                var open = cursor.Next();
                shape = ShapeOf(open.Text);

                if (cursor.Peek().Kind == MermaidTokenKind.Text)
                {
                    label = Unquote(cursor.Next().Text);
                }

                if (cursor.Peek().IsShapeClose)
                {
                    cursor.Next();
                }
                else
                {
                    Diagnose($"形状 {open.Text} 没有配对的收尾符号，已按矩形处理。", open);
                }
            }

            // 简写的类名：A:::className
            if (cursor.Peek().Kind == MermaidTokenKind.Colon)
            {
                while (cursor.Peek().Kind == MermaidTokenKind.Colon)
                {
                    cursor.Next();
                }

                if (cursor.Peek().Kind == MermaidTokenKind.Word)
                {
                    _classes.Add(new MermaidClassDeclaration(cursor.Next().Text, null, [token.Text]));
                }
            }

            return new MermaidNodeDeclaration(token.Text, label, shape, _openSubgraphs.Count > 0 ? _openSubgraphs.Peek() : null);
        }

        /// <summary>
        /// 读取连线以及它可能带的形式。
        /// </summary>
        /// <remarks>
        /// 两种标签写法都要认：箭头后跟 <c>|标签|</c>，以及 <c>A -- 标签 --&gt; B</c>
        /// 这种把标签夹在两条线型之间的写法。后者要先往后收一段才知道它是标签，
        /// 收错了就退回去。
        /// </remarks>
        private (ArrowStyle Arrow, LineStyle Line, string? Label) ArrowWithLabel(Cursor cursor)
        {
            var first = cursor.Next();
            var (arrow, line) = StyleOf(first);

            // 写法一：箭头之后紧跟竖线定界的标签。
            if (cursor.Peek().Kind == MermaidTokenKind.Pipe)
            {
                cursor.Next();

                var label = cursor.Peek().Kind == MermaidTokenKind.Text ? Unquote(cursor.Next().Text) : null;

                if (cursor.Peek().Kind == MermaidTokenKind.Pipe)
                {
                    cursor.Next();
                }

                return (arrow, line, label);
            }

            // 写法二：A -- 标签 --> B。只有在当前是"无箭头"线型时才可能是这种。
            if (first.Arrow is MermaidArrowKind.Open or MermaidArrowKind.DottedOpen or MermaidArrowKind.ThickOpen)
            {
                var saved = cursor.Position;
                var between = new List<string>();

                while (!cursor.AtLineEnd && cursor.Peek().Kind != MermaidTokenKind.Arrow)
                {
                    between.Add(cursor.Next().Text);
                }

                if (!cursor.AtLineEnd && between.Count > 0 && cursor.Peek().Kind == MermaidTokenKind.Arrow)
                {
                    var second = cursor.Next();
                    var (secondArrow, secondLine) = StyleOf(second);

                    return (secondArrow, secondLine, Unquote(string.Join(' ', between)));
                }

                cursor.Position = saved;
            }

            return (arrow, line, null);
        }

        /// <summary>把一条节点声明记下来，后来者带信息时覆盖前面的。</summary>
        private void Declare(MermaidNodeDeclaration node)
        {
            var index = _nodes.FindIndex(n => string.Equals(n.Id, node.Id, StringComparison.Ordinal));

            if (index < 0)
            {
                _nodes.Add(node);
                return;
            }

            var existing = _nodes[index];

            // 先出现 <c>A</c> 再出现 <c>A[文本]</c> 是常见写法：先连起来，后补说明。
            // 后一次带上了信息就覆盖，否则保留已有的——反过来做会丢掉文本。
            var carriesInformation = node.Label is not null || node.Shape != NodeShape.Rect;

            // 归属哪个子图分两种情况，真实语料里两种都出现过：
            //   在子图里被引用、而此前不属于任何子图 → 归这个子图。
            //     写法是「先画全局连线，再用 subgraph 划分层次」。
            //   在子图里被**带标签地声明**、而此前已属于别的子图 → 改归这个子图。
            //     写法是「先在别处提到它，再在它真正的归属处正式声明」。
            // 仅仅被引用（不带标签）不足以把节点从原属子图搬走，否则共享节点会随引用
            // 次序漂移——同一份输入解析两次可能落到不同子图。
            var parent = node.Parent is not null && (existing.Parent is null || carriesInformation)
                ? node.Parent
                : existing.Parent;

            if (carriesInformation)
            {
                _nodes[index] = node with { Parent = parent };
            }
            else if (!string.Equals(parent, existing.Parent, StringComparison.Ordinal))
            {
                _nodes[index] = existing with { Parent = parent };
            }
        }

        /// <summary>把这一行剩下的内容检查一遍，记下不认识的记号。</summary>
        private void Justify(Cursor cursor)
        {
            while (!cursor.AtLineEnd)
            {
                var token = cursor.Next();

                if (token.Kind == MermaidTokenKind.Unknown)
                {
                    Diagnose($"不认识的字符：{token.Text}", token);
                }
            }
        }

        private void Diagnose(string message, MermaidToken at) =>
            _diagnostics.Add(new MermaidDiagnostic(message, at.Line, at.Column));

        private static string Unquote(string text)
        {
            var trimmed = text.Trim();

            return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
                ? trimmed[1..^1]
                : trimmed;
        }

        /// <summary>形状定界符到 IR 形状的映射。</summary>
        private static NodeShape ShapeOf(string open) => open switch
        {
            "([" => NodeShape.Stadium,
            "((" => NodeShape.Circle,
            "{{" => NodeShape.Hexagon,
            "[(" => NodeShape.Cylinder,
            "[[" => NodeShape.Rect,
            "(" => NodeShape.Rounded,
            "{" => NodeShape.Diamond,
            _ => NodeShape.Rect,
        };

        /// <summary>连线记号到 IR 的箭头与线型的映射。</summary>
        private static (ArrowStyle Arrow, LineStyle Line) StyleOf(MermaidToken token) => token.Arrow switch
        {
            MermaidArrowKind.Arrow => (ArrowStyle.Arrow, LineStyle.Solid),
            MermaidArrowKind.Open => (ArrowStyle.None, LineStyle.Solid),
            MermaidArrowKind.Dotted => (ArrowStyle.Arrow, LineStyle.Dotted),
            MermaidArrowKind.DottedOpen => (ArrowStyle.None, LineStyle.Dotted),
            MermaidArrowKind.Thick => (ArrowStyle.Arrow, LineStyle.Solid),
            MermaidArrowKind.ThickOpen => (ArrowStyle.None, LineStyle.Solid),
            _ => (ArrowStyle.Arrow, LineStyle.Solid),
        };
    }
}
