namespace DuetDiagram.Render;

/// <summary>
/// 视口剔除的两个数。
/// </summary>
/// <remarks>
/// <para>
/// 这两个数字是**产品行为**而不是实现细节，所以给它们一个明确的落脚处，
/// 而不是散落在调用处当作魔数。它们各自都有"为什么是这个值"的理由，
/// 写在魔数旁边没人看得见。
/// </para>
/// <para>
/// **这里是它们的唯一落脚处。** 判据与用法在 <see cref="CullingPolicy"/> 上，
/// 那一份配置的缺省值取这里。两处各写一份的话迟早对不上，
/// 而对不上时的表现是"有时快有时慢"，看不出规律。
/// </para>
/// </remarks>
public static class ViewportCulling
{
    /// <summary>
    /// 视口向外预取的边距。
    /// </summary>
    /// <remarks>
    /// 预取的用处是让快速平移时边缘不出现空白：等到元素进入视口才画，
    /// 已经晚了一帧。一百像素大约是一个节点宽度多一点，够覆盖一次拖动的位移。
    /// 取太大则每次多画不少视口外的东西，抵消了裁剪的意义。
    /// </remarks>
    public const double PrefetchMargin = 100;

    /// <summary>
    /// 超过这个元素数才启用索引。
    /// </summary>
    /// <remarks>
    /// 索引本身有维护成本，而元素少的时候"逐个判一遍"本来就很快。
    /// 与其在两种模式之间反复切换，不如定一个阈值，低于它就直接遍历——
    /// 少一个需要推理的状态。
    /// </remarks>
    public const int VirtualizationThreshold = 500;
}
