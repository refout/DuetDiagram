using DuetDiagram.Core.Model;
using DuetDiagram.Core.Time;

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

    /// <summary>
    /// 布局意图的归属方。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **由调用方声明，不按指令类型推断。** 同一份 DSL 文本可能由模型生成，
    /// 也可能由人手工写回，解析器分不清两者——语法上它们一模一样。
    /// </para>
    /// <para>
    /// 曾经考虑过"pin 一律算 Human、其余算 Llm"这种推断，看起来省事，
    /// 但人在手工写 <c>same-rank</c> 时就会被标错，而标错的代价是
    /// 人工设定的约束被下一次自动重排冲掉——用户每次微调都会白做。
    /// 归属方这条机制的意义全在"标对了才生效"，宁可让调用方多传一个参数。
    /// </para>
    /// <para>
    /// 缺省是 <see cref="ConstraintOwner.Llm"/>，因为 DSL 的主要来源是模型。
    /// </para>
    /// </remarks>
    public ConstraintOwner Owner { get; init; } = ConstraintOwner.Llm;

    /// <summary>
    /// 时间来源，用来给约束打创建时间。
    /// </summary>
    /// <remarks>
    /// 不直接读系统时钟：创建时间虽然不进哈希，但它出现在约束记录里，
    /// 而测试要能断言"映射两次得到同一份产物"。注入之后这条断言才成立。
    /// </remarks>
    public ITimeProvider Time { get; init; } = SystemTimeProvider.Instance;
}
