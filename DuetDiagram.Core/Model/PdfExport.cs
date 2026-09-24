namespace DuetDiagram.Core.Model;

/// <summary>
/// 一次 PDF 导出的产物。
/// </summary>
/// <remarks>
/// <para>
/// **它放在 Core 的理由与 <see cref="SvgExport"/>、<see cref="BitmapExport"/> 一样。**
/// 真正写 PDF 的是渲染层，而把这个产物原样交给调用方的是工具层，两层之间只共用 Core。
/// </para>
/// <para>
/// **它带的是编码之后的字节。** 交给调用方的东西要能直接落盘、直接进 JSON 载荷；
/// 把文档对象交出去的话，每个调用方都要自己再写一遍，而两次写出来的未必一样。
/// </para>
/// <para>
/// **页数单独带出来，而不是让调用方去数 <c>/Type /Page</c>。** 它是"这一次导出按哪一档定的范围"
/// 的直接证据——一份三页的文档导出一页还是三页，看这一项就知道。
/// </para>
/// </remarks>
/// <param name="Pdf">PDF 字节。同一份绘制列表与同一组选项导出两次逐字节相同。</param>
/// <param name="Pages">页数。一份文档的每一页各占一页。</param>
/// <param name="Dropped">这次导出丢了什么。</param>
public sealed record PdfExport(
    byte[] Pdf,
    int Pages,
    IReadOnlyList<DroppedFeature> Dropped);
