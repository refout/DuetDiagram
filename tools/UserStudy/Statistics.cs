namespace DuetDiagram.Tools.UserStudy;

/// <summary>
/// 四条判定要用到的统计量。
/// </summary>
/// <remarks>
/// <para>
/// **能算精确的地方不近似。** 三个被测项、十来个人的样本量下，卡方近似算出来的 p
/// 与精确分布能差好几个数量级，而这份数据唯一的用途就是决定被测项保不保。
/// 所以 Friedman 走的是**条件置换检验**：把每个评分者那一组秩的全部排列都过一遍，
/// 数出"统计量不低于实测值"的组合占多少。卡方那一档同时列出来做对照——
/// 两个数摆在一起，读的人自己看得出近似偏在哪边。
/// </para>
/// <para>
/// **并列按平均秩算，而且并列本身要进置换分布。** 评分是 1 到 5 的整数，并列是常态。
/// 把并列压成平均秩、却仍然按"没有并列"的全排列去算分布，得到的是一个两头都不对的数：
/// 统计量按有并列的算，参考分布按无并列的算。这里的做法是**每个评分者各自的秩向量**
/// 决定他自己那一组可能取值——他那组秩里有并列，可能的取值就少几种，
/// 而每种等可能。没有并列时它退化成教科书上的精确 Friedman 检验。
/// </para>
/// </remarks>
internal static class Statistics
{
    /// <summary>判两个浮点数算不算相等时用的容差。</summary>
    public const double Tolerance = 1e-9;

    #region 秩

    /// <summary>一组取值各自的秩，从 1 开始；并列的取这一组的平均秩。</summary>
    /// <remarks>
    /// 并列取平均秩而不是"先出现的排前面"：后者会让同一份数据换个输入次序就得到
    /// 不同的统计量，而"同一份数据永远同一组数"是这一层的硬要求。
    /// </remarks>
    public static double[] Ranks(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var order = Enumerable.Range(0, values.Count).OrderBy(index => values[index]).ToArray();
        var ranks = new double[values.Count];
        var start = 0;

        while (start < order.Length)
        {
            var end = start;

            while (end + 1 < order.Length && values[order[end + 1]] == values[order[start]])
            {
                end++;
            }

            // 位置 start 到 end 对应秩 start+1 到 end+1，取它们的平均。
            var average = ((start + 1) + (end + 1)) / 2.0;

            for (var index = start; index <= end; index++)
            {
                ranks[order[index]] = average;
            }

            start = end + 1;
        }

        return ranks;
    }

    /// <summary>并列校正项：一组秩里每个并列组算 <c>t³ − t</c>，加起来。</summary>
    /// <remarks>
    /// 它的用途是抵消"并列让秩的方差变小"这件事。没有并列时它是零，
    /// 两个校正公式也就都退化成教科书上的那一版。
    /// </remarks>
    public static double TieCorrection(IReadOnlyList<double> ranks)
    {
        ArgumentNullException.ThrowIfNull(ranks);

        var correction = 0.0;
        var start = 0;
        var sorted = ranks.OrderBy(rank => rank).ToArray();

        while (start < sorted.Length)
        {
            var end = start;

            while (end + 1 < sorted.Length && sorted[end + 1] == sorted[start])
            {
                end++;
            }

            var size = end - start + 1;

            correction += ((size * size * size) - size) / 1.0;

            start = end + 1;
        }

        return correction;
    }

    #endregion

    #region Friedman

    /// <summary>
    /// Friedman 检验。评分矩阵按"每个评分者一行、每个被测项一列"给。
    /// </summary>
    /// <param name="scores">评分矩阵。</param>
    /// <param name="exactLimit">被测项数不超过它时算精确分布，超过时只给卡方那一档。</param>
    /// <remarks>
    /// 精确分布按秩和的组合数算，被测项一多状态就爆炸——四个被测项时还只有几千个状态，
    /// 六个就到了几百万。所以超过四个就退回卡方近似，并在结果里标出来用的是哪一档：
    /// 拿一个算不准的数当判据，比明说"这一档是近似的"更糟。
    /// </remarks>
    public static FriedmanResult Friedman(IReadOnlyList<double[]> scores, int exactLimit = 4)
    {
        ArgumentNullException.ThrowIfNull(scores);

        if (scores.Count == 0)
        {
            throw new ArgumentException("没有评分者。", nameof(scores));
        }

        var raters = scores.Count;
        var arms = scores[0].Length;

        if (arms < 3)
        {
            throw new ArgumentException("Friedman 检验至少要三个方案。", nameof(scores));
        }

        var rankSums = new double[arms];
        var tieTotal = 0.0;

        foreach (var row in scores)
        {
            if (row.Length != arms)
            {
                throw new ArgumentException("每一行都要给全部方案的评分。", nameof(scores));
            }

            var ranks = Ranks(row);

            for (var arm = 0; arm < arms; arm++)
            {
                rankSums[arm] += ranks[arm];
            }

            tieTotal += TieCorrection(ranks);
        }

        var statistic = Spread(rankSums, raters, arms);
        var chiSquare = ChiSquare(statistic, raters, arms, tieTotal);
        var degreesOfFreedom = arms - 1;

        var exact = arms <= exactLimit
            ? ExactTail(scores, statistic)
            : (double?)null;

        return new FriedmanResult(
            statistic,
            chiSquare,
            degreesOfFreedom,
            ChiSquareUpperTail(chiSquare, degreesOfFreedom),
            exact,
            Exact: exact is not null);
    }

    /// <summary>各被测项秩和相对平均秩的离差平方和。Friedman 统计量就是它。</summary>
    private static double Spread(IReadOnlyList<double> rankSums, int raters, int arms)
    {
        var mean = raters * (arms + 1) / 2.0;
        var total = 0.0;

        foreach (var sum in rankSums)
        {
            total += (sum - mean) * (sum - mean);
        }

        return total;
    }

    /// <summary>并列校正后的卡方统计量。</summary>
    private static double ChiSquare(double spread, int raters, int arms, double tieTotal)
    {
        var raw = 12.0 * spread / (raters * (double)arms * (arms + 1));
        var denominator = raters * ((double)arms * arms * arms - arms);
        var correction = denominator <= 0 ? 1.0 : 1.0 - (tieTotal / denominator);

        // 校正因子为零意味着所有人都把三个被测项并列了——那时秩里没有信息，
        // 卡方给零，而"零除以零"会得到 NaN。
        return correction <= Tolerance ? 0.0 : raw / correction;
    }

    /// <summary>
    /// 精确分布那一侧：统计量不低于实测值的概率。
    /// </summary>
    /// <remarks>
    /// 状态是"各被测项秩和"的**多重集**（升序放）。S 与哪个被测项排在哪一列无关，
    /// 所以 (10,20,30) 与 (30,20,10) 是同一个状态，合成一个能省掉大半状态。
    /// 每个评分者可能贡献的那几种秩向量由他自己那一组秩的排列给出，每种等可能。
    /// </remarks>
    private static double? ExactTail(IReadOnlyList<double[]> scores, double observed)
    {
        var raters = scores.Count;
        var arms = scores[0].Length;

        var contributions = new List<double[][]>(raters);

        foreach (var row in scores)
        {
            var ranks = Ranks(row);
            var distinct = DistinctPermutations(ranks);

            contributions.Add(distinct);
        }

        // 状态用秩和的整数倍表示：秩可能是 x.5，乘二就都是整数。超过 int 的范围就放弃精确那一档。
        var scale = 2;
        var states = new Dictionary<long, double> { [Pack(new int[arms])] = 1.0 };

        foreach (var options in contributions)
        {
            var next = new Dictionary<long, double>();

            foreach (var (key, weight) in states)
            {
                var sums = Unpack(key, arms);

                foreach (var option in options)
                {
                    var moved = new int[arms];

                    for (var arm = 0; arm < arms; arm++)
                    {
                        moved[arm] = sums[arm] + (int)Math.Round(option[arm] * scale);
                    }

                    Array.Sort(moved);

                    var packed = Pack(moved);

                    next[packed] = next.GetValueOrDefault(packed) + weight;
                }
            }

            states = next;
        }

        var total = states.Values.Sum();
        var tail = 0.0;

        foreach (var (key, weight) in states)
        {
            var sums = Unpack(key, arms);
            var sumsAsDouble = new double[arms];

            for (var arm = 0; arm < arms; arm++)
            {
                sumsAsDouble[arm] = sums[arm] / (double)scale;
            }

            if (Spread(sumsAsDouble, raters, arms) >= observed - Tolerance)
            {
                tail += weight;
            }
        }

        return total <= 0 ? null : tail / total;
    }

    /// <summary>一组取值（可能有并列）的全部不同排列。</summary>
    private static double[][] DistinctPermutations(IReadOnlyList<double> values)
    {
        var found = new List<double[]>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = new double[values.Count];
        var used = new bool[values.Count];

        Walk(0);

        return [.. found];

        void Walk(int depth)
        {
            if (depth == values.Count)
            {
                var key = string.Join(",", current);

                if (seen.Add(key))
                {
                    found.Add([.. current]);
                }

                return;
            }

            for (var index = 0; index < values.Count; index++)
            {
                if (used[index])
                {
                    continue;
                }

                // 同一个取值只从第一个还没用掉的位置出发一次：并列的那些位置互换
                // 得到的是同一个排列，不去重的话并列越多算得越慢而结果不变。
                if (index > 0 && values[index] == values[index - 1] && !used[index - 1])
                {
                    continue;
                }

                used[index] = true;
                current[depth] = values[index];

                Walk(depth + 1);

                used[index] = false;
            }
        }
    }

    private static long Pack(int[] values)
    {
        var packed = 0L;

        foreach (var value in values)
        {
            packed = (packed << 16) | (uint)value;
        }

        return packed;
    }

    private static int[] Unpack(long packed, int count)
    {
        var values = new int[count];

        for (var index = count - 1; index >= 0; index--)
        {
            values[index] = (int)(packed & 0xFFFF);
            packed >>= 16;
        }

        return values;
    }

    #endregion

    #region Kendall's W

    /// <summary>
    /// Kendall's W：评分者之间的一致程度，取值 0 到 1。
    /// </summary>
    /// <remarks>
    /// 一表示所有人给出的名次完全一样，零表示各排各的。它是描述性的，
    /// 不带 p 值——判定门要的是"大家看法有多一致"这个数本身。
    /// </remarks>
    public static double KendallW(IReadOnlyList<double[]> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);

        if (scores.Count == 0)
        {
            throw new ArgumentException("没有评分者。", nameof(scores));
        }

        var raters = scores.Count;
        var arms = scores[0].Length;
        var rankSums = new double[arms];
        var tieTotal = 0.0;

        foreach (var row in scores)
        {
            var ranks = Ranks(row);

            for (var arm = 0; arm < arms; arm++)
            {
                rankSums[arm] += ranks[arm];
            }

            tieTotal += TieCorrection(ranks);
        }

        var denominator = ((double)raters * raters * (arms * arms * arms - arms)) - (raters * tieTotal);

        return denominator <= Tolerance ? 0.0 : 12.0 * Spread(rankSums, raters, arms) / denominator;
    }

    #endregion

    #region Wilcoxon 符号秩

    /// <summary>
    /// 两个被测项配对的 Wilcoxon 符号秩检验，双侧。
    /// </summary>
    /// <param name="left">前一个被测项的评分，按评分者顺序。</param>
    /// <param name="right">后一个被测项的评分，同一个评分者要对上同一个位置。</param>
    /// <remarks>
    /// **精确那一档按符号的全部组合数算，不是查表。** 差为零的对按惯例丢掉，
    /// 剩下的按 <c>|差|</c> 排序取平均秩；参考分布是"每个 |差| 各自带上正负号"的全部组合，
    /// 每种等可能。这样 <c>|差|</c> 里有并列也算得对——条件在观测到的那些 |差| 上，
    /// 并列只影响秩的大小，不影响"符号怎么分配"。
    /// </remarks>
    public static WilcoxonResult Wilcoxon(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Count != right.Count)
        {
            throw new ArgumentException("两个方案的评分要对上同一个评分者。", nameof(right));
        }

        var differences = new List<double>();

        for (var index = 0; index < left.Count; index++)
        {
            var difference = left[index] - right[index];

            // 差为零的对不提供方向信息，按惯例丢掉，样本量跟着变小。
            if (Math.Abs(difference) > Tolerance)
            {
                differences.Add(difference);
            }
        }

        if (differences.Count == 0)
        {
            return new WilcoxonResult(0, 0, 0, 1, Exact: true, Pairs: 0);
        }

        var magnitudes = differences.Select(Math.Abs).ToArray();
        var ranks = Ranks(magnitudes);

        var positive = 0.0;
        var negative = 0.0;

        for (var index = 0; index < differences.Count; index++)
        {
            if (differences[index] > 0)
            {
                positive += ranks[index];
            }
            else
            {
                negative += ranks[index];
            }
        }

        var statistic = Math.Min(positive, negative);
        var p = SignTail(ranks, statistic);

        return new WilcoxonResult(positive, negative, statistic, p, Exact: true, Pairs: differences.Count);
    }

    /// <summary>双侧精确 p：两倍的单侧尾，上限为一。</summary>
    private static double SignTail(IReadOnlyList<double> ranks, double statistic)
    {
        // 秩可能是 x.5，乘二之后是整数，于是"子集和不超过某个整数"可以直接数。
        var scaled = ranks.Select(rank => (int)Math.Round(rank * 2)).ToArray();
        var limit = (int)Math.Round(statistic * 2);
        var total = scaled.Sum();

        // 计数按"和为多少"分布。每个秩要么算进正号那一侧，要么算进负号那一侧。
        var counts = new double[total + 1];
        counts[0] = 1;

        var reachable = 0;

        foreach (var rank in scaled)
        {
            for (var sum = reachable; sum >= 0; sum--)
            {
                if (counts[sum] > 0)
                {
                    counts[sum + rank] += counts[sum];
                }
            }

            reachable += rank;
        }

        var tail = 0.0;

        for (var sum = 0; sum <= Math.Min(limit, total); sum++)
        {
            tail += counts[sum];
        }

        var all = Math.Pow(2, scaled.Length);

        return Math.Min(1.0, 2.0 * tail / all);
    }

    #endregion

    #region Holm-Bonferroni

    /// <summary>
    /// Holm-Bonferroni 逐步校正。
    /// </summary>
    /// <param name="pValues">这一族里全部比较的原始 p。</param>
    /// <param name="alpha">族错误率。判定门取 0.05。</param>
    /// <remarks>
    /// 阈值是 <c>alpha / (剩余比较数)</c>，逐步收紧而不是一律除以总比较数。
    /// 一律除以总数那是 Bonferroni，它比 Holm 保守；协议里点名的是 Holm。
    /// 逐步走到第一个不达标的就停，后面的全部算不达标——停下来的那一步说明
    /// 这一族里已经有说不清的东西了，继续往下比会把族错误率放大。
    /// </remarks>
    public static IReadOnlyList<HolmStep> Holm(IReadOnlyList<double> pValues, double alpha)
    {
        ArgumentNullException.ThrowIfNull(pValues);

        var order = Enumerable.Range(0, pValues.Count).OrderBy(index => pValues[index]).ToArray();
        var steps = new HolmStep[pValues.Count];
        var stopped = false;

        for (var position = 0; position < order.Length; position++)
        {
            var index = order[position];
            var threshold = alpha / (order.Length - position);
            var rejected = !stopped && pValues[index] <= threshold;

            if (!rejected)
            {
                stopped = true;
            }

            steps[index] = new HolmStep(pValues[index], threshold, rejected);
        }

        return steps;
    }

    #endregion

    #region 卡方分布

    /// <summary>卡方分布的上尾概率 <c>P(X &gt; x)</c>。</summary>
    /// <remarks>
    /// 它只用来给精确那一档做对照，所以走的是标准的级数加连分式，精度按双精度给足。
    /// 用不上查表，也不必引入分布库。
    /// </remarks>
    public static double ChiSquareUpperTail(double chiSquare, int degreesOfFreedom)
    {
        if (chiSquare <= 0)
        {
            return 1;
        }

        return RegularizedGammaQ(degreesOfFreedom / 2.0, chiSquare / 2.0);
    }

    private static double RegularizedGammaQ(double a, double x)
    {
        // x 比 a 小的时候级数收敛快，大的时候连分式收敛快。两边都取各自擅长的那个。
        return x < a + 1 ? 1.0 - GammaSeries(a, x) : GammaContinuedFraction(a, x);
    }

    private static double GammaSeries(double a, double x)
    {
        var term = 1.0 / a;
        var sum = term;

        for (var step = 1; step <= 500; step++)
        {
            term *= x / (a + step);
            sum += term;

            if (Math.Abs(term) < Math.Abs(sum) * 1e-15)
            {
                break;
            }
        }

        return sum * Math.Exp((-x + (a * Math.Log(x))) - LogGamma(a));
    }

    private static double GammaContinuedFraction(double a, double x)
    {
        const double Tiny = 1e-300;

        var b = x + 1 - a;
        var c = 1 / Tiny;
        var d = 1 / b;
        var h = d;

        for (var step = 1; step <= 500; step++)
        {
            var an = -step * (step - a);

            b += 2;
            d = (an * d) + b;

            if (Math.Abs(d) < Tiny)
            {
                d = Tiny;
            }

            c = b + (an / c);

            if (Math.Abs(c) < Tiny)
            {
                c = Tiny;
            }

            d = 1 / d;

            var delta = d * c;

            h *= delta;

            if (Math.Abs(delta - 1) < 1e-15)
            {
                break;
            }
        }

        return Math.Exp((-x + (a * Math.Log(x))) - LogGamma(a)) * h;
    }

    /// <summary>对数伽马，Lanczos 近似。</summary>
    private static double LogGamma(double x)
    {
        double[] coefficients =
        [
            676.5203681218851, -1259.1392167224028, 771.32342877765313,
            -176.61502916214059, 12.507343278686905, -0.13857109526572012,
            9.9843695780195716e-6, 1.5056327351493116e-7,
        ];

        if (x < 0.5)
        {
            // 反射公式。这一条路径在卡方里用不到（a 至少是 1），留着是为了这个函数自己站得住。
            return Math.Log(Math.PI / Math.Abs(Math.Sin(Math.PI * x))) - LogGamma(1 - x);
        }

        var z = x - 1;
        var sum = 0.99999999999980993;

        for (var index = 0; index < coefficients.Length; index++)
        {
            sum += coefficients[index] / (z + index + 1);
        }

        var t = z + coefficients.Length - 0.5;

        return 0.5 * Math.Log(2 * Math.PI) + ((z + 0.5) * Math.Log(t)) - t + Math.Log(sum);
    }

    #endregion
}

/// <summary>Friedman 检验的结果。</summary>
/// <param name="Spread">各被测项秩和相对平均秩的离差平方和。</param>
/// <param name="ChiSquare">并列校正后的卡方统计量。</param>
/// <param name="DegreesOfFreedom">自由度，等于被测项数减一。</param>
/// <param name="ChiSquareP">卡方那一档的 p。它是对照，不是判据。</param>
/// <param name="ExactP">精确分布的 p。被测项太多没算时为空。</param>
/// <param name="Exact">判据用的是不是精确那一档。</param>
internal sealed record FriedmanResult(
    double Spread,
    double ChiSquare,
    int DegreesOfFreedom,
    double ChiSquareP,
    double? ExactP,
    bool Exact)
{
    /// <summary>判定用的 p：能算精确就用精确，否则退回卡方。</summary>
    public double P => ExactP ?? ChiSquareP;
}

/// <summary>Wilcoxon 符号秩检验的结果。</summary>
/// <param name="PositiveRankSum">正差那一侧的秩和。</param>
/// <param name="NegativeRankSum">负差那一侧的秩和。</param>
/// <param name="Statistic">两侧里小的那个秩和。</param>
/// <param name="P">双侧 p。</param>
/// <param name="Exact">是不是精确分布算出来的。</param>
/// <param name="Pairs">去掉零差之后还剩几对。</param>
internal sealed record WilcoxonResult(
    double PositiveRankSum,
    double NegativeRankSum,
    double Statistic,
    double P,
    bool Exact,
    int Pairs);

/// <summary>Holm-Bonferroni 的一步。</summary>
/// <param name="P">这一条比较的原始 p。</param>
/// <param name="Threshold">它在这一步要低于的阈值。</param>
/// <param name="Rejected">这一步算不算通过。</param>
internal sealed record HolmStep(double P, double Threshold, bool Rejected);
