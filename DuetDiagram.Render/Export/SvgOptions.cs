namespace DuetDiagram.Render;

/// <summary>
/// 导出 SVG 时的选择。
/// </summary>
/// <remarks>
/// 只放"同一个绘制列表能导出成不止一种合理结果"的那几项。写死能过的都写死在导出器里：
/// 每多一个开关，就多一份要有人维护的组合。
/// </remarks>
public sealed record SvgOptions
{
    /// <summary>
    /// 要不要铺背景。
    /// </summary>
    /// <remarks>
    /// 默认铺。不铺的话，深色文字放到深色主题的查看器里会看不见，
    /// 而用户会以为导出丢了东西。要透明底时显式关掉。
    /// </remarks>
    public bool IncludeBackground { get; init; } = true;

    /// <summary>
    /// 内容四周的留白。
    /// </summary>
    /// <remarks>
    /// 默认不留。绘制列表的宽高已经是"内容占多大"，再留一圈会让 viewBox 与内容对不上——
    /// 而"对不上"正是那种只有对着屏幕才看得出来的偏差。要留白时显式给。
    /// </remarks>
    public double Padding { get; init; }
}
