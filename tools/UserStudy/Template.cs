using System.Globalization;
using System.Text;

namespace DuetDiagram.Tools.UserStudy;

/// <summary>默认路径与默认口径。</summary>
internal static class Options
{
    /// <summary>录入数据。</summary>
    public const string DefaultDataPath = "reports/user-study/study.json";

    /// <summary>结论报告。</summary>
    public const string DefaultReportPath = "reports/phase2-user-study.md";

    /// <summary>量表、分配表与录入模板放哪儿。</summary>
    public const string DefaultTemplateRoot = "reports/user-study";

    /// <summary>默认收多少人。</summary>
    public const int DefaultRaters = 12;

    /// <summary>协议规定的人数下限。</summary>
    public const int MinRaters = 12;

    /// <summary>协议规定的人数上限。</summary>
    public const int MaxRaters = 15;

    /// <summary>默认被测的三个手段。</summary>
    public static readonly string[] DefaultArms = ["脉冲", "角标", "虚线轮廓"];

    /// <summary>默认这份测试测的是什么。</summary>
    public const string DefaultTitle = "变更高亮的三种手段";
}

/// <summary>
/// 量表、顺序分配表与录入模板。
/// </summary>
/// <remarks>
/// 这三样是收数据之前就得定下来的东西。量表事后改会把已经打过的分变得没法解释；
/// 顺序分配事后改等于承认之前收的那批顺序是偏的。所以它们由脚本生成，
/// 而不是每次口头讲一遍。
/// </remarks>
internal static class Template
{
    /// <summary>把三样产物写到 <paramref name="root"/> 下。</summary>
    /// <param name="root">产物目录。</param>
    /// <param name="raters">收多少人。</param>
    /// <param name="output">往哪里报进度。传空则写标准输出。</param>
    /// <returns>进程退出码。</returns>
    public static int Run(string root, int raters, TextWriter? output = null)
    {
        var log = output ?? Console.Out;
        var dataPath = Path.Combine(root, Path.GetFileName(Options.DefaultDataPath));

        // 录入文件已经在了就用它，不覆盖。里面可能已经录了一半的数据，
        // 而"重新生成模板"这个动作不该把真人填过的东西擦掉。
        var existing = File.Exists(dataPath);
        var study = existing ? StudyJson.Load(dataPath) : Skeleton(raters);

        if (!existing)
        {
            StudyJson.Save(dataPath, study);
        }

        var scalePath = Path.Combine(root, "scale.md");
        var assignmentPath = Path.Combine(root, "assignment.md");

        StudyJson.EnsureDirectory(scalePath);
        File.WriteAllText(scalePath, Scale(study));
        File.WriteAllText(assignmentPath, AssignmentTable(study));

        log.WriteLine(existing
            ? $"{dataPath} 已经在了，不覆盖；分配表按它的受试者名单重排。"
            : $"写了 {dataPath}：{study.Raters.Count} 名受试者的空录入，评分留空。");
        log.WriteLine($"写了 {scalePath}");
        log.WriteLine($"写了 {assignmentPath}");

        var counts = Ordering.PositionCounts(
            study.Arms,
            study.Raters.Select(rater => (IReadOnlyList<string>)rater.Order));

        log.WriteLine(Ordering.BalanceNote(study.Arms, counts));

        return 0;
    }

    /// <summary>一份空的录入骨架，受试者按轮转拉丁方排好顺序。</summary>
    public static Study Skeleton(int raters)
    {
        var study = new Study
        {
            Title = Options.DefaultTitle,
            Arms = [.. Options.DefaultArms],
            ScaleMin = 1,
            ScaleMax = 5,
        };

        for (var index = 0; index < raters; index++)
        {
            study.Raters.Add(new RaterEntry
            {
                Id = $"R{index + 1:00}",
                Order = [.. Ordering.OrderFor(index, study.Arms)],
                Scores = study.Arms.ToDictionary(arm => arm, _ => (int?)null),
                Notes = null,
            });
        }

        return study;
    }

    /// <summary>量表。</summary>
    public static string Scale(Study study)
    {
        var text = new StringBuilder();

        text.AppendLine($"# 量表：{study.Title}");
        text.AppendLine();
        text.AppendLine("每个手段用过之后按下面的档打分。打分只看「变更被标出来这件事有多容易看见、");
        text.AppendLine("有多不碍事」，不比较别的，也不要因为喜欢哪个手段而给分。");
        text.AppendLine();
        text.AppendLine($"| 分 | 意思 |");
        text.AppendLine("|---|---|");
        text.AppendLine("| 1 | 完全没注意到有变更 |");
        text.AppendLine("| 2 | 事后回想才觉得好像有变化 |");
        text.AppendLine("| 3 | 找一下能看出变更在哪儿 |");
        text.AppendLine("| 4 | 一眼就看出变更在哪儿 |");
        text.AppendLine("| 5 | 一眼看出变更在哪儿，而且完全不干扰读图 |");
        text.AppendLine();
        text.AppendLine($"量表范围：{study.ScaleMin} 到 {study.ScaleMax} 分。");
        text.AppendLine();
        text.AppendLine("每个手段再记一条定性反馈，问的是同一个问题：");
        text.AppendLine();
        text.AppendLine("> 你刚才注意到画面上哪些东西变了？靠的是什么看出来的？");
        text.AppendLine();
        text.AppendLine("各手段之间不提示「接下来换一种标法」，让受试者自己发现。");
        text.AppendLine();

        return text.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>顺序分配表。</summary>
    public static string AssignmentTable(Study study)
    {
        var text = new StringBuilder();

        text.AppendLine("# 顺序分配");
        text.AppendLine();
        text.AppendLine("受试者按编号轮转取拉丁方的行：每连续若干人构成一个完整的拉丁方，");
        text.AppendLine("每个手段在每一个位置上出现的次数相同。不做这一步的话，先看到的那一个会拿到偏低的分，");
        text.AppendLine("而那个差会被算成手段之间的差。");
        text.AppendLine();
        text.AppendLine($"| 受试者 | {string.Join(" | ", Enumerable.Range(1, study.Arms.Count).Select(i => $"第 {i} 位"))} |");
        text.AppendLine($"|---|{string.Join("|", Enumerable.Repeat("---", study.Arms.Count))}|");

        foreach (var rater in study.Raters)
        {
            var cells = Enumerable.Range(0, study.Arms.Count)
                .Select(position => position < rater.Order.Count ? rater.Order[position] : "—");

            text.AppendLine($"| {rater.Id} | {string.Join(" | ", cells)} |");
        }

        text.AppendLine();
        text.AppendLine("## 均衡核对");
        text.AppendLine();

        var counts = Ordering.PositionCounts(
            study.Arms,
            study.Raters.Select(rater => (IReadOnlyList<string>)rater.Order));

        text.AppendLine($"| 手段 | {string.Join(" | ", Enumerable.Range(1, study.Arms.Count).Select(i => $"第 {i} 位"))} |");
        text.AppendLine($"|---|{string.Join("|", Enumerable.Repeat("---", study.Arms.Count))}|");

        for (var arm = 0; arm < study.Arms.Count; arm++)
        {
            text.AppendLine(
                $"| {study.Arms[arm]} | {string.Join(" | ", counts[arm].Select(cell => cell.ToString(CultureInfo.InvariantCulture)))} |");
        }

        text.AppendLine();
        text.AppendLine(Ordering.BalanceNote(study.Arms, counts));
        text.AppendLine();

        if (study.Raters.Count < Options.MinRaters || study.Raters.Count > Options.MaxRaters)
        {
            text.AppendLine(
                $"**人数是 {study.Raters.Count}，落在协议要求的 {Options.MinRaters} 到 "
                + $"{Options.MaxRaters} 人之外。**");
            text.AppendLine();
        }

        return text.ToString().ReplaceLineEndings("\n");
    }
}
