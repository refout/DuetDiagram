using System.Text.Json.Nodes;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Tools.CompareHarness;

/// <summary>
/// 一条匹配器：描述"什么样的节点（或分组）算数"。
/// </summary>
/// <param name="Label">
/// 标签的同义词表，任一命中即可。按**子串**比，因为「连接」要能对上「连接邮件服务器」，
/// 而反过来不行——写长了就漏掉同义写法。空表表示不限标签。
/// </param>
/// <param name="LabelAll">
/// 标签里必须**全部**出现的词。用来判"一个节点同时覆盖了两件事"——
/// 检查项写"有两个节点"时，模型把两件事写进一个节点的标签里是常见写法，
/// 而人判的话那是通过的。<see cref="Label"/> 管"任一"，这个管"全都"。
/// </param>
/// <param name="Shape">形状。空表示不限。</param>
/// <param name="InGroup">
/// 所属分组标签的同义词表。按**包含**算：节点在嵌套分组里，也算在外层分组里。
/// </param>
internal sealed record Matcher(string[] Label, NodeShape? Shape, string[]? InGroup, string[]? LabelAll = null)
{
    /// <summary>只看标签这一部分。分组没有形状，所以它只走这一条。</summary>
    public bool MatchesLabel(string? label) =>
        (Label.Length == 0
            || (label is not null && Label.Any(word => label.Contains(word, StringComparison.OrdinalIgnoreCase))))
        && (LabelAll is null
            || (label is not null && LabelAll.All(word => label.Contains(word, StringComparison.OrdinalIgnoreCase))));

    /// <summary>标签与形状这两项与所在位置无关，可以单独判。</summary>
    public bool Matches(string? label, NodeShape shape) =>
        MatchesLabel(label) && (Shape is null || Shape == shape);

    public override string ToString()
    {
        var words = Label.Length == 0 ? "任意端点" : $"「{string.Join('／', Label)}」";

        return LabelAll is null ? words : $"{words}且标签含{string.Join('、', LabelAll)}";
    }
}

/// <summary>
/// 检查项的可执行形式。
/// </summary>
/// <remarks>
/// <para>
/// 检查项原本是一句自然语言（"有「检查文件大小」的判定节点"），判它得人读一遍图。
/// 这里把它写成对结构清单的谓词：机器能判，而且**判据是显式的**。
/// </para>
/// <para>
/// 显式是这一层唯一的价值。拿字符串相似度加阈值也能给出 0/1，但阈值取多少没有客观依据，
/// 判断藏在那个数字里，读的人无从反驳；谓词写在提示词文件里，谁都可以指着某一条说
/// "这个判据不对"，然后改它。相似度阈值没有这种可争议的抓手。
/// </para>
/// <para>
/// 判的对象是**结构清单**（<see cref="StructureListing"/>）——评分者看到的那一份，
/// 不是原始文本，也不是 IR。同一份判据因此既能机器判、也能人工判；
/// 将来要拿人工评分来校谓词，两边看的是同一件东西。
/// </para>
/// <para>
/// 谓词只判**该有的在不在**，不判**多出来的该不该有**。所以它能算端到端准确率，
/// 算不了人工修正步骤——"这份图还得多删一个节点"不在任何检查项的描述范围里。
/// </para>
/// </remarks>
internal abstract record Predicate
{
    /// <summary>求值。</summary>
    /// <param name="index">结构清单的索引。</param>
    /// <returns>通过与否，以及不通过时的一句话原因。</returns>
    public abstract Verdict Evaluate(StructureIndex index);

    #region 从 JSON 读

    /// <summary>
    /// 从 JSON 读一条谓词。
    /// </summary>
    /// <param name="node">谓词所在的 JSON 节点。</param>
    /// <param name="where">出错时用来指明位置的路径，例如 <c>S01 第 2 项</c>。</param>
    /// <exception cref="InvalidDataException">
    /// 谓词写坏了。这里不兜底——写坏的谓词会静默判错，比直接崩掉糟得多。
    /// </exception>
    public static Predicate Parse(JsonNode? node, string where)
    {
        if (node is not JsonObject obj)
        {
            throw new InvalidDataException($"{where}：谓词必须是一个对象。");
        }

        return Text(obj["kind"], where, "kind") switch
        {
            "node" => new NodePredicate(Matchers(obj["each"], where, "each")),
            "group" => new GroupPredicate(Matchers(obj["each"], where, "each")),
            "groupMembers" => new GroupMembersPredicate(
                MatcherOf(obj["group"], where, "group"),
                MatchersOrEmpty(obj["members"], where, "members"),
                MatchersOrEmpty(obj["memberGroups"], where, "memberGroups")),
            "edge" => new EdgePredicate(
                MatcherOf(obj["from"], where, "from"),
                MatcherOf(obj["to"], where, "to"),
                Synonyms(obj["label"], where, "label")),
            "chain" => new ChainPredicate(Matchers(obj["steps"], where, "steps")),
            "order" => new OrderPredicate(Matchers(obj["steps"], where, "steps")),
            "reach" => new ReachPredicate(
                MatcherOf(obj["from"], where, "from"),
                MatcherOf(obj["to"], where, "to")),
            "terminal" => new TerminalPredicate(MatcherOf(obj["node"], where, "node")),
            "noShape" => new NoShapePredicate(
                ParseShape(obj["shape"], where),
                MatchersOrEmpty(obj["inGroup"], where, "inGroup")),
            "count" => CountOf(obj, where),
            "fanout" => new FanoutPredicate(
                MatcherOf(obj["node"], where, "node"),
                Number(obj["atLeast"], where, "atLeast") ?? 2),
            "all" => new AllPredicate(Predicates(obj["of"], where, "of")),
            "any" => new AnyPredicate(Predicates(obj["of"], where, "of")),
            var kind => throw new InvalidDataException($"{where}：认不出的谓词种类 {kind}。"),
        };
    }

    private static Predicate CountOf(JsonObject obj, string where)
    {
        var exact = Number(obj["equals"], where, "equals");
        var atLeast = Number(obj["atLeast"], where, "atLeast");
        var atMost = Number(obj["atMost"], where, "atMost");

        // 一个范围都不给的话这条谓词恒成立，写它的人多半是漏了字段。
        // 恒成立的谓词不会报错，只会一直给分，所以在这里拦下来。
        return exact is null && atLeast is null && atMost is null
            ? throw new InvalidDataException($"{where}：count 要给出 equals、atLeast、atMost 里的至少一个，否则它恒成立。")
            : new CountPredicate(MatcherOf(obj["match"], where, "match"), exact, atLeast, atMost);
    }

    private static Predicate[] Predicates(JsonNode? node, string where, string field) =>
        node is JsonArray array && array.Count > 0
            ? [.. array.Select((item, i) => Parse(item, $"{where} 的 {field}[{i}]"))]
            : throw new InvalidDataException($"{where}：{field} 必须是一个非空数组。");

    private static Matcher[] Matchers(JsonNode? node, string where, string field)
    {
        var matchers = MatchersOrEmpty(node, where, field);

        return matchers.Length > 0
            ? matchers
            : throw new InvalidDataException($"{where}：{field} 必须是一个非空数组。");
    }

    private static Matcher[] MatchersOrEmpty(JsonNode? node, string where, string field)
    {
        if (node is null)
        {
            return [];
        }

        return node is JsonArray array
            ? [.. array.Select((item, i) => MatcherOf(item, where, $"{field}[{i}]"))]
            : throw new InvalidDataException($"{where}：{field} 必须是一个数组。");
    }

    private static Matcher MatcherOf(JsonNode? node, string where, string field)
    {
        if (node is not JsonObject obj)
        {
            throw new InvalidDataException($"{where}：{field} 必须是一个对象。");
        }

        return new Matcher(
            Synonyms(obj["label"], where, $"{field}.label") ?? [],
            obj["shape"] is null ? null : ParseShape(obj["shape"], where),
            Synonyms(obj["inGroup"], where, $"{field}.inGroup"),
            Synonyms(obj["labelAll"], where, $"{field}.labelAll"));
    }

    private static string[]? Synonyms(JsonNode? node, string where, string field) => node switch
    {
        null => null,
        JsonArray array =>
        [
            .. array.Select(item => item is JsonValue value && value.TryGetValue<string>(out var text)
                ? text
                : throw new InvalidDataException($"{where}：{field} 里混了非字符串项。")),
        ],
        _ => throw new InvalidDataException($"{where}：{field} 必须是字符串数组。"),
    };

    private static string Text(JsonNode? node, string where, string field) =>
        node?.GetValue<string>() ?? throw new InvalidDataException($"{where}：缺少 {field}。");

    private static int? Number(JsonNode? node, string where, string field) => node switch
    {
        null => null,
        JsonValue value when value.TryGetValue<int>(out var number) => number,
        _ => throw new InvalidDataException($"{where}：{field} 必须是整数。"),
    };

    private static NodeShape ParseShape(JsonNode? node, string where)
    {
        var text = node?.GetValue<string>() ?? throw new InvalidDataException($"{where}：缺少 shape。");

        return Enum.TryParse<NodeShape>(text, ignoreCase: true, out var shape)
            ? shape
            : throw new InvalidDataException($"{where}：认不出的形状 {text}。");
    }

    #endregion

    #region 共用的指派

    /// <summary>把一组匹配器指派到互不相同的节点上。节点数不多，回溯足够。</summary>
    /// <param name="allow">额外的约束，用来把候选缩到某个范围（例如"必须是这个分组的后代"）。</param>
    private static bool AssignDistinct(
        StructureIndex index,
        IReadOnlyList<Matcher> matchers,
        int at,
        HashSet<string> used,
        Func<string, bool>? allow = null)
    {
        if (at == matchers.Count)
        {
            return true;
        }

        foreach (var id in index.NodesMatching(matchers[at]))
        {
            if ((allow is null || allow(id)) && used.Add(id)
                && AssignDistinct(index, matchers, at + 1, used, allow))
            {
                return true;
            }

            used.Remove(id);
        }

        return false;
    }

    private static bool AssignDistinctGroups(
        StructureIndex index,
        IReadOnlyList<Matcher> matchers,
        int at,
        HashSet<string> used)
    {
        if (at == matchers.Count)
        {
            return true;
        }

        foreach (var id in index.GroupIdsMatching(matchers[at]))
        {
            if (used.Add(id) && AssignDistinctGroups(index, matchers, at + 1, used))
            {
                return true;
            }

            used.Remove(id);
        }

        return false;
    }

    /// <summary>这些匹配器能不能按顺序串成一串边。</summary>
    private static bool ChainFrom(
        StructureIndex index,
        IReadOnlyList<Matcher> steps,
        int at,
        string? previous,
        HashSet<string> used)
    {
        if (at == steps.Count)
        {
            return true;
        }

        foreach (var id in index.NodesMatching(steps[at]))
        {
            if (!used.Add(id))
            {
                continue;
            }

            if ((previous is null || index.HasEdge(previous, id))
                && ChainFrom(index, steps, at + 1, id, used))
            {
                return true;
            }

            used.Remove(id);
        }

        return false;
    }

    private static string Show(Matcher matcher) => matcher.ToString();

    private static string Show(IReadOnlyList<Matcher> matchers) => string.Join('、', matchers.Select(Show));

    #endregion

    #region 各种谓词

    /// <summary>每个匹配器都要有一个互不相同的节点对上。</summary>
    private sealed record NodePredicate(Matcher[] Each) : Predicate
    {
        public override Verdict Evaluate(StructureIndex index) =>
            AssignDistinct(index, Each, 0, [])
                ? Verdict.Ok
                : Verdict.Fail($"找不到 {Show(Each)} 这几个互不相同的节点（图里共 {index.Nodes.Count} 个节点）。");
    }

    /// <summary>每个匹配器都要有一个互不相同的分组对上。</summary>
    private sealed record GroupPredicate(Matcher[] Each) : Predicate
    {
        public override Verdict Evaluate(StructureIndex index)
        {
            var missing = Each.Where(m => index.GroupIdsMatching(m).Count == 0).ToArray();

            if (missing.Length > 0)
            {
                return Verdict.Fail(
                    $"没有标签命中 {Show(missing)} 的分组（图里共 {index.Groups.Count} 个分组）。");
            }

            return AssignDistinctGroups(index, Each, 0, [])
                ? Verdict.Ok
                : Verdict.Fail($"{Show(Each)} 这几个分组不是互不相同的。");
        }
    }

    /// <summary>某个分组（含嵌套）里要有这些节点。</summary>
    private sealed record GroupMembersPredicate(Matcher Group, Matcher[] Members, Matcher[] MemberGroups) : Predicate
    {
        public override Verdict Evaluate(StructureIndex index)
        {
            var candidates = index.GroupIdsMatching(Group);

            if (candidates.Count == 0)
            {
                return Verdict.Fail($"没有标签命中 {Show(Group)} 的分组。");
            }

            foreach (var groupId in candidates)
            {
                if (Members.Length > 0
                    && !AssignDistinct(index, Members, 0, [], id => index.IsDescendant(id, groupId)))
                {
                    continue;
                }

                if (MemberGroups.Length > 0
                    && !MemberGroups.All(m => index.GroupIdsMatching(m).Any(id => index.IsDescendant(id, groupId))))
                {
                    continue;
                }

                return Verdict.Ok;
            }

            return Verdict.Fail($"分组 {Show(Group)} 里找不到 {Show([.. Members, .. MemberGroups])}。");
        }
    }

    /// <summary>存在一条从某处到某处的边。</summary>
    private sealed record EdgePredicate(Matcher From, Matcher To, string[]? Label) : Predicate
    {
        public override Verdict Evaluate(StructureIndex index)
        {
            foreach (var edge in index.Edges)
            {
                if (!index.EndpointMatches(edge.From, From)
                    || !index.EndpointMatches(edge.To, To)
                    || !LabelMatches(edge.Label))
                {
                    continue;
                }

                return Verdict.Ok;
            }

            return Verdict.Fail(
                $"没有从 {Show(From)} 到 {Show(To)} 的边{LabelNote()}。");
        }

        private bool LabelMatches(string? label) =>
            Label is null
            || (label is not null && Label.Any(word => label.Contains(word, StringComparison.OrdinalIgnoreCase)));

        private string LabelNote() =>
            Label is null ? string.Empty : $"，标签含「{string.Join('／', Label)}」";
    }

    /// <summary>这些匹配器能按顺序串成一串**相邻**的边。</summary>
    private sealed record ChainPredicate(Matcher[] Steps) : Predicate
    {
        public override Verdict Evaluate(StructureIndex index) =>
            ChainFrom(index, Steps, 0, null, [])
                ? Verdict.Ok
                : Verdict.Fail($"没有把 {Show(Steps)} 依次连起来的边（要求相邻）。");
    }

    /// <summary>
    /// 这些匹配器在流程里按这个顺序出现，中间允许隔着别的节点。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与 <see cref="ChainPredicate"/> 的差别只在"中间能不能隔东西"。
    /// 检查项说"按顺序串联""依次经过"时，中间多一个判定节点不算错——
    /// 模型在两步之间插一个「成功?」是常见写法，而人判的话不会因此说顺序不对。
    /// 说"有边"的地方才是真的要求相邻，那种地方用 chain。
    /// </para>
    /// <para>
    /// 代价是它比 chain 松：隔多少个节点都算。所以只在检查项本身没要求相邻时用它。
    /// </para>
    /// </remarks>
    private sealed record OrderPredicate(Matcher[] Steps) : Predicate
    {
        public override Verdict Evaluate(StructureIndex index)
        {
            for (var i = 0; i + 1 < Steps.Length; i++)
            {
                if (!ReachPredicate.Reaches(index, Steps[i], Steps[i + 1]))
                {
                    return Verdict.Fail($"{Show(Steps[i])} 之后沿边走不到 {Show(Steps[i + 1])}。");
                }
            }

            return Verdict.Ok;
        }
    }

    /// <summary>某个匹配器指向的节点是一条分支的终点：它没有出边。</summary>
    /// <remarks>
    /// 检查项写"报错并结束"时，模型有两种画法：真的画一个「结束」节点，
    /// 或者让「报错」就是叶子。后者在图上一样表示这条分支到此为止，人判算通过，
    /// 所以不能只判"能不能走到结束节点"——那会把前一种写法当成唯一的正确答案。
    /// </remarks>
    private sealed record TerminalPredicate(Matcher Node) : Predicate
    {
        public override Verdict Evaluate(StructureIndex index)
        {
            var candidates = index.EndpointsMatching(Node);

            if (candidates.Count == 0)
            {
                return Verdict.Fail($"没有标签命中 {Show(Node)} 的节点或分组。");
            }

            return candidates.Any(id => index.TargetsOf(id).Count == 0)
                ? Verdict.Ok
                : Verdict.Fail($"标签命中 {Show(Node)} 的节点都还有出边，不是终点。");
        }
    }

    /// <summary>从一个匹配器沿边能走到另一个匹配器，中间可以过别的节点与分组。</summary>
    private sealed record ReachPredicate(Matcher From, Matcher To) : Predicate
    {
        public override Verdict Evaluate(StructureIndex index)
        {
            if (index.EndpointsMatching(From).Count == 0)
            {
                return Verdict.Fail($"没有标签命中 {Show(From)} 的节点或分组。");
            }

            if (index.EndpointsMatching(To).Count == 0)
            {
                return Verdict.Fail($"没有标签命中 {Show(To)} 的节点或分组。");
            }

            return Reaches(index, From, To)
                ? Verdict.Ok
                : Verdict.Fail($"从 {Show(From)} 沿边走不到 {Show(To)}。");
        }

        /// <summary>有没有一条从 From 到 To 的路径，至少一条边。</summary>
        public static bool Reaches(StructureIndex index, Matcher from, Matcher to)
        {
            var starts = index.EndpointsMatching(from);

            if (starts.Count == 0)
            {
                return false;
            }

            var seen = new HashSet<string>(starts, StringComparer.Ordinal);
            var queue = new Queue<string>(starts);

            while (queue.Count > 0)
            {
                foreach (var next in index.TargetsOf(queue.Dequeue()))
                {
                    if (index.EndpointMatches(next, to))
                    {
                        return true;
                    }

                    if (seen.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            return false;
        }
    }

    /// <summary>某个形状的节点一个都不该有。</summary>
    private sealed record NoShapePredicate(NodeShape Shape, Matcher[] InGroup) : Predicate
    {
        public override Verdict Evaluate(StructureIndex index)
        {
            var found = index.Nodes
                .Where(node => node.Shape == Shape)
                .Where(node => InGroup.Length == 0 || InGroup.Any(scope => index.NodeMatches(node.Id, scope)))
                .Select(node => node.Label ?? node.Id)
                .ToArray();

            return found.Length == 0
                ? Verdict.Ok
                : Verdict.Fail($"不该有 {Shape} 节点，却有 {found.Length} 个：{string.Join('、', found.Take(5))}。");
        }
    }

    /// <summary>符合匹配器的节点数量要落在一个范围里。</summary>
    private sealed record CountPredicate(Matcher Match, int? Exact, int? AtLeast, int? AtMost) : Predicate
    {
        public override Verdict Evaluate(StructureIndex index)
        {
            var count = index.NodesMatching(Match).Count;

            if (Exact is { } exact && count != exact)
            {
                return Verdict.Fail($"{Show(Match)} 的节点应有 {exact} 个，实际 {count} 个。");
            }

            if (AtLeast is { } least && count < least)
            {
                return Verdict.Fail($"{Show(Match)} 的节点至少应有 {least} 个，实际 {count} 个。");
            }

            if (AtMost is { } most && count > most)
            {
                return Verdict.Fail($"{Show(Match)} 的节点至多应有 {most} 个，实际 {count} 个。");
            }

            return Verdict.Ok;
        }
    }

    /// <summary>某个节点要有至少这么多条出边，去往互不相同的目标。</summary>
    private sealed record FanoutPredicate(Matcher Node, int AtLeast) : Predicate
    {
        public override Verdict Evaluate(StructureIndex index)
        {
            var sources = index.EndpointsMatching(Node);

            if (sources.Count == 0)
            {
                return Verdict.Fail($"没有标签命中 {Show(Node)} 的节点或分组。");
            }

            var best = sources.Max(id => index.TargetsOf(id).Distinct(StringComparer.Ordinal).Count());

            return best >= AtLeast
                ? Verdict.Ok
                : Verdict.Fail($"{Show(Node)} 的分支应有 {AtLeast} 条，实际最多 {best} 条。");
        }
    }

    private sealed record AllPredicate(Predicate[] Of) : Predicate
    {
        public override Verdict Evaluate(StructureIndex index)
        {
            foreach (var predicate in Of)
            {
                if (predicate.Evaluate(index) is { Pass: false } verdict)
                {
                    return verdict;
                }
            }

            return Verdict.Ok;
        }
    }

    private sealed record AnyPredicate(Predicate[] Of) : Predicate
    {
        public override Verdict Evaluate(StructureIndex index)
        {
            var reasons = new List<string>();

            foreach (var verdict in Of.Select(predicate => predicate.Evaluate(index)))
            {
                if (verdict.Pass)
                {
                    return Verdict.Ok;
                }

                reasons.Add(verdict.Reason ?? string.Empty);
            }

            return Verdict.Fail($"以下几条都不成立：{string.Join("；", reasons)}");
        }
    }

    #endregion
}

/// <summary>一条检查项的判定。</summary>
/// <param name="Pass">是否通过。</param>
/// <param name="Reason">不通过时的一句话原因。报告里要给人看，所以得指出缺的是什么。</param>
internal sealed record Verdict(bool Pass, string? Reason = null)
{
    public static Verdict Ok { get; } = new(true);

    public static Verdict Fail(string reason) => new(false, reason);
}

/// <summary>
/// 结构清单的索引。
/// </summary>
/// <remarks>
/// <para>
/// 求值要反复问三类问题：哪些节点符合某个匹配器、某个节点的出边去哪、某个节点在不在某个分组里。
/// 每次现扫一遍清单是 O(节点数)，而求值本身带回溯，叠起来会让报告从"秒出"变成"等一会儿"。
/// 所以先把清单摊成几张表。
/// </para>
/// <para>
/// 分组归属按**包含**算：节点在嵌套分组里，也算在外层分组里。
/// "业务层含五个服务"这种说法本来就是包含的意思，逐层往上走才对得上。
/// </para>
/// </remarks>
internal sealed class StructureIndex
{
    private readonly Dictionary<string, ListedNode> _nodes;
    private readonly Dictionary<string, ListedGroup> _groups;
    private readonly Dictionary<string, List<string>> _targets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _nodeGroup = new(StringComparer.Ordinal);

    public StructureIndex(StructureListing listing)
    {
        Listing = listing;
        _nodes = listing.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        _groups = listing.Groups.ToDictionary(group => group.Id, StringComparer.Ordinal);

        foreach (var edge in listing.Edges)
        {
            if (!_targets.TryGetValue(edge.From, out var list))
            {
                list = [];
                _targets[edge.From] = list;
            }

            list.Add(edge.To);
        }

        // 节点的所属分组从分组的成员表反推。成员表是权威的那一份——
        // 它在投影那一步已经按父级重推过，两种格式到这里已经同源。
        foreach (var group in listing.Groups)
        {
            foreach (var member in group.Members.Where(_nodes.ContainsKey))
            {
                _nodeGroup[member] = group.Id;
            }
        }
    }

    public StructureListing Listing { get; }

    public IReadOnlyList<ListedNode> Nodes => Listing.Nodes;

    public IReadOnlyList<ListedGroup> Groups => Listing.Groups;

    public IReadOnlyList<ListedEdge> Edges => Listing.Edges;

    public IReadOnlyList<string> TargetsOf(string id) => _targets.GetValueOrDefault(id) ?? [];

    public bool HasEdge(string from, string to) => TargetsOf(from).Contains(to, StringComparer.Ordinal);

    public bool NodeMatches(string id, Matcher matcher) =>
        _nodes.TryGetValue(id, out var node)
        && matcher.Matches(node.Label, node.Shape)
        && InGroups(node.Id, matcher.InGroup);

    /// <summary>符合匹配器的节点。</summary>
    public IReadOnlyList<string> NodesMatching(Matcher matcher) =>
        [.. _nodes.Values.Where(node => matcher.Matches(node.Label, node.Shape) && InGroups(node.Id, matcher.InGroup)).Select(node => node.Id)];

    /// <summary>符合匹配器的分组。分组只有标签可比。</summary>
    public IReadOnlyList<string> GroupIdsMatching(Matcher matcher) =>
        [.. _groups.Values.Where(group => matcher.MatchesLabel(group.Label)).Select(group => group.Id)];

    /// <summary>节点是否在某个分组的范围里（含嵌套）。分组标签按子串比。</summary>
    public bool InGroup(string nodeId, string groupLabel) =>
        Ancestors(nodeId).Any(id => _groups[id].Label.Contains(groupLabel, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 节点或嵌套分组是不是某个分组的后代（含嵌套）。
    /// </summary>
    /// <remarks>
    /// 两种标识的父级链存在不同的地方：节点的在所属分组那张表里，分组的在自己的外层字段里。
    /// 只按节点那张表查的话，嵌套分组永远查不到祖先，而"某个分组里含某个子分组"
    /// 会静默判成不成立。
    /// </remarks>
    public bool IsDescendant(string id, string groupId) =>
        (_nodes.ContainsKey(id) ? Ancestors(id) : AncestorGroups(id))
            .Contains(groupId, StringComparer.Ordinal);

    /// <summary>
    /// 端点匹配：先按节点找，再按分组找。
    /// </summary>
    /// <remarks>
    /// 边的端点可以落在分组上——分层架构图里「原始层 --&gt; 明细层」就是拿分组当端点的。
    /// 顺序与本仓其它几处一致（先节点、后组合）：九个集合共用一个标识命名空间，
    /// 同一个标识不会既是节点又是分组，先查哪个都不影响结果。
    /// </remarks>
    public bool EndpointMatches(string id, Matcher matcher) =>
        _nodes.ContainsKey(id) ? NodeMatches(id, matcher) : GroupMatches(id, matcher);

    /// <summary>符合匹配器的端点，节点与分组都算。</summary>
    public IReadOnlyList<string> EndpointsMatching(Matcher matcher) =>
    [
        .. _nodes.Keys.Where(id => NodeMatches(id, matcher))
            .Concat(_groups.Keys.Where(id => GroupMatches(id, matcher))),
    ];

    private bool GroupMatches(string id, Matcher matcher)
    {
        if (!_groups.TryGetValue(id, out var group))
        {
            return false;
        }

        if (!matcher.MatchesLabel(group.Label))
        {
            return false;
        }

        if (matcher.InGroup is null || matcher.InGroup.Length == 0)
        {
            return true;
        }

        // 分组**自己的标签**也算在内：「第一波里的东西」与「就是第一波这个分组」
        // 在端点这个位置上是同一件事。
        return matcher.InGroup.Any(word => group.Label.Contains(word, StringComparison.OrdinalIgnoreCase))
            || AncestorGroups(id).Any(parent => matcher.InGroup.Any(
                word => _groups[parent].Label.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    private IEnumerable<string> AncestorGroups(string id)
    {
        var current = _groups.GetValueOrDefault(id)?.Parent;

        while (current is not null && _groups.TryGetValue(current, out var group))
        {
            yield return current;
            current = group.Parent;
        }
    }

    private IEnumerable<string> Ancestors(string nodeId)
    {
        var current = _nodeGroup.GetValueOrDefault(nodeId);

        while (current is not null && _groups.TryGetValue(current, out var group))
        {
            yield return current;
            current = group.Parent;
        }
    }

    private bool InGroups(string nodeId, string[]? labels) =>
        labels is null || labels.Length == 0 || labels.Any(label => InGroup(nodeId, label));
}
