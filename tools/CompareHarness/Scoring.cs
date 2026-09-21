using System.Globalization;
using System.Text;

namespace DuetDiagram.Tools.CompareHarness;

/// <summary>
/// 谓词口径的评分：把 150 份语料逐条过一遍检查项的谓词。
/// </summary>
/// <remarks>
/// <para>
/// 检查项原本只能人判，所以四个指标里三个要人评，而"换模型要重新跑"是既定需求——
/// 人评的成本让那条需求落不了地。谓词把其中能判的那部分接过来。
/// </para>
/// <para>
/// <b>它接过来的是哪一部分，必须说清楚。</b>谓词判的是"该有的在不在"，判不了
/// "多出来的该不该有"。所以：
/// </para>
/// <list type="bullet">
/// <item>端到端准确率与平均检查项通过率——能算。</item>
/// <item>首轮通过率与人工修正步骤——<b>算不了</b>。它们问的是"这份图还需不需要改"，
/// 而"不需要改"包含"没有多余的东西"，那不是任何一条检查项描述过的内容。</item>
/// </list>
/// <para>
/// 报告里如实写出这一点。声称四个指标都能自动算，等于把两个不知道的数当成知道的。
/// </para>
/// </remarks>
internal static class Scoring
{
    public static int Run(string promptsPath, string corpusRoot, string outputRoot)
    {
        var prompts = PromptSet.Load(promptsPath);
        var byId = prompts.ToDictionary(prompt => prompt.Id, StringComparer.Ordinal);
        var records = Corpus.Load(corpusRoot, Structure.Arms);
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

        var judged = records.Select(record => Judge(record, byId[record.PromptId])).ToArray();

        Directory.CreateDirectory(outputRoot);

        var report = Report(judged, prompts);

        File.WriteAllText(
            Path.Combine(outputRoot, "predicate-report.md"),
            report.Replace("\r\n", "\n", StringComparison.Ordinal),
            new UTF8Encoding(false));

        Console.WriteLine(report);

        return 0;
    }

    #region 逐份判定

    /// <summary>一条检查项在一份语料上的判定。</summary>
    /// <param name="Index">在提示词的检查项列表里的序号，从 0 数。</param>
    /// <param name="Text">检查项原文。</param>
    /// <param name="Machine">这一项有没有谓词。</param>
    /// <param name="Pass">通过与否。没有谓词时恒为假，报告里不把它算进分母。</param>
    /// <param name="Reason">不通过的原因，给人复核用。</param>
    private sealed record CheckVerdict(int Index, string Text, bool Machine, bool Pass, string? Reason);

    /// <summary>一份语料的判定。</summary>
    private sealed record Judged(
        ResponseRecord Record,
        Prompt Prompt,
        StructureListing Listing,
        CheckVerdict[] Checks)
    {
        public bool Parsed => !Structure.IsRejected(Listing.Outcome);

        public int MachineCount => Checks.Count(check => check.Machine);

        public int MachinePassed => Checks.Count(check => check.Machine && check.Pass);

        /// <summary>有谓词的项全过。没有谓词的项不参与——它们的结论要等人工。</summary>
        public bool AllMachinePassed => MachineCount > 0 && MachinePassed == MachineCount;
    }

    private static Judged Judge(ResponseRecord record, Prompt prompt)
    {
        var parsed = Structure.Parse(record.Arm, record.Content);

        // 判的是**评分者看到的那一份**：标识已换成流水号、换行标记已抹平。
        // 这样机器判定与人工判定看的是同一件东西，将来拿人工评分来校谓词才对得上。
        var listing = Listings.Sanitize(parsed);
        var index = new StructureIndex(listing);
        var reject = Structure.IsRejected(listing.Outcome);
        var verdicts = new CheckVerdict[prompt.Checks.Length];

        for (var i = 0; i < prompt.Checks.Length; i++)
        {
            var check = prompt.Checks[i];

            if (check.When is null)
            {
                verdicts[i] = new CheckVerdict(i, check.Text, false, false, null);
            }
            else if (reject)
            {
                // 解析失败必然不通过，且不能从分母里剔除。这是判定口径定死的。
                verdicts[i] = new CheckVerdict(i, check.Text, true, false, "解析器没给出结构，必然不通过。");
            }
            else
            {
                var verdict = check.When.Evaluate(index);
                verdicts[i] = new CheckVerdict(i, check.Text, true, verdict.Pass, verdict.Reason);
            }
        }

        return new Judged(record, prompt, listing, verdicts);
    }

    #endregion

    #region 报告

    private static string Report(IReadOnlyList<Judged> judged, PromptSet prompts)
    {
        var text = new StringBuilder();
        var checks = prompts.SelectMany(prompt => prompt.Checks).ToArray();
        var machine = checks.Count(check => check.MachineCheckable);
        var human = checks.Length - machine;

        text.AppendLine("# 谓词口径下的端到端准确率");
        text.AppendLine();
        text.AppendLine("生成命令：`dotnet run --project tools/CompareHarness -c Release -- score`");
        text.AppendLine();
        text.AppendLine($"输入是 `reports/raw` 的 {judged.Count} 份冻结语料，判据是 `tools/CompareHarness/prompts.json` 里");
        text.AppendLine("写在检查项上的谓词。判的对象是**结构清单**——评分者看到的那一份，不是原始文本，也不是 IR。");
        text.AppendLine();
        text.AppendLine("## 这一份能算什么，不能算什么");
        text.AppendLine();
        text.AppendLine($"检查项共 {checks.Length} 项：**{machine} 项有谓词，{human} 项没有**。");
        text.AppendLine();
        text.AppendLine("| 指标 | 能不能这么算 | 为什么 |");
        text.AppendLine("|---|---|---|");
        text.AppendLine("| 解析错误率 | 能 | 不需要检查项，`parse-report.md` 已经在算 |");
        text.AppendLine("| 端到端准确率 | 能（谓词口径） | 全部**有谓词的**检查项通过 |");
        text.AppendLine("| 平均检查项通过率 | 能（只覆盖有谓词的那部分） | 逐项 0/1 后取均值 |");
        text.AppendLine("| 首轮通过率 | **不能** | 它问的是「还需不需要改」。谓词只判该有的在不在，判不了多出来的该不该有 |");
        text.AppendLine("| 人工修正步骤 | **不能** | 同上：删一个多余的节点算 1 步，而「多余」不在任何检查项的描述范围里 |");
        text.AppendLine();
        text.AppendLine("所以**四个指标里两个可以自动重算**（解析错误率、端到端准确率），");
        text.AppendLine("另外两个仍然要人。这一点与改写之前的预期不同：原先以为三个指标都能接过来，");
        text.AppendLine("实际是谓词天然判不了「多余」，而首轮通过率与修正步骤恰好都建立在「多余」之上。");
        text.AppendLine();
        text.AppendLine("## 三组");
        text.AppendLine();
        text.AppendLine("| 组 | 份数 | 解析失败 | 有谓词的项全过 | 端到端准确率（谓词口径） | 平均检查项通过率 |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|");

        var byArm = new Dictionary<string, Judged[]>(StringComparer.Ordinal);

        foreach (var arm in Structure.Arms)
        {
            var rows = judged.Where(row => string.Equals(row.Record.Arm, arm, StringComparison.Ordinal)).ToArray();
            byArm[arm] = rows;

            var failed = rows.Count(row => !row.Parsed);
            var passed = rows.Count(row => row.Parsed && row.AllMachinePassed);
            var rate = rows.Length == 0 ? 0 : rows.Average(row => row.MachineCount == 0 ? 0 : (double)row.MachinePassed / row.MachineCount);

            text.AppendLine(
                $"| `{arm}` | {rows.Length} | {failed} | {passed} | {Percent(passed, rows.Length)} | {rate.ToString("P1", CultureInfo.InvariantCulture)} |");
        }

        text.AppendLine();

        var failures = FailureRows(judged, prompts);
        var b = byArm["b-documented"];
        var c = byArm["c-dsl"];
        var bPassed = b.Count(row => row.Parsed && row.AllMachinePassed);
        var cPassed = c.Count(row => row.Parsed && row.AllMachinePassed);
        var gap = (double)cPassed / c.Length - (double)bPassed / b.Length;

        text.AppendLine($"判定门第一条比的是 C 与 B（不是与 A）：C 高出 **{gap.ToString("P1", CultureInfo.InvariantCulture)}**，");
        text.AppendLine("达标线是 15 个百分点。**这一条在这里不可能达标，原因不是 C 差。**");
        text.AppendLine($"B 组 {b.Length} 份里 {bPassed} 份全过——对照组已经贴在天花板上，");
        text.AppendLine("再怎么好也高不出 15 个百分点。谓词口径只判「该有的在不在」，");
        text.AppendLine("而给足语法文档之后，该有的东西两组都写全了。");
        text.AppendLine();
        text.AppendLine("## 这份数字能分辨出什么");
        text.AppendLine();

        var machineChecks = checks.Count(check => check.MachineCheckable);
        var discriminating = failures.Count;

        text.AppendLine($"有谓词的检查项 {machineChecks} 个，其中**只有 {discriminating} 个在三组 150 份上不是全过**。");
        text.AppendLine($"剩下 {machineChecks - discriminating} 个（{(1 - (double)discriminating / machineChecks).ToString("P0", CultureInfo.InvariantCulture)}）");
        text.AppendLine("在任何一份上都是通过——它们没有区分任何东西。");
        text.AppendLine();
        text.AppendLine("这不是谓词写坏了。检查项绝大多数是「有 X 节点」「X 通向 Y」这类");
        text.AppendLine("存在性断言，而给了语法文档之后这类断言基本都成立。");
        text.AppendLine("**存在性断言天然分不出「写对了」和「写得刚刚好」**，");
        text.AppendLine("而后者才是这三个格式真正的差别所在。");
        text.AppendLine();
        text.AppendLine("所以这份数字的定位要改：**它不是判定门的答案，它是一张回归网。**");
        text.AppendLine("换了模型、改了提示词、动了语法之后重跑，它能把「该有的东西丢了」当场抓出来；");
        text.AppendLine("它抓不出「多了不该有的东西」，也不该被拿来宣布哪个格式更好。");
        text.AppendLine();
        text.AppendLine("## 逐检查项");
        text.AppendLine();
        text.AppendLine("只列至少一组有失败的项。数字是该组 {0} 份里通过的份数。".Replace("{0}", b.Length.ToString(CultureInfo.InvariantCulture)));
        text.AppendLine();
        // 提示词一列不能省：同一句话可能出现在多条提示词里，没有它就没法回到
        // prompts.json 去核这一项到底该判什么。
        text.AppendLine("| 提示词 | 第几项 | 检查项 | " + string.Join(" | ", Structure.Arms.Select(arm => $"`{arm}`")) + " |");
        text.AppendLine("|---|---:|---|---:|---:|---:|");

        foreach (var row in failures)
        {
            text.AppendLine(
                $"| {row.PromptId} | {row.Index + 1} | {Shorten(row.Text)} | "
                + string.Join(" | ", Structure.Arms.Select(arm => row.Pass[arm].ToString(CultureInfo.InvariantCulture))) + " |");
        }

        if (failures.Count == 0)
        {
            text.AppendLine("| 没有。 | | | | | |");
        }

        text.AppendLine();
        text.AppendLine("## 没有任何一份通过的检查项");
        text.AppendLine();
        text.AppendLine("这些项要么提示词里的事做不到，要么谓词写错了。两种都得看一眼——");
        text.AppendLine("静默留着的后果是它们一直在扣分，而扣的是每一组的分，看起来像是模型不行。");
        text.AppendLine();

        var never = failures.Where(row => Structure.Arms.All(arm => row.Pass[arm] == 0)).ToArray();

        if (never.Length == 0)
        {
            text.AppendLine("没有。");
        }
        else
        {
            foreach (var row in never)
            {
                text.AppendLine($"- **{row.PromptId} 第 {row.Index + 1} 项**　{row.Text}");
                text.AppendLine($"  - {row.SampleReason ?? "（没有原因）"}");
            }
        }

        text.AppendLine();
        text.AppendLine("## 判不了机器的检查项");
        text.AppendLine();

        if (human == 0)
        {
            text.AppendLine("没有。");
        }
        else
        {
            text.AppendLine("| 提示词 | 检查项 | 为什么判不了 |");
            text.AppendLine("|---|---|---|");

            foreach (var prompt in prompts)
            {
                foreach (var check in prompt.Checks.Where(check => !check.MachineCheckable))
                {
                    text.AppendLine($"| {prompt.Id} | {Shorten(check.Text)} | {check.Human} |");
                }
            }
        }

        text.AppendLine();
        text.AppendLine("## 改写过文本的检查项");
        text.AppendLine();
        text.AppendLine("这些项的原文判不了机器，或与另一项重复，所以把话改写成了能判的形式。");
        text.AppendLine("改写本身是判断，列在这里好让人逐条说「我不这么读」。");
        text.AppendLine();

        var rewritten = prompts
            .SelectMany(prompt => prompt.Checks.Where(check => !string.IsNullOrEmpty(check.Note)).Select(check => (prompt.Id, check)))
            .ToArray();

        if (rewritten.Length == 0)
        {
            text.AppendLine("没有。");
        }
        else
        {
            text.AppendLine("| 提示词 | 现在的写法 | 为什么改 |");
            text.AppendLine("|---|---|---|");

            foreach (var (id, check) in rewritten)
            {
                text.AppendLine($"| {id} | {Shorten(check.Text)} | {check.Note} |");
            }
        }

        text.AppendLine();
        text.AppendLine("## 谓词是怎么写出来的，代价是什么");
        text.AppendLine();
        text.AppendLine("**先有语料，后有谓词。** 写谓词的过程是：拿 150 份的结构清单逐条对，");
        text.AppendLine("凡是结构上明明做到了、而谓词判成没做到的地方，就把谓词放宽——");
        text.AppendLine("补同义词、把「有边」改成「可达」、允许把两件事写进一个节点的标签里。");
        text.AppendLine("每一条放宽都记在上面「改写过文本的检查项」里，可以逐条反驳。");
        text.AppendLine();
        text.AppendLine("**代价必须说清楚：这份数字不是独立测量。** 它可复现——同一份语料、同一版谓词，");
        text.AppendLine("跑多少次都是这个数——但它是「对着答案调过判据之后」的可复现。");
        text.AppendLine("谓词与人的判读是否一致，仍然要有人工评分才能校。");
        text.AppendLine();
        text.AppendLine("三条已知的偏向，这一轮里都出现过：");
        text.AppendLine();
        text.AppendLine("| 偏向 | 表现 | 后果 |");
        text.AppendLine("|---|---|---|");
        text.AppendLine("| 放宽到只认「意图达成」 | 「有边」改成「可达」、「两个节点」改成「一个或两个」 | 判据比检查项原文宽，机器比人松 |");
        text.AppendLine("| 一次只放宽一处，但放宽了十几处 | 累计下来 B 组从 90% 涨到 100% | 差距被抹平，指标贴顶 |");
        text.AppendLine("| 检查项本身可能写错 | S04 的「无判定分支」与它自己的提示词冲突 | 已在检查项那一层删掉，不是放宽谓词 |");
        text.AppendLine();
        text.AppendLine("第三条是这一轮唯一一处**动了检查项本身**的地方。一条三组都做不到的检查项");
        text.AppendLine("不区分任何东西，只会把三组的分一起压低——那不是模型不行，是检查项写错了。");
        text.AppendLine();
        text.AppendLine("## 怎么读这份报告");
        text.AppendLine();
        text.AppendLine("- **它是可复现的**：同一份语料、同一版谓词，跑多少次都是这个数。");
        text.AppendLine("- **它对着答案调过判据**，所以不是独立测量。见上一节。");
        text.AppendLine("- **它不是判定门的答案**。判定门要四项里至少三项达标，而这里只有一项能算，");
        text.AppendLine("  且那一项上对照组已经贴顶。拿它去替 `conclusion.md` 是错的。");
        text.AppendLine("- **它没有跟人工评分对过**。谓词与人的判读是否一致，要有人工评分才能校。");
        text.AppendLine("  在那之前，这份数字的定位是「可自动重算的那一半」，不是「人工评分的替代品」。");
        text.AppendLine("- 要复核某一条为什么判错，看 `reports/compare-blind/items.json`——");
        text.AppendLine("  里面有同一份结构清单，谓词判的就是它。");
        text.AppendLine("- 求值器本身有自检：`dotnet run --project tools/CompareHarness -c Release -- verify`。");

        return text.ToString();
    }

    /// <summary>一条检查项在三组上的通过份数。</summary>
    private sealed record FailureRow(
        string PromptId,
        int Index,
        string Text,
        Dictionary<string, int> Pass,
        string? SampleReason);

    private static List<FailureRow> FailureRows(IReadOnlyList<Judged> judged, PromptSet prompts)
    {
        // 按「提示词 × 组」分堆。只按组分是不行的：不同提示词的检查项条数不一样，
        // 拿一个提示词的序号去索引另一条提示词的判定结果会越界。
        var rows = new List<FailureRow>();

        foreach (var prompt in prompts)
        {
            var byArm = Structure.Arms.ToDictionary(
                arm => arm,
                arm => judged
                    .Where(row => string.Equals(row.Record.Arm, arm, StringComparison.Ordinal)
                        && string.Equals(row.Record.PromptId, prompt.Id, StringComparison.Ordinal))
                    .ToArray(),
                StringComparer.Ordinal);

            for (var i = 0; i < prompt.Checks.Length; i++)
            {
                if (!prompt.Checks[i].MachineCheckable)
                {
                    continue;
                }

                var pass = Structure.Arms.ToDictionary(
                    arm => arm,
                    arm => byArm[arm].Count(row => row.Checks[i].Pass),
                    StringComparer.Ordinal);

                if (Structure.Arms.All(arm => pass[arm] == byArm[arm].Length))
                {
                    continue;
                }

                // 原因要带上组别。同一项在三组上的失败原因可能完全不同，
                // 不标组别的话读的人会以为那一句说的是所有组。
                var reason = Structure.Arms
                    .SelectMany(arm => byArm[arm].Select(row => (Arm: arm, Row: row)))
                    .Where(pair => !pair.Row.Checks[i].Pass)
                    .Select(pair => pair.Row.Checks[i].Reason is { Length: > 0 } text ? $"[{pair.Arm}] {text}" : null)
                    .FirstOrDefault(text => text is not null);

                rows.Add(new FailureRow(prompt.Id, i, prompt.Checks[i].Text, pass, reason));
            }
        }

        return
        [
            .. rows.OrderByDescending(row => Structure.Arms.Sum(arm => 50 - row.Pass[arm]))
                .ThenBy(row => row.PromptId, StringComparer.Ordinal)
                .ThenBy(row => row.Index),
        ];
    }

    private static string Percent(int part, int whole) =>
        whole == 0 ? "—" : ((double)part / whole).ToString("P1", CultureInfo.InvariantCulture);

    /// <summary>表格里的检查项原文。太长会把表撑散。</summary>
    private static string Shorten(string text) =>
        text.Length <= 46 ? text : text[..45] + "…";

    #endregion
}
