using System.Globalization;
using System.Text;

namespace DuetDiagram.Tools.UserStudy;

/// <summary>四条判定里的一条。</summary>
/// <param name="Name">判定名。</param>
/// <param name="Criterion">判据的文字表述。</param>
/// <param name="Measured">算出来的值，已经格式化成报告里要写的样子。</param>
/// <param name="Value">算出来的值，用来比较。</param>
/// <param name="Threshold">要跨过的门槛。</param>
/// <param name="ThresholdText">门槛在报告里的写法。p 值与 W 的小数位数不一样，各写各的。</param>
/// <param name="Passed">达标与否。</param>
/// <param name="Lines">这一条要写进报告的补充行。</param>
internal sealed record GateResult(
    string Name,
    string Criterion,
    string Measured,
    double Value,
    double Threshold,
    string ThresholdText,
    bool Passed,
    IReadOnlyList<string> Lines);

/// <summary>一组配对比较。</summary>
internal sealed record PairResult(string Left, string Right, double RawP, double Threshold, bool Rejected);

/// <summary>一份数据的全部计算结果。</summary>
internal sealed record Evaluation(
    IReadOnlyList<string> Arms,
    IReadOnlyList<double> Means,
    IReadOnlyList<double> Medians,
    IReadOnlyList<double> MeanRanks,
    FriedmanResult Friedman,
    double Kendall,
    IReadOnlyList<PairResult> Pairs,
    string Leading,
    IReadOnlyList<GateResult> Gates,
    int PassedGates,
    string Decision,
    string Rationale);

/// <summary>
/// 四条统计判定与结论。
/// </summary>
/// <remarks>
/// <para>
/// **判定门写死成四条，每条都给出算出来的值。** 只给一句"通过 / 不通过"，
/// 读的人没法判断差多少——而"两条达标时交评审组"这条规则本身就要求知道差多少。
/// </para>
/// <para>
/// **没有数据就不生成报告。** 生成一份空的或占位的报告，后来的读者会以为测过了，
/// 而实际上什么都没测。这条比"报告要齐全"重要。
/// </para>
/// </remarks>
internal static class Analysis
{
    /// <summary>族错误率。四条判定与 Holm 校正都用它。</summary>
    public const double Alpha = 0.05;

    /// <summary>一致度的门槛。</summary>
    public const double KendallThreshold = 0.3;

    /// <summary>达标条数到多少就保留。</summary>
    public const int KeepThreshold = 3;

    /// <summary>达标条数到多少就交评审组。</summary>
    public const int ReviewThreshold = 2;

    /// <summary>跑一遍分析：读数据、算判定、写报告。</summary>
    /// <param name="dataPath">录入数据的位置。</param>
    /// <param name="reportPath">报告要写到哪里。</param>
    /// <param name="output">往哪里报进度。传空则写标准输出。</param>
    /// <returns>进程退出码。没有数据不是错误，返回零。</returns>
    public static int Run(string dataPath, string reportPath, TextWriter? output = null)
    {
        var log = output ?? Console.Out;

        if (!File.Exists(dataPath))
        {
            log.WriteLine($"还没有数据：{dataPath} 不存在。");
            log.WriteLine("先跑 template 生成量表、顺序分配表与录入模板；收上来之后再跑 analyze。");
            log.WriteLine("这一趟没有生成任何报告。");

            return 0;
        }

        var study = StudyJson.Load(dataPath);
        var (complete, skipped) = Split(study);

        if (study.Arms.Count < 3 || complete.Count < 2)
        {
            log.WriteLine(
                $"还没有数据：{dataPath} 里能参与计算的完整录入 {complete.Count} 条、被测手段 {study.Arms.Count} 个。");
            log.WriteLine("统计判定至少要三个手段、两条填完的录入。这一趟没有生成任何报告。");

            return 0;
        }

        var evaluation = Evaluate(study, complete);
        var report = Report(study, complete.Count, skipped, evaluation, StudyJson.Fingerprint(dataPath), dataPath);

        StudyJson.EnsureDirectory(reportPath);
        File.WriteAllText(reportPath, report);

        log.WriteLine($"写了 {reportPath}：{complete.Count} 条录入参与计算，{skipped} 条未填完被跳过。");

        foreach (var gate in evaluation.Gates)
        {
            log.WriteLine($"  {(gate.Passed ? "达标" : "未达")}　{gate.Name}　{gate.Measured}（门槛 {gate.ThresholdText}）");
        }

        log.WriteLine($"  结论：{evaluation.Decision}");

        return 0;
    }

    /// <summary>把录入分成"填完了"和"没填完"两堆。</summary>
    public static (List<RaterEntry> Complete, int Skipped) Split(Study study)
    {
        var complete = new List<RaterEntry>();
        var skipped = 0;

        foreach (var rater in study.Raters)
        {
            if (IsComplete(study, rater))
            {
                complete.Add(rater);
            }
            else
            {
                skipped++;
            }
        }

        return (complete, skipped);
    }

    /// <summary>这一条录入是不是每个手段都给了量表范围内的分。</summary>
    public static bool IsComplete(Study study, RaterEntry rater)
    {
        foreach (var arm in study.Arms)
        {
            if (!rater.Scores.TryGetValue(arm, out var score)
                || score is null
                || score < study.ScaleMin
                || score > study.ScaleMax)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>四条判定与结论。</summary>
    public static Evaluation Evaluate(Study study, IReadOnlyList<RaterEntry> raters)
    {
        var arms = study.Arms;
        var matrix = raters
            .Select(rater => arms.Select(arm => (double)rater.Scores[arm]!.Value).ToArray())
            .ToArray();

        var means = new double[arms.Count];
        var medians = new double[arms.Count];
        var meanRanks = new double[arms.Count];

        for (var arm = 0; arm < arms.Count; arm++)
        {
            var column = matrix.Select(row => row[arm]).ToArray();

            means[arm] = column.Average();
            medians[arm] = Median(column);
        }

        foreach (var row in matrix)
        {
            var ranks = Statistics.Ranks(row);

            for (var arm = 0; arm < arms.Count; arm++)
            {
                meanRanks[arm] += ranks[arm];
            }
        }

        for (var arm = 0; arm < arms.Count; arm++)
        {
            meanRanks[arm] /= matrix.Length;
        }

        var friedman = Statistics.Friedman(matrix);
        var kendall = Statistics.KendallW(matrix);

        // 领先项是平均分最高的那一个。并列时取声明次序在前的——判据要一个确定的项，
        // 而"大家其实分不出高下"这件事由第一、三条判定去说，不在这里含糊过去。
        var leading = 0;

        for (var arm = 1; arm < arms.Count; arm++)
        {
            if (means[arm] > means[leading] + Statistics.Tolerance)
            {
                leading = arm;
            }
        }

        var pairs = Pairs(matrix, arms);
        var gates = Gates(study, arms, means, leading, friedman, kendall, pairs);
        var passed = gates.Count(gate => gate.Passed);
        var (decision, rationale) = Verdict(passed);

        return new Evaluation(
            arms,
            means,
            medians,
            meanRanks,
            friedman,
            kendall,
            pairs,
            arms[leading],
            gates,
            passed,
            decision,
            rationale);
    }

    /// <summary>三组配对比较，经 Holm 校正。</summary>
    /// <remarks>
    /// 族就是这三组，所以第一步的阈值正好是族错误率除以三——判定门里写的那个数就是它。
    /// 校正之后逐步收紧，走到第一个不达标的就停：那一步说明这一族里已经有说不清的东西了。
    /// </remarks>
    private static IReadOnlyList<PairResult> Pairs(double[][] matrix, IReadOnlyList<string> arms)
    {
        var raw = new List<(int Left, int Right, double P)>();

        for (var left = 0; left < arms.Count; left++)
        {
            for (var right = left + 1; right < arms.Count; right++)
            {
                var p = Statistics.Wilcoxon(
                    matrix.Select(row => row[left]).ToArray(),
                    matrix.Select(row => row[right]).ToArray()).P;

                raw.Add((left, right, p));
            }
        }

        var steps = Statistics.Holm(raw.Select(item => item.P).ToArray(), Alpha);

        return [.. raw.Select((item, index) => new PairResult(
            arms[item.Left],
            arms[item.Right],
            item.P,
            steps[index].Threshold,
            steps[index].Rejected))];
    }

    private static IReadOnlyList<GateResult> Gates(
        Study study,
        IReadOnlyList<string> arms,
        IReadOnlyList<double> means,
        int leading,
        FriedmanResult friedman,
        double kendall,
        IReadOnlyList<PairResult> pairs)
    {
        var leadingPairs = pairs
            .Where(pair => pair.Left == arms[leading] || pair.Right == arms[leading])
            .ToArray();

        var pairLines = new List<string>
        {
            friedman.Exact
                ? "精确分布那一档；卡方那一档列在下面做对照。"
                : "卡方近似那一档——被测手段太多，精确分布的状态数会爆炸。",
            $"统计量 S = {F(friedman.Spread)}，自由度 {friedman.DegreesOfFreedom}。",
            $"卡方那一档：χ² = {F(friedman.ChiSquare)}，p = {P(friedman.ChiSquareP)}。",
        };

        var gateOne = new GateResult(
            "Friedman 检验",
            "p < 0.05：三个手段之间至少有一对存在系统差异。",
            $"p = {P(friedman.P)}",
            friedman.P,
            Alpha,
            P(Alpha),
            friedman.P < Alpha,
            pairLines);

        var pairDetail = new List<string>
        {
            "三组配对一起做 Holm 校正，族大小三，第一步的阈值就是 0.05 / 3。",
        };

        foreach (var pair in pairs)
        {
            var mark = pair.Left == arms[leading] || pair.Right == arms[leading] ? "（涉及领先项）" : string.Empty;

            pairDetail.Add(
                $"{pair.Left} 对 {pair.Right}：原始 p = {P(pair.RawP)}，"
                + $"Holm 阈值 = {P(pair.Threshold)}，{(pair.Rejected ? "达标" : "不达标")}{mark}");
        }

        var worstP = leadingPairs.Max(pair => pair.RawP);
        var tightest = leadingPairs.Min(pair => pair.Threshold);
        var gateTwo = new GateResult(
            "Wilcoxon + Holm-Bonferroni",
            "领先项与其余每一项的配对比较，经 Holm 校正后都要达标。",
            $"最松的一条 p = {P(worstP)}",
            worstP,
            tightest,
            P(tightest),
            leadingPairs.All(pair => pair.Rejected),
            pairDetail);

        var gateThree = new GateResult(
            "Kendall's W",
            "W ≥ 0.3：评分者之间的看法足够一致，均值才有意义。",
            $"W = {F(kendall)}",
            kendall,
            KendallThreshold,
            F(KendallThreshold),
            kendall >= KendallThreshold,
            [
                "零表示各排各的，一表示名次完全一致。",
                "这一条不达标时，前面两条即使显著也只能说明分歧很大。",
            ]);

        var scoreThreshold = ScoreThreshold(study);
        var gateFour = new GateResult(
            "平均排序分",
            $"领先项的平均分 ≥ {F(scoreThreshold)}（五分量表上的四分）。",
            $"{arms[leading]}的平均分 = {F(means[leading])}",
            means[leading],
            scoreThreshold,
            F(scoreThreshold),
            means[leading] >= scoreThreshold,
            [
                $"全部手段的平均分：{string.Join("、", arms.Select((arm, index) => $"{arm} {F(means[index])}"))}。",
            ]);

        return [gateOne, gateTwo, gateThree, gateFour];
    }

    /// <summary>平均分那一档的门槛。</summary>
    /// <remarks>
    /// 判定门写的是"四分（五分量表）"。换一个量表时按同一比例折算，
    /// 免得有人把量表改成十分制之后门槛还停在四分上。
    /// </remarks>
    private static double ScoreThreshold(Study study) =>
        study.ScaleMin + ((study.ScaleMax - study.ScaleMin) * 0.75);

    /// <summary>达标条数换算成结论。</summary>
    public static (string Decision, string Rationale) Verdict(int passed)
    {
        if (passed >= KeepThreshold)
        {
            return (
                "保留",
                $"四条判定里有 {passed} 条达标，跨过了「至少三条」的门槛，按当前设计保留。");
        }

        if (passed == ReviewThreshold)
        {
            return (
                "交评审组",
                "只有两条达标，未达「至少三条」的门槛。按协议交三人评审组（一名设计师、"
                + "一名工程师、一名产品）多数决，评审组成员不得参与本设计。");
        }

        return (
            "砍掉",
            $"只达标 {passed} 条，既没跨过「至少三条」的门槛，也不够交评审组的条件，这一设计砍掉。"
            + "协议没有单独写这一档的处置，这里按与「两条」那一档相反的方向从严处理。");
    }

    /// <summary>把一次计算写成报告。</summary>
    /// <remarks>
    /// 正文里不写生成时间。同一份数据每次跑出同一份文件，比对才有意义；
    /// 而"这份报告是哪天跑的"在提交记录里查得到，不必写进正文。
    /// </remarks>
    public static string Report(
        Study study,
        int complete,
        int skipped,
        Evaluation evaluation,
        string fingerprint,
        string dataPath)
    {
        var text = new StringBuilder();

        text.AppendLine($"# 用户测试：{study.Title}");
        text.AppendLine();
        text.AppendLine("本文件由 `dotnet run --project tools/UserStudy -c Release -- analyze` 生成，不要手改。");
        text.AppendLine($"数据：`{dataPath}`，指纹 `{fingerprint}`。");
        text.AppendLine();

        text.AppendLine("## 数据");
        text.AppendLine();
        text.AppendLine($"- 参与计算：{complete} 人；未填完被跳过：{skipped} 人。");
        text.AppendLine($"- 量表：{study.ScaleMin} 到 {study.ScaleMax} 分。");
        text.AppendLine($"- 被测手段：{string.Join("、", study.Arms)}。");

        var orders = study.Raters
            .Where(rater => IsComplete(study, rater))
            .Select(rater => (IReadOnlyList<string>)rater.Order)
            .ToArray();

        if (orders.Length > 0 && orders.All(order => order.Count == study.Arms.Count))
        {
            text.AppendLine(
                $"- 顺序分配：{Ordering.BalanceNote(study.Arms, Ordering.PositionCounts(study.Arms, orders))}");
        }
        else
        {
            text.AppendLine("- 顺序分配：录入里没有记全每个人看的手段顺序，这一项没法核对。");
        }

        text.AppendLine();

        text.AppendLine("## 描述统计");
        text.AppendLine();
        text.AppendLine("| 手段 | 平均分 | 中位数 | 平均秩 |");
        text.AppendLine("|---|---|---|---|");

        for (var arm = 0; arm < study.Arms.Count; arm++)
        {
            text.AppendLine(
                $"| {study.Arms[arm]} | {F(evaluation.Means[arm])} | {F(evaluation.Medians[arm])} | "
                + $"{F(evaluation.MeanRanks[arm])} |");
        }

        text.AppendLine();

        text.AppendLine("## 四条判定");
        text.AppendLine();
        text.AppendLine("| # | 判定 | 门槛 | 实测 | 结果 |");
        text.AppendLine("|---|---|---|---|---|");

        for (var index = 0; index < evaluation.Gates.Count; index++)
        {
            var gate = evaluation.Gates[index];

            text.AppendLine(
                $"| {index + 1} | {gate.Name} | {gate.ThresholdText} | {gate.Measured} | "
                + $"{(gate.Passed ? "达标" : "未达标")} |");
        }

        text.AppendLine();

        for (var index = 0; index < evaluation.Gates.Count; index++)
        {
            var gate = evaluation.Gates[index];

            text.AppendLine($"### {index + 1}. {gate.Name}");
            text.AppendLine();
            text.AppendLine(gate.Criterion);
            text.AppendLine();
            text.AppendLine($"- 实测：{gate.Measured}，门槛：{gate.ThresholdText}，"
                + $"{(gate.Passed ? "达标" : "未达标")}。");

            foreach (var line in gate.Lines)
            {
                text.AppendLine($"- {line}");
            }

            text.AppendLine();
        }

        text.AppendLine("## 结论");
        text.AppendLine();
        text.AppendLine($"**{evaluation.Decision}**（达标 {evaluation.PassedGates} / {evaluation.Gates.Count} 条）。");
        text.AppendLine();
        text.AppendLine(evaluation.Rationale);
        text.AppendLine();

        var notes = study.Raters
            .Where(rater => !string.IsNullOrWhiteSpace(rater.Notes))
            .Select(rater => $"- {rater.Id}：{rater.Notes!.Trim()}")
            .ToArray();

        text.AppendLine("## 定性反馈");
        text.AppendLine();

        if (notes.Length == 0)
        {
            text.AppendLine("录入里没有写反馈。");
        }
        else
        {
            foreach (var note in notes)
            {
                text.AppendLine(note);
            }
        }

        text.AppendLine();

        // 换行统一成 LF。报告要逐字节比对，也要进版本库，而仓库按 LF 归一化；
        // 在 Windows 上留成 CRLF 的话，同一份数据换一台机器跑就整篇对不上。
        return text.ToString().ReplaceLineEndings("\n");
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(value => value).ToArray();

        if (sorted.Length == 0)
        {
            return 0;
        }

        var middle = sorted.Length / 2;

        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    /// <summary>四位小数。报告里的数字要按同一种格式写，否则同一份数据两次跑出来会不一样。</summary>
    private static string F(double value) => value.ToString("0.0000", CultureInfo.InvariantCulture);

    /// <summary>p 值用更多位。判定门卡在 0.05 附近时，四位小数看不出差多少。</summary>
    private static string P(double value) => value.ToString("0.000000", CultureInfo.InvariantCulture);
}
