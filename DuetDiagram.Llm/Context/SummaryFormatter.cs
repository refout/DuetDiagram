using System.Globalization;
using System.Text;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Llm.Context;

/// <summary>
/// 把结构化摘要渲染成一段文本。
/// </summary>
/// <remarks>
/// <para>
/// 文本是从 <see cref="DiagramSummary"/> 渲染的，不是另写一份。
/// 两者各写一份的话，改了一处忘了另一处，模型看到的与程序读到的是两份不同的内容，
/// 而两边各自都自洽。
/// </para>
/// <para>
/// **参照时刻必须显式传进来。** 「最近修改」那一段写的是「几分钟前」这种相对时间，
/// 而相对时间随调用时刻变化——不传参照时刻的话，同一份摘要两次调用会得到不同的文本，
/// 而「同一份文档生成的摘要逐字节相同」这条判据就无从验证。
/// 结构化对象里存的是绝对时间戳，相对时间只在这一层算。
/// </para>
/// </remarks>
public static class SummaryFormatter
{
    /// <summary>渲染一份摘要。</summary>
    /// <param name="summary">结构化摘要。</param>
    /// <param name="now">算相对时间用的参照时刻。</param>
    public static string Format(DiagramSummary summary, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var text = new StringBuilder();

        text.Append("图状态：\n");
        Line(text, "节点", Nodes(summary.Nodes));
        Line(text, "边", Edges(summary.Edges));
        Line(text, "布局", Layout(summary.Layout));
        Line(text, "锁定", Join(summary.PinnedNodes));
        Line(text, "可用样式", Join(summary.StyleTokens));
        Line(text, "最近修改", Changes(summary.RecentChanges, now));

        return text.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// 写一行。
    /// </summary>
    /// <remarks>
    /// 换行写死 <c>\n</c>，不用 <see cref="StringBuilder.AppendLine()"/>：后者取的是运行平台的换行符，
    /// 于是同一份摘要在两个平台上逐字节不同。这份文本要进上下文、也要进快照比对，
    /// 换个平台就变的东西没法用。
    /// </remarks>
    private static void Line(StringBuilder text, string title, string value) =>
        text.Append("- ").Append(title).Append('：').Append(value).Append('\n');

    private static string Nodes(IReadOnlyList<SummaryNode> nodes) =>
        nodes.Count == 0
            ? "无"
            : string.Join(", ", nodes.Select(node => Node(node)));

    /// <summary>
    /// 一个节点的写法：标识后面跟括号，括号里是显示文本与形状。
    /// </summary>
    /// <remarks>
    /// 缺省值不写：矩形是绝大多数节点的形状，每个都写上「,rect」会把这一行撑满，
    /// 而真正需要看出来的那些非矩形反倒被淹掉。标签为空时同理。
    /// </remarks>
    private static string Node(SummaryNode node)
    {
        var traits = new List<string>(2);

        if (node.Label.Length > 0)
        {
            traits.Add(node.Label);
        }

        if (node.Shape != NodeShape.Rect)
        {
            traits.Add(node.Shape.ToString().ToLowerInvariant());
        }

        return traits.Count == 0
            ? node.Id
            : $"{node.Id}({string.Join(',', traits)})";
    }

    private static string Edges(IReadOnlyList<SummaryEdge> edges) =>
        edges.Count == 0
            ? "无"
            : string.Join(", ", edges.Select(edge => Edge(edge)));

    private static string Edge(SummaryEdge edge)
    {
        var arrow = $"{edge.From}→{edge.To}";

        return edge.Label.Length == 0 ? arrow : $"{arrow}\"{edge.Label}\"";
    }

    /// <summary>
    /// 布局那一段：方向、层数、同层分组。
    /// </summary>
    /// <remarks>
    /// 层数与同层分组要有层投影才写得出。宿主没给的时候只写方向，
    /// 而不是按拓扑自己算一个——算出来的会与实际画面各按一套算法。
    /// </remarks>
    private static string Layout(SummaryLayout layout)
    {
        var parts = new List<string>(3)
        {
            layout.Direction.ToString(),
        };

        if (layout.LayerCount is { } count)
        {
            parts.Add($"{count.ToString(CultureInfo.InvariantCulture)}层");
        }

        if (layout.SameLayerGroups.Count > 0)
        {
            var groups = string.Join(
                '、',
                layout.SameLayerGroups.Select(group => string.Join('/', group)));

            parts.Add($"{groups}同层");
        }

        return string.Join(", ", parts);
    }

    private static string Changes(IReadOnlyList<SummaryChange> changes, DateTimeOffset now) =>
        changes.Count == 0
            ? "无"
            : string.Join("; ", changes.Select(change => Change(change, now)));

    private static string Change(SummaryChange change, DateTimeOffset now)
    {
        var body = change.Fields.Count > 0
            ? string.Join('、', change.Fields.Select(Field))
            : change.IsBulkChange
                ? $"批量变更 {change.OriginalChangeCount.ToString(CultureInfo.InvariantCulture)} 项"
                : change.CommandId;

        return $"{body} ({Who(change)}, {Age(change.Timestamp, now)})";
    }

    /// <summary>
    /// 一条字段明细的写法。
    /// </summary>
    /// <remarks>
    /// 元素级的名字（带 <c>@</c> 前缀）说的是「整个元素被增删」，写成
    /// 「标识.@node = 标识」会让人读成某个字段被赋了一个值。两者分开写。
    /// </remarks>
    private static string Field(SummaryFieldChange change) =>
        FieldNames.IsElementLevel(change.Field)
            ? $"{change.ElementId} {ElementWord(change.Kind)}"
            : $"{change.ElementId}.{change.Field} = {change.NewValue ?? "空"}";

    private static string ElementWord(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => "已加入",
        ChangeKind.Removed => "已移除",
        _ => "已变更",
    };

    /// <summary>谁改的。有操作者标识时一并写上，否则只写来源。</summary>
    private static string Who(SummaryChange change) =>
        string.IsNullOrEmpty(change.ActorId)
            ? change.Source.ToString().ToLowerInvariant()
            : $"{change.Source.ToString().ToLowerInvariant()}:{change.ActorId}";

    /// <summary>
    /// 相对时间。
    /// </summary>
    /// <remarks>
    /// 参照时刻早于变更时刻时按「刚刚」算：那说明调用方给的参照时刻不对，
    /// 而写一个负数秒前比写「刚刚」更容易被误当成真事。
    /// </remarks>
    private static string Age(DateTimeOffset timestamp, DateTimeOffset now)
    {
        var elapsed = now - timestamp;

        if (elapsed <= TimeSpan.Zero)
        {
            return "刚刚";
        }

        if (elapsed.TotalMinutes < 1)
        {
            return $"{(int)elapsed.TotalSeconds}秒前";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"{(int)elapsed.TotalMinutes}分钟前";
        }

        return elapsed.TotalDays < 1
            ? $"{(int)elapsed.TotalHours}小时前"
            : $"{(int)elapsed.TotalDays}天前";
    }

    private static string Join(IReadOnlyList<string> values) =>
        values.Count == 0 ? "无" : string.Join(", ", values);
}
