using DuetDiagram.Core.Model;

namespace DuetDiagram.Dsl.Export;

/// <summary>
/// 一次 DSL 导出丢了什么。
/// </summary>
/// <remarks>
/// <para>
/// **IR 的表达力严格强于 DSL。** 页面、字体、标签、动作、文本样式预设、富文本内容、
/// 数学排版模式、自定义形状的路径、组合的折叠状态与内部方向、端口的偏移与来源标记、
/// 约束的归属方——这些在 DSL 里都没有对应写法。
/// </para>
/// <para>
/// 导出因此必然是有损的，差别只在于有没有说出来。静默丢失会让调用方
/// 以为导出的文本就是全部内容。报告为空**不等于**无损，只等于没有东西落进
/// 已知的丢失清单；那份清单是照着 IR 逐字段对出来的，新加字段时要跟着补——
/// 不补的话它不会报错，只会从报告里悄悄消失。
/// </para>
/// </remarks>
/// <param name="Dropped">丢失清单。没有丢失时为空。</param>
public sealed record DslExportReport(IReadOnlyList<DroppedFeature> Dropped)
{
    /// <summary>什么都没丢。</summary>
    public static DslExportReport Empty { get; } = new([]);
}

/// <summary>
/// 导出产物。
/// </summary>
/// <remarks>
/// 只有文本，没有 sidecar：<c>pin</c> 是从 sidecar 读出来写进文本的，
/// 反向不存在——文本本身已经带走了固定位置。
/// </remarks>
/// <param name="Text">DSL 文本。同一份文档两次导出逐字节相同。</param>
/// <param name="Report">这次导出丢了什么。</param>
public sealed record DslExportResult(string Text, DslExportReport Report);
