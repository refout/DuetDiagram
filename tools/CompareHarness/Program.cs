namespace DuetDiagram.Tools.CompareHarness;

/// <summary>
/// 对比测试的语料生成器与评分装置。
/// </summary>
/// <remarks>
/// 用法：用命令行运行本工程，加上 generate、listings、semantic、score、verify、sample 或 agreement 参数。
/// 可用开关：--parallel 并发数、--force 覆盖已有结果、--secrets 凭据路径。
/// </remarks>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        if (args.Contains("listings", StringComparer.Ordinal))
        {
            try
            {
                return Listings.Run(
                    ReadOption(args, "--prompts") ?? Options.DefaultPromptsPath,
                    ReadOption(args, "--corpus") ?? Options.DefaultCorpusRoot,
                    ReadOption(args, "--out") ?? Options.DefaultListingsRoot);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{ex.GetType().Name}：{ex.Message}");
                return 1;
            }
        }

        if (args.Contains("semantic", StringComparer.Ordinal))
        {
            try
            {
                return Semantic.Run(
                    ReadOption(args, "--corpus") ?? Options.DefaultCorpusRoot,
                    ReadOption(args, "--out") ?? Options.DefaultListingsRoot);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{ex.GetType().Name}：{ex.Message}");
                return 1;
            }
        }

        if (args.Contains("score", StringComparer.Ordinal))
        {
            try
            {
                return Scoring.Run(
                    ReadOption(args, "--prompts") ?? Options.DefaultPromptsPath,
                    ReadOption(args, "--corpus") ?? Options.DefaultCorpusRoot,
                    ReadOption(args, "--out") ?? Options.DefaultListingsRoot);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                Console.Error.WriteLine($"{ex.GetType().Name}：{ex.Message}");
                return 1;
            }
        }

        if (args.Contains("verify", StringComparer.Ordinal))
        {
            return Verify.Run(ReadOption(args, "--prompts") ?? Options.DefaultPromptsPath);
        }

        if (args.Contains("sample", StringComparer.Ordinal))
        {
            try
            {
                return Sample.Run(
                    ReadOption(args, "--prompts") ?? Options.DefaultPromptsPath,
                    ReadOption(args, "--corpus") ?? Options.DefaultCorpusRoot,
                    ReadOption(args, "--out") ?? Options.DefaultListingsRoot,
                    int.TryParse(ReadOption(args, "--per-arm"), out var perArm) ? perArm : Sample.DefaultPerArm);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{ex.GetType().Name}：{ex.Message}");
                return 1;
            }
        }

        if (args.Contains("agreement", StringComparer.Ordinal))
        {
            try
            {
                var outputRoot = ReadOption(args, "--out") ?? Options.DefaultListingsRoot;

                return Agreement.Run(
                    ReadOption(args, "--prompts") ?? Options.DefaultPromptsPath,
                    ReadOption(args, "--corpus") ?? Options.DefaultCorpusRoot,
                    outputRoot,
                    ReadOption(args, "--human") ?? Path.Combine(outputRoot, Options.HumanDirectoryName));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{ex.GetType().Name}：{ex.Message}");
                return 1;
            }
        }

        if (!args.Contains("generate", StringComparer.Ordinal))
        {
            Console.Error.WriteLine($"未知命令。{string.Join(' ', args)}");
            PrintUsage();
            return 1;
        }

        var options = new Options(
            ReadOption(args, "--prompts") ?? Options.DefaultPromptsPath,
            ReadOption(args, "--arms") ?? Options.DefaultArmsDirectory,
            ReadOption(args, "--secrets") ?? Options.DefaultSecretsPath,
            ReadOption(args, "--out") ?? Options.DefaultOutputRoot,
            int.TryParse(ReadOption(args, "--parallel"), out var parallelism) ? parallelism : 3,
            args.Contains("--force", StringComparer.Ordinal),
            (ReadOption(args, "--only") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        using var cancellation = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            // 中断时让已经在飞的请求跑完并把结果落盘，不要留下半截文件。
            e.Cancel = true;
            Console.WriteLine("收到中断，等待进行中的请求结束……");
            cancellation.Cancel();
        };

        try
        {
            return await Generator.RunAsync(options, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("已中断。已落盘的结果不会重复生成，重跑即可续上。");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{ex.GetType().Name}：{ex.Message}");
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("对比测试语料生成器与评分装置");
        Console.WriteLine();
        Console.WriteLine("  generate                生成语料");
        Console.WriteLine("  listings                把冻结语料转成盲评清单，并算出解析统计");
        Console.WriteLine("  semantic                把冻结语料读成 IR 再校验，算出语义拒绝率");
        Console.WriteLine("  score                   用检查项上的谓词判 150 份，算出谓词口径的端到端准确率");
        Console.WriteLine("  verify                  自检谓词求值器（不需要语料，随时可跑）");
        Console.WriteLine("  sample                  抽一批条目交给人判，用来校谓词");
        Console.WriteLine("  agreement               读人工评分，算它与谓词的一致率并列出分歧");
        Console.WriteLine();
        Console.WriteLine("  --prompts <路径>        提示词文件，默认 tools/CompareHarness/prompts.json");
        Console.WriteLine("  --arms <目录>           组定义目录，默认 tools/CompareHarness/arms");
        Console.WriteLine("  --secrets <路径>        凭据文件，默认 secrets/bigmodel.local.json");
        Console.WriteLine("  --corpus <目录>         冻结语料目录（listings、semantic、score 与 agreement 用），默认 reports/raw");
        Console.WriteLine("  --out <目录>            结果目录，generate 默认 reports/raw，其余默认 reports/compare-blind");
        Console.WriteLine("  --per-arm <数量>        抽样时每组收几份，默认 20");
        Console.WriteLine("  --human <目录>          人工评分目录，默认 <结果目录>/human");
        Console.WriteLine("  --parallel <数量>       并发请求数，默认 3");
        Console.WriteLine("  --only <标识,...>       只跑指定的提示词，用于冒烟与补跑");
        Console.WriteLine("  --force                 覆盖已有结果，默认跳过");
        Console.WriteLine();
        Console.WriteLine("凭据文件不会被提交，格式：");
        Console.WriteLine("""  { "endpoint": "...", "apiKey": "...", "model": "..." }""");
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
