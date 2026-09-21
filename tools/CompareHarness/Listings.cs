using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DuetDiagram.Tools.CompareHarness;

/// <summary>
/// 盲评装置：把冻结语料变成一份看不出组别的结构清单。
/// </summary>
/// <remarks>
/// <para>
/// 双盲靠"结构清单"做到：Mermaid 与 DSL 的文本形态一眼可辨，
/// 所以评分者看的不是原始文本，而是从生成结果解析出来的结构清单——
/// 只投影节点、分组与边，标识整批换成流水号，看不出是哪个组。
/// </para>
/// <para>
/// 这个命令同时算出**解析错误率**——四个指标里唯一不需要人判的一项。
/// 那一项是真的，不是模拟的。
/// </para>
/// </remarks>
internal static class Listings
{
    /// <summary>打乱用的种子。写进产物，好让同一条命令永远给出同一份清单。</summary>
    private const ulong Seed = 20260921UL;

    public static int Run(string promptsPath, string corpusRoot, string outputRoot)
    {
        var prompts = PromptSet.Load(promptsPath);
        var byId = prompts.ToDictionary(p => p.Id, StringComparer.Ordinal);

        var records = Corpus.Load(corpusRoot, [.. StructureArms]);
        var (missing, drifted) = Corpus.Reconcile(records, prompts);

        if (missing.Length > 0)
        {
            Console.Error.WriteLine($"语料里有提示词文件里没有的条目：{string.Join('、', missing)}");
            return 1;
        }

        if (drifted.Length > 0)
        {
            Console.Error.WriteLine($"下列记录的原始提示词与 {promptsPath} 对不上：{string.Join('、', drifted)}");
            Console.Error.WriteLine("语料与提示词文件不是同一版，检查项会配错对象。");
            return 1;
        }

        var items = new List<Item>();

        foreach (var record in records)
        {
            var prompt = byId[record.PromptId];

            var parsed = Structure.Parse(record.Arm, record.Content);

            items.Add(new Item(record, prompt, parsed, Sanitize(parsed)));
        }

        Shuffle(items, Seed);

        Directory.CreateDirectory(outputRoot);

        Write(outputRoot, "rater.md", RaterSheet(items));
        Write(outputRoot, "items.json", ItemsJson(items));
        Write(outputRoot, "key.json", KeyJson(items));

        var report = ParseReport(items, records.Count, StructureArms.Length);

        Write(outputRoot, "parse-report.md", report);

        Console.WriteLine($"清单已生成到 {outputRoot}");
        Console.WriteLine(report);
        Console.WriteLine();
        Console.WriteLine("key.json 把匿名编号对回组别，**不要交给评分者**。");

        return 0;
    }

    private static readonly string[] StructureArms = Structure.Arms;

    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>
    /// 落盘。
    /// </summary>
    /// <remarks>
    /// 换行统一成 <c>\n</c>。<see cref="StringBuilder.AppendLine()"/> 用的是
    /// <see cref="Environment.NewLine"/>，不统一的话 Windows 与 Linux 上跑出来的是两份不同的文件，
    /// 而"逐字节可复现"正是这些产物不进版本库的唯一理由。
    /// </remarks>
    private static void Write(string root, string name, string text) =>
        File.WriteAllText(
            Path.Combine(root, name),
            text.Replace("\r\n", "\n", StringComparison.Ordinal),
            Utf8);

    /// <summary>
    /// 一份待评的条目。
    /// </summary>
    /// <param name="Raw">真实标识的那份。<b>只给不盲的报告用。</b></param>
    /// <param name="Listing">抹过格式痕迹的那份。给评分者看的就是它。</param>
    private sealed record Item(
        ResponseRecord Record,
        Prompt Prompt,
        StructureListing Raw,
        StructureListing Listing)
    {
        public string Anon { get; set; } = string.Empty;
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
    internal static StructureListing Sanitize(StructureListing listing)
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

    /// <summary>
    /// 确定性打乱。
    /// </summary>
    /// <remarks>
    /// 不用 <see cref="Random"/>：它的序列在不同 .NET 版本之间不保证稳定，
    /// 而这个装置的全部价值在于"同一条命令永远给出同一份清单"。
    /// xorshift64* 只有几行，序列在任何平台上都一样。
    /// </remarks>
    private static void Shuffle(List<Item> items, ulong seed)
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
            items[i].Anon = (i + 1).ToString("D3", CultureInfo.InvariantCulture);
        }
    }

    private static string RaterSheet(IReadOnlyList<Item> items)
    {
        var text = new StringBuilder();

        text.AppendLine("# 结构清单（盲评用）");
        text.AppendLine();
        text.AppendLine($"{items.Count} 份，已打乱。每份是一条提示词的一次生成结果，**看不出它来自哪种格式**。");
        text.AppendLine();
        text.AppendLine("清单里的内容：节点（标识、标签、形状）、分组（标识、标签、成员）、边（起点、终点、标签）。");
        text.AppendLine();
        text.AppendLine("读法：");
        text.AppendLine();
        text.AppendLine("- 节点写成 `n1「标签」`。**标识是流水号，不是原文**——原文的标识写法会暴露组别，已整批替换。");
        text.AppendLine("  判读请看标签，不要看编号。");
        text.AppendLine("- **没有标形状的节点是直角矩形**，那是两种格式共同的默认值。");
        text.AppendLine("- 节点只有编号、没有标签，说明这次生成没有给出显示文本（150 份里只有 1 个这样的节点）。");
        text.AppendLine("- 标签里的换行标记已换成空格。");
        text.AppendLine("- 清单不含端口、样式、坐标与布局意图——那些只有一种格式能表达，放进来会暴露组别。");
        text.AppendLine("  代价是**这份清单看不见布局表达能力**，判读时不要把这一点算成某一方的优点。");
        text.AppendLine("- 检查项在 `tools/CompareHarness/prompts.json` 里。它**不随语料冻结**——"
            + "检查项从来不会被发给模型，所以事后改写它不影响语料；"
            + "但改写是判断，改过哪些、为什么改，记在 `reports/compare-blind/predicate-report.md` 里。");
        text.AppendLine();
        text.AppendLine("评分口径见 `docs/Compare-Criteria.md`。");
        text.AppendLine();

        foreach (var item in items)
        {
            text.AppendLine("---");
            text.AppendLine();
            text.AppendLine($"## 编号 {item.Anon}");
            text.AppendLine();
            text.AppendLine($"**难度** {item.Prompt.Tier}　**标题** {item.Prompt.Title}");
            text.AppendLine();
            text.AppendLine("### 提示词");
            text.AppendLine();
            text.AppendLine(Quote(item.Prompt.Text));
            text.AppendLine();
            var machine = item.Prompt.Checks.Count(check => check.MachineCheckable);

            text.AppendLine($"### 检查项（{item.Prompt.Checks.Length}）");
            text.AppendLine();

            for (var i = 0; i < item.Prompt.Checks.Length; i++)
            {
                var check = item.Prompt.Checks[i];

                // 有谓词的项在这里标一下，但**不是叫评分者跳过**。
                // 那份判据还没有跟人核过，两边都判才能发现它写错的地方；
                // 而判据写错的后果是整份报告偏掉，且看不出偏在哪里。
                text.AppendLine($"{i + 1}. {check.Text}{(check.MachineCheckable ? "（有机器判据）" : string.Empty)}");
            }

            if (machine > 0)
            {
                text.AppendLine();
                text.AppendLine(
                    $"这一份有 {machine} 项带机器判据。**照常逐项判**——那份判据还没有跟人核过，"
                    + "两边都判才能发现它写错的地方；两边不一致的项会在汇总时报出来。");
            }

            text.AppendLine();
            text.AppendLine("### 结构清单");
            text.AppendLine();
            text.Append(Describe(item.Listing));
        }

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

    private static string ItemsJson(IReadOnlyList<Item> items)
    {
        var array = new JsonArray();

        foreach (var item in items)
        {
            array.Add(new JsonObject
            {
                ["anon"] = item.Anon,
                ["promptId"] = item.Prompt.Id,
                ["tier"] = item.Prompt.Tier,
                ["title"] = item.Prompt.Title,
                ["prompt"] = item.Prompt.Text,
                ["checks"] = new JsonArray(
                [
                    .. item.Prompt.Checks.Select(check => (JsonNode)new JsonObject
                    {
                        ["text"] = check.Text,
                        ["machine"] = check.MachineCheckable,
                        ["human"] = check.Human,
                    }),
                ]),
                ["outcome"] = item.Listing.Outcome.ToString(),
                ["direction"] = item.Listing.Direction,
                ["nodes"] = new JsonArray([.. item.Listing.Nodes.Select(n => (JsonNode)new JsonObject
                {
                    ["id"] = n.Id,
                    ["label"] = n.Label,
                    ["shape"] = n.Shape.ToString(),
                })]),
                ["groups"] = new JsonArray([.. item.Listing.Groups.Select(g => (JsonNode)new JsonObject
                {
                    ["id"] = g.Id,
                    ["label"] = g.Label,
                    ["members"] = new JsonArray([.. g.Members.Select(m => (JsonNode)m)]),
                })]),
                ["edges"] = new JsonArray([.. item.Listing.Edges.Select(e => (JsonNode)new JsonObject
                {
                    ["from"] = e.From,
                    ["to"] = e.To,
                    ["label"] = e.Label,
                })]),
            });
        }

        return new JsonObject
        {
            ["note"] = "结构清单，不含组别。seed 固定，重跑给出同一份。",
            ["seed"] = Seed,
            ["items"] = array,
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string KeyJson(IReadOnlyList<Item> items)
    {
        var array = new JsonArray();

        foreach (var item in items)
        {
            array.Add(new JsonObject
            {
                ["anon"] = item.Anon,
                ["arm"] = item.Record.Arm,
                ["promptId"] = item.Prompt.Id,
                ["tier"] = item.Prompt.Tier,
            });
        }

        return new JsonObject
        {
            ["warning"] = "这份文件把匿名编号对回组别。**不要交给评分者。**",
            ["seed"] = Seed,
            ["items"] = array,
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ParseReport(IReadOnlyList<Item> items, int total, int armCount)
    {
        var perArm = items
            .GroupBy(i => i.Record.Arm, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToArray();

        var text = new StringBuilder();

        text.AppendLine("# 解析统计（不盲，可机械核对）");
        text.AppendLine();
        text.AppendLine($"语料 {total} 份（{armCount} 组），模型 {items[0].Record.Model}。");
        text.AppendLine();
        text.AppendLine("这一份是**真实测量**，不是模拟：它由 `tools/CompareHarness` 用产品里那两个解析器");
        text.AppendLine("直接跑出来。四项指标里只有「解析错误率」不需要人判，就是这一项。");
        text.AppendLine();
        text.AppendLine("口径见 `docs/Compare-Criteria.md`：语法拒绝＝解析器报错、拿不到任何结构。");
        text.AppendLine("本表把「图类型不是流程图」单独列出来，因为它与语法写坏是两种毛病——");
        text.AppendLine("前者要改提示词，后者要改语法。");
        text.AppendLine();
        text.AppendLine("| 组 | 干净 | 有诊断但有结构 | 语法拒绝 | 图类型不对 | 拒绝合计 | 解析错误率 |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|");

        foreach (var arm in perArm)
        {
            var clean = arm.Count(i => i.Listing.Outcome == ParseOutcome.Clean);
            var partial = arm.Count(i => i.Listing.Outcome == ParseOutcome.Partial);
            var rejected = arm.Count(i => i.Listing.Outcome == ParseOutcome.Rejected);
            var unsupported = arm.Count(i => i.Listing.Outcome == ParseOutcome.Unsupported);
            var errors = rejected + unsupported;

            text.AppendLine($"| `{arm.Key}` | {clean} | {partial} | {rejected} | {unsupported} | {errors} | "
                + $"{Percent(errors, arm.Count())} |");
        }

        text.AppendLine();

        var c = perArm.FirstOrDefault(g => g.Key == "c-dsl");
        var b = perArm.FirstOrDefault(g => g.Key == "b-documented");

        if (c is not null && b is not null)
        {
            var cErrors = c.Count(i => Structure.IsRejected(i.Listing.Outcome));
            var bErrors = b.Count(i => Structure.IsRejected(i.Listing.Outcome));
            var cRate = (double)cErrors / c.Count();
            var bRate = (double)bErrors / b.Count();

            text.AppendLine("## 判定门第四条：解析错误率");
            text.AppendLine();
            text.AppendLine($"C 组 {cErrors}/{c.Count()}（{Percent(cErrors, c.Count())}），"
                + $"B 组 {bErrors}/{b.Count()}（{Percent(bErrors, b.Count())}）。");
            text.AppendLine();

            if (bErrors == 0)
            {
                text.AppendLine("B 组一个拒绝都没有，相对下降无从谈起——按口径这一条**不达标**。");
                text.AppendLine("阈值写的是「低至少 50%」，分母为零时它没有意义，不能算通过。");
            }
            else
            {
                var drop = 1 - (cRate / bRate);
                text.AppendLine($"相对 B 组下降 {drop.ToString("P1", CultureInfo.InvariantCulture)}，"
                    + $"阈值是 50%，**{(drop >= 0.5 ? "达标" : "不达标")}**。");
            }

            text.AppendLine();
        }

        text.AppendLine("## 逐条");
        text.AppendLine();
        text.AppendLine("| 组 | 提示词 | 结局 | 节点 | 分组 | 边 | 诊断 | 端点是分组的边 |");
        text.AppendLine("|---|---|---|---:|---:|---:|---:|---|");

        foreach (var item in items.OrderBy(i => i.Record.Arm, StringComparer.Ordinal)
                     .ThenBy(i => i.Prompt.Id, StringComparer.Ordinal))
        {
            // 这一列用真实标识：报告是不盲的，出了问题要能回到语料里核。
            text.AppendLine($"| `{item.Record.Arm}` | {item.Prompt.Id} | {item.Raw.Outcome} "
                + $"| {item.Raw.Nodes.Count} | {item.Raw.Groups.Count} | {item.Raw.Edges.Count} "
                + $"| {item.Raw.Diagnostics.Count} "
                + $"| {string.Join('；', item.Raw.GroupEndpoints)} |");
        }

        text.AppendLine();
        text.AppendLine("## 诊断原文");
        text.AppendLine();

        var diagnosed = items
            .Where(i => i.Listing.Diagnostics.Count > 0)
            .OrderBy(i => i.Record.Arm, StringComparer.Ordinal)
            .ThenBy(i => i.Prompt.Id, StringComparer.Ordinal)
            .ToArray();

        if (diagnosed.Length == 0)
        {
            text.AppendLine("没有。");
        }

        foreach (var item in diagnosed)
        {
            text.AppendLine($"- `{item.Record.Arm}` / {item.Prompt.Id}（{item.Raw.Outcome}）");

            foreach (var diagnostic in item.Raw.Diagnostics)
            {
                text.AppendLine($"  - {diagnostic}");
            }
        }

        text.AppendLine();
        text.AppendLine("## 语义拒绝率：现在算不出来");
        text.AppendLine();
        text.AppendLine("口径要求把语义拒绝（引用了不存在的节点、分组成环等）与语法拒绝分开报。");
        text.AppendLine("**这一半现在做不了**，原因在解析器的抽象语法树上：");
        text.AppendLine();
        text.AppendLine("- Mermaid 的解析器读到 `A --> B` 会顺手把 A、B 记成节点；DSL 的解析器只记边。");
        text.AppendLine("  于是「引用了不存在的节点」在 Mermaid 侧根本无法表达，在 DSL 侧却能看见。");
        text.AppendLine("  直接比这一项，比的是两个解析器的宽松程度，不是两种格式。");
        text.AppendLine("- 分组的外层字段由解析器的栈赋值，成环写不出来；成员表又是从父级推出来的，");
        text.AppendLine("  所以「分组成环」「成员不存在」同样不可表达。");
        text.AppendLine();
        text.AppendLine("真正的语义校验在 `DiagramValidator` 里，它吃的是 IR。要算这一项，");
        text.AppendLine("得先有 Mermaid→IR（P1-09）与 DSL→IR（P1-17）的映射。**在那之前这一项留空**，");
        text.AppendLine("填一个估计值进去等于把猜测洗成测量。");
        text.AppendLine();
        text.AppendLine("能算的语义问题是「边指向分组」——它两边都可表达，见上表的最后一列。");
        text.AppendLine("这一列同时是 IR 端点约定那条待决策项的证据（2026-09-21 按「端点可以是组合」定下，见 `docs/IR-Schema.md`）。");
        text.AppendLine();
        text.AppendLine("**判据是标识字符串**：端点标识与某个分组标识相同就算一条。所以它有一个已知的误报模式——");
        text.AppendLine("节点与分组撞名时，指向节点的边会被记成组端点。`c-dsl` 的 C02 正是如此：");
        text.AppendLine("模型把泳道与其中一个节点都取名叫 `pay`，而那条边指的是节点。");
        text.AppendLine("Mermaid 侧的条目才是真正的子图端点（`ODS --> DWD` 那种分层流向的写法）；");
        text.AppendLine("DSL 侧的条目是撞名，它本身是真问题，但属 P1-17 的 `group-and-node-share-a-name`，不该算进这一列。");
        text.AppendLine();
        text.AppendLine("## 装置自身的两处缝");
        text.AppendLine();
        text.AppendLine("跑通之后才看得见的，记下来免得日后当成格式的差别：");
        text.AppendLine();
        text.AppendLine("**一、B 组的语法参考承诺了解析器不接受的写法。**");
        text.AppendLine("`arms/b-documented.md` 里写了 `linkStyle`，而解析器对它是「暂不支持，已跳过」并记一条诊断。");
        text.AppendLine("文档与解析器对不上，受罚的是模型。这次**没有被触发**——150 份里只有 `b-documented/S04`");
        text.AppendLine("用了一条 `click`（文档里根本没写，是模型自己的 Mermaid 常识），所以实际影响为零。");
        text.AppendLine("修法是把 `linkStyle` 从参考里删掉，但改参考就要整组重新生成语料，");
        text.AppendLine("为一条没有触发的缝付这个代价不值。**留作已知缺陷**。");
        text.AppendLine();
        text.AppendLine("**二、两种格式的抽象语法树形状不同。**");
        text.AppendLine("隐式节点、嵌套分组、边指向分组这三处两边不一样，已在 `Structure.cs` 里抹平，");
        text.AppendLine("抹平的方向写在那个文件的注释里。抹平是手工做的，**它是这个装置里最该被复查的一段**。");
        text.AppendLine();

        var truncations = items.Where(i => string.Equals(i.Record.FinishReason, "length", StringComparison.Ordinal)).ToArray();

        text.AppendLine("## 被截断的响应");
        text.AppendLine();

        if (truncations.Length == 0)
        {
            text.AppendLine("没有。");
        }
        else
        {
            text.AppendLine($"{truncations.Length} 份的结束原因是 `length`，输出被截断。");
            text.AppendLine("截断的响应不能代表格式本身的能力，判读时要单列：");
            text.AppendLine();
            text.AppendLine("| 组 | 提示词 |");
            text.AppendLine("|---|---|");

            foreach (var item in truncations)
            {
                text.AppendLine($"| `{item.Record.Arm}` | {item.Prompt.Id} |");
            }
        }

        return text.ToString();
    }

    private static string Percent(int part, int whole) =>
        whole == 0 ? "—" : ((double)part / whole).ToString("P1", CultureInfo.InvariantCulture);

    private static string Quote(string text) =>
        string.Join('\n', text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => "> " + line));
}
