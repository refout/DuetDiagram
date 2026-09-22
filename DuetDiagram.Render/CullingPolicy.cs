namespace DuetDiagram.Render;

/// <summary>
/// 剔除的判据与范围。
/// </summary>
/// <remarks>
/// <para>
/// 两个数放在一起、只此一处。分开写的话迟早对不上，而对不上时的表现是
/// "有时快有时慢"，看不出规律，也就无从查起。
/// </para>
/// <para>
/// 缺省值来自 <see cref="ViewportCulling"/>，那里是这两个数的**唯一**落脚处。
/// 本类型把它们变成可以按需替换的一份配置，供测试与诊断面板使用。
/// </para>
/// </remarks>
public sealed record CullingPolicy
{
    public CullingPolicy(
        int threshold = ViewportCulling.VirtualizationThreshold,
        double prefetchMargin = ViewportCulling.PrefetchMargin)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(threshold, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(prefetchMargin);

        Threshold = threshold;
        PrefetchMargin = prefetchMargin;
    }

    /// <summary>超过这个元素数才走索引。恰好等于它时也走。</summary>
    public int Threshold { get; }

    /// <summary>查询时视口向外扩的边距。</summary>
    public double PrefetchMargin { get; }

    /// <summary>缺省策略。</summary>
    public static CullingPolicy Default { get; } = new();

    /// <summary>这个元素数该走哪一档。</summary>
    /// <remarks>
    /// 判据只有这一处。调用方各自写一遍的话，两处边界差一个元素就会让
    /// "什么时候切换"变得不可预测。
    /// </remarks>
    public RenderMode ModeFor(int elementCount) =>
        elementCount >= Threshold ? RenderMode.Virtualized : RenderMode.Immediate;

    /// <summary>
    /// 查询用的区域：视口向外扩一圈。
    /// </summary>
    /// <remarks>
    /// 不扩的话，贴边的元素会在快速平移时一闪一闪——等到它进了视口才画，已经晚了一帧。
    /// 扩太多则等于没剔除：每次都把视口外一大片也取出来画。
    /// </remarks>
    public SpatialRect VisibleArea(SpatialRect viewport) => viewport.Inflate(PrefetchMargin);
}
