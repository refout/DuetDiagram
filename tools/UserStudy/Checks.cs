namespace DuetDiagram.Tools.UserStudy;

/// <summary>
/// 统计量的自检。
/// </summary>
/// <remarks>
/// <para>
/// 每一条都拿一个**手算得出来**的答案去对。手算得出来是关键：拿另一份实现当参照，
/// 两边一起错的时候自检照样通过，而这份数据唯一的用途就是决定被测项保不保。
/// </para>
/// <para>
/// 不需要真人数据，随时可跑。改完统计量先跑这个。
/// </para>
/// </remarks>
internal static class Checks
{
    public static int Run(TextWriter? output = null)
    {
        var log = output ?? Console.Out;
        var failures = 0;

        void Report(string name, bool ok, string detail)
        {
            if (ok)
            {
                log.WriteLine($"  通过　{name}");
            }
            else
            {
                failures++;
                log.WriteLine($"  失败　{name}：{detail}");
            }
        }

        void Near(string name, double actual, double expected, double tolerance = 1e-9) =>
            Report(
                name,
                Math.Abs(actual - expected) <= tolerance,
                $"算得 {actual.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}，"
                + $"应为 {expected.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}");

        log.WriteLine("平均秩与并列");

        var ranks = Statistics.Ranks([1.0, 2, 2, 4]);

        Report(
            "并列取这一组的平均秩",
            ranks.SequenceEqual([1.0, 2.5, 2.5, 4.0]),
            $"算得 [{string.Join(", ", ranks)}]");

        var reverse = Statistics.Ranks([5.0, 1, 3]);

        Report(
            "秩与输入的先后无关",
            reverse.SequenceEqual([3.0, 1.0, 2.0]),
            $"算得 [{string.Join(", ", reverse)}]");

        Near("并列校正：一组并列两个", Statistics.TieCorrection([1.0, 2.5, 2.5, 4.0]), 6);
        Near("并列校正：两组各并列两个", Statistics.TieCorrection([1.5, 1.5, 3.5, 3.5]), 12);
        Near("并列校正：没有并列时为零", Statistics.TieCorrection([1.0, 2, 3]), 0);

        log.WriteLine("Friedman 检验");

        // 三个人给的名次完全一样：秩和是 3 / 6 / 9，离差平方和是 9 + 0 + 9。
        // 参考分布是六的十二次方分之一那一档里最小的一种，也就是六个共同名次占 6 的三次方种。
        var agree = new[] { new[] { 1.0, 2, 3 }, new[] { 1.0, 2, 3 }, new[] { 1.0, 2, 3 } };
        var agreed = Statistics.Friedman(agree);

        Near("完全一致时的离差平方和", agreed.Spread, 18);
        Near("三人完全一致时的精确 p", agreed.P, 1.0 / 36.0);
        Report("人数与手段数都少时走精确那一档", agreed.Exact, "应当走精确分布");

        // 所有人都给三列同一个分：秩全是 2，秩和相等，离差平方和为零。
        var flat = new[] { new[] { 3.0, 3, 3 }, new[] { 3.0, 3, 3 }, new[] { 3.0, 3, 3 } };
        var flattened = Statistics.Friedman(flat);

        Near("全体同分时的离差平方和为零", flattened.Spread, 0);
        Near("全体同分时 p 为一", flattened.P, 1);

        // 一个人正着排、一个人反着排：秩和全是 4，正好对消。
        var cancel = new[] { new[] { 1.0, 2, 3 }, new[] { 3.0, 2, 1 } };

        Near("名次正反对消时 p 为一", Statistics.Friedman(cancel).P, 1);

        log.WriteLine("Wilcoxon 符号秩");

        // 六个人一致：六对差全为正，|差| 的秩全是 3.5，正侧秩和 21、负侧 0。
        // 参考分布是六个符号的六十四种组合，只有全负那一种能让负侧秩和不大于零。
        Near(
            "六人一致时的双侧精确 p",
            Statistics.Wilcoxon([5.0, 5, 5, 5, 5, 5], [1.0, 1, 1, 1, 1, 1]).P,
            2.0 / 64.0);

        // 四个人一致时最小可能 p 是 0.125，够不到 0.05——这正是协议要十二人以上的原因。
        Near(
            "四人一致时的双侧精确 p",
            Statistics.Wilcoxon([5.0, 5, 5, 5], [1.0, 1, 1, 1]).P,
            2.0 / 16.0);

        // 两正两负：正侧秩和与负侧秩和相等，双侧 p 到顶。
        Near("正负各半时 p 为一", Statistics.Wilcoxon([5.0, 1, 5, 1], [1.0, 5, 1, 5]).P, 1);

        var identical = Statistics.Wilcoxon([4.0, 3, 2], [4.0, 3, 2]);

        Report("没有差不为零的对时样本量为零", identical.Pairs == 0 && Math.Abs(identical.P - 1) <= 1e-9, $"p 算得 {identical.P}");

        log.WriteLine("Kendall's W");

        Near("名次完全一致时为一", Statistics.KendallW([[1.0, 2, 3], [1.0, 2, 3]]), 1);
        Near("名次完全相反时为零", Statistics.KendallW([[1.0, 2, 3], [3.0, 2, 1]]), 0);

        log.WriteLine("Holm-Bonferroni");

        var allPass = Statistics.Holm([0.01, 0.02, 0.04], 0.05);

        Near("第一步的阈值是族错误率除以三", allPass[0].Threshold, 0.05 / 3);
        Report("三条都过第一步时全部达标", allPass.All(step => step.Rejected), "应当全部达标");

        var stopped = Statistics.Holm([0.02, 0.03, 0.04], 0.05);

        Report("第一步不达标就全停", stopped.All(step => !step.Rejected), "应当全部不达标");

        var partial = Statistics.Holm([0.01, 0.03, 0.04], 0.05);

        Report(
            "第二步不达标时只留第一步",
            partial[0].Rejected && !partial[1].Rejected && !partial[2].Rejected,
            $"算得 [{string.Join(", ", partial.Select(step => step.Rejected))}]");

        log.WriteLine("卡方分布的上尾");

        Near("自由度二时是 e 的负二分之 x 次", Statistics.ChiSquareUpperTail(2, 2), Math.Exp(-1));
        Near("自由度四时是 (1 + x/2) 乘上它", Statistics.ChiSquareUpperTail(2, 4), 2 * Math.Exp(-1));
        Near("自由度一在 3.841459 处约等于 0.05", Statistics.ChiSquareUpperTail(3.841458820694124, 1), 0.05);
        Near("零处为一", Statistics.ChiSquareUpperTail(0, 2), 1);

        log.WriteLine("顺序分配");

        foreach (var count in new[] { Options.MinRaters, Options.MaxRaters })
        {
            var orders = Enumerable.Range(0, count)
                .Select(index => (IReadOnlyList<string>)Ordering.OrderFor(index, Options.DefaultArms))
                .ToArray();

            var counts = Ordering.PositionCounts(Options.DefaultArms, orders);

            Report(
                $"{count} 人时顺序均衡",
                Ordering.IsBalanced(counts),
                Ordering.BalanceNote(Options.DefaultArms, counts));

            Near($"{count} 人时每个位置上每项各 {count / Options.DefaultArms.Length} 次", counts[0][0], count / Options.DefaultArms.Length);
        }

        var odd = Enumerable.Range(0, 13)
            .Select(index => (IReadOnlyList<string>)Ordering.OrderFor(index, Options.DefaultArms))
            .ToArray();

        Report(
            "十三人时判得出不均衡",
            !Ordering.IsBalanced(Ordering.PositionCounts(Options.DefaultArms, odd)),
            "十三人不是手段数的整数倍，应当判为不均衡");

        log.WriteLine("结论的换算");

        Report("四条达标保留", Analysis.Verdict(4).Decision == "保留", Analysis.Verdict(4).Decision);
        Report("三条达标保留", Analysis.Verdict(3).Decision == "保留", Analysis.Verdict(3).Decision);
        Report("两条达标交评审组", Analysis.Verdict(2).Decision == "交评审组", Analysis.Verdict(2).Decision);
        Report("一条达标砍掉", Analysis.Verdict(1).Decision == "砍掉", Analysis.Verdict(1).Decision);
        Report("零条达标砍掉", Analysis.Verdict(0).Decision == "砍掉", Analysis.Verdict(0).Decision);

        log.WriteLine("端到端");

        EndToEnd(Report);

        log.WriteLine();
        log.WriteLine(failures == 0
            ? "全部通过。"
            : $"有 {failures} 条没通过。");

        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// 造一份十二人的数据跑完整条链路。
    /// </summary>
    /// <remarks>
    /// 十二个人给的名次完全一致：Friedman 的精确 p 是六的十二次方分之六，
    /// 一致度为一，领先项平均分五分，两组配对也都过得了 Holm。四条应当全达标。
    /// 同一份数据跑两遍还要逐字节一样——报告里不写生成时间就是为了这一条。
    /// </remarks>
    private static int EndToEnd(Action<string, bool, string> report)
    {
        var directory = Path.Combine(Path.GetTempPath(), "duet-user-study-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var study = Template.Skeleton(Options.DefaultRaters);

            foreach (var rater in study.Raters)
            {
                rater.Scores[Options.DefaultArms[0]] = 5;
                rater.Scores[Options.DefaultArms[1]] = 3;
                rater.Scores[Options.DefaultArms[2]] = 1;
                rater.Notes = "第三个最省事。";
            }

            var dataPath = Path.Combine(directory, "study.json");

            StudyJson.Save(dataPath, study);

            var evaluation = Analysis.Evaluate(study, study.Raters);

            report("判定门恰好四条", evaluation.Gates.Count == 4, $"算得 {evaluation.Gates.Count} 条");
            report(
                "十二人一致时四条全达标",
                evaluation.PassedGates == 4,
                string.Join(
                    "；",
                    evaluation.Gates.Select(gate => $"{gate.Name} {gate.Measured} {(gate.Passed ? "达标" : "未达标")}")));

            var first = Path.Combine(directory, "a.md");
            var second = Path.Combine(directory, "b.md");
            var firstCode = Analysis.Run(dataPath, first, TextWriter.Null);
            var secondCode = Analysis.Run(dataPath, second, TextWriter.Null);

            report("有数据时写出报告", File.Exists(first), "报告没写出来");
            report("退出码为零", firstCode == 0 && secondCode == 0, $"算得 {firstCode}、{secondCode}");
            report(
                "同一份数据两次跑出同一份文件",
                File.Exists(second) && File.ReadAllText(first) == File.ReadAllText(second),
                "两次的结果不一致");

            var text = File.ReadAllText(first);

            report("报告里写了结论", text.Contains("**保留**", StringComparison.Ordinal), "报告里找不到结论");
            report("报告里写了指纹", text.Contains(StudyJson.Fingerprint(dataPath), StringComparison.Ordinal), "报告里找不到数据指纹");

            // 数据文件不在时什么都不该生成。生成一份空的或占位的报告，
            // 后来的读者会以为测过了。
            var missingReport = Path.Combine(directory, "missing.md");
            var missingCode = Analysis.Run(Path.Combine(directory, "没有这个文件.json"), missingReport, TextWriter.Null);

            report(
                "没有数据时不生成报告",
                !File.Exists(missingReport) && missingCode == 0,
                File.Exists(missingReport) ? "生成了报告" : $"退出码 {missingCode}");

            // 录入里只填了一半时，那一条不参与计算，但要被数出来。
            var half = Template.Skeleton(Options.DefaultRaters);

            half.Raters[0].Scores[Options.DefaultArms[0]] = 5;
            half.Raters[0].Scores[Options.DefaultArms[1]] = 4;

            for (var index = 1; index < half.Raters.Count; index++)
            {
                half.Raters[index].Scores[Options.DefaultArms[0]] = 5;
                half.Raters[index].Scores[Options.DefaultArms[1]] = 4;
                half.Raters[index].Scores[Options.DefaultArms[2]] = 3;
            }

            var (complete, skipped) = Analysis.Split(half);

            report(
                "没填完的录入被跳过并数出来",
                complete.Count == Options.DefaultRaters - 1 && skipped == 1,
                $"算得参与 {complete.Count} 条、跳过 {skipped} 条");

            return 0;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
