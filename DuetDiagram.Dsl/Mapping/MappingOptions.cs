using DuetDiagram.Core.Model;

namespace DuetDiagram.Dsl.Mapping;

/// <summary>
/// 映射的调用参数。
/// </summary>
/// <remarks>
/// <para>
/// 做成一个参数包而不是给 <c>Map</c> 加一长串可选参数，是因为这些值往后只会增加
/// （归属方、调色板、时间来源都是这类）。每加一项就改一次签名，所有调用点都要跟着动；
/// 收在一处则只在需要的那一处填。
/// </para>
/// </remarks>
public sealed record MappingOptions
{
    /// <summary>
    /// 产出文档的标识。
    /// </summary>
    /// <remarks>
    /// DSL 文本里没有文档标识（它描述的是图，不是文档），所以只能由调用方给。
    /// 不给一个默认值：默认值会让两份不同的输入产出同名的文档，
    /// 而文档标识是 sidecar 文件名与布局缓存归属判定的依据，重名会串台。
    /// </remarks>
    public required string DocumentId { get; init; }

    /// <summary>
    /// 调色板。
    /// </summary>
    /// <remarks>
    /// **映射层不发明颜色。** DSL 里写的是令牌名（<c>style=danger</c>），
    /// 令牌到具体颜色的映射由主题提供，模型不需要知道 <c>#cc0000</c> 这种细节。
    /// 为空表示产出空调色板，由渲染层用兜底外观——这比在这里塞一套默认配色好：
    /// 默认配色会被算进视觉哈希，于是换个主题就变成了"文档内容变了"。
    /// </remarks>
    public Palette? Palette { get; init; }
}
