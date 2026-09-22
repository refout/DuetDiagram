namespace DuetDiagram.Render;

/// <summary>
/// 一段耗时的分布。
/// </summary>
/// <remarks>
/// 只给三个数：中间值、九十五分位、最大值。
/// 均值在这里没有用——一帧卡到一百毫秒就足以让人察觉，而它在两百帧的均值里
/// 只值零点五毫秒，看不出任何东西。最大值能看出"卡过"，分位数能看出"经常卡"。
/// </remarks>
/// <param name="Median">中间值。</param>
/// <param name="P95">九十五分位。</param>
/// <param name="Max">最大值。</param>
public readonly record struct DiagnosticsStatistics(double Median, double P95, double Max)
{
    /// <summary>还没有样本。</summary>
    public static DiagnosticsStatistics Empty { get; } = new(0, 0, 0);
}

/// <summary>
/// 一次读数：窗口里的分布，加上最近那一帧的明细。
/// </summary>
/// <param name="FrameCount">窗口里有多少帧。</param>
/// <param name="Latest">最近记下的那一帧。</param>
/// <param name="Total">整帧的分布。</param>
/// <param name="Cull">剔除那一段的分布。</param>
/// <param name="Raster">光栅化那一段的分布。</param>
public sealed record DiagnosticsSummary(
    int FrameCount,
    DiagnosticsFrame Latest,
    DiagnosticsStatistics Total,
    DiagnosticsStatistics Cull,
    DiagnosticsStatistics Raster);

/// <summary>
/// 帧时采样器：定长环形窗口，记的时候不分配。
/// </summary>
/// <remarks>
/// <para>
/// **窗口长度固定，与运行时长无关。** 攒着全部历史的话，程序跑久了内存一直涨，
/// 而算分位数要排的数组也越来越长——最后诊断面板自己成了卡顿的来源，
/// 而它正是用来查卡顿的。
/// </para>
/// <para>
/// **记的时候不分配。** 每帧往一个预分配的数组里写一格，写满了覆盖最旧的那格。
/// 每帧新建一个列表的话，垃圾回收本身就会出现在面板显示的数字里，量的是自己。
/// 读数时才排一次序，而读数每秒只有几次，不是每帧。
/// </para>
/// <para>
/// 关掉时不做任何事。它挂在渲染回调里，每帧都会被调一次，所以关掉之后
/// 除了那一次布尔判断之外不能有任何开销——诊断工具自己成了开销就没法用了。
/// </para>
/// </remarks>
public sealed class DiagnosticsSampler
{
    /// <summary>窗口里放多少帧。四秒左右，够看出一次卡顿。</summary>
    public const int DefaultCapacity = 240;

    private readonly DiagnosticsFrame[] _frames;
    private readonly double[] _scratch;
    private readonly double[] _stages;
    private readonly bool[] _stageSeen;

    private int _next;
    private int _count;

    public DiagnosticsSampler(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _frames = new DiagnosticsFrame[capacity];
        _scratch = new double[capacity];
        _stages = new double[Enum.GetValues<DiagnosticsStage>().Length];
        _stageSeen = new bool[_stages.Length];
    }

    /// <summary>开着才记。关着的时候 <see cref="Record"/> 只做一次判断就返回。</summary>
    public bool Enabled { get; set; }

    /// <summary>窗口能放多少帧。</summary>
    public int Capacity => _frames.Length;

    /// <summary>窗口里现在有多少帧。还没写满时小于容量。</summary>
    public int Count => _count;

    /// <summary>记一帧。写满之后覆盖最旧的那一格。</summary>
    public void Record(in DiagnosticsFrame frame)
    {
        if (!Enabled)
        {
            return;
        }

        _frames[_next] = frame;
        _next = _next + 1 == _frames.Length ? 0 : _next + 1;

        if (_count < _frames.Length)
        {
            _count++;
        }
    }

    /// <summary>记一段重活的耗时。同名的那一段会被后一次覆盖。</summary>
    public void RecordStage(DiagnosticsStage stage, double milliseconds)
    {
        var index = (int)stage;

        _stages[index] = milliseconds;
        _stageSeen[index] = true;
    }

    /// <summary>这一段重活量过没有。</summary>
    public bool HasStage(DiagnosticsStage stage) => _stageSeen[(int)stage];

    /// <summary>这一段重活最近一次的耗时。没量过时为空。</summary>
    public double? Stage(DiagnosticsStage stage) =>
        _stageSeen[(int)stage] ? _stages[(int)stage] : null;

    /// <summary>丢掉窗口里的帧。重活的记录不动——它们与帧无关。</summary>
    public void Clear()
    {
        _next = 0;
        _count = 0;
    }

    /// <summary>
    /// 算一次读数。
    /// </summary>
    /// <remarks>
    /// 排的是窗口的一份副本，不动环形缓冲区本身：直接排原数组的话，
    /// 环形顺序就被打乱了，而"覆盖最旧的那一格"正是靠那个顺序。
    /// </remarks>
    public DiagnosticsSummary Summary()
    {
        if (_count == 0)
        {
            return new DiagnosticsSummary(
                0,
                default,
                DiagnosticsStatistics.Empty,
                DiagnosticsStatistics.Empty,
                DiagnosticsStatistics.Empty);
        }

        var latest = _frames[_next == 0 ? _count - 1 : _next - 1];

        return new DiagnosticsSummary(
            _count,
            latest,
            Statistics(DiagnosticsField.Total),
            Statistics(DiagnosticsField.Cull),
            Statistics(DiagnosticsField.Raster));
    }

    /// <summary>窗口里取哪一列。三列写死，不给委托——那会每次读数都造一个闭包。</summary>
    private enum DiagnosticsField
    {
        Total,
        Cull,
        Raster,
    }

    private DiagnosticsStatistics Statistics(DiagnosticsField field)
    {
        for (var index = 0; index < _count; index++)
        {
            var frame = _frames[index];

            _scratch[index] = field switch
            {
                DiagnosticsField.Total => frame.Total,
                DiagnosticsField.Cull => frame.Cull,
                _ => frame.Raster,
            };
        }

        Array.Sort(_scratch, 0, _count);

        return new DiagnosticsStatistics(
            Percentile(0.5),
            Percentile(0.95),
            _scratch[_count - 1]);
    }

    /// <summary>
    /// 取分位。用最近秩法：名次向上取整，落在最后一个样本上。
    /// </summary>
    /// <remarks>
    /// 不插值。插值出来的数不是任何一个真实样本，而诊断面板要回答的是
    /// "最坏能坏到哪儿"，那种问题只有真实样本答得了。
    /// </remarks>
    private double Percentile(double fraction)
    {
        var rank = (int)Math.Ceiling(fraction * _count);

        return _scratch[Math.Clamp(rank - 1, 0, _count - 1)];
    }
}
