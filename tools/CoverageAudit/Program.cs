using System.Text;

namespace CoverageAudit;

internal static class Program
{
    private const double Threshold = 0.85;

    private static int Main(string[] args)
    {
        var listMissing = args.Contains("--list-missing", StringComparer.Ordinal)
                         || args.Contains("--list", StringComparer.Ordinal);

        var items = Matrix.Items;
        var total = items.Count;
        var yes = items.Count(i => i.Status == Status.Yes);
        var partial = items.Count(i => i.Status == Status.Partial);
        var no = items.Count(i => i.Status == Status.No);

        var covered = yes + partial;
        var coverage = total == 0 ? 0 : (double)covered / total;
        var weighted = total == 0 ? 0 : (yes + 0.5 * partial) / total;

        var root = FindRepositoryRoot();
        var missingEvidence = VerifyEvidence(root, items);

        if (listMissing)
        {
            PrintMissing(items);
        }

        PrintSummary(total, yes, partial, no, coverage, weighted, missingEvidence);

        return coverage >= Threshold ? 0 : 1;
    }

    private static void PrintSummary(
        int total, int yes, int partial, int no, double coverage, double weighted,
        IReadOnlyList<string> missingEvidence)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        Console.WriteLine("draw.io 覆盖率取证");
        Console.WriteLine($"  分母来源：{Matrix.Source}");
        Console.WriteLine($"  总数：{total}");
        Console.WriteLine($"  有：{yes}    部分：{partial}    无：{no}");
        Console.WriteLine($"  覆盖率（有+部分）/ 总数 = {coverage.ToString("P1", culture)}");
        Console.WriteLine($"  加权覆盖率（有 + 0.5×部分）/ 总数 = {weighted.ToString("P1", culture)}");
        Console.WriteLine($"  门禁阈值：{Threshold.ToString("P0", culture)}");

        var byCategory = Matrix.Items
            .GroupBy(i => i.Category)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        Console.WriteLine("  逐档计数：");
        foreach (var group in byCategory)
        {
            var gYes = group.Count(i => i.Status == Status.Yes);
            var gPartial = group.Count(i => i.Status == Status.Partial);
            var gNo = group.Count(i => i.Status == Status.No);
            Console.WriteLine($"    {group.Key,-10} 有 {gYes,-3} 部分 {gPartial,-3} 无 {gNo,-3} 共 {group.Count()}");
        }

        if (missingEvidence.Count > 0)
        {
            Console.WriteLine("  证据文件缺失（应在仓库里存在）：");
            foreach (var line in missingEvidence)
            {
                Console.WriteLine($"    {line}");
            }
        }

        var passed = coverage >= Threshold;
        Console.WriteLine(passed ? "  [通过] 覆盖率达到阈值" : "  [未通过] 覆盖率低于阈值");
    }

    private static void PrintMissing(IReadOnlyList<Capability> items)
    {
        var missing = items.Where(i => i.Status == Status.No).ToList();

        Console.WriteLine($"全部记成「无」的条目（共 {missing.Count} 条），每条带一句为什么：");
        foreach (var item in missing)
        {
            Console.WriteLine($"- [{item.Category}] {item.Feature}：{item.Note}");
        }
    }

    private static IReadOnlyList<string> VerifyEvidence(string root, IReadOnlyList<Capability> items)
    {
        var problems = new List<string>();

        foreach (var item in items.Where(i => i.Status != Status.No && i.Evidence.Length > 0))
        {
            var path = item.Evidence.Split(':')[0];
            var full = Path.Combine(root, path);
            if (!File.Exists(full))
            {
                problems.Add($"[{item.Category}] {item.Feature} -> {path} 不在仓库里");
            }
        }

        return problems;
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Directory.Build.props"))
                || Directory.Exists(Path.Combine(dir, "DuetDiagram.Core")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return Environment.CurrentDirectory;
    }
}
