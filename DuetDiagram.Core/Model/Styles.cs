namespace DuetDiagram.Core.Model;

/// <summary>
/// 四边间距。用于内边距一类需要分边指定的场合。
/// </summary>
public sealed record Thickness(double Left, double Top, double Right, double Bottom)
{
    public static Thickness Uniform(double value) => new(value, value, value, value);
}

/// <summary>
/// 文本样式。
/// </summary>
/// <remarks>
/// <para>
/// 全部字段可选。这一点是刻意的：样式是逐层覆盖的，
/// 节点可以只声明"字号大一点"，其余继承分组、预设或调色板的设定。
/// 把字段做成必填会迫使每一层都写全，那样任何一处改动都要改遍所有层级。
/// </para>
/// <para>
/// 字体变化会改变标签的实际宽度，进而改变节点尺寸与布局结果。
/// 因此字体相关的字段会被计入结构哈希——它不只是外观。
/// </para>
/// </remarks>
public sealed record TextStyle
{
    public string? FontFamily { get; init; }

    public double? FontSize { get; init; }

    public FontWeight? FontWeight { get; init; }

    public bool? Italic { get; init; }

    public bool? Underline { get; init; }

    public bool? Strikethrough { get; init; }

    /// <summary>文字颜色。取调色板令牌名，不直接写颜色值。</summary>
    public string? FontColor { get; init; }

    public string? TextBackgroundColor { get; init; }

    public TextAlign? Align { get; init; }

    public VerticalAlign? VerticalAlign { get; init; }

    public bool? WordWrap { get; init; }

    public double? LineHeight { get; init; }

    /// <summary>首行缩进。</summary>
    public double? TextIndent { get; init; }

    public Thickness? Padding { get; init; }

    public LabelPosition? LabelPosition { get; init; }

    public WritingDirection? WritingDirection { get; init; }

    /// <summary>是否与另一个样式完全等价。哈希与差异比较用它，避免逐字段写判断。</summary>
    public bool IsEquivalentTo(TextStyle? other) => other is not null && Equals(this, other);
}

/// <summary>
/// 节点样式。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="NodeDef.StyleToken"/> 的关系：令牌是首选，它表达的是语义
/// （"这是个危险节点"），换主题时整体跟着变；这里的字段是具体覆盖，
/// 用于令牌表达不了的场合。两者同时存在时以本记录为准。
/// </para>
/// <para>
/// 具体颜色值允许出现在这里，而**不允许**出现在令牌该在的地方。
/// 也就是说：能用令牌表达的就不该写颜色值，写了的都是有意为之的例外。
/// </para>
/// </remarks>
public sealed record NodeStyle
{
    /// <summary>填充色。颜色值形如 #rrggbb，也可以写调色板令牌名。</summary>
    public string? Fill { get; init; }

    /// <summary>描边色。</summary>
    public string? Stroke { get; init; }

    /// <summary>文字颜色。</summary>
    public string? Text { get; init; }

    /// <summary>边框线型。</summary>
    public LineStyle? Border { get; init; }

    /// <summary>描边粗细。</summary>
    public double? Weight { get; init; }

    /// <summary>圆角半径。</summary>
    public double? Radius { get; init; }

    /// <summary>不透明度，取值 0 到 1。</summary>
    public double? Opacity { get; init; }

    /// <summary>角标文字。用于在节点右上角标序号或状态。</summary>
    public string? Badge { get; init; }
}

/// <summary>
/// 边样式。
/// </summary>
/// <remarks>
/// <see cref="Line"/>、<see cref="Arrow"/>、<see cref="StyleToken"/> 原本直接放在
/// <see cref="EdgeDef"/> 上。收进本记录之后，边的语义字段与外观字段分开，
/// 判断"这次改动要不要重排"时可以直接看外观部分有没有变。
/// </remarks>
public sealed record EdgeStyle
{
    public LineStyle? Line { get; init; }

    public ArrowStyle? Arrow { get; init; }

    public string? Color { get; init; }

    public double? Weight { get; init; }

    /// <summary>走线方式。改变它会改变连线的几何，但不改变节点位置。</summary>
    public EdgeRoute? Route { get; init; }

    public LabelPosition? LabelPosition { get; init; }

    /// <summary>调色板令牌名，与 <see cref="NodeDef.StyleToken"/> 同理。</summary>
    public string? StyleToken { get; init; }
}

/// <summary>
/// 端口定义。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IsCustom"/> 区分两种来源：引擎按形状自动均分的端口，与人工摆在指定位置的端口。
/// 自动端口在节点尺寸变化时应当重算，自定义端口不该被冲掉——这个区分不做，
/// 用户手摆的端口每次重排都会跑回默认位置。
/// </para>
/// <para>
/// 端口的方位会改变连线的出入点，因此它计入结构哈希。
/// 端口变化不影响节点坐标，但影响连线走向，而走线属于求解的一部分。
/// </para>
/// </remarks>
public sealed record PortDef
{
    public required string Name { get; init; }

    public PortSide Side { get; init; } = PortSide.Right;

    /// <summary>沿所在边的偏移。取值 0 到 1，0 是一端、1 是另一端，0.5 是居中。</summary>
    public double Offset { get; init; } = 0.5;

    /// <summary>是否由人工或模型指定位置。自动端口会被重算，自定义端口不会。</summary>
    public bool IsCustom { get; init; }
}
