namespace DuetDiagram.Poc.MermaiderConstraints;

internal static class Program
{
    private static int Main(string[] args)
    {
        // 不带参数时跑验证；带上这个开关只打印接口清单，用于人工核对库的能力边界。
        if (args.Contains("--api", StringComparer.Ordinal))
        {
            ApiDump.Run("Sugiyama", "Mermaider");
            return 0;
        }

        return Probe.Run();
    }
}
