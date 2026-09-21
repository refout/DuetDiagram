using System.Globalization;
using System.Text;

namespace DuetDiagram.Tools.CompareHarness;

/// <summary>
/// 人工评分与谓词的一致率。
/// </summary>
/// <remarks>
/// <para>
/// 谓词是照着 150 份语料调出来的，调得对不对，只有人工判读能核。这个命令读抽样评分文件，
/// 逐项把"机器怎么说"与"人怎么说"摆在一起，算出一致率并列出分歧。
/// </para>
/// <para>
/// 它同时把**谓词判不了的那一半**收上来：人工修正步数与"多出来的东西"。
/// 谓词只判该有的在不在，判不了多出来的该不该有——那两个指标一直缺的就是这部分数据。
/// </para>
/// <para>
/// 样本是 20 份一组，而判定门要的是 50 份一组，所以这里的差异**不足以过门**。
/// 它的定位是校准，不是判定。
/// </para>
/// </remarks>
internal static class Agreement
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public static int Run(string promptsPath, string corpusRoot, string outputRoot, string humanRoot)
    {
        var prompts = PromptSet.Load(promptsPath);
        var keyPath = Path.Combine(outputRoot, SampleKey.FileName);
        var key = SampleKey.Load(keyPath);

        // 检查项改过之后，评分文件里的"第 3 项"指的是另一句话。这种错不会让任何东西变红，
        // 只会把两组不同的判定按序号对齐，算出一个看起来正常的一致率。
        var checks = Digest.OfChecks(prompts);
        if (!string.Equals(checks, key.ChecksFingerprint, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"检查项已经改过：抽样时是 {key.ChecksFingerprint}，现在是 {checks}。");
            Console.Error.WriteLine("评分文件里的序号现在指着别的话，按序号对齐会把判定安到错的地方。");
            Console.Error.WriteLine("要么把检查项改回去，要么重新抽样、重新评分。");
            return 1;
        }

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

        var ratings = RatingsFile.LoadAll(humanRoot, key, prompts);

        if (ratings.Count == 0)
        {
            Console.Error.WriteLine($"还没人交评分。目录：{Display.Path(humanRoot)}");
            Console.Error.WriteLine($"复制 {Display.Path(Path.Combine(humanRoot, SampleKey.TemplateFileName))} 成 ratings-<评分者>.json 填好再来。");
            return 1;
        }

        // 语料里每份响应按"组 + 提示词"唯一，抽样对照表记的就是这两个字段。
        var byIdentity = Blind.Build(prompts, records)
            .ToDictionary(item => (item.Record.Arm, item.Prompt.Id));

        var sample = new List<SampleRow>(key.Entries.Count);

        foreach (var entry in key.Entries)
        {
            if (!byIdentity.TryGetValue((entry.Arm, entry.PromptId), out var item))
            {
                throw new InvalidDataException(
                    $"抽样清单里的 {entry.Arm}/{entry.PromptId} 在语料里找不到。语料换过之后，抽样清单就失效了。");
            }

            sample.Add(new SampleRow(entry, Judging.Run(item)));
        }

        var cells = BuildCells(sample, ratings);

        Directory.CreateDirectory(outputRoot);

        var report = Report(prompts, key, ratings, sample, cells, humanRoot, corpusRoot);

        File.WriteAllText(
            Path.Combine(outputRoot, "agreement.md"),
            report.Replace("\r\n", "\n", StringComparison.Ordinal),
            Utf8);

        Console.WriteLine(report);

        return 0;
    }

    #region 逐项摆在一起

    /// <summary>抽样清单里的一份，连同它的机器判定。</summary>
    private sealed record SampleRow(SampleEntry Entry, Judged Judged);

    /// <summary>
    /// 一个（条目 × 检查项）的格子。
    /// </summary>
    /// <param name="HumanPass">人判的结论。1 通过、0 不通过、null 未判。</param>
    /// <param name="MachinePass">机器判的结论。这一项没有谓词时无意义。</param>
    private sealed record Cell(
        string Anon,
        string Arm,
        string PromptId,
        int Index,
        string Text,
        bool Machine,
        bool MachinePass,
        int? HumanPass,
        string? Reason);

    private sealed record RaterCells(RatingsFile File, Cell[] Cells)
    {
        /// <summary>有谓词、且人判了的那部分。一致率的分母就是它。</summary>
        public Cell[] Comparable => [.. Cells.Where(cell => cell.Machine && cell.HumanPass is not null)];

        public int JudgedItems => Cells
            .GroupBy(cell => cell.Anon, StringComparer.Ordinal)
            .Count(group => group.All(cell => cell.HumanPass is not null));
    }

    private static RaterCells[] BuildCells(IReadOnlyList<SampleRow> sample, IReadOnlyList<RatingsFile> ratings) =>
    [
        .. ratings.Select(file => new RaterCells(file, BuildFor(sample, file))),
    ];

    private static Cell[] BuildFor(IReadOnlyList<SampleRow> sample, RatingsFile file)
    {
        var cells = new List<Cell>();

        foreach (var row in sample)
        {
            var rating = file.Items.GetValueOrDefault(row.Entry.Anon);
            var verdicts = row.Judged.Checks;

            for (var i = 0; i < verdicts.Length; i++)
            {
                cells.Add(new Cell(
                    row.Entry.Anon,
                    row.Entry.Arm,
                    row.Entry.PromptId,
                    i,
                    verdicts[i].Text,
                    verdicts[i].Machine,
                    verdicts[i].Pass,
                    rating?.Checks[i],
                    verdicts[i].Reason));
            }
        }

        return [.. cells];
    }

    #endregion

    #region 报告

    private static string Report(
        PromptSet prompts,
        SampleKey key,
        IReadOnlyList<RatingsFile> ratings,
        IReadOnlyList<SampleRow> sample,
        RaterCells[] cells,
        string humanRoot,
        string corpusRoot)
    {
        var text = new StringBuilder();
        var machine = prompts.SelectMany(prompt => prompt.Checks).Count(check => check.MachineCheckable);
        var all = prompts.SelectMany(prompt => prompt.Checks).Count();

        text.AppendLine("# 谓词与人工评分的一致率");
        text.AppendLine();
        text.AppendLine("生成命令：`dotnet run --project tools/CompareHarness -c Release -- agreement`");
        text.AppendLine();
        text.AppendLine($"输入是 `{Display.Path(humanRoot)}` 下的评分文件与 `{SampleKey.FileName}` 记的抽样清单，");
        text.AppendLine($"语料来自 `{Display.Path(corpusRoot)}`。机器那一侧判的是**结构清单**——评分者看到的那一份。");
        text.AppendLine();
        text.AppendLine("## 一句话");
        text.AppendLine();

        var comparable = cells.SelectMany(rater => rater.Comparable).ToArray();
        var agreed = comparable.Count(cell => cell.HumanPass == (cell.MachinePass ? 1 : 0));
        var raters = string.Join('、', ratings.Select(file => file.Rater));

        text.AppendLine($"评分者：{raters}。逐项一致率 {Percent(agreed, comparable.Length)}"
            + $"（{agreed}/{comparable.Length} 个「有谓词且人判了」的格子）。");
        text.AppendLine();
        text.AppendLine($"样本 {sample.Count} 份，每组最多 {key.PerArm} 份。**它不够用来过判定门**——");
        text.AppendLine("门要的是每组 50 份，而且这一批是「谓词判不通过的优先收」抽出来的，");
        text.AppendLine("不是随机样本，所以这里的分组对比没有代表性。它的用途只有一个：看谓词哪里写错了。");
        text.AppendLine();
        text.AppendLine($"清单编号：{key.Fingerprint}　检查项：{key.ChecksFingerprint}");
        text.AppendLine();
        text.AppendLine("评分文件的指纹（内容一变，指纹就变）：");
        text.AppendLine();
        text.AppendLine("| 评分者 | 文件 | 指纹 |");
        text.AppendLine("|---|---|---|");

        foreach (var file in ratings)
        {
            text.AppendLine($"| {file.Rater} | `{Path.GetFileName(file.Path)}` | `{file.Fingerprint}` |");
        }

        text.AppendLine();
        text.AppendLine("## 覆盖");
        text.AppendLine();
        text.AppendLine("未判的格子不进分母。判到一半就收工是允许的，但覆盖多少必须写出来——");
        text.AppendLine("只看一致率而不知道它建立在几个格子上，读不出这个数可不可信。");
        text.AppendLine();
        text.AppendLine("| 评分者 | 逐项判完的份数 | 逐项已判 | 逐项未判 | 修正步数已填 |");
        text.AppendLine("|---|---:|---:|---:|---:|");

        foreach (var rater in cells)
        {
            var total = rater.Cells.Length;
            var judged = rater.Cells.Count(cell => cell.HumanPass is not null);
            var steps = rater.File.Items.Values.Count(rating => rating.Steps is not null);

            text.AppendLine($"| {rater.File.Rater} | {rater.JudgedItems}/{sample.Count} | {judged}/{total} "
                + $"| {total - judged} | {steps}/{sample.Count} |");
        }

        text.AppendLine();
        text.AppendLine("## 逐项一致率");
        text.AppendLine();
        text.AppendLine("分母只有「有谓词且人判了」的格子：没有谓词的项机器本来就判不了，未判的项人不算数。");
        text.AppendLine();
        text.AppendLine("| 评分者 | 一致 | 可比 | 一致率 |");
        text.AppendLine("|---|---:|---:|---:|");

        foreach (var rater in cells)
        {
            var pairs = rater.Comparable;
            var same = pairs.Count(cell => cell.HumanPass == (cell.MachinePass ? 1 : 0));

            text.AppendLine($"| {rater.File.Rater} | {same} | {pairs.Length} | {Percent(same, pairs.Length)} |");
        }

        text.AppendLine();
        text.AppendLine("## 端到端一致率");
        text.AppendLine();
        text.AppendLine("一份的结论取「这一份里所有**有谓词**的项」，两边都用这一个口径，才是同一件事。");
        text.AppendLine("人那一侧若还判了没有谓词的项，那些项不进这张表——机器没有对应的判定可对。");
        text.AppendLine("**只判了一半的份不进这张表**：拿一部分项去说「这一份全过」会把它算成通过，");
        text.AppendLine("而缺的那几项恰好可能就是不过的。这种份算进分母只会让一致率虚高。");
        text.AppendLine();
        text.AppendLine("| 评分者 | 一致 | 可比份数 | 一致率 |");
        text.AppendLine("|---|---:|---:|---:|");

        foreach (var rater in cells)
        {
            var byItem = rater.Cells
                .Where(cell => cell.Machine)
                .GroupBy(cell => cell.Anon, StringComparer.Ordinal)
                .Where(group => group.Any() && group.All(cell => cell.HumanPass is not null))
                .ToArray();

            var same = byItem.Count(group =>
                group.All(cell => cell.MachinePass) == group.All(cell => cell.HumanPass == 1));

            text.AppendLine($"| {rater.File.Rater} | {same} | {byItem.Length} | {Percent(same, byItem.Length)} |");
        }

        text.AppendLine();
        Disagreements(text, cells);
        HumanOnly(text, ratings, sample);
        InterRater(text, cells);
        Caveats(text, key, all, machine);

        return text.ToString();
    }

    private static void Disagreements(StringBuilder text, RaterCells[] cells)
    {
        text.AppendLine("## 分歧逐条");
        text.AppendLine();
        text.AppendLine("**这一节是这份报告真正有用的部分。** 一致率是个摘要，要改的是这些具体的项。");
        text.AppendLine();
        text.AppendLine("「谓词判成过、人判不过」说明判据太松；反过来说明太严。");
        text.AppendLine("两种都要看一眼：太松的会让指标虚高，太严的会把好结果压下去，看起来像模型不行。");
        text.AppendLine();

        var rows = cells
            .SelectMany(rater => rater.Comparable
                .Where(cell => cell.HumanPass != (cell.MachinePass ? 1 : 0))
                .Select(cell => (Rater: rater.File.Rater, Cell: cell)))
            .ToArray();

        if (rows.Length == 0)
        {
            text.AppendLine("没有。");
            text.AppendLine();
            return;
        }

        text.AppendLine("| 评分者 | 编号 | 组 | 提示词 | 第几项 | 检查项 | 谓词 | 人 | 谓词给的理由 |");
        text.AppendLine("|---|---|---|---|---:|---|---|---|---|");

        foreach (var (rater, cell) in rows.OrderBy(row => row.Cell.PromptId, StringComparer.Ordinal)
                     .ThenBy(row => row.Cell.Index))
        {
            text.AppendLine($"| {rater} | {cell.Anon} | `{cell.Arm}` | {cell.PromptId} | {cell.Index + 1} "
                + $"| {Shorten(cell.Text)} | {(cell.MachinePass ? "过" : "不过")} "
                + $"| {(cell.HumanPass == 1 ? "过" : "不过")} | {cell.Reason ?? "—"} |");
        }

        text.AppendLine();
        text.AppendLine("### 分歧最多的检查项");
        text.AppendLine();
        text.AppendLine("同一条检查项在多份上反复分歧，多半是这一条本身写得含糊，而不是某一份生成得差。");
        text.AppendLine();
        text.AppendLine("| 提示词 | 第几项 | 检查项 | 分歧 | 可比 |");
        text.AppendLine("|---|---:|---|---:|---:|");

        var byCheck = cells
            .SelectMany(rater => rater.Comparable)
            .GroupBy(cell => (cell.PromptId, cell.Index))
            .Select(group => (
                group.Key.PromptId,
                group.Key.Index,
                Text: group.First().Text,
                Total: group.Count(),
                Bad: group.Count(cell => cell.HumanPass != (cell.MachinePass ? 1 : 0))))
            .Where(row => row.Bad > 0)
            .OrderByDescending(row => row.Bad)
            .ThenBy(row => row.PromptId, StringComparer.Ordinal)
            .ThenBy(row => row.Index)
            .ToArray();

        foreach (var row in byCheck)
        {
            text.AppendLine($"| {row.PromptId} | {row.Index + 1} | {Shorten(row.Text)} | {row.Bad} | {row.Total} |");
        }

        text.AppendLine();
    }

    /// <summary>
    /// 人判出来的、谓词看不见的那一半。
    /// </summary>
    /// <remarks>
    /// 这一节是这次校准的另一个产出：判定门里"首轮通过率"与"人工修正步骤"两项，
    /// 谓词永远算不了，只有人工评分能给。它们在这里第一次有了数。
    /// </remarks>
    private static void HumanOnly(StringBuilder text, IReadOnlyList<RatingsFile> ratings, IReadOnlyList<SampleRow> sample)
    {
        text.AppendLine("## 人判出来的、谓词看不见的东西");
        text.AppendLine();
        text.AppendLine("这一节与谓词无关，是人工评分的原始汇总。判定门里「首轮通过率」与「人工修正步骤」");
        text.AppendLine("两项只能这么算——谓词判的是「该有的在不在」，而这两项问的是「多出来的该不该有」。");
        text.AppendLine();
        text.AppendLine("**样本是分层抽的，不是随机样本，所以这里的分组对比不能当过门的依据。**");
        text.AppendLine("它只说明这一批人判下来是什么样。");
        text.AppendLine();
        text.AppendLine("| 评分者 | 组 | 份数 | 首轮通过（步数 0） | 首轮通过率 | 平均修正步数 | 提到多余东西 |");
        text.AppendLine("|---|---|---:|---:|---:|---:|---:|");

        foreach (var file in ratings)
        {
            foreach (var arm in Structure.Arms)
            {
                var rows = sample
                    .Where(row => string.Equals(row.Entry.Arm, arm, StringComparison.Ordinal))
                    .Select(row => (row.Entry.Anon, Rating: file.Items.GetValueOrDefault(row.Entry.Anon)))
                    .Where(row => row.Rating is not null)
                    .ToArray();

                var steps = rows.Where(row => row.Rating!.Steps is not null).Select(row => row.Rating!.Steps!.Value).ToArray();
                var zero = steps.Count(value => value == 0);
                var extra = rows.Count(row => !string.IsNullOrWhiteSpace(row.Rating!.Extra));

                text.AppendLine($"| {file.Rater} | `{arm}` | {rows.Length} | {zero} "
                    + $"| {Percent(zero, steps.Length)} "
                    + $"| {(steps.Length == 0 ? "—" : steps.Average().ToString("F2", CultureInfo.InvariantCulture))} "
                    + $"| {extra} |");
            }
        }

        text.AppendLine();

        var extras = ratings
            .SelectMany(file => file.Items
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.Extra))
                .Select(pair => (file.Rater, Anon: pair.Key, pair.Value.Extra)))
            .ToArray();

        text.AppendLine("### 被指出来的多余东西");
        text.AppendLine();
        text.AppendLine("这是谓词结构上判不了的那一类问题。它现在是自由文本，还没有进任何指标——");
        text.AppendLine("先把它记下来，看清它长什么样，再决定要不要给它一个口径。");
        text.AppendLine();

        if (extras.Length == 0)
        {
            text.AppendLine("没有。");
        }
        else
        {
            text.AppendLine("| 评分者 | 编号 | 多出来的东西 |");
            text.AppendLine("|---|---|---|");

            foreach (var (rater, anon, extra) in extras)
            {
                text.AppendLine($"| {rater} | {anon} | {extra} |");
            }
        }

        text.AppendLine();
    }

    /// <summary>
    /// 评分者之间的一致程度。
    /// </summary>
    /// <remarks>
    /// 只有一位评分者时这一节跳过：一个人跟自己没法比，写一张空的表只会让人以为算过。
    /// </remarks>
    private static void InterRater(StringBuilder text, RaterCells[] cells)
    {
        if (cells.Length < 2)
        {
            text.AppendLine("## 评分者之间");
            text.AppendLine();
            text.AppendLine($"只有 {cells.Length} 位评分者，无从比较。");
            text.AppendLine("评分者之间对不上，说明检查项写得让人读不准，那要先改检查项，而不是先信一致率。");
            text.AppendLine();
            return;
        }

        text.AppendLine("## 评分者之间");
        text.AppendLine();
        text.AppendLine("只比两人都判了的格子。对不上说明检查项写得让人读不准——");
        text.AppendLine("那要先改检查项，因为谓词是照着一句话写的，那句话本身含糊，谓词也就无从写准。");
        text.AppendLine();
        text.AppendLine("| 评分者 | 评分者 | 一致 | 都判了 | 一致率 |");
        text.AppendLine("|---|---|---:|---:|---:|");

        for (var i = 0; i < cells.Length; i++)
        {
            for (var j = i + 1; j < cells.Length; j++)
            {
                var left = cells[i].Cells.ToDictionary(cell => (cell.Anon, cell.Index));
                var pairs = cells[j].Cells
                    .Where(cell => cell.HumanPass is not null
                        && left.TryGetValue((cell.Anon, cell.Index), out var other)
                        && other.HumanPass is not null)
                    .Select(cell => (Left: left[(cell.Anon, cell.Index)].HumanPass, Right: cell.HumanPass))
                    .ToArray();

                var same = pairs.Count(pair => pair.Left == pair.Right);

                text.AppendLine($"| {cells[i].File.Rater} | {cells[j].File.Rater} | {same} | {pairs.Length} "
                    + $"| {Percent(same, pairs.Length)} |");
            }
        }

        text.AppendLine();
    }

    private static void Caveats(StringBuilder text, SampleKey key, int all, int machine)
    {
        text.AppendLine("## 这份数字能说什么，不能说什么");
        text.AppendLine();
        text.AppendLine("**能说：谓词写错了哪些地方。** 分歧逐条就是清单。改完谓词重跑 `score`，");
        text.AppendLine("那份回归网才算有依据——在那之前，它是一张没跟人核过的网。");
        text.AppendLine();
        text.AppendLine("**不能说：哪个格式更好。** 三个理由，缺一不可：");
        text.AppendLine();
        text.AppendLine($"- 样本每组只有 {key.PerArm} 份，而判定门要的是每组 50 份。"
            + "差 1 份就是 5 个百分点，比阈值本身还粗。");
        text.AppendLine("- 抽样是分层的，不是随机的：谓词判不通过的优先收。分组之间的对比因此不成立。");

        if (all > machine)
        {
            text.AppendLine($"- 全部提示词里一共 {all} 项检查项，其中 {machine} 项有谓词、{all - machine} 项没有。");
            text.AppendLine("  没有谓词的那些人判了、机器判不了，进不了这张表的任何一格。");
        }
        else
        {
            text.AppendLine($"- 全部 {all} 项检查项都有谓词，所以这张表没有\"人判了而机器判不了\"的缺口。");
        }
        text.AppendLine();
        text.AppendLine("**一致率高不等于谓词对。** 样本偏向谓词判不通过的地方，");
        text.AppendLine("「谓词把错的判成对」那种错抽不进来——补位进来的那些按定义全是谓词全过。");
        text.AppendLine("所以这一节读的是「分歧在哪」，不是「一致率有多高」。");
        text.AppendLine();
        text.AppendLine("**人工修正步数是这一轮唯一的新数据。** 判定门的四项里，");
        text.AppendLine("解析错误率早就能算，端到端准确率的谓词口径贴了顶，");
        text.AppendLine("剩下两项一直卡在「没有人工评分」上——现在这一批给了它们一个起点，");
        text.AppendLine("虽然样本太小，还不足以支撑判定门那一步。");
    }

    private static string Percent(int part, int whole) =>
        whole == 0 ? "—" : ((double)part / whole).ToString("P1", CultureInfo.InvariantCulture);

    private static string Shorten(string text) =>
        text.Length <= 46 ? text : text[..45] + "…";

    #endregion
}
