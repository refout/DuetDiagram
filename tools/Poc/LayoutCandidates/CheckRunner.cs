using System.Diagnostics;

namespace DuetDiagram.Poc.LayoutCandidates;

/// <summary>
/// 对候选引擎跑同一套判据并打印结论。
/// </summary>
/// <remarks>
/// 判据分两类。一类是"能不能表达"——固定位置、层内顺序、对齐这些意图在输入模型里有没有入口，
/// 没有入口就等于这个能力不存在，引擎内部做得再好也用不上。
/// 另一类是"做得好不好"——坐标是否有界、是否确定、耗时是否可接受。
/// 两类都要看，只测性能会选出一个画不出能看的图的引擎，只测功能会选出一个慢到不可用的引擎。
/// </remarks>
internal static class CheckRunner
{
    /// <summary>估算"同层节点紧挨着排"所需的宽度，再放宽若干倍作为合理性上限。</summary>
    private const double ColumnPitch = 80 + 36;

    private const double SaneFactor = 3;

    public static void Run(ILayoutCandidate candidate)
    {
        Console.WriteLine();
        Console.WriteLine($"########## {candidate.Name} ##########");
        Console.WriteLine();

        ReportInputModel(candidate);
        ReportPinning(candidate);
        ReportSameRank(candidate);
        ReportDeterminism(candidate);
        ReportDirection(candidate);
        ReportSpacing(candidate);
        ReportCoordinateSanity(candidate);
        ReportCompound(candidate);
        ReportScale(candidate);
    }

    /// <summary>只跑规模与耗时。用于对比同一引擎的不同调用入口，避免重复跑完整套判据。</summary>
    public static void RunScaleOnly(ILayoutCandidate candidate)
    {
        Console.WriteLine();
        Console.WriteLine($"########## {candidate.Name}（仅规模与耗时）##########");
        ReportScale(candidate);
    }

    private static void ReportInputModel(ILayoutCandidate candidate)
    {
        var facts = candidate.InputFacts;

        Report("输入模型·节点", facts.HasPinnedField ? Support.Partial : Support.NotSupported, facts.NodeMembers);
        Report("输入模型·图与选项", facts.HasOrderOrAlignOrPlaceField ? Support.Partial : Support.NotSupported, facts.LayoutMembers);

        Report(
            "可表达的约束",
            facts.HasPinnedField && facts.HasOrderOrAlignOrPlaceField ? Support.Supported : Support.Partial,
            $"固定位置字段={facts.HasPinnedField}，层内顺序或对齐或相对位置字段={facts.HasOrderOrAlignOrPlaceField}");
    }

    private static void ReportPinning(ILayoutCandidate candidate)
    {
        PinProbeResult probe;

        try
        {
            probe = candidate.ProbePinned(GraphShapes.Diamond(), LayoutKnobs.Default);
        }
        catch (Exception ex)
        {
            // 探测本身抛异常也是一条结论：说明这条用法不被支持，但不该让整个验证流程中断。
            Report("固定位置行为", Support.NotSupported, $"探测时抛出 {ex.GetType().Name}：{ex.Message}");
            return;
        }

        Report(
            "固定位置行为",
            probe.Attempted && probe.Honored ? Support.Supported : Support.NotSupported,
            probe.Evidence);
    }

    private static void ReportSameRank(ILayoutCandidate candidate)
    {
        SameRankProbeResult probe;

        try
        {
            probe = candidate.ProbeSameRank(GraphShapes.Diamond(), LayoutKnobs.Default);
        }
        catch (Exception ex)
        {
            Report("同层约束行为", Support.NotSupported, $"探测时抛出 {ex.GetType().Name}：{ex.Message}");
            return;
        }

        Report(
            "同层约束行为",
            probe.Attempted && probe.Honored ? Support.Supported : Support.NotSupported,
            probe.Evidence);
    }

    private static void ReportDeterminism(ILayoutCandidate candidate)    {
        var first = TryCompute(candidate, GraphShapes.Diamond(), LayoutKnobs.Default);
        var second = TryCompute(candidate, GraphShapes.Diamond(), LayoutKnobs.Default);

        if (first is null || second is null)
        {
            Report("确定性", Support.NotSupported, "计算失败");
            return;
        }

        // 逐个坐标比较，浮点误差按半个像素容忍。
        var same = first.Nodes.Length == second.Nodes.Length
            && first.Nodes.All(a =>
            {
                var b = second.Find(a.Id);
                return b is not null
                    && Math.Abs(a.X - b.X) < 0.5
                    && Math.Abs(a.Y - b.Y) < 0.5;
            });

        Report("确定性", same ? Support.Supported : Support.NotSupported, same ? "两次计算结果完全一致" : "两次计算结果不同");
    }

    private static void ReportDirection(ILayoutCandidate candidate)
    {
        var layouts = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var direction in new[] { "TD", "LR", "RL", "BT" })
        {
            var result = TryCompute(candidate, GraphShapes.Diamond(), LayoutKnobs.Default with { Direction = direction });

            if (result is null)
            {
                layouts[direction] = "<失败>";
                continue;
            }

            layouts[direction] = string.Join(
                ";",
                result.Nodes
                    .OrderBy(n => n.Id, StringComparer.Ordinal)
                    .Select(n => $"{n.Id}:{n.X:0.##},{n.Y:0.##}"));
        }

        var distinct = layouts.Values.Distinct(StringComparer.Ordinal).Count();
        var detail = string.Join(", ", layouts.Select(kv => $"{kv.Key} → {kv.Value.Split(';')[0]}"));

        Report(
            "方向",
            distinct == 4 ? Support.Supported : distinct > 1 ? Support.Partial : Support.NotSupported,
            $"四种方向产生 {distinct} 种互不相同的排布：{detail}");
    }

    private static void ReportSpacing(ILayoutCandidate candidate)
    {
        var baseline = TryCompute(candidate, GraphShapes.Diamond(), LayoutKnobs.Default);
        var wide = TryCompute(candidate, GraphShapes.Diamond(), LayoutKnobs.Default with { NodeSpacing = 200 });
        var tall = TryCompute(candidate, GraphShapes.Diamond(), LayoutKnobs.Default with { LayerSpacing = 400 });

        if (baseline is null || wide is null || tall is null)
        {
            Report("间距", Support.NotSupported, "计算失败");
            return;
        }

        var nodeSpacingMoved = Moved(baseline, wide, horizontal: true);
        var layerSpacingMoved = Moved(baseline, tall, horizontal: false);

        Report(
            "间距",
            nodeSpacingMoved && layerSpacingMoved ? Support.Supported : nodeSpacingMoved || layerSpacingMoved ? Support.Partial : Support.NotSupported,
            $"节点间距生效={nodeSpacingMoved}，层间距生效={layerSpacingMoved}");
    }

    /// <summary>
    /// 坐标是否有界。
    /// </summary>
    /// <remarks>
    /// 这是整套判据里最关键的一条。图形本身完全合法，唯一的变化是"有没有交叉边"，
    /// 两者的横向宽度应当接近。差距达到数十倍说明坐标分配没有上界，
    /// 直接后果是画布宽度失去意义，渲染出来要么内容小到看不清，要么必须靠缩放兜住。
    /// </remarks>
    private static void ReportCoordinateSanity(ILayoutCandidate candidate)
    {
        var cases = new (string Label, CandidateGraph Graph, int Breadth)[]
        {
            ("菱形", GraphShapes.Diamond(), 2),
            ("两列十层完全二分", GraphShapes.Bipartite(depth: 9, breadth: 2), 2),
            ("十列十层无交叉", GraphShapes.Layered(depth: 10, breadth: 10, withCrossings: false), 10),
            ("十列十层带交叉", GraphShapes.Layered(depth: 10, breadth: 10, withCrossings: true), 10),
        };

        foreach (var (label, graph, breadth) in cases)
        {
            var result = TryCompute(candidate, graph, LayoutKnobs.Default);

            if (result is null)
            {
                Report($"横宽·{label}", Support.NotSupported, "计算失败");
                continue;
            }

            var span = result.Nodes.Max(n => n.X) - result.Nodes.Min(n => n.X);
            var ceiling = breadth * ColumnPitch * SaneFactor;

            Report(
                $"横宽·{label}",
                span <= ceiling ? Support.Supported : Support.NotSupported,
                $"横向散布 {span:0}（合理上限约 {ceiling:0}，{graph.Nodes.Length} 节点 {graph.Edges.Length} 边）");
        }
    }

    private static void ReportCompound(ILayoutCandidate candidate)
    {
        var graph = GraphShapes.Compound();
        var result = TryCompute(candidate, graph, LayoutKnobs.Default);

        if (result is null)
        {
            Report("复合分组", Support.NotSupported, "计算失败");
            return;
        }

        if (result.Groups.Length == 0)
        {
            Report("复合分组", Support.NotSupported, "结果里没有任何分组框");
            return;
        }

        // 分组框必须真的把成员包住，否则画出来就是子节点露在框外。
        var contained = graph.Groups
            .Where(g => result.Groups.Any(b => b.Id == g.Id))
            .All(g => g.Members.All(member =>
            {
                var node = result.Find(member);
                var box = result.Groups.First(b => b.Id == g.Id);

                return node is not null
                    && node.X >= box.X - 0.5
                    && node.Y >= box.Y - 0.5
                    && node.Right <= box.Right + 0.5
                    && node.Bottom <= box.Bottom + 0.5;
            }));

        Report(
            "复合分组",
            contained ? Support.Supported : Support.Partial,
            $"返回分组框 {result.Groups.Length} 个，成员全部落在框内={contained}");
    }

    private static void ReportScale(ILayoutCandidate candidate)
    {
        var cases = new (string Label, CandidateGraph Graph)[]
        {
            ("长链 1000", GraphShapes.Chain(1000)),
            ("多层 20×50", GraphShapes.Layered(depth: 20, breadth: 50, withCrossings: true)),
        };

        foreach (var (label, graph) in cases)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = TryCompute(candidate, graph, LayoutKnobs.Default);
            stopwatch.Stop();

            if (result is null)
            {
                Report($"规模·{label}", Support.NotSupported, $"{stopwatch.Elapsed.TotalMilliseconds:0} ms 内失败");
                continue;
            }

            var span = result.Nodes.Max(n => n.X) - result.Nodes.Min(n => n.X);

            Report(
                $"规模·{label}",
                Support.Informational,
                $"{stopwatch.Elapsed.TotalMilliseconds:0.0} ms，横向散布 {span:0}，画布 {result.Width:0}x{result.Height:0}");
        }
    }

    private static bool Moved(CandidateResult baseline, CandidateResult other, bool horizontal) =>
        baseline.Nodes.Any(a =>
        {
            var b = other.Find(a.Id);
            return b is not null && Math.Abs((horizontal ? a.X : a.Y) - (horizontal ? b.X : b.Y)) > 0.5;
        });

    /// <summary>
    /// 计算并吞掉异常。
    /// </summary>
    /// <remarks>
    /// 候选引擎在超出能力范围时可能抛异常，而"抛异常"本身就是一条有效结论，
    /// 不应该让整个验证流程中断。返回空表示这次计算没有可用结果。
    /// </remarks>
    private static CandidateResult? TryCompute(ILayoutCandidate candidate, CandidateGraph graph, LayoutKnobs knobs)
    {
        try
        {
            return candidate.Compute(graph, knobs);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Report(string subject, Support support, string evidence)
    {
        var label = support switch
        {
            Support.Supported => "支持    ",
            Support.Partial => "部分支持",
            Support.NotSupported => "不支持  ",
            _ => "信息    ",
        };

        Console.WriteLine($"[{label}] {subject}：{evidence}");
    }

    private enum Support
    {
        Supported,
        Partial,
        NotSupported,
        Informational,
    }
}
