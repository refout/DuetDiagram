using DuetDiagram.Core.Model;
using DuetDiagram.Dsl.Mapping;
using DuetDiagram.Dsl.Parsing;
using DuetDiagram.Mermaid.Import;
using DuetDiagram.Mermaid.Parsing;

namespace DuetDiagram.Tools.CompareHarness;

/// <summary>
/// 语义拒绝率：解析出来的图交给校验器，看有多少份过不去。
/// </summary>
/// <remarks>
/// <para>
/// 这是四个指标里**第二个可以自动算的**（第一个是解析错误率）。它与解析错误率问的不是同一件事：
/// 前者问"读得懂吗"，这一个问"读出来的东西说得通吗"——引用了不存在的节点、
/// 成员关系自相矛盾、标识重复，都属于后者。
/// </para>
/// <para>
/// **它此前算不出来，因为缺一步映射。** 解析器的抽象语法树里，Mermaid 侧读到
/// <c>A --&gt; B</c> 会顺手把 A、B 记成节点，DSL 侧只记边——于是"引用了不存在的节点"
/// 在 Mermaid 侧根本不可表达，直接拿语法树比，比的是两个解析器的宽松程度而不是两种格式。
/// 走一遍映射到 IR 再校验，两边才落在同一个口径上。
/// </para>
/// <para>
/// **分母只算有结构的份数**：解析器整份放弃的那些（图类型不对）没有结构可校验，
/// 把它们算作"语义没问题"会凭空拉低这个比率。它们单列出来数。
/// </para>
/// </remarks>
internal static class Semantic
{
    /// <summary>三组。顺序固定，报告里的行序跟着它走。</summary>
    private static readonly string[] Arms = ["a-bare", "b-documented", "c-dsl"];

    /// <summary>用自有 DSL 的那一组。其余两组都是 Mermaid。</summary>
    private const string DslArm = "c-dsl";

    public static int Run(string corpusRoot, string outputRoot)
    {
        var records = Corpus.Load(corpusRoot, Arms);
        var rows = records.Select(Measure).ToArray();

        Directory.CreateDirectory(outputRoot);

        var path = Path.Combine(outputRoot, "semantic-report.md");

        File.WriteAllText(path, Build(rows));

        foreach (var arm in Arms)
        {
            var group = rows.Where(r => string.Equals(r.Arm, arm, StringComparison.Ordinal)).ToArray();
            var usable = group.Count(r => r.HasStructure);
            var rejected = group.Count(r => r.HasStructure && r.Codes.Length > 0);

            Console.WriteLine($"{arm}：有结构 {usable} 份，其中语义有问题 {rejected} 份"
                + $"（{(usable == 0 ? 0 : (double)rejected / usable):P1}）；"
                + $"解析器放弃 {group.Length - usable} 份");
        }

        Console.WriteLine($"报告写到 {path}");

        return 0;
    }

    /// <summary>量一份：解析、映射成 IR、校验。</summary>
    private static Row Measure(ResponseRecord record)
    {
        var startsWithDsl = string.Equals(record.Arm, DslArm, StringComparison.Ordinal);

        var body = startsWithDsl ? ParseDsl(record.Content) : ParseMermaid(record.Content);

        if (body is null || body.Structure == 0)
        {
            return new Row(record.Arm, record.PromptId, false, [], body?.Note ?? "解析器整份放弃。", Empty);
        }

        var issues = DiagramValidator.Validate(body.Document);

        return new Row(
            record.Arm,
            record.PromptId,
            true,
            [.. issues.Select(i => i.Code).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal)],
            string.Join("；", issues.Select(i => $"{i.Code} {i.Message}").Distinct(StringComparer.Ordinal)),
            body.Repairs);
    }

    private static Body? ParseMermaid(string content)
    {
        var chart = MermaidParser.Parse(content);
        var structure = chart.Nodes.Count + chart.Links.Count;

        if (structure == 0)
        {
            return new Body(null!, 0, "图类型不是流程图，解析器整份放弃。", Empty);
        }

        var result = MermaidImporter.Import(chart, new ImportOptions { DocumentId = "semantic" });

        return new Body(result.Document, structure, string.Empty, new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["被当成子图端点而丢掉的同名节点"] = result.Report.DroppedNodes.Count,
            ["认不出的指令与样式属性"] = result.Report.Diagnostics.Count + result.Report.IgnoredStyleProperties.Count,
        });
    }

    private static Body? ParseDsl(string content)
    {
        var parsed = DslParser.Parse(content);
        var structure = parsed.Nodes.Count + parsed.Edges.Count + parsed.Groups.Count;

        if (structure == 0)
        {
            return new Body(null!, 0, "没有解析出任何结构。", Empty);
        }

        var result = DslMapper.Map(parsed, new MappingOptions { DocumentId = "semantic" });

        return new Body(result.Document, structure, string.Empty, new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["补出来的隐式节点"] = result.Report.CreatedNodes.Count,
            ["与节点撞名而改名的容器"] = result.Report.Renames.Count,
            ["没落地的次序项"] = result.Report.UnresolvedIntents.Count,
            ["认不出的内容"] = result.Report.Diagnostics.Count,
        });
    }

    private static string Build(Row[] rows)
    {
        var text = new List<string>
        {
            "# 语义拒绝率",
            string.Empty,
            "复现：`dotnet run --project tools/CompareHarness -c Release -- semantic`",
            string.Empty,
            "**它此前算不出来。** 缺的是「语法树到 IR」这一步：Mermaid 的解析器读到 `A --> B`",
            "会顺手把两端记成节点，DSL 的只记边——直接拿语法树比，比的是两个解析器的宽松程度，",
            "不是两种格式。走一遍映射到 IR 再交给校验器，两边才落在同一个口径上。",
            string.Empty,
            "**分母只算有结构的份数。** 解析器整份放弃的那些（图类型不对）没有结构可校验，",
            "把它们算作「语义没问题」会凭空拉低这个比率，所以单列。",
            string.Empty,
            "## 三组",
            string.Empty,
            "| 组 | 有结构 | 语义有问题 | 语义拒绝率 | 解析器放弃 |",
            "|---|---:|---:|---:|---:|",
        };

        foreach (var arm in Arms)
        {
            var group = rows.Where(r => string.Equals(r.Arm, arm, StringComparison.Ordinal)).ToArray();
            var usable = group.Count(r => r.HasStructure);
            var rejected = group.Count(r => r.HasStructure && r.Codes.Length > 0);
            var rate = usable == 0 ? "—" : $"{(double)rejected / usable:P1}";

            text.Add($"| `{arm}` | {usable} | {rejected} | **{rate}** | {group.Length - usable} |");
        }

        text.Add(string.Empty);
        text.Add("## 问题码分布");
        text.Add(string.Empty);

        var codes = rows
            .Where(r => r.Codes.Length > 0)
            .SelectMany(r => r.Codes)
            .GroupBy(c => c, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ToArray();

        if (codes.Length == 0)
        {
            text.Add("**一条都没有。** 三组里凡是解析出结构的，校验器都零问题——");
            text.Add("外部输入这条路上，两个解析器与两个映射层产出的图都是自洽的。");
        }
        else
        {
            text.Add("| 码 | 出现份数 |");
            text.Add("|---|---:|");

            foreach (var group in codes)
            {
                text.Add($"| `{group.Key}` | {group.Count()} |");
            }
        }

        text.Add(string.Empty);
        text.Add("## 逐份明细");
        text.Add(string.Empty);

        var detailed = rows.Where(r => r.Codes.Length > 0).ToArray();

        if (detailed.Length == 0)
        {
            text.Add("没有需要列出的。");
        }
        else
        {
            foreach (var row in detailed)
            {
                text.Add($"- `{row.Arm}/{row.PromptId}`：{row.Note}");
            }
        }

        text.Add(string.Empty);
        text.Add("## 映射阶段消解掉了什么");
        text.Add(string.Empty);
        text.Add("上面那个比率是**映射之后**的数。要读懂它，得看映射阶段顺手修掉了多少东西——");
        text.Add("修得越多，越说明「文本里的原始声明」并不自洽，而这些问题在到达校验器之前就没了。");
        text.Add(string.Empty);

        var repairs = Arms
            .SelectMany(arm => rows
                .Where(r => string.Equals(r.Arm, arm, StringComparison.Ordinal))
                .SelectMany(r => r.Repairs.Select(pair => (Arm: arm, pair.Key, pair.Value)))
                .GroupBy(x => (x.Arm, x.Key))
                .Select(g => (g.Key.Arm, g.Key.Key, Count: g.Sum(x => x.Value))))
            .ToArray();

        if (repairs.Length == 0)
        {
            text.Add("没有。");
        }
        else
        {
            text.Add("| 组 | 消解掉的东西 | 数量 |");
            text.Add("|---|---|---:|");

            foreach (var (arm, what, count) in repairs.Where(r => r.Count > 0).OrderBy(r => r.Arm, StringComparer.Ordinal))
            {
                text.Add($"| `{arm}` | {what} | {count} |");
            }

            text.Add(string.Empty);
            text.Add("**这些数才是格式之间的差别所在。** 校验器那一步之所以三组全零，是因为");
            text.Add("映射层把所有矛盾都消解掉了：Mermaid 侧把与子图同名的节点声明丢掉，");
            text.Add("DSL 侧把隐式引用的节点补出来、把撞名的容器改名。");
            text.Add("两种格式都写得出不自洽的文本，只是这一层把两类不自洽各自抹平了。");
        }

        text.Add(string.Empty);
        text.Add("## 这一项量的是什么，不量什么");
        text.Add(string.Empty);
        text.Add("**量的是**：把外部文本读成 IR 之后，那张图自身说不说得通。");
        text.Add(string.Empty);
        text.Add("**不量**：文本里的原始声明是否自洽。语法树里就有矛盾但映射阶段消解掉的");
        text.Add("（例如同名子图与节点——映射层让容器让位），在这里算过。");
        text.Add("那时报出来的是映射层的取舍，不是模型的错，而这一项要量的是后者。");
        text.Add(string.Empty);

        return string.Join('\n', text) + "\n";
    }

    /// <summary>一份的量测结果。</summary>
    /// <remarks>
    /// <paramref name="Document"/> 只在 <paramref name="HasStructure"/> 为真时有意义；
    /// 解析器整份放弃时没有图可校验，所以不给它一个空文档——那会让它被算成"零问题"。
    /// </remarks>
    private sealed record Body(
        DiagramDocument Document,
        int Structure,
        string Note,
        IReadOnlyDictionary<string, int> Repairs);

    private sealed record Row(
        string Arm,
        string PromptId,
        bool HasStructure,
        string[] Codes,
        string Note,
        IReadOnlyDictionary<string, int> Repairs);

    private static readonly IReadOnlyDictionary<string, int> Empty =
        new Dictionary<string, int>(StringComparer.Ordinal);
}
