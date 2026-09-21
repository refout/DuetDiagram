using BenchmarkDotNet.Running;

namespace DuetDiagram.Benchmarks;

/// <summary>
/// 性能基线测量。
/// </summary>
/// <remarks>
/// 用法：用命令行运行本工程，可加 --filter 只跑部分基准。
/// 本程序只负责产生数据，不写结论——数字要连同误差棒一起看，由人来判。
/// </remarks>
internal static class Program
{
    private static void Main(string[] args) =>
        BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(args);
}
