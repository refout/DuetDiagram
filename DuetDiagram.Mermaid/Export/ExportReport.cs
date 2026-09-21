namespace DuetDiagram.Mermaid.Export;

/// <summary>
/// 一类没能写进文本的东西。
/// </summary>
/// <param name="Feature">是什么，例如"端口""布局约束"。</param>
/// <param name="Ids">涉及哪些元素。整份文档级的属性为空。</param>
/// <param name="Reason">为什么写不出来。</param>
/// <remarks>
/// 按类收拢而不是逐个元素一条：一次导出里同一类丢失往往涉及几十个元素，
/// 逐条列出来之后报告会长到没人看，而"丢了端口，涉及 A、B、C"一眼就够。
/// </remarks>
public sealed record DroppedFeature(string Feature, IReadOnlyList<string> Ids, string Reason);

/// <summary>
/// 一次导出丢了什么。
/// </summary>
/// <remarks>
/// <para>
/// **IR 的表达力严格强于 Mermaid**：端口、富文本、数学模式、图层、页面、标签、动作、
/// 字体、四类布局约束、样式令牌、调色板——Mermaid 都没有对应语法。所以导出必然是有损的，
/// 差别只在于有没有说出来。
/// </para>
/// <para>
/// 静默丢失会让用户以为导出的文件就是全部内容，而实际不是。所以每一类都留一条记录。
/// 这与导入侧同一态度：代价可以付，但要说清付了什么。
/// </para>
/// <para>
/// 报告为空**不等于**导出无损，只等于没有东西落在已知的丢失清单里。
/// 已知清单是照着 IR 逐字段对出来的，新加字段时要跟着补——
/// 不补的话它不会报错，只会从报告里悄悄消失。
/// </para>
/// </remarks>
/// <param name="Dropped">丢失清单。没有丢失时为空。</param>
public sealed record ExportReport(IReadOnlyList<DroppedFeature> Dropped)
{
    /// <summary>什么都没丢。</summary>
    public static ExportReport Empty { get; } = new([]);
}

/// <summary>
/// 导出产物。
/// </summary>
/// <param name="Text">Mermaid 文本。同一份文档两次导出逐字节相同。</param>
/// <param name="Report">这次导出丢了什么。</param>
public sealed record ExportResult(string Text, ExportReport Report);
