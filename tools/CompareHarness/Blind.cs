using System.Globalization;
using System.Text;

namespace DuetDiagram.Tools.CompareHarness;

/// <summary>
/// 一份待评的条目。
/// </summary>
/// <param name="Record">原始响应。<b>带组别，不要给评分者看。</b></param>
/// <param name="Prompt">对应的提示词与检查项。</param>
/// <param name="Raw">真实标识的那份清单。<b>只给不盲的报告用。</b></param>
/// <param name="Listing">抹过格式痕迹的那份。给评分者看的就是它。</param>
internal sealed record BlindItem(
    ResponseRecord Record,
    Prompt Prompt,
    StructureListing Raw,
    StructureListing Listing)
{
    /// <summary>匿名编号。打乱之后按位置赋，所以顺序本身就是盲的。</summary>
    public string Id { get; set; } = string.Empty;
}

/// <summary>
/// 把语料变成看不出组别的条目。
/// </summary>
/// <remarks>
/// <para>
/// 双盲靠"结构清单"做到：Mermaid 与 DSL 的文本形态一眼可辨，所以评分者看的不是原始文本，
/// 而是从生成结果解析出来的结构清单——只投影节点、分组与边，标识整批换成流水号。
/// </para>
/// <para>
/// 这一段被三个命令共用（出清单、抽样、算一致率）。**必须共用**：
/// 三条路径各自解析一遍的话，同一份响应在三个地方可能得到不同的清单，
/// 而"人判的是哪一份"就说不清了——一致率也就不成立。
/// </para>
/// </remarks>
internal static class Blind
{
    /// <summary>打乱用的种子。写进产物，好让同一条命令永远给出同一份清单。</summary>
    public const ulong Seed = 20260921UL;

    /// <summary>解析、抹平、打乱、编号。顺序就是产物的顺序。</summary>
    public static IReadOnlyList<BlindItem> Build(PromptSet prompts, IReadOnlyList<ResponseRecord> records)
    {
        var byId = prompts.ToDictionary(prompt => prompt.Id, StringComparer.Ordinal);
        var items = new List<BlindItem>(records.Count);

        foreach (var record in records)
        {
            var parsed = Structure.Parse(record.Arm, record.Content);

            items.Add(new BlindItem(record, byId[record.PromptId], parsed, Sanitize(parsed)));
        }

        Shuffle(items, Seed);

        return items;
    }

    /// <summary>
    /// 确定性打乱并编号。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 不用 <see cref="Random"/>：它的序列在不同 .NET 版本之间不保证稳定，
    /// 而这个装置的全部价值在于"同一条命令永远给出同一份清单"。
    /// xorshift64* 只有几行，序列在任何平台上都一样。
    /// </para>
    /// <para>
    /// 编号按打乱之后的位置赋，所以编号本身就带着顺序，看不出组别。
    /// 抽样那一处用的是另一个种子：两个装置各自打乱，抽样结果才不会
    /// 恰好是全量清单里一段连续的编号。
    /// </para>
    /// </remarks>
    public static void Shuffle(List<BlindItem> items, ulong seed)
    {
        var state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

        int NextInt(int bound)
        {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            return (int)((state * 0x2545F4914F6CDD1DUL) % (ulong)bound);
        }

        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = NextInt(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }

        for (var i = 0; i < items.Count; i++)
        {
            items[i].Id = (i + 1).ToString("D3", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// 抹掉清单里残留的格式痕迹。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 投影到结构清单之后还有两处会自报组别，实测出来的，不是设想的：
    /// </para>
    /// <list type="number">
    /// <item>
    /// <b>标识的写法。</b>三组泾渭分明：<c>c-dsl</c> 的 451 个标识里有 43 个带连字符，
    /// <c>b-documented</c> 的 506 个里一个都没有；反过来 <c>b-documented</c> 有 11 个下划线、
    /// 14 个全大写，<c>c-dsl</c> 两样都是零。一个带连字符的标识等于在清单上写明了"我是 DSL 组"。
    /// 处理办法是把标识整批换成 <c>n1</c>、<c>n2</c> 这样的流水号——
    /// **这是唯一能证明中立、而不是尽力而为的做法**。
    /// 标签保留：检查项判的就是标签与结构（"有「填邮箱」「发送重置链接」…四个节点"），
    /// 而 150 份里只有 1 个节点没有标签，换成流水号几乎不丢信息。
    /// </item>
    /// <item>
    /// <b>标签里的换行标记。</b><c>&lt;br&gt;</c> 出现在 <c>a-bare</c> 的 13 个标签与
    /// <c>b-documented</c> 的 8 个里，<c>c-dsl</c> 一个都没有——又是自报组别。
    /// 它是排版标记不是内容（渲染出来就是个换行），统一换成空格。
    /// </item>
    /// </list>
    /// <para>
    /// 两处都在报告里如实登记。抹平本身是一种改动，改了什么必须能被看见。
    /// </para>
    /// </remarks>
    public static StructureListing Sanitize(StructureListing listing)
    {
        // 节点与分组各有一张表，因为它们是两个命名空间。
        // 合成一张表的话，源文档里同一个标识既当节点又当分组时（模型偶尔这么写），
        // 两个不同的东西会被编成同一个流水号，而清单里出现重号会让求值器
        // 把节点当成分组——它会静默给出一个错的结果，看不出是编错了号。
        var nodeTokens = new Dictionary<string, string>(StringComparer.Ordinal);
        var groupTokens = new Dictionary<string, string>(StringComparer.Ordinal);

        static string Assign(Dictionary<string, string> map, int offset, string id)
        {
            if (!map.TryGetValue(id, out var token))
            {
                token = $"n{offset + map.Count + 1}";
                map[id] = token;
            }

            return token;
        }

        // 先给节点编号，再给分组——分组的流水号排在节点之后，读起来有层次。
        foreach (var node in listing.Nodes)
        {
            _ = Assign(nodeTokens, 0, node.Id);
        }

        foreach (var group in listing.Groups)
        {
            _ = Assign(groupTokens, nodeTokens.Count, group.Id);
        }

        // 引用按「先节点、后分组」解析，与映射层遇到撞名时的取舍一致：
        // 保持原标识的是节点，被改名的才是容器，所以引用处指的是节点。
        string Token(string id) => nodeTokens.GetValueOrDefault(id) ?? groupTokens[id];

        // 外层分组按分组解析。这一处不能走上面那条「先节点」的路：
        // 外层字段在投影那一步就已经确定是分组，撞名时它指的还是分组。
        string GroupToken(string id) => groupTokens.GetValueOrDefault(id) ?? nodeTokens[id];

        var nodes = listing.Nodes
            .Select(node => node with { Id = Assign(nodeTokens, 0, node.Id), Label = Flatten(node.Label) })
            .ToList();

        var groups = listing.Groups
            .Select(group => group with
            {
                Id = Assign(groupTokens, nodeTokens.Count, group.Id),
                Label = Flatten(group.Label) ?? string.Empty,
                Members = [.. group.Members.Select(Token)],
                Parent = group.Parent is null ? null : GroupToken(group.Parent),
            })
            .ToList();

        var edges = listing.Edges
            .Select(edge => edge with { From = Token(edge.From), To = Token(edge.To), Label = Flatten(edge.Label) })
            .ToList();

        var groupIds = groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);

        return listing with
        {
            Nodes = nodes,
            Groups = groups,
            Edges = edges,
            GroupEndpoints =
            [
                .. edges
                    .Where(e => groupIds.Contains(e.From) || groupIds.Contains(e.To))
                    .Select(e => $"{e.From} -> {e.To}")
                    .Distinct(StringComparer.Ordinal),
            ],
        };
    }

    /// <summary>把标签里的换行标记换成空格。渲染出来本来就是个换行，不是内容。</summary>
    private static string? Flatten(string? label) => label is null
        ? null
        : label
            .Replace("<br/>", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", " ", StringComparison.OrdinalIgnoreCase)
            .Trim();

    #region 渲染

    /// <summary>一份条目的正文：提示词、检查项、结构清单。评分者判的就是这一段。</summary>
    public static string RenderItem(BlindItem item)
    {
        var text = new StringBuilder();
        var machine = item.Prompt.Checks.Count(check => check.MachineCheckable);
        var total = item.Prompt.Checks.Length;

        text.AppendLine($"**难度** {item.Prompt.Tier}　**标题** {item.Prompt.Title}");
        text.AppendLine();
        text.AppendLine("### 提示词");
        text.AppendLine();
        text.AppendLine(Quote(item.Prompt.Text));
        text.AppendLine();
        text.AppendLine($"### 检查项（{total}）");
        text.AppendLine();

        // 逐项标注只在**两种都有**的时候才有信息量。全都是机器判据时每一项都带同一个标记，
        // 一页下来只剩噪声；全都没有时标了也没用。所以只在那一种情形下逐项标。
        var mixed = machine > 0 && machine < total;

        for (var i = 0; i < total; i++)
        {
            var check = item.Prompt.Checks[i];

            text.AppendLine($"{i + 1}. {check.Text}{(mixed && check.MachineCheckable ? "（有机器判据）" : string.Empty)}");
        }

        if (machine > 0)
        {
            text.AppendLine();

            // 机器判据**不是叫评分者跳过**。那份判据还没有跟人核过，
            // 两边都判才能发现它写错的地方；而判据写错的后果是整份报告偏掉，且看不出偏在哪里。
            var scope = machine == total ? "这一份的检查项都有机器判据" : $"这一份有 {machine} 项带机器判据";
            text.AppendLine(
                $"{scope}。**照常逐项判**——那份判据还没有跟人核过，"
                + "两边都判才能发现它写错的地方；两边不一致的项会在汇总时报出来。");
        }

        text.AppendLine();
        text.AppendLine("### 结构清单");
        text.AppendLine();
        text.Append(Describe(item.Listing));

        return text.ToString();
    }

    private static string Describe(StructureListing listing)
    {
        var text = new StringBuilder();

        text.AppendLine($"方向：{listing.Direction}");
        text.AppendLine();
        text.AppendLine($"**节点（{listing.Nodes.Count}）**");
        text.AppendLine();

        if (listing.Nodes.Count == 0)
        {
            text.AppendLine("（没有）");
        }

        foreach (var node in listing.Nodes)
        {
            var label = node.Label is not null && !string.Equals(node.Label, node.Id, StringComparison.Ordinal)
                ? $"「{node.Label}」"
                : string.Empty;

            var shape = node.Shape == Core.Model.NodeShape.Rect ? string.Empty : $"（{node.Shape}）";

            text.AppendLine($"- {node.Id}{label}{shape}");
        }

        text.AppendLine();
        text.AppendLine($"**分组（{listing.Groups.Count}）**");
        text.AppendLine();

        if (listing.Groups.Count == 0)
        {
            text.AppendLine("（没有）");
        }

        foreach (var (group, depth) in Nest(listing.Groups))
        {
            var indent = new string(' ', depth * 2);
            var members = string.Join('、', listing.Nodes.Where(n => group.Members.Contains(n.Id)).Select(n => n.Id));
            var label = string.IsNullOrEmpty(group.Label) ? string.Empty : $"「{group.Label}」";

            text.AppendLine($"{indent}- {group.Id}{label}：{(members.Length == 0 ? "（没有节点成员）" : members)}");
        }

        text.AppendLine();
        text.AppendLine($"**边（{listing.Edges.Count}）**");
        text.AppendLine();

        if (listing.Edges.Count == 0)
        {
            text.AppendLine("（没有）");
        }

        foreach (var edge in listing.Edges)
        {
            var label = string.IsNullOrEmpty(edge.Label) ? string.Empty : $"「{edge.Label}」";
            text.AppendLine($"- {edge.From} -> {edge.To}{label}");
        }

        text.AppendLine();

        return text.ToString();
    }

    /// <summary>
    /// 给分组算出缩进深度。声明顺序保证外层先出现，所以照原顺序输出就是一棵排好的树。
    /// </summary>
    /// <remarks>
    /// 深度用有界循环算，不递归：外层字段由解析器的栈赋值，正常不会成环，
    /// 但一份报告生成器卡死比多写三行代码糟糕得多。
    /// </remarks>
    private static IEnumerable<(ListedGroup Group, int Depth)> Nest(IReadOnlyList<ListedGroup> groups)
    {
        var byId = groups.ToDictionary(g => g.Id, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var depth = 0;
            var cursor = group.Parent;

            while (cursor is not null && depth < groups.Count && byId.TryGetValue(cursor, out var outer))
            {
                depth++;
                cursor = outer.Parent;
            }

            yield return (group, depth);
        }
    }

    public static string Quote(string text) =>
        string.Join('\n', text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => "> " + line));

    #endregion
}
