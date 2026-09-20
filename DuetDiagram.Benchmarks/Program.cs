using BenchmarkDotNet.Running;

namespace DuetDiagram.Benchmarks;

/// <summary>
/// 性能基线测量。
/// </summary>
/// <remarks>
/// 用法：用命令行运行本工程，可加 --filter 只跑部分基准。
/// 结果同时写入 reports/phase0b-baseline.md，本程序只负责产生数据。
/// </remarks>
internal static class Program
{
    private static void Main(string[] args) =>
        BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(args);
}
