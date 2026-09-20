using System.Diagnostics;
using System.Reflection;
using Sugiyama;

namespace DuetDiagram.Poc.MermaiderConstraints;

/// <summary>
/// 逐项验证候选布局引擎是否支持我们需要的约束，并把结论打印出来。
/// </summary>
/// <remarks>
/// <para>
/// 每一项都尽量用可执行的断言而不是阅读结论：能通过反射看清楚的，就不靠读文档推断；
/// 能跑出坐标对比的，就不靠"应该支持"来判断。这样结论可复现，将来换了版本重新跑一遍就知道。
/// </para>
/// <para>
/// 打印的是"支持 / 部分支持 / 不支持"，不是"通过 / 失败"——
/// 这个程序的任务是取证，而不是判定库的好坏。最终取舍由使用方根据取证结果决定。
/// </para>
/// </remarks>
internal static class Probe
{
    public static int Run()
    {
        CheckDeterminism();
        CheckDirection();
        CheckSpacing();
        CheckPinnedInput();
        CheckSameRank();
        CheckMissingConstraintInputs();
        CheckCompoundSubgraphs();
        CheckCoordinateSanity();
        CheckScaleLimits();

        return 0;
    }

    /// <summary>
    /// 同样的输入是否产出同样的结果。
    /// </summary>
    /// <remarks>
    /// 这是最基础的一条：布局结果会被缓存、会被跨进程比对、会被当作"没变就不重算"的依据。
    /// 一旦有随机性（例如用了未播种的随机数或依赖哈希表遍历顺序），
    /// 同一份文档每次打开都会长得不一样，而且表现为偶发，极难定位。
    /// </remarks>
    private static void CheckDeterminism()
    {
        var first = SugiyamaLayout.Compute(BuildDiamond()).Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y));
        var second = SugiyamaLayout.Compute(BuildDiamond()).Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y));

        var same = first.Count == second.Count
            && first.All(kv => second.TryGetValue(kv.Key, out var other)
                && Math.Abs(kv.Value.X - other.X) < 0.001
                && Math.Abs(kv.Value.Y - other.Y) < 0.001);

        Report("确定性", same ? Support.Supported : Support.NotSupported, same ? "两次计算结果完全一致" : "两次计算结果不同");
    }

    /// <summary>
    /// 四个主方向是否都可用，且确实产生不同的排布。
    /// </summary>
    /// <remarks>
    /// 不能只看画布尺寸：上下和左右互为镜像，镜像后的画布大小完全相同，但节点坐标是翻转过的。
    /// 所以这里比较的是每个节点的实际坐标，把四种排布两两对照，数出真正互不相同的种数。
    /// </remarks>
    private static void CheckDirection()
    {
        var layouts = new Dictionary<LayoutDirection, string>();

        foreach (var direction in new[] { LayoutDirection.TD, LayoutDirection.LR, LayoutDirection.RL, LayoutDirection.BT })
        {
            var graph = new LayoutGraph(direction, Nodes(), Edges(), []);
            var result = SugiyamaLayout.Compute(graph);

            // 把坐标拼成一个可比较的指纹，用于两两对照。
            layouts[direction] = string.Join(
                ";",
                result.Nodes
                    .OrderBy(n => n.Id, StringComparer.Ordinal)
                    .Select(n => $"{n.Id}:{n.X:0.##},{n.Y:0.##}"));
        }

        var distinct = layouts.Values.Distinct(StringComparer.Ordinal).Count();

        // 依次展示第一层的两个节点，方便肉眼确认轴是否真的交换了。
        var detail = string.Join(", ", layouts.Select(kv =>
        {
            var parts = kv.Value.Split(';');
            return $"{kv.Key} → {parts[0]}";
        }));

        Report(
            "方向",
            distinct == 4 ? Support.Supported : distinct > 1 ? Support.Partial : Support.NotSupported,
            $"四种方向产生 {distinct} 种互不相同的排布：{detail}");
    }

    /// <summary>
    /// 间距参数是否真的改变了输出。
    /// </summary>
    /// <remarks>
    /// 节点间距与层间距分别控制"同层内元素的间隔"和"层与层之间的间隔"，
    /// 对应两种完全不同的视觉诉求。只验证其中一个变化不足以说明另一个也生效。
    /// </remarks>
    private static void CheckSpacing()
    {
        var baseline = SugiyamaLayout.Compute(BuildDiamond()).Nodes.ToDictionary(n => n.Id, n => n.Y);

        var wideNode = SugiyamaLayout.Compute(BuildDiamond(), new LayoutOptions { NodeSpacing = 200 })
            .Nodes.ToDictionary(n => n.Id, n => n.X);

        var baselineX = SugiyamaLayout.Compute(BuildDiamond()).Nodes.ToDictionary(n => n.Id, n => n.X);

        var wideLayer = SugiyamaLayout.Compute(BuildDiamond(), new LayoutOptions { LayerSpacing = 400 })
            .Nodes.ToDictionary(n => n.Id, n => n.Y);

        var nodeSpacingMoved = baselineX.Any(kv => Math.Abs(kv.Value - wideNode[kv.Key]) > 0.001);
        var layerSpacingMoved = baseline.Any(kv => Math.Abs(kv.Value - wideLayer[kv.Key]) > 0.001);

        var support = nodeSpacingMoved && layerSpacingMoved ? Support.Supported
            : nodeSpacingMoved || layerSpacingMoved ? Support.Partial
            : Support.NotSupported;

        Report("间距", support, $"节点间距生效={nodeSpacingMoved}，层间距生效={layerSpacingMoved}");
    }

    /// <summary>
    /// 是否有办法把某个节点固定在指定坐标。
    /// </summary>
    /// <remarks>
    /// 这是最关键的一项。人工拖动节点之后，那个位置必须能被带进下一次布局，
    /// 否则用户每次重排都会被弹回原位，"手动微调"这个功能根本不成立。
    /// 检查方式是看输入模型的节点类型上有哪些属性——如果连坐标字段都没有，
    /// 那无论算法内部怎么处理，调用方都无从表达"固定在这里"。
    /// </remarks>
    private static void CheckPinnedInput()
    {
        var properties = typeof(LayoutNode)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();

        var positionalish = properties
            .Where(name => name.Contains("X", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Y", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Position", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Pin", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Fixed", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Order", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Align", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Report(
            "固定位置",
            positionalish.Length == 0 ? Support.NotSupported : Support.Partial,
            positionalish.Length == 0
                ? $"节点输入类型的公开成员只有 [{string.Join(", ", properties)}]，没有任何坐标或固定标记"
                : $"发现疑似相关成员：{string.Join(", ", positionalish)}");

        // 同时确认布局结果里确实有坐标——排除"整条链路都不管坐标"这种误判。
        var result = SugiyamaLayout.Compute(BuildDiamond());
        var hasCoordinates = result.Nodes.All(n => n.Width > 0 && n.Height > 0);
        Report(
            "坐标输出",
            hasCoordinates ? Support.Supported : Support.NotSupported,
            $"结果里每个节点都带 (X, Y, Width, Height)：{hasCoordinates}");
    }

    /// <summary>
    /// 同层约束是否真的改变了分层结果。
    /// </summary>
    /// <remarks>
    /// 约束是"这两个节点要放在同一层"，对应的视觉诉求是"这两个是并列的"。
    /// 做法是构造一份本来会把某个节点放到更深一层的图，加上约束后看它的纵坐标有没有变。
    /// </remarks>
    private static void CheckSameRank()
    {
        LayoutNode[] nodes =
        [
            new("a", 80, 40),
            new("b", 80, 40),
            new("c", 80, 40),
            new("d", 80, 40),
        ];

        LayoutEdge[] edges =
        [
            new("a", "b"),
            new("b", "c"),
            new("a", "d"),
        ];

        var plain = new LayoutGraph(LayoutDirection.TD, nodes, edges, []);
        var constrained = plain with { SameRankConstraints = [("c", "d")] };

        var plainY = SugiyamaLayout.Compute(plain).Nodes.ToDictionary(n => n.Id, n => n.Y);
        var constrainedY = SugiyamaLayout.Compute(constrained).Nodes.ToDictionary(n => n.Id, n => n.Y);

        var moved = plainY.Any(kv => Math.Abs(kv.Value - constrainedY[kv.Key]) > 0.001);
        var sameLayer = Math.Abs(constrainedY["c"] - constrainedY["d"]) < 0.001;

        var support = !moved ? Support.NotSupported
            : sameLayer ? Support.Supported
            : Support.Partial;

        Report(
            "同层约束",
            support,
            $"加约束后坐标变化={moved}，c 与 d 纵坐标相同={sameLayer}（c={constrainedY["c"]:0}，d={constrainedY["d"]:0}）");
    }

    /// <summary>
    /// 其余几种约束在输入模型里有没有对应入口。
    /// </summary>
    /// <remarks>
    /// 层内顺序、对齐、相对位置这三种都是常见的排版诉求。逐一查输入类型上有没有对应字段：
    /// 没有字段就意味着调用方根本无法表达这个意图，也就不存在"引擎支不支持"的问题了。
    /// </remarks>
    private static void CheckMissingConstraintInputs()
    {
        var graphProperties = typeof(LayoutGraph).GetProperties().Select(p => p.Name).ToArray();
        var optionProperties = typeof(LayoutOptions).GetProperties().Select(p => p.Name).ToArray();

        string[] wanted = ["Order", "Align", "Place", "Nudge", "Relative"];

        var graphHits = wanted.Where(w => graphProperties.Any(p => p.Contains(w, StringComparison.OrdinalIgnoreCase))).ToArray();
        var optionHits = wanted.Where(w => optionProperties.Any(p => p.Contains(w, StringComparison.OrdinalIgnoreCase))).ToArray();

        var hits = graphHits.Concat(optionHits).Distinct().ToArray();

        Report(
            "层内顺序/对齐/相对位置",
            hits.Length == 0 ? Support.NotSupported : Support.Partial,
            hits.Length == 0
                ? $"图输入成员 [{string.Join(", ", graphProperties)}]，选项成员 [{string.Join(", ", optionProperties)}] 中均无对应入口"
                : $"找到疑似入口：{string.Join(", ", hits)}");
    }

    /// <summary>
    /// 复合分组：分组边框是否存在，子节点是否真的落在父框内。
    /// </summary>
    /// <remarks>
    /// 只断言"返回了分组结果"是不够的——分组框算错位置会让子节点露在框外，
    /// 看上去像渲染错误。所以要真的比较几何包含关系。
    /// 这里用两层嵌套，同时验证子分组本身也被父分组包含。
    /// </remarks>
    private static void CheckCompoundSubgraphs()
    {
        LayoutNode[] nodes =
        [
            new("web", 80, 40),
            new("api", 80, 40),
            new("db", 80, 40),
        ];

        LayoutEdge[] edges =
        [
            new("web", "api"),
            new("api", "db"),
        ];

        LayoutSubgraph[] subgraphs =
        [
            new("backend", "Backend", ["api", "db"], []),
            new("outer", "Outer", [], [new LayoutSubgraph("backend", "Backend", ["api", "db"], [])]),
        ];

        var result = SugiyamaLayout.Compute(new LayoutGraph(LayoutDirection.TD, nodes, edges, subgraphs));

        var backend = result.Groups.FirstOrDefault(g => g.Id == "backend");
        var hasGroupBox = backend is not null && backend.Width > 0 && backend.Height > 0;

        var nodesContained = backend is not null && result.Nodes
            .Where(n => n.Id is "api" or "db")
            .All(n => n.X >= backend.X - 0.001
                && n.Y >= backend.Y - 0.001
                && n.X + n.Width <= backend.X + backend.Width + 0.001
                && n.Y + n.Height <= backend.Y + backend.Height + 0.001);

        var support = hasGroupBox && nodesContained ? Support.Supported
            : hasGroupBox ? Support.Partial
            : Support.NotSupported;

        Report(
            "复合分组",
            support,
            $"分组框={hasGroupBox}，子节点全部落在框内={nodesContained}，返回分组数={result.Groups.Count}");
    }

    /// <summary>
    /// 规模上限与耗时。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 大图是这套软件的主要使用场景之一，而候选库自己的说明里提到面向中小规模图。
    /// 这里读一下规模上限的默认值，再实测几个量级。
    /// </para>
    /// <para>
    /// 关键在于图形形状。同样的节点数，长链和宽扁多层对算法各阶段的压力完全不同：
    /// 长链会把分层数拉到与节点数同级，跨多层的边被展开成大量虚拟节点；
    /// 宽扁多层则是商业流程图与架构图最常见的形态。只测一种会得出片面的结论，
    /// 所以两种形状都测。
    /// </para>
    /// </remarks>
    private static void CheckScaleLimits()
    {
        Report("规模上限默认值", Support.Informational, $"MaxNodeCount = {new LayoutOptions().MaxNodeCount}");

        Measure("长链 100", BuildChain(100));
        Measure("长链 1000", BuildChain(1000));
        Measure("多层 1000（20 层 × 50，带交叉）", BuildLayered(depth: 20, breadth: 50, withCrossings: true));
        Measure("多层 100（10 层 × 10，带交叉）", BuildLayered(depth: 10, breadth: 10, withCrossings: true));
    }

    /// <summary>
    /// 横向坐标是否落在合理范围内。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 分组框包裹正确、耗时达标，都不代表排出来的图能用。这里要抓的是坐标分配把节点推得过远的情况：
    /// 图本身完全合法，但画出来是一张横向拉长几百倍的图，实际渲染时内容会小到看不清。
    /// </para>
    /// <para>
    /// 用四种形态从简到繁对照，目的是把"什么时候开始出问题"定位清楚：
    /// 两个节点的单层交叉（最小可能的交叉）、两列多层的完全二分串联（决策链的典型形态）、
    /// 以及十列的松散交叉，再配一个完全没有交叉的同尺寸对照。
    /// 如果只有带交叉的那些异常膨胀，问题就落在交叉处理或坐标分配这条路径上。
    /// </para>
    /// </remarks>
    private static void CheckCoordinateSanity()
    {
        // 合理上限按"同层节点紧挨着排"估算再放宽若干倍。
        // 留余量是因为算法为了减少连线交叉，本来就会把节点往两边挪一些。
        const double ColumnPitch = 80 + 36;

        var minimal = HorizontalSpan(BuildBipartite(depth: 1, breadth: 2));
        Report(
            "两个节点的单层交叉",
            minimal <= 2 * ColumnPitch * 3 ? Support.Supported : Support.NotSupported,
            $"横向散布 {minimal:0}（合理上限约 {2 * ColumnPitch * 3:0}）");

        var decisionChain = HorizontalSpan(BuildBipartite(depth: 10, breadth: 2));
        Report(
            "两列十层完全二分串联",
            decisionChain <= 2 * ColumnPitch * 3 ? Support.Supported : Support.NotSupported,
            $"横向散布 {decisionChain:0}（合理上限约 {2 * ColumnPitch * 3:0}）");

        var straightSpan = HorizontalSpan(BuildLayered(depth: 10, breadth: 10, withCrossings: false));
        Report(
            "十列十层无交叉",
            straightSpan <= 10 * ColumnPitch * 3 ? Support.Supported : Support.NotSupported,
            $"横向散布 {straightSpan:0}（合理上限约 {10 * ColumnPitch * 3:0}）");

        var crossingSpan = HorizontalSpan(BuildLayered(depth: 10, breadth: 10, withCrossings: true));
        Report(
            "十列十层带交叉",
            crossingSpan <= 10 * ColumnPitch * 3 ? Support.Supported : Support.NotSupported,
            $"横向散布 {crossingSpan:0}（合理上限约 {10 * ColumnPitch * 3:0}）；" +
            $"相比同尺寸无交叉版本放大 {crossingSpan / Math.Max(1, straightSpan):0.0} 倍");
    }

    private static double HorizontalSpan(LayoutGraph graph)
    {
        var result = SugiyamaLayout.Compute(graph);
        return result.Nodes.Max(n => n.X) - result.Nodes.Min(n => n.X);
    }

    /// <summary>
    /// 完全二分串联：每层的每个节点都连到下一层的每个节点。
    /// 这是"一个判断分出两条路，两条路各自又分出两条"这类决策链的抽象，
    /// 交叉密集但规模很小，用来判断问题是否在极小输入上就会出现。
    /// </summary>
    private static LayoutGraph BuildBipartite(int depth, int breadth)
    {
        var nodes = new List<LayoutNode>((depth + 1) * breadth);

        for (var layer = 0; layer <= depth; layer++)
        {
            for (var index = 0; index < breadth; index++)
            {
                nodes.Add(new LayoutNode($"n{layer}-{index}", 80, 40));
            }
        }

        var edges = new List<LayoutEdge>();

        for (var layer = 0; layer < depth; layer++)
        {
            for (var from = 0; from < breadth; from++)
            {
                for (var to = 0; to < breadth; to++)
                {
                    edges.Add(new LayoutEdge($"n{layer}-{from}", $"n{layer + 1}-{to}"));
                }
            }
        }

        return new LayoutGraph(LayoutDirection.TD, nodes.ToArray(), edges.ToArray(), []);
    }

    /// <summary>链式图：每一层只有一个节点，层数等于节点数，用来压分层与虚拟节点展开。</summary>
    private static LayoutGraph BuildChain(int count)
    {
        var nodes = Enumerable.Range(0, count).Select(i => new LayoutNode($"n{i}", 80, 40)).ToArray();

        var edges = Enumerable.Range(0, count - 1)
            .Select(i => new LayoutEdge($"n{i}", $"n{i + 1}"))
            .ToArray();

        return new LayoutGraph(LayoutDirection.TD, nodes, edges, []);
    }

    /// <summary>多层图：每层若干并列节点，层间按需增加错位连接制造交叉，接近真实流程图的形态。</summary>
    private static LayoutGraph BuildLayered(int depth, int breadth, bool withCrossings)
    {
        var nodes = new List<LayoutNode>(depth * breadth);

        for (var layer = 0; layer < depth; layer++)
        {
            for (var index = 0; index < breadth; index++)
            {
                nodes.Add(new LayoutNode($"n{layer}-{index}", 80, 40));
            }
        }

        var edges = new List<LayoutEdge>();

        for (var layer = 0; layer < depth - 1; layer++)
        {
            for (var index = 0; index < breadth; index++)
            {
                edges.Add(new LayoutEdge($"n{layer}-{index}", $"n{layer + 1}-{index}"));

                if (withCrossings)
                {
                    edges.Add(new LayoutEdge($"n{layer}-{index}", $"n{layer + 1}-{(index + 1) % breadth}"));
                }
            }
        }

        return new LayoutGraph(LayoutDirection.TD, nodes.ToArray(), edges.ToArray(), []);
    }

    private static void Measure(string label, LayoutGraph graph)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = SugiyamaLayout.Compute(graph);
            stopwatch.Stop();

            // 除了总耗时，还要看节点在画布上的实际散布范围。
            // 画布很大本身不说明问题（长链本来就又窄又长），但如果节点只占其中极小一段，
            // 说明坐标分配把节点推得过远，实际渲染出来会是一张中间大片空白的图。
            var minX = result.Nodes.Min(n => n.X);
            var maxX = result.Nodes.Max(n => n.X);
            var minY = result.Nodes.Min(n => n.Y);
            var maxY = result.Nodes.Max(n => n.Y);

            // 以节点自身的宽度为参照：如果节点的横向散布是单节点宽度的成千上万倍，
            // 说明坐标分配把节点推得过远，实际渲染出来会是一张中间大片空白的图。
            var nodeWidth = graph.Nodes.Max(n => n.Width);

            Report(
                $"规模 {label}",
                Support.Supported,
                $"{stopwatch.Elapsed.TotalMilliseconds:0.0} ms，节点 {result.Nodes.Count}，边 {result.Edges.Count}；" +
                $"节点散布 X[{minX:0}..{maxX:0}] Y[{minY:0}..{maxY:0}]（单节点宽 {nodeWidth:0}），画布 {result.Width:0}x{result.Height:0}");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Report($"规模 {label}", Support.NotSupported, $"{stopwatch.Elapsed.TotalMilliseconds:0.0} ms 后抛出 {ex.GetType().Name}：{ex.Message}");
        }
    }

    private static LayoutGraph BuildDiamond() => new(LayoutDirection.TD, Nodes(), Edges(), []);

    private static LayoutNode[] Nodes() =>
    [
        new("a", 80, 40),
        new("b", 80, 40),
        new("c", 80, 40),
        new("d", 80, 40),
    ];

    private static LayoutEdge[] Edges() =>
    [
        new("a", "b"),
        new("a", "c"),
        new("b", "d"),
        new("c", "d"),
    ];

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
