namespace DuetDiagram.Core.Model;

/// <summary>
/// 一次 SVG 导出的产物。
/// </summary>
/// <remarks>
/// <para>
/// **它放在 Core 是因为导出这件事跨了三层。** 真正把绘制列表写成 SVG 的是渲染层，
/// 而把这个产物原样交给调用方的是工具层，两层之间只共用 Core。放在渲染层的话，
/// 工具层要么引渲染层（它不该引），要么自己再拼一个同形状的记录（于是"丢失清单"
/// 有了两种形状）。
/// </para>
/// <para>
/// **空清单不等于无损。** 它只等于"没有东西落进已知的丢失清单"——
/// 那几类没被写出来的东西各自在 <see cref="Dropped"/> 里有一句为什么。
/// </para>
/// </remarks>
/// <param name="Svg">SVG 文本。同一份绘制列表两次导出逐字节相同。</param>
/// <param name="Dropped">这次导出丢了什么。</param>
public sealed record SvgExport(string Svg, IReadOnlyList<DroppedFeature> Dropped);
