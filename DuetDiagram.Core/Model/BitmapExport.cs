namespace DuetDiagram.Core.Model;

/// <summary>
/// 一次位图导出的产物。
/// </summary>
/// <remarks>
/// <para>
/// **它放在 Core 的理由与 <see cref="SvgExport"/> 一样。** 真正把绘制列表光栅化的是渲染层，
/// 而把这个产物原样交给调用方的是工具层，两层之间只共用 Core。放在渲染层的话，
/// 工具层要么引渲染层（它不该引），要么自己再拼一个同形状的记录。
/// </para>
/// <para>
/// **它带的是编码之后的字节，不是像素缓冲。** 交给调用方的东西要能直接落盘、
/// 直接进 JSON 载荷；把一块像素缓冲交出去的话，每个调用方都要自己再编一次码，
/// 而两次编码的结果未必一样。
/// </para>
/// <para>
/// **宽高单独带出来，而不是让调用方去解 PNG 头。** 导出尺寸是"这一次导出用了哪一档裁剪与缩放"
/// 的直接证据，界面上要显示它，自检也要核它。
/// </para>
/// </remarks>
/// <param name="Png">PNG 字节。同一份绘制列表与同一组选项导出两次逐像素相同。</param>
/// <param name="Width">位图宽度，像素。</param>
/// <param name="Height">位图高度，像素。</param>
/// <param name="Dropped">这次导出丢了什么。</param>
public sealed record BitmapExport(
    byte[] Png,
    int Width,
    int Height,
    IReadOnlyList<DroppedFeature> Dropped);
