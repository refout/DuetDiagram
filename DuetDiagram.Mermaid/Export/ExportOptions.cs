namespace DuetDiagram.Mermaid.Export;

/// <summary>
/// 导出的调用参数。
/// </summary>
public sealed record ExportOptions
{
    /// <summary>
    /// 一级缩进用的字符串。
    /// </summary>
    /// <remarks>
    /// 做成参数而不是写死，是因为导出的文本多半要进版本库或贴进对话，
    /// 而那边对缩进宽度的偏好各不相同。缩进只影响可读性，不影响语义——
    /// Mermaid 的子图边界由 <c>end</c> 决定，不靠缩进。
    /// </remarks>
    public string Indent { get; init; } = "  ";
}
