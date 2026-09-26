using System.Globalization;
using System.Text.Json.Nodes;
using DuetDiagram.Core.Model;
using DuetDiagram.Layout;

namespace LayoutScore;

/// <summary>
/// 布局质量评分装置。
/// </summary>
/// <remarks>
/// <para>
/// 把产品里那份布局引擎跑在种子语料上，按 <see cref="Rubric"/> 写死的四个维度给分，
/// 印综合分与逐条明细，退出码表达是否达到阈值。
/// </para>
/// <para>
/// 它量的是"算出来的图好不好看"，不是"算不算得出来"——后者由布局引擎自己的
/// 硬保证与测试管。所以这里不看异常、不看降级级别，只看最终那批坐标。
/// </para>
/// <para>
/// 它**不写报告文件**：报告是手写的，装置只印数。与覆盖率取证装置同一口径——
/// 装置比数字重要，而报告要写清口径与"哪些没验"，那不是一次运行能产出的东西。
/// </para>
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        var ratingsPath = Value(args, "--ratings");
        var dump = Value(args, "--dump");

        if (dump is not null)
        {
            Dump(dump);
            return 0;
        }

        var selfCheck = Rubric.SelfCheck();

        if (selfCheck.Count > 0)
        {
            Console.WriteLine("几何自检没通过，分数不作数：");
            foreach (var line in selfCheck)
            {
                Console.WriteLine($"  {line}");
            }

            return 2;
        }

        var engine = new ConstraintLayoutEngine();
        var cards = new List<ScoreCard>(Corpus.Graphs.Count);
        var failures = new List<Failure>();

        foreach (var graph in Corpus.Graphs)
        {
            try
            {
                var job = LayoutRequestFactory.FromDocument(graph.Document, Corpus.Measure);
                var request = job.ToRequest();
                var result = engine.Layout(request.Nodes, request.Edges, request.Options, request.Groups);

                cards.Add(Rubric.Score(graph.Id, graph.Note, result, request.Options));
            }
            catch (Exception ex)
            {
                // 排不出来是一种结果，不是"这次没量到"。把它记下来而不是跳过——
                // 跳过的话报告上少一行，读的人会以为语料里本来就没有这份图。
                failures.Add(new Failure(graph.Id, graph.Note, ex.GetType().Name, ex.Message));
            }
        }

        PrintFailures(failures);

        // 排不出来的太多时不出分。剩下的那几份已经不足以代表语料，
        // 而一个只覆盖一小半语料的分数比没有分数更糟——它看起来像一个结论。
        if (failures.Count > Corpus.Graphs.Count / 3)
        {
            Console.WriteLine($"排不出来的超过三分之一（{failures.Count}/{Corpus.Graphs.Count}），"
                + "剩下的不足以代表语料，这次不出综合分。");

            return 2;
        }

        PrintCards(cards);
        PrintDetails(cards);

        var weighted = WeightedComposite(cards);
        var mean = cards.Count == 0 ? 0 : cards.Average(card => card.Composite);
        var worst = cards.Count == 0 ? 0 : cards.Min(card => card.Composite);

        PrintTotals(cards, failures, weighted, mean, worst);
        PrintComparison(cards, ratingsPath);

        var passed = weighted >= Rubric.PassThreshold;
        Console.WriteLine(passed
            ? $"  [通过] 综合质量分 {Show(weighted)} 达到阈值 {Show(Rubric.PassThreshold)}"
            : $"  [未通过] 综合质量分 {Show(weighted)} 低于阈值 {Show(Rubric.PassThreshold)}");

        return passed ? 0 : 1;
    }

    /// <summary>
    /// 排不出来的那些。
    /// </summary>
    /// <remarks>
    /// 单列在最前面，且**不计入综合分**——"能不能排"与"排得好不好看"是两个问题，
    /// 而这一条任务量的是后者。混进综合分的话，一份排不出来的图会把平均分拖下去，
    /// 而读的人从分数上看不出"是排得难看"还是"根本没排出来"。
    /// </remarks>
    /// <summary>
    /// 把一份图的节点与折线原样印出来。
    /// </summary>
    /// <remarks>
    /// 分数对不上直觉时，要看的是坐标本身。装置能自证到这一层，"这个分是怎么来的"
    /// 才不会变成一句解释不清的话。
    /// </remarks>
    private static void Dump(string id)
    {
        var graph = Corpus.Graphs.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

        if (graph is null)
        {
            Console.WriteLine($"语料里没有 {id}。有的是：{string.Join('、', Corpus.Graphs.Select(item => item.Id))}");
            return;
        }

        var job = LayoutRequestFactory.FromDocument(graph.Document, Corpus.Measure);
        var request = job.ToRequest();
        var result = new ConstraintLayoutEngine().Layout(
            request.Nodes, request.Edges, request.Options, request.Groups);

        Console.WriteLine($"{graph.Id}（{graph.Note}）方向 {request.Options.Direction}，外接框 {Show(result.Width)}×{Show(result.Height)}");
        Console.WriteLine("  节点：");
        foreach (var node in result.Nodes.OrderBy(node => node.Y).ThenBy(node => node.X))
        {
            Console.WriteLine($"    {node.Id,-8} x={Show(node.X),8} y={Show(node.Y),8} {Show(node.Width)}×{Show(node.Height)}");
        }

        Console.WriteLine("  折线：");
        foreach (var edge in result.Edges)
        {
            Console.WriteLine($"    {edge.Id,-14} {string.Join(" -> ", edge.Points.Select(point => $"({Show(point.X)},{Show(point.Y)})"))}");
        }
    }

    private static void PrintFailures(IReadOnlyList<Failure> failures)
    {
        if (failures.Count == 0)
        {
            return;
        }

        Console.WriteLine($"排不出来的（{failures.Count} 份，不计入综合分）：");

        foreach (var failure in failures)
        {
            Console.WriteLine($"  {failure.Id}（{failure.Note}）：{failure.Kind}");
            Console.WriteLine($"    {failure.Message}");
        }

        Console.WriteLine();
    }

    private static void PrintCards(IReadOnlyList<ScoreCard> cards)
    {
        Console.WriteLine("一、评分");
        Console.WriteLine($"  语料：{cards.Count} 份种子图排出来了，尺寸固定 80×40，间距取文档声明的那一组");
        Console.WriteLine();
        Console.WriteLine("  图                层   节点   边    重叠    交叉    紧凑    方向     综合");
        Console.WriteLine("  -------------------------------------------------------------------------");

        foreach (var card in cards)
        {
            var metrics = card.Metrics;

            Console.WriteLine(
                $"  {card.Id,-16}{metrics.LayerCount,3}{metrics.NodeCount,6}{metrics.EdgeCount,5}"
                + $"{Show(card.Overlap),8}{Show(card.Crossing),8}{Show(card.Compactness),8}"
                + $"{Show(card.Direction),8}{Show(card.Composite),9}");
        }

        Console.WriteLine();
    }

    /// <summary>
    /// 每个维度的原始数。
    /// </summary>
    /// <remarks>
    /// 分数是算出来的，原始数才是可复现的那一份——报告要能让人从这些数重算出分来。
    /// 分母一起印出来，是因为"零个重叠对"在八节点的图与三十节点的图里分量不一样。
    /// </remarks>
    private static void PrintDetails(IReadOnlyList<ScoreCard> cards)
    {
        Console.WriteLine("二、明细（每个维度的原始数）");
        Console.WriteLine();
        Console.WriteLine("  图               重叠对/对数   交叉对/对数  穿节点/边数     参照面积     实际面积   前进/后退边");
        Console.WriteLine("  ------------------------------------------------------------------------------------------");

        foreach (var card in cards)
        {
            var metrics = card.Metrics;

            Console.WriteLine(
                $"  {card.Id,-16}{metrics.OverlappingPairs,6}/{metrics.NodePairCount,-8}"
                + $"{metrics.CrossingPairs,6}/{metrics.EdgePairCount,-8}"
                + $"{metrics.EdgesCrossingNodes,6}/{metrics.EdgeCount,-8}"
                + $"{Show(metrics.IdealArea),13}{Show(metrics.ActualArea),13}"
                + $"{metrics.ForwardEdges,7}/{metrics.BackwardEdges}");
        }

        Console.WriteLine();
    }

    private static void PrintTotals(
        IReadOnlyList<ScoreCard> cards,
        IReadOnlyList<Failure> failures,
        double weighted,
        double mean,
        double worst)
    {
        Console.WriteLine("三、口径与汇总");
        Console.WriteLine("  几何自检：6 组线段对全部通过（交叉那一项的判定本身先验一遍）");
        Console.WriteLine("  每个维度满分 100，综合分 = "
            + $"{Show(Rubric.WeightOverlap)}×重叠 + {Show(Rubric.WeightCrossing)}×交叉 "
            + $"+ {Show(Rubric.WeightCompactness)}×紧凑 + {Show(Rubric.WeightDirection)}×方向");
        Console.WriteLine($"  语料综合分（按节点数加权）= {Show(weighted)}");
        Console.WriteLine($"  简单平均 = {Show(mean)}    最低一份 = {Show(worst)}");

        if (failures.Count > 0)
        {
            Console.WriteLine($"  另有 {failures.Count} 份排不出来，未计入上面的综合分。");
        }

        var flagged = cards.Where(card => card.Composite < Rubric.PassThreshold).ToList();

        if (flagged.Count == 0)
        {
            Console.WriteLine("  排出来的那些里，没有哪一份低于阈值。");
        }
        else
        {
            Console.WriteLine("  低于阈值的那些：");
            foreach (var card in flagged)
            {
                Console.WriteLine($"    {card.Id}：{Show(card.Composite)}（{card.Note}）");
            }
        }

        Console.WriteLine();
    }

    /// <summary>
    /// 与人工评分对照。
    /// </summary>
    /// <remarks>
    /// 人工评分文件是可选的：没有它就把这一栏记成"未验"，**不生成一份占位的对照**。
    /// 与用户测试、对比测试同一口径——装置齐了不等于测过了，而没有数据时写一个数字出来，
    /// 后来的读者会以为验过了。
    /// </remarks>
    private static void PrintComparison(IReadOnlyList<ScoreCard> cards, string? ratingsPath)
    {
        Console.WriteLine("四、与人工评分对照：");

        if (ratingsPath is null)
        {
            Console.WriteLine("    未验——没有给 --ratings。装置只印自己的分，人那一侧没有数据。");
            Console.WriteLine();
            return;
        }

        if (!File.Exists(ratingsPath))
        {
            Console.WriteLine($"    未验——{ratingsPath} 不在。");
            Console.WriteLine();
            return;
        }

        var root = JsonNode.Parse(File.ReadAllText(ratingsPath)) as JsonObject;

        if (root is null)
        {
            Console.WriteLine($"    未验——{ratingsPath} 的根节点必须是一个对象。");
            Console.WriteLine();
            return;
        }

        var deltas = new List<double>();

        foreach (var card in cards)
        {
            if (root[card.Id] is not JsonValue value || !value.TryGetValue<double>(out var human))
            {
                Console.WriteLine($"    {card.Id,-16} 人工评分没给，跳过");
                continue;
            }

            var delta = card.Composite - human;
            deltas.Add(delta);
            Console.WriteLine($"    {card.Id,-16} 装置 {Show(card.Composite)}  人工 {Show(human)}  差 {Show(delta)}");
        }

        if (deltas.Count == 0)
        {
            Console.WriteLine("    未验——文件里没有一份对得上语料的图。");
            Console.WriteLine();
            return;
        }

        var meanAbsolute = deltas.Average(Math.Abs);
        Console.WriteLine($"    平均绝对差 = {Show(meanAbsolute)}（{deltas.Count} 份对上了）");

        var drifted = deltas.Where(delta => Math.Abs(delta) > DriftThreshold).ToList();
        Console.WriteLine(drifted.Count == 0
            ? $"    没有哪一份的差超过 {Show(DriftThreshold)}。"
            : $"    有 {drifted.Count} 份的差超过 {Show(DriftThreshold)}——记下来，不掩盖。");

        Console.WriteLine();
    }

    /// <summary>装置与人差到这个数以上就记一笔。</summary>
    private const double DriftThreshold = 15;

    /// <summary>语料综合分：按节点数加权。大图更难排，也更能说明问题。</summary>
    private static double WeightedComposite(IReadOnlyList<ScoreCard> cards)
    {
        var total = cards.Sum(card => card.Metrics.NodeCount);

        if (total == 0)
        {
            return 0;
        }

        return cards.Sum(card => card.Composite * card.Metrics.NodeCount) / total;
    }

    private static string Show(double value) =>
        value.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>取 <c>--名字 值</c> 里的值。没给返回空。</summary>
    private static string? Value(string[] args, string name)
    {
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}

/// <summary>
/// 一份排不出来的种子图。
/// </summary>
/// <param name="Id">图标识。</param>
/// <param name="Note">形状。</param>
/// <param name="Kind">异常类型名。异常在布局层被当成"引擎自己出错了"往下传，这里记的是最终那一个。</param>
/// <param name="Message">异常消息。</param>
internal sealed record Failure(string Id, string Note, string Kind, string Message);
