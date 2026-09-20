namespace DuetDiagram.Poc.SharedTools;

/// <summary>
/// 验证一份工具定义能不能同时供内部模型与外部代理使用。
/// </summary>
/// <remarks>
/// 用可执行的断言回答两个问题：共用一份定义时两边是否完全一致；
/// 以及退化成各自维护时两边的参数约束会不会漂移。退出码表达整体结果。
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Contains("--api", StringComparer.Ordinal))
        {
            ApiDump.Run("Microsoft.Extensions.AI.Abstractions", "Microsoft.Extensions.AI", "ModelContextProtocol.Core");
            return 0;
        }

        Console.WriteLine("工具定义共用验证");
        Console.WriteLine("覆盖：共用一份定义的两侧一致性、各自维护时的 schema 漂移");
        Console.WriteLine();

        var results = Checks.Run();

        var shared = results.Where(r => r.Name.StartsWith("共用·", StringComparison.Ordinal)).ToArray();
        var separate = results.Where(r => r.Name.StartsWith("各自维护·", StringComparison.Ordinal)).ToArray();

        Console.WriteLine("=== 共用一份定义 ===");

        foreach (var result in shared)
        {
            Console.WriteLine($"    [{(result.Passed ? "通过" : "未通过")}] {result.Name}：{result.Detail}");
        }

        Console.WriteLine();
        Console.WriteLine("=== 各自维护（方案留的退路）===");

        foreach (var result in separate)
        {
            Console.WriteLine($"    [{(result.Passed ? "通过" : "未通过")}] {result.Name}：{result.Detail}");
        }

        Console.WriteLine();

        var failures = results.Count(r => !r.Passed);

        Console.WriteLine(failures == 0 ? "全部检查通过" : $"有 {failures} 项未通过");

        return failures == 0 ? 0 : 1;
    }
}
