using DuetDiagram.Core.Model;

namespace DuetDiagram.Mermaid.Import;

/// <summary>
/// 导入的调用参数。
/// </summary>
public sealed record ImportOptions
{
    /// <summary>
    /// 产出文档的标识。
    /// </summary>
    /// <remarks>
    /// Mermaid 文本里没有文档标识（它描述的是图，不是文档），所以只能由调用方给。
    /// 不给一个默认值：默认值会让两份不同的输入产出同名的文档，
    /// 而文档标识是 sidecar 文件名与布局缓存归属判定的依据，重名会串台。
    /// </remarks>
    public required string DocumentId { get; init; }

    /// <summary>
    /// 调色板。
    /// </summary>
    /// <remarks>
    /// Mermaid 写的是具体颜色（<c>fill:#f9f</c>），那些落 IR 的节点样式；
    /// 调色板是另一条路——宿主自己那套主题令牌。导入层不发明主题色，
    /// 所以为空表示产出空调色板，由渲染层用兜底外观。
    /// </remarks>
    public Palette? Palette { get; init; }
}
