namespace DuetDiagram.Tools.UserStudy;

/// <summary>
/// 用户测试的量表生成器与分析装置。
/// </summary>
/// <remarks>
/// 用法：用命令行运行本工程，加上 verify、template 或 analyze 参数。
/// 可用开关：--root 产物目录、--raters 人数、--data 录入数据、--out 报告位置。
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal))
        {
            PrintUsage();

            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            if (args.Contains("verify", StringComparer.Ordinal))
            {
                Console.WriteLine("统计量与判定门的自检");

                return Checks.Run();
            }

            if (args.Contains("template", StringComparer.Ordinal))
            {
                var raters = ReadOption(args, "--raters") is { } text && int.TryParse(text, out var parsed)
                    ? parsed
                    : Options.DefaultRaters;

                if (raters < 2)
                {
                    Console.Error.WriteLine("至少要两个人：统计判定比的是评分者之间的差。");

                    return 1;
                }

                return Template.Run(ReadOption(args, "--root") ?? Options.DefaultTemplateRoot, raters);
            }

            if (args.Contains("analyze", StringComparer.Ordinal))
            {
                return Analysis.Run(
                    ReadOption(args, "--data") ?? Options.DefaultDataPath,
                    ReadOption(args, "--out") ?? Options.DefaultReportPath);
            }

            Console.Error.WriteLine($"未知命令。{string.Join(' ', args)}");
            PrintUsage();

            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{ex.GetType().Name}：{ex.Message}");

            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("用户测试的量表生成器与分析装置");
        Console.WriteLine();
        Console.WriteLine("  verify                  自检统计量与判定门（不需要真人数据，随时可跑）");
        Console.WriteLine("  template                生成量表、顺序分配表与录入模板");
        Console.WriteLine("  analyze                 读录入数据，算四条判定，写结论报告");
        Console.WriteLine();
        Console.WriteLine($"  --root <目录>           三样模板产物放哪儿，默认 {Options.DefaultTemplateRoot}");
        Console.WriteLine($"  --raters <人数>         生成几个人的空录入，默认 {Options.DefaultRaters}");
        Console.WriteLine($"  --data <路径>           录入数据，默认 {Options.DefaultDataPath}");
        Console.WriteLine($"  --out <路径>            结论报告，默认 {Options.DefaultReportPath}");
        Console.WriteLine();
        Console.WriteLine("四条判定与它们的门槛：");
        Console.WriteLine("  Friedman 检验           p < 0.05");
        Console.WriteLine("  Wilcoxon + Holm         p < 0.05 / 3");
        Console.WriteLine("  Kendall's W             W >= 0.3");
        Console.WriteLine("  平均排序分              领先项平均分 >= 五分量表上的四分");
        Console.WriteLine();
        Console.WriteLine("至少三条达标就保留；恰好两条交三人评审组多数决；不足两条砍掉。");
        Console.WriteLine();
        Console.WriteLine("没有数据时 analyze 明确报「还没有数据」并什么都不生成——");
        Console.WriteLine("生成一份空的或占位的报告，后来的读者会以为测过了。");
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
