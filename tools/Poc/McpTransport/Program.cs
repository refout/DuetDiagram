using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DuetDiagram.Poc.McpTransport;

/// <summary>
/// 传输验证的动作入口。
/// </summary>
/// <remarks>
/// 同一个可执行文件既是客户端也是服务端：服务端模式由父进程拉起来，通过标准输入输出对话。
/// 这样不需要额外的可执行文件，也不会把验证脚手架拆成两个项目。
/// </remarks>
internal static class Program
{
    /// <summary>进入服务端模式的开关。</summary>
    public const string ServerSwitch = "--stdio-server";

    /// <summary>指定消息记录文件路径的开关。父子进程模式下服务端用它把观察结果带回来。</summary>
    public const string LogTargetSwitch = "--log-target";

    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--api", StringComparer.Ordinal))
        {
            ApiDump.Run("ModelContextProtocol", "ModelContextProtocol.Core");
            return 0;
        }

        if (args.Contains(ServerSwitch, StringComparer.Ordinal))
        {
            return await RunServerAsync(args).ConfigureAwait(false);
        }

        return await RunChecksAsync().ConfigureAwait(false);
    }

    /// <summary>服务端模式：跑起来一直到对端断开。</summary>
    private static async Task<int> RunServerAsync(string[] args)
    {
        var log = new MessageLog(ReadOption(args, LogTargetSwitch));

        var host = ServerHost.Build(mcp => mcp.WithStdioServerTransport(), log);

        await host.RunAsync().ConfigureAwait(false);

        return 0;
    }

    /// <summary>客户端模式：依次跑内存流对回环与父子进程回环。</summary>
    private static async Task<int> RunChecksAsync()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        Console.WriteLine("MCP 传输验证");
        Console.WriteLine("覆盖：初始化握手、工具发现与调用、自定义会话字段的双向传递");
        Console.WriteLine();

        var reports = new List<RoundtripReport>();

        try
        {
            reports.Add(await Roundtrips.WireAsync(cancellation.Token).ConfigureAwait(false));

            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("拿不到当前可执行文件路径，无法拉起服务端子进程。");

            var logPath = Path.Combine(Path.GetTempPath(), $"duetdiagram-mcp-{Environment.ProcessId}.log");

            reports.Add(await Roundtrips.StdioAsync(executable, logPath, cancellation.Token).ConfigureAwait(false));

            reports.Add(await HttpRoundtrip.RunAsync(cancellation.Token).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"验证过程中断：{ex.GetType().Name} - {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }

        var failures = 0;

        foreach (var report in reports)
        {
            Console.WriteLine($"=== {report.Transport} ===");

            foreach (var (name, passed, detail) in report.Checks)
            {
                if (!passed)
                {
                    failures++;
                }

                Console.WriteLine($"    [{(passed ? "通过" : "未通过")}] {name}：{detail}");
            }

            Console.WriteLine();
        }

        Console.WriteLine(failures == 0 ? "全部检查通过" : $"有 {failures} 项未通过");

        return failures == 0 ? 0 : 1;
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
