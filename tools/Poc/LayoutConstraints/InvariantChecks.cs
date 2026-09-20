using System.Diagnostics;

namespace DuetDiagram.Poc.LayoutConstraints;

/// <summary>
/// 场景与不变量检查。
/// </summary>
/// <remarks>
/// 每一项检查都对应一条能算出来的性质，而不是"看起来对"。
/// 约束补齐这种东西最容易出的问题是"大部分情况没问题、偶尔悄悄错一个"，
/// 只有把不变量写成断言，才能在改动之后立刻发现退化。
/// </remarks>
internal static class InvariantChecks
{
    private static int _failures;

    public static int RunAll()
    {
        _failures = 0;

        BasicDiamond();
        AnchorSurvivesRelayout();
        AnchorPushesFreeNodeAside();
        SameRankGroupStaysTogether();
        SameRankWithAnchors();
        ContradictoryAnchorsAreReported();
        Scale();

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "全部不变量通过"
            : $"有 {_failures} 项不变量未通过");

        return _failures == 0 ? 0 : 1;
    }

    /// <summary>不设任何约束时，补齐流程不应改变引擎的结果。</summary>
    private static void BasicDiamond()
    {
        var graph = new ConstraintGraph(
            "基础菱形",
            [
                new ConstraintNode("a", 80, 40),
                new ConstraintNode("b", 80, 40),
                new ConstraintNode("c", 80, 40),
                new ConstraintNode("d", 80, 40),
            ],
            [
                new ConstraintEdge("a", "b"),
                new ConstraintEdge("a", "c"),
                new ConstraintEdge("b", "d"),
                new ConstraintEdge("c", "d"),
            ],
            []);

        var outcome = ConstraintLayoutPipeline.Compute(graph, ConstraintOptions.Default);

        Section(graph.Name);
        Check("节点数守恒", outcome.Nodes.Length == 4, $"期望 4，实际 {outcome.Nodes.Length}");
        Check("无残留重叠", outcome.Diagnostics.ResidualOverlaps == 0, $"残留 {outcome.Diagnostics.ResidualOverlaps}");
        Check("锚点偏差为零", outcome.Diagnostics.MaxAnchorDeviation == 0, $"偏差 {outcome.Diagnostics.MaxAnchorDeviation}");
        ReportTiming(outcome);
    }

    /// <summary>
    /// 硬保证：把节点固定到任意位置之后重排，它的坐标必须原样保住。
    /// </summary>
    /// <remarks>
    /// 这里故意把固定坐标放得离自然位置很远，用来确认不是"碰巧一致"。
    /// </remarks>
    private static void AnchorSurvivesRelayout()
    {
        var graph = new ConstraintGraph(
            "锚点在重排后保住",
            [
                new ConstraintNode("a", 80, 40),
                new ConstraintNode("b", 80, 40),
                new ConstraintNode("c", 80, 40),
                new ConstraintNode("d", 80, 40, new Anchor(1200, 900)),
            ],
            [
                new ConstraintEdge("a", "b"),
                new ConstraintEdge("a", "c"),
                new ConstraintEdge("b", "d"),
                new ConstraintEdge("c", "d"),
            ],
            []);

        var outcome = ConstraintLayoutPipeline.Compute(graph, ConstraintOptions.Default);
        var placed = outcome.Find("d");

        Section(graph.Name);
        Check("锚点偏差为零", outcome.Diagnostics.MaxAnchorDeviation == 0, $"偏差 {outcome.Diagnostics.MaxAnchorDeviation}");
        Check(
            "锚点坐标精确",
            placed is not null && Math.Abs(placed.X - 1200) < 0.01 && Math.Abs(placed.Y - 900) < 0.01,
            placed is null ? "节点缺失" : $"实际 ({placed.X:0.##}, {placed.Y:0.##})");
        Check("无残留重叠", outcome.Diagnostics.ResidualOverlaps == 0, $"残留 {outcome.Diagnostics.ResidualOverlaps}");
        ReportTiming(outcome);
    }

    /// <summary>
    /// 锚点落在别的节点身上时，必须是被挤的那个让开，锚点不动。
    /// </summary>
    /// <remarks>
    /// 碰撞是**先量出来再构造**的：先跑一遍不带锚点的布局，读出某个节点的真实位置，
    /// 再把锚点精确放在那个位置上。直接猜一个坐标很容易猜偏，
    /// 那样测试会在"其实没重叠"的情况下通过，看起来一切正常却什么都没验到。
    /// </remarks>
    private static void AnchorPushesFreeNodeAside()
    {
        ConstraintNode[] Nodes(Anchor? anchorOnE) =>
        [
            new ConstraintNode("a", 80, 40),
            new ConstraintNode("b", 80, 40),
            new ConstraintNode("c", 80, 40),
            new ConstraintNode("d", 80, 40),
            new ConstraintNode("e", 80, 40, anchorOnE),
        ];

        ConstraintEdge[] edges =
        [
            new ConstraintEdge("a", "b"),
            new ConstraintEdge("a", "c"),
            new ConstraintEdge("b", "d"),
            new ConstraintEdge("c", "d"),
            new ConstraintEdge("a", "e"),
        ];

        // 第一遍：不带锚点，量出 a 自然落在哪里。
        var baseline = ConstraintLayoutPipeline.Compute(
            new ConstraintGraph("量取基准位置", Nodes(null), edges, []),
            ConstraintOptions.Default);

        var target = baseline.Find("a")!;
        var anchor = new Anchor(target.X, target.Y);

        // 第二遍：把 e 精确钉在 a 身上，制造一次必然发生的碰撞。
        var graph = new ConstraintGraph("锚点挤开自由节点", Nodes(anchor), edges, []);
        var outcome = ConstraintLayoutPipeline.Compute(graph, ConstraintOptions.Default);

        var a = outcome.Find("a")!;
        var e = outcome.Find("e")!;

        Section(graph.Name);
        Check("锚点偏差为零", outcome.Diagnostics.MaxAnchorDeviation == 0, $"偏差 {outcome.Diagnostics.MaxAnchorDeviation}");
        Check(
            "锚点坐标精确落在被侵占的位置",
            Math.Abs(e.X - anchor.X) < 0.01 && Math.Abs(e.Y - anchor.Y) < 0.01,
            $"目标 ({anchor.X:0.##}, {anchor.Y:0.##})，实际 ({e.X:0.##}, {e.Y:0.##})");
        Check("自由节点被挤走", Math.Abs(a.X - target.X) > 0.5 || Math.Abs(a.Y - target.Y) > 0.5, $"a 由 ({target.X:0.##}, {target.Y:0.##}) 移到 ({a.X:0.##}, {a.Y:0.##})");
        Check("无残留重叠", outcome.Diagnostics.ResidualOverlaps == 0, $"残留 {outcome.Diagnostics.ResidualOverlaps}");
        Check("让位确实介入过", outcome.Diagnostics.ReflowedNodes > 0, $"让位节点数 {outcome.Diagnostics.ReflowedNodes}");
        ReportTiming(outcome);
    }

    /// <summary>
    /// 同层组在展开之后必须真的同处一层，而且不能和邻居叠在一起。
    /// </summary>
    private static void SameRankGroupStaysTogether()
    {
        // a → b → d 与 a → c → e。不加约束时 d 与 e 分处不同深度。
        var graph = new ConstraintGraph(
            "同层组",
            [
                new ConstraintNode("a", 80, 40),
                new ConstraintNode("b", 80, 40),
                new ConstraintNode("c", 80, 40),
                new ConstraintNode("d", 80, 40),
                new ConstraintNode("e", 80, 40),
            ],
            [
                new ConstraintEdge("a", "b"),
                new ConstraintEdge("a", "c"),
                new ConstraintEdge("b", "d"),
                new ConstraintEdge("c", "e"),
            ],
            [new SameRankGroup("pair", ["d", "e"])]);

        var outcome = ConstraintLayoutPipeline.Compute(graph, ConstraintOptions.Default);
        var d = outcome.Find("d");
        var e = outcome.Find("e");

        Section(graph.Name);
        Check("节点数守恒", outcome.Nodes.Length == 5, $"期望 5，实际 {outcome.Nodes.Length}");

        var gap = d is not null && e is not null ? Math.Abs(d.Y - e.Y) : double.NaN;
        Check("同层组成员纵坐标一致", gap < 0.01, $"极差 {gap:0.##}");
        Check("无残留重叠", outcome.Diagnostics.ResidualOverlaps == 0, $"残留 {outcome.Diagnostics.ResidualOverlaps}");

        // 展开后成员之间不能互相压住，这一点靠超节点尺寸申报正确来保证。
        var horizontalGap = d is not null && e is not null ? Math.Abs(d.X - e.X) : double.NaN;
        Check("展开后成员横向不重叠", horizontalGap >= 80, $"横向间距 {horizontalGap:0.##}");
        ReportTiming(outcome);
    }

    /// <summary>同层组与锚点同时存在时的组合行为。</summary>
    private static void SameRankWithAnchors()
    {
        var graph = new ConstraintGraph(
            "同层组加锚点",
            [
                new ConstraintNode("a", 80, 40),
                new ConstraintNode("b", 80, 40),
                new ConstraintNode("c", 80, 40),
                new ConstraintNode("d", 80, 40),
                new ConstraintNode("e", 80, 40),
                new ConstraintNode("f", 80, 40, new Anchor(700, 500)),
            ],
            [
                new ConstraintEdge("a", "b"),
                new ConstraintEdge("a", "c"),
                new ConstraintEdge("b", "d"),
                new ConstraintEdge("c", "e"),
                new ConstraintEdge("d", "f"),
            ],
            [new SameRankGroup("pair", ["d", "e"])]);

        var outcome = ConstraintLayoutPipeline.Compute(graph, ConstraintOptions.Default);
        var d = outcome.Find("d");
        var e = outcome.Find("e");

        Section(graph.Name);
        Check("节点数守恒", outcome.Nodes.Length == 6, $"期望 6，实际 {outcome.Nodes.Length}");
        Check("锚点偏差为零", outcome.Diagnostics.MaxAnchorDeviation == 0, $"偏差 {outcome.Diagnostics.MaxAnchorDeviation}");
        Check("同层组成员纵坐标一致", d is not null && e is not null && Math.Abs(d.Y - e.Y) < 0.01, $"极差 {(d is not null && e is not null ? Math.Abs(d.Y - e.Y) : double.NaN):0.##}");
        Check("无残留重叠", outcome.Diagnostics.ResidualOverlaps == 0, $"残留 {outcome.Diagnostics.ResidualOverlaps}");
        ReportTiming(outcome);
    }

    /// <summary>
    /// 两个锚点互相重叠时，必须如实报出无解，而不是偷偷挪动其中一个。
    /// </summary>
    /// <remarks>
    /// 这是输入本身有矛盾的情况，两个节点都不可动，任何一方让步都等于违背用户意图。
    /// 期望的行为只有一个：两个锚点都不动，并把残留重叠数报出来，
    /// 由上层决定是拒绝这次布局还是提示用户。这种情况应该在拖动阶段就被阻止，
    /// 这里验证的是万一漏过去了，系统不会假装没事。
    /// </remarks>
    private static void ContradictoryAnchorsAreReported()
    {
        var graph = new ConstraintGraph(
            "矛盾的锚点",
            [
                new ConstraintNode("a", 80, 40),
                new ConstraintNode("b", 80, 40, new Anchor(100, 100)),
                new ConstraintNode("c", 80, 40, new Anchor(110, 105)),
            ],
            [
                new ConstraintEdge("a", "b"),
                new ConstraintEdge("b", "c"),
            ],
            []);

        var outcome = ConstraintLayoutPipeline.Compute(graph, ConstraintOptions.Default);

        Section(graph.Name);
        Check("锚点偏差仍为零", outcome.Diagnostics.MaxAnchorDeviation == 0, $"偏差 {outcome.Diagnostics.MaxAnchorDeviation}");
        Check(
            "如实报出残留重叠",
            outcome.Diagnostics.ResidualOverlaps > 0,
            $"残留 {outcome.Diagnostics.ResidualOverlaps}（期望大于零，说明系统如实报了无解而不是假装解决）");
        ReportTiming(outcome);
    }

    /// <summary>规模与分阶段耗时。</summary>
    /// <remarks>
    /// <para>
    /// 锚点这样布置：把中间五层的节点分别钉到前五层的条带上，横坐标与目标层的节点一一对齐。
    /// 这样每个锚点都必然压住一个自由节点，而锚点彼此既不同行相邻（列距正常）也不跨行重叠（行距正常）。
    /// </para>
    /// <para>
    /// 这一点是刻意的：如果锚点自己也互相压住，就变成了无解的输入，
    /// 残留重叠数会一直大于零，把"自由节点有没有被成功挤开"这个真正要看的信号淹掉。
    /// 无解输入另有专门的用例覆盖。
    /// </para>
    /// </remarks>
    private static void Scale()
    {
        const int layers = 20;
        const int breadth = 50;
        const int firstAnchoredLayer = 5;
        const int anchoredLayers = 5;
        const double columnPitch = 116;
        const double rowPitch = 112;

        var nodes = new List<ConstraintNode>(layers * breadth);

        for (var layer = 0; layer < layers; layer++)
        {
            for (var index = 0; index < breadth; index++)
            {
                var id = $"n{layer}-{index}";

                var offset = layer - firstAnchoredLayer;
                var anchored = offset >= 0 && offset < anchoredLayers;

                // 钉到第 offset 层的条带上：与那一层的自由节点完全重合。
                var anchor = anchored ? new Anchor(index * columnPitch, 40 + (offset * rowPitch)) : null;

                nodes.Add(new ConstraintNode(id, 80, 40, anchor));
            }
        }

        var edges = new List<ConstraintEdge>();

        for (var layer = 0; layer < layers - 1; layer++)
        {
            for (var index = 0; index < breadth; index++)
            {
                edges.Add(new ConstraintEdge($"n{layer}-{index}", $"n{layer + 1}-{index}"));
            }
        }

        var graph = new ConstraintGraph(
            $"规模 {layers}×{breadth}，{anchoredLayers * breadth} 个锚点各压住一个自由节点",
            [.. nodes],
            [.. edges],
            []);

        // 跑三遍。第一遍会包含即时编译与内存池预热的成本，拿它当算法耗时会把结论带偏——
        // 一个只需要几毫秒的算法很容易被报成十几毫秒。
        var runs = new List<(double Total, LayoutOutcome Outcome)>();

        for (var i = 0; i < 3; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var run = ConstraintLayoutPipeline.Compute(graph, ConstraintOptions.Default);
            stopwatch.Stop();

            runs.Add((stopwatch.Elapsed.TotalMilliseconds, run));
        }

        var outcome = runs[^1].Outcome;

        Section(graph.Name);
        Check("节点数守恒", outcome.Nodes.Length == layers * breadth, $"期望 {layers * breadth}，实际 {outcome.Nodes.Length}");
        Check("锚点偏差为零", outcome.Diagnostics.MaxAnchorDeviation == 0, $"偏差 {outcome.Diagnostics.MaxAnchorDeviation}");
        Check("无残留重叠", outcome.Diagnostics.ResidualOverlaps == 0, $"残留 {outcome.Diagnostics.ResidualOverlaps}");

        var setupCost = outcome.Diagnostics.ContractionTime + outcome.Diagnostics.RestorationTime + outcome.Diagnostics.ReflowTime;
        Check(
            "补齐阶段在预算内",
            setupCost < ConstraintLayoutPipeline.ReflowBudget,
            $"补齐合计 {setupCost.TotalMilliseconds:0.0} ms，预算 {ConstraintLayoutPipeline.ReflowBudget.TotalMilliseconds:0} ms");

        Console.WriteLine($"    总耗时（三次）：{string.Join(" / ", runs.Select(r => $"{r.Total:0.0} ms"))}");
        Console.WriteLine($"    最后一次：收缩 {outcome.Diagnostics.ContractionTime.TotalMilliseconds:0.00} ms / " +
                          $"引擎 {outcome.Diagnostics.EngineTime.TotalMilliseconds:0.0} ms / " +
                          $"展开回填 {outcome.Diagnostics.RestorationTime.TotalMilliseconds:0.00} ms / " +
                          $"行内让位 {outcome.Diagnostics.ReflowTime.TotalMilliseconds:0.00} ms");
        Console.WriteLine($"    被让位的自由节点 {outcome.Diagnostics.ReflowedNodes}");
    }
    private static void Section(string name)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {name} ===");
    }

    private static void ReportTiming(LayoutOutcome outcome)
    {
        var diagnostics = outcome.Diagnostics;

        Console.WriteLine(
            $"    收缩 {diagnostics.ContractionTime.TotalMilliseconds:0.00} ms / " +
            $"引擎 {diagnostics.EngineTime.TotalMilliseconds:0.0} ms / " +
            $"展开回填 {diagnostics.RestorationTime.TotalMilliseconds:0.00} ms / " +
            $"行内让位 {diagnostics.ReflowTime.TotalMilliseconds:0.00} ms（让位 {diagnostics.ReflowedNodes} 个节点）");
    }

    private static void Check(string name, bool passed, string detail)
    {
        if (!passed)
        {
            _failures++;
        }

        Console.WriteLine($"    [{(passed ? "通过" : "未通过")}] {name}：{detail}");
    }
}
