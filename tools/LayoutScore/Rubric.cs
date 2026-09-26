using DuetDiagram.Core.Model;
using DuetDiagram.Layout;

namespace LayoutScore;

/// <summary>
/// 一次布局量出来的原始数。
/// </summary>
/// <remarks>
/// <para>
/// 只装**从结果里数得出来的量**，不装任何分数。分数由 <see cref="Rubric"/> 从这些数算出来，
/// 这样换一套权重或换一个阈值都不必重新跑布局——而重跑布局是这里唯一不确定的一步。
/// </para>
/// <para>
/// 所有计数都是整数、所有面积都是同一批坐标算出来的和，所以同一份布局结果两次测量逐位相同。
/// </para>
/// </remarks>
/// <param name="NodeCount">节点数。</param>
/// <param name="EdgeCount">边数。</param>
/// <param name="LayerCount">按主方向分出来的层数。由节点在结果里的坐标聚类得到。</param>
/// <param name="NodePairCount">节点两两配对数。重叠率的分母。</param>
/// <param name="OverlappingPairs">互相压住的节点对数。</param>
/// <param name="EdgePairCount">边两两配对数。交叉率的分母。</param>
/// <param name="CrossingPairs">折线互相穿过的边对数。</param>
/// <param name="EdgesCrossingNodes">折线穿过其它节点的边数。由布局诊断给出。</param>
/// <param name="IdealArea">按这次请求的间距、照实际分层排下来，外接框最小能到多少。</param>
/// <param name="ActualArea">实际外接框的面积。</param>
/// <param name="ForwardEdges">沿主方向前进的边数。</param>
/// <param name="BackwardEdges">逆主方向倒退的边数。</param>
internal sealed record LayoutMetrics(
    int NodeCount,
    int EdgeCount,
    int LayerCount,
    int NodePairCount,
    int OverlappingPairs,
    int EdgePairCount,
    int CrossingPairs,
    int EdgesCrossingNodes,
    double IdealArea,
    double ActualArea,
    int ForwardEdges,
    int BackwardEdges);

/// <summary>
/// 一个图跑完布局之后的评分卡。
/// </summary>
/// <param name="Id">图的标识。</param>
/// <param name="Note">这个图是什么形状，一句话。</param>
/// <param name="Metrics">量出来的原始数。</param>
/// <param name="Overlap">重叠维度得分，0 到 100。</param>
/// <param name="Crossing">交叉维度得分，0 到 100。</param>
/// <param name="Compactness">紧凑度维度得分，0 到 100。</param>
/// <param name="Direction">方向一致维度得分，0 到 100。</param>
/// <param name="Composite">加权综合分，0 到 100。</param>
internal sealed record ScoreCard(
    string Id,
    string Note,
    LayoutMetrics Metrics,
    double Overlap,
    double Crossing,
    double Compactness,
    double Direction,
    double Composite);

/// <summary>
/// 评分口径。
/// </summary>
/// <remarks>
/// <para>
/// **四个维度与它们的权重写死在这里**，与覆盖率取证装置同一条口径：装置本身比数字重要。
/// 换一个人跑同一个装置应得到同一个数，而"这个数是怎么来的"要能追到具体维度与具体节点。
/// </para>
/// <para>
/// 四个维度各回答一个"图看起来糟不糟"的问题，都是**从布局结果直接数出来的**，
/// 不需要人看、不需要模型判、不依赖时钟或哈希顺序：
/// </para>
/// <list type="number">
/// <item><b>重叠</b>：有没有节点压在一起。这是最刺眼的缺陷，权重与交叉并列最高。</item>
/// <item><b>交叉</b>：连线之间穿来穿去、连线穿过无关节点。折线图最难读的就是这个。</item>
/// <item><b>紧凑度</b>：外接框有没有比"照实际分层按请求间距排下来"更空。空得越多越像散架。</item>
/// <item><b>方向一致</b>：边是不是都顺着主方向走。逆着走的边要绕一大圈才画得出来。</item>
/// </list>
/// <para>
/// 每个维度的分都是 100 减掉"不合格的比例"，所以满分 100 的含义是"这一项一个毛病都没有"。
/// 综合分是加权平均，权重见 <see cref="WeightOverlap"/> 等常数，四者之和为 1。
/// </para>
/// <para>
/// **两个维度量的是图本身的形状，不是布局的毛病**，所以它们的低分要照实读：
/// 方向一致在带回边的图上本来就低（循环流程图就有回边），紧凑度在扇形图上本来就低
/// （十二条分支中间那一大片空白是形状决定的）。报告里逐图列出，看图的人知道低分是哪来的，
/// 而不是以为引擎算错了。
/// </para>
/// </remarks>
internal static class Rubric
{
    /// <summary>综合分要达到这个数才算通过。</summary>
    public const double PassThreshold = 85;

    public const double WeightOverlap = 0.30;

    public const double WeightCrossing = 0.30;

    public const double WeightCompactness = 0.20;

    public const double WeightDirection = 0.20;

    /// <summary>几何比较用的容差。与布局层量重叠用的是同一个数量级。</summary>
    private const double Epsilon = 1e-9;

    /// <summary>量一份布局结果。</summary>
    /// <param name="result">布局结果。</param>
    /// <param name="options">这次布局用的选项。紧凑度的参照要用它请求的间距。</param>
    public static LayoutMetrics Measure(EngineLayoutResult result, LayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(options);

        var nodes = result.Nodes;
        var edges = result.Edges;

        var overlapping = 0;
        for (var i = 0; i < nodes.Length; i++)
        {
            for (var j = i + 1; j < nodes.Length; j++)
            {
                if (nodes[i].Overlaps(nodes[j]))
                {
                    overlapping++;
                }
            }
        }

        var crossing = 0;
        for (var i = 0; i < edges.Length; i++)
        {
            for (var j = i + 1; j < edges.Length; j++)
            {
                if (PolylinesCross(edges[i].Points, edges[j].Points))
                {
                    crossing++;
                }
            }
        }

        var layers = Layers(result, options);
        var (forward, backward) = DirectionCounts(result, options.Direction);

        return new LayoutMetrics(
            nodes.Length,
            edges.Length,
            layers.Count,
            Pairs(nodes.Length),
            overlapping,
            Pairs(edges.Length),
            crossing,
            result.Diagnostics.EdgesCrossingNodes,
            IdealArea(layers, options),
            result.Width * result.Height,
            forward,
            backward);
    }

    /// <summary>重叠维度：没有被压住的节点对就是满分。</summary>
    public static double OverlapScore(LayoutMetrics metrics) =>
        100 * (1 - Ratio(metrics.OverlappingPairs, metrics.NodePairCount));

    /// <summary>交叉维度：折线之间不互穿、也不穿无关节点就是满分。</summary>
    /// <remarks>
    /// 两项相乘而不是相加：它们是两个独立的原因（边与边之间、边与节点之间），
    /// 相加的话一边全错、另一边全对，得到的分与两边各错一半一样——那是两回事。
    /// </remarks>
    public static double CrossingScore(LayoutMetrics metrics) =>
        100 * (1 - Ratio(metrics.CrossingPairs, metrics.EdgePairCount))
            * (1 - Ratio(metrics.EdgesCrossingNodes, metrics.EdgeCount));

    /// <summary>紧凑度维度：外接框不比"照实际分层排下来"更大就是满分。</summary>
    /// <remarks>
    /// <para>
    /// 参照不是"节点总面积"，而是**拿实际分出来的层，按这次请求的间距重算一遍最小外接框**。
    /// 用节点总面积当参照的话，量到的是"这个图的形状密不密"——十二条分支的扇形图中间
    /// 本来就空着一大片，那是形状决定的，不是布局排松了。用重算的外接框当参照，
    /// 量到的才是"布局有没有比它该占的地方占得更多"。
    /// </para>
    /// <para>
    /// 实际比参照小是允许的（引擎把某些层压得更紧），按满分算——这一项只罚松，不罚紧。
    /// </para>
    /// </remarks>
    public static double CompactnessScore(LayoutMetrics metrics)
    {
        if (metrics.ActualArea <= 0 || metrics.IdealArea <= 0)
        {
            return 100;
        }

        return 100 * Math.Min(1, metrics.IdealArea / metrics.ActualArea);
    }

    /// <summary>方向一致维度：边都顺着主方向走就是满分。</summary>
    public static double DirectionScore(LayoutMetrics metrics) =>
        100 * Ratio(metrics.ForwardEdges, metrics.ForwardEdges + metrics.BackwardEdges);

    /// <summary>加权综合分。</summary>
    public static double Composite(LayoutMetrics metrics) =>
        (WeightOverlap * OverlapScore(metrics))
        + (WeightCrossing * CrossingScore(metrics))
        + (WeightCompactness * CompactnessScore(metrics))
        + (WeightDirection * DirectionScore(metrics));

    /// <summary>给一个图打分。</summary>
    /// <param name="id">图的标识。</param>
    /// <param name="note">这个图是什么形状。</param>
    /// <param name="result">布局结果。</param>
    /// <param name="options">这次布局用的选项。</param>
    public static ScoreCard Score(string id, string note, EngineLayoutResult result, LayoutOptions options)
    {
        var metrics = Measure(result, options);

        return new ScoreCard(
            id,
            note,
            metrics,
            OverlapScore(metrics),
            CrossingScore(metrics),
            CompactnessScore(metrics),
            DirectionScore(metrics),
            Composite(metrics));
    }

    /// <summary>几何自检。返回空表示全部通过。</summary>
    /// <remarks>
    /// <para>
    /// **一个从没数出过东西的计数器不算验过。** 语料上跑出来交叉是零，那可能是引擎排得好，
    /// 也可能是这个判定写反了——两者在报告上长得一模一样。所以这里拿六组手算得出的线段对
    /// 把判定本身验一遍：一组该判成穿过，五组不该。
    /// </para>
    /// <para>
    /// 五组"不该"覆盖的正是正交路由的常见形态：平行、共端点、共线压在一起、T 形接上、离得远。
    /// 判据改了而自检没跟着改的话，这里会立刻报出来。
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> SelfCheck()
    {
        var failures = new List<string>();

        Check("两条线段交叉成 X", true, P(0, 0), P(10, 10), P(0, 10), P(10, 0));
        Check("平行但不重合", false, P(0, 0), P(10, 0), P(0, 5), P(10, 5));
        Check("共用一个端点（拐角）", false, P(0, 0), P(10, 0), P(10, 0), P(10, 10));
        Check("共线压在一起（主干分股）", false, P(0, 0), P(10, 0), P(5, 0), P(15, 0));
        Check("T 形接上", false, P(0, 0), P(10, 0), P(5, 0), P(5, 10));
        Check("离得远", false, P(0, 0), P(1, 1), P(5, 5), P(6, 6));

        return failures;

        void Check(string name, bool expected, LayoutPoint a1, LayoutPoint a2, LayoutPoint b1, LayoutPoint b2)
        {
            var actual = SegmentsCross(a1, a2, b1, b2);

            if (actual != expected)
            {
                failures.Add($"{name}：期望 {(expected ? "穿过" : "不穿过")}，实际 {(actual ? "穿过" : "不穿过")}");
            }
        }

        static LayoutPoint P(double x, double y) => new(x, y);
    }

    /// <summary>按主方向把节点分成层。</summary>
    /// <remarks>
    /// 布局结果里没有层号，只有坐标——所以层要自己认。判据是**在主方向那个轴上的区间有没有
    /// 重叠**：同一层的节点被引擎对齐到同一个区间，不同层之间隔着层间距，两段的区间不重叠。
    /// 用区间重叠而不是比较坐标相等：坐标是浮点数，比相等会把同一层拆成好几层，
    /// 而层数一多参照面积就虚高，紧凑度会凭空掉分。
    /// </remarks>
    private static List<List<PlacedNode>> Layers(EngineLayoutResult result, LayoutOptions options)
    {
        var vertical = options.RanksAreVertical;
        var ordered = result.Nodes
            .OrderBy(node => vertical ? node.Y : node.X)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToList();

        var layers = new List<List<PlacedNode>>();
        var end = double.NegativeInfinity;

        foreach (var node in ordered)
        {
            var start = vertical ? node.Y : node.X;
            var bottom = vertical ? node.Bottom : node.Right;

            if (layers.Count == 0 || start >= end - Epsilon)
            {
                layers.Add([]);
                end = bottom;
            }
            else
            {
                end = Math.Max(end, bottom);
            }

            layers[^1].Add(node);
        }

        return layers;
    }

    /// <summary>照实际分层、按请求间距排下来，外接框最小能到多少。</summary>
    private static double IdealArea(IReadOnlyList<List<PlacedNode>> layers, LayoutOptions options)
    {
        if (layers.Count == 0)
        {
            return 0;
        }

        var vertical = options.RanksAreVertical;
        var along = 0.0;
        var across = 0.0;

        foreach (var layer in layers)
        {
            along += layer.Max(node => vertical ? node.Height : node.Width);
            across = Math.Max(across, Span(layer, vertical, options.NodeSpacing));
        }

        along += (layers.Count - 1) * options.LayerSpacing;

        return along * across;
    }

    /// <summary>一层的节点在横跨方向上一字排开要占多少。</summary>
    private static double Span(IReadOnlyList<PlacedNode> layer, bool vertical, double spacing)
    {
        var sizes = layer.Select(node => vertical ? node.Width : node.Height).ToList();

        return sizes.Sum() + (Math.Max(0, sizes.Count - 1) * spacing);
    }

    /// <summary>
    /// 数边是顺着主方向走还是逆着走。
    /// </summary>
    /// <remarks>
    /// 只看折线首尾两点在**主方向那个轴**上的先后，不看中途怎么绕——绕路是路由的事，
    /// 而这一项量的是"这条边在层与层之间是往下还是往上"。
    /// </remarks>
    private static (int Forward, int Backward) DirectionCounts(EngineLayoutResult result, Direction direction)
    {
        var forward = 0;
        var backward = 0;

        foreach (var edge in result.Edges)
        {
            if (edge.Points.Length < 2)
            {
                continue;
            }

            var from = edge.Points[0];
            var to = edge.Points[^1];
            var delta = direction is Direction.TB or Direction.BT ? to.Y - from.Y : to.X - from.X;

            var isForward = direction switch
            {
                Direction.TB => delta > 0,
                Direction.BT => delta < 0,
                Direction.LR => delta > 0,
                _ => delta < 0,
            };

            if (isForward)
            {
                forward++;
            }
            else if (Math.Abs(delta) > Epsilon)
            {
                backward++;
            }
        }

        return (forward, backward);
    }

    /// <summary>两条折线有没有相交。</summary>
    private static bool PolylinesCross(LayoutPoint[] first, LayoutPoint[] second)
    {
        for (var i = 0; i + 1 < first.Length; i++)
        {
            for (var j = 0; j + 1 < second.Length; j++)
            {
                if (SegmentsCross(first[i], first[i + 1], second[j], second[j + 1]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 两条线段有没有真正穿过。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 判据是"交点落在两条线段各自的内部"。下面三种都不算：
    /// </para>
    /// <list type="bullet">
    /// <item><b>共线压在一起</b>：从同一个节点出去的多条边常常先共一段再分开，
    /// 那是一条主干分成几股，是正常画法。</item>
    /// <item><b>T 形接上</b>：一条边的端头顶在另一条的中间，看起来是一个分叉。</item>
    /// <item><b>共用一个端点</b>：两条边接在同一个节点上。</item>
    /// </list>
    /// <para>
    /// 只数真正的穿过，是因为另外三种都不影响"这条线往哪儿走"这个判断——
    /// 而交叉这一项要量的正是那个。
    /// </para>
    /// </remarks>
    private static bool SegmentsCross(LayoutPoint a1, LayoutPoint a2, LayoutPoint b1, LayoutPoint b2) =>
        Opposite(Cross(b1, b2, a1), Cross(b1, b2, a2))
        && Opposite(Cross(a1, a2, b1), Cross(a1, a2, b2));

    /// <summary>两个叉积的符号是不是相反。任一个为零时不算——那种情形是共线或端点相接。</summary>
    private static bool Opposite(double first, double second) =>
        (first > Epsilon && second < -Epsilon) || (first < -Epsilon && second > Epsilon);

    private static double Cross(LayoutPoint a, LayoutPoint b, LayoutPoint c) =>
        ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));

    private static int Pairs(int count) => count < 2 ? 0 : count * (count - 1) / 2;

    private static double Ratio(int numerator, int denominator) =>
        denominator == 0 ? 0 : Math.Clamp((double)numerator / denominator, 0, 1);
}
