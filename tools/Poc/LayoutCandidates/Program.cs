namespace DuetDiagram.Poc.LayoutCandidates;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Contains("--api", StringComparer.Ordinal))
        {
            ApiDump.Run("Mostlylucid.Dagre", "Sugiyama");
            return 0;
        }

        // 只比规模与耗时时，把同一引擎的不同调用入口也拉进来，
        // 因为这决定了它能不能在大图上用。
        if (args.Contains("--perf", StringComparer.Ordinal))
        {
            foreach (var candidate in AllCandidates())
            {
                CheckRunner.RunScaleOnly(candidate);
            }

            return 0;
        }

        // 主候选用索引式入口：它在规模测例上快数倍，是实际会采用的那条路径。
        // 默认入口的完整判据结果在 reports/phase0a-layout.md 里有记录，两者结论一致。
        ILayoutCandidate[] candidates =
        [
            new DagreCandidate(useIndexedLayout: true),
            new SugiyamaCandidate(),
        ];

        foreach (var candidate in candidates)
        {
            CheckRunner.Run(candidate);
        }

        return 0;
    }

    private static ILayoutCandidate[] AllCandidates() =>
    [
        new DagreCandidate(),
        new DagreCandidate(useIndexedLayout: true),
        new SugiyamaCandidate(),
    ];
}
