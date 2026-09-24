namespace DuetDiagram.Core.Model;

/// <summary>
/// 字段的作用域。
/// </summary>
/// <remarks>
/// 这个分类与哈希口径是同一件事的两面：结构类的字段改了要让坐标失效，
/// 视觉类的只影响像素，第三类两者都不影响。
/// 两处必须保持一致，否则会出现"改了某个字段哈希说不用重排、布局却按新值算"这类错位。
/// </remarks>
public enum FieldScope
{
    /// <summary>改了要让已算出的坐标失效，必须重新求解布局。</summary>
    Structural,

    /// <summary>改了只需要重绘。</summary>
    Visual,

    /// <summary>只影响交互或由宿主自定义，两个哈希都不计。</summary>
    Neither,
}

/// <summary>
/// 一个字段的元数据。
/// </summary>
/// <param name="Name">进协议的规范名。</param>
/// <param name="Owner">属于哪种定义。</param>
/// <param name="Scope">作用域，与哈希口径一致。</param>
/// <param name="Atomic">
/// 值是一个整体，不能按成员合并。成员列表这类字段为真——
/// 两边各往同一个列表里加一个成员，看起来改的是不同部分，但结果是两个不同的列表，
/// 无法在不丢失一方意图的前提下合并。
/// </param>
public sealed record FieldDescriptor(string Name, string Owner, FieldScope Scope, bool Atomic = false);

/// <summary>
/// 字段名的唯一来源。
/// </summary>
/// <remarks>
/// <para>
/// 写成常量而不是各处写裸字符串。裸字符串的代价已经被验证过一次：
/// 错误码曾经在两处各写一份，名字不一致而没有任何东西会报错。
/// </para>
/// <para>
/// **元素级与字段级用前缀区分。** 元素级的名字带 <c>@</c>，表示"整个元素被增删"；
/// 字段级不带前缀，表示"某个字段改了"。
/// </para>
/// <para>
/// 这个前缀不是装饰。不加的话 <c>layer</c> 会有两种读法：图层元素被增删，
/// 还是某个节点的图层字段改了——两者靠 <c>ElementId</c> 也分不出来，
/// 因为标识本身不带类型信息。
/// </para>
/// </remarks>
public static class FieldNames
{

    #region 元素级：整个元素被增删

    public const string NodeElement = "@node";
    public const string EdgeElement = "@edge";
    public const string CompositeElement = "@composite";
    public const string TagElement = "@tag";
    public const string ActionElement = "@action";
    public const string FontElement = "@font";
    public const string TextPresetElement = "@text-preset";
    public const string LayerElement = "@layer";
    public const string PageElement = "@page";
    public const string PaletteEntryElement = "@palette-entry";

    #endregion

    #region 字段级

    public const string Label = "label";
    public const string Shape = "shape";
    public const string Parent = "parent";
    public const string Layer = "layer";
    public const string Page = "page";
    public const string StyleToken = "styleToken";
    public const string Style = "style";
    public const string Text = "text";
    public const string Ports = "ports";
    public const string RichText = "richText";
    public const string MathMode = "mathMode";
    public const string Desc = "desc";
    public const string Meta = "meta";

    #region 样式与文本的子字段
    //
    // 样式与文本本身是记录类型，各自有若干成员。把它们拆成子字段而不是只留一个整体字段，
    // 是因为两件事都要求拆：界面上一个成员一个控件，而冲突判定要能认出
    // "一边改填充、一边改描边"这两件事可以共存。
    // 只留整体字段的话，前者的界面只能给一个 JSON 文本框，后者会把两次互不相干的修改判成冲突。

    public const string StyleFill = "style.fill";
    public const string StyleStroke = "style.stroke";
    public const string StyleWeight = "style.weight";
    public const string StyleBorder = "style.border";
    public const string StyleRadius = "style.radius";
    public const string StyleOpacity = "style.opacity";
    public const string StyleBadge = "style.badge";

    public const string TextFontFamily = "text.fontFamily";
    public const string TextFontSize = "text.fontSize";
    public const string TextFontWeight = "text.fontWeight";
    public const string TextItalic = "text.italic";
    public const string TextUnderline = "text.underline";
    public const string TextStrikethrough = "text.strikethrough";
    public const string TextFontColor = "text.fontColor";
    public const string TextAlign = "text.align";

    #endregion

    public const string From = "from";
    public const string To = "to";
    public const string FromPort = "fromPort";
    public const string ToPort = "toPort";

    public const string Members = "members";
    public const string CompositeDirection = "direction";
    public const string Collapsed = "collapsed";
    public const string LocalLayout = "localLayout";

    public const string Kind = "kind";
    public const string Direction = "direction";
    public const string NodeSpacing = "layout.nodeSpacing";
    public const string LayerSpacing = "layout.layerSpacing";
    public const string SameRank = "layout.sameRank";
    public const string Order = "layout.order";
    public const string Align = "layout.align";
    public const string Place = "layout.place";

    #region 画布设置的子字段
    //
    // 与样式、调色板同一口径：拆成子字段而不是只留一个整体。
    // 画布设置是一个子对象，一次调用可能改到其中几项；拆开之后，
    // 一边调网格、一边调背景色这两件互不相干的事才不会被判成冲突。

    public const string CanvasGrid = "canvas.grid";
    public const string CanvasGridSize = "canvas.gridSize";
    public const string CanvasPageSize = "canvas.pageSize";
    public const string CanvasOrientation = "canvas.orientation";
    public const string CanvasBackground = "canvas.background";
    public const string CanvasInfinite = "canvas.infinite";

    #endregion

    #region 调色板条目的子字段
    //
    // 与样式那一组同一个口径：拆成子字段而不是只留一个整体条目。
    // 界面上一个成员一个控件；而冲突判定要能认出"一边改填充、一边改描边"可以共存，
    // 只留整体条目的话会把两次互不相干的修改判成冲突。

    public const string PaletteFill = "palette.fill";
    public const string PaletteStroke = "palette.stroke";
    public const string PaletteText = "palette.text";
    public const string PaletteWeight = "palette.weight";

    #endregion

    #region 文本预设的子字段
    //
    // 与调色板那一组同一个口径：预设的样式记录拆成子字段，界面上一个成员一个控件，
    // 冲突判定也要能认出"一边改字号、一边改字重"可以共存。
    //
    // 名字用 preset. 前缀而不是复用 text.*：text.* 登记在节点名下，写在节点上；
    // 这一组写在预设的样式记录上，归属不同。取值的语法两边一致——都是 TextStyle 的成员。

    public const string PresetFontFamily = "preset.fontFamily";
    public const string PresetFontSize = "preset.fontSize";
    public const string PresetFontWeight = "preset.fontWeight";
    public const string PresetItalic = "preset.italic";
    public const string PresetUnderline = "preset.underline";
    public const string PresetStrikethrough = "preset.strikethrough";
    public const string PresetFontColor = "preset.fontColor";
    public const string PresetAlign = "preset.align";

    #endregion

    /// <summary>是否表示"整个元素被增删"。</summary>
    public static bool IsElementLevel(string? name) => name?.StartsWith('@') ?? false;

    #endregion
}

/// <summary>
/// 字段元数据表。
/// </summary>
/// <remarks>
/// <para>
/// 它服务的第一个场合是冲突判定：两边改了同一个元素时，改的是不是同一个字段
/// 决定了能不能自动合并。第二个场合是把"这个字段改了要不要重排"这件事集中定义，
/// 而不是散落在哈希函数与命令实现里各判一次。
/// </para>
/// <para>
/// 没有登记的字段会被 <see cref="Descriptor"/> 当作"不知道作用域"处理，
/// 而不是猜一个。猜的话，某个字段明明影响布局却被当成纯外观，
/// 表现是"改了它之后图没重排，看起来没生效"——这类问题极难定位。
/// </para>
/// </remarks>
public static class FieldRegistry
{
    private static readonly FieldDescriptor[] AllFields =
    [
        // 节点
        new(FieldNames.Label, "节点", FieldScope.Visual),
        new(FieldNames.Shape, "节点", FieldScope.Visual),
        new(FieldNames.Parent, "节点", FieldScope.Structural),
        new(FieldNames.Layer, "节点", FieldScope.Visual),
        new(FieldNames.Page, "节点", FieldScope.Structural),
        new(FieldNames.StyleToken, "节点", FieldScope.Visual),
        new(FieldNames.Style, "节点", FieldScope.Visual),
        new(FieldNames.Text, "节点", FieldScope.Visual),
        new(FieldNames.Ports, "节点", FieldScope.Structural, Atomic: true),
        new(FieldNames.RichText, "节点", FieldScope.Visual),
        new(FieldNames.MathMode, "节点", FieldScope.Visual),
        new(FieldNames.Desc, "节点", FieldScope.Visual),
        new(FieldNames.Meta, "节点", FieldScope.Neither),

        // 样式与文本的子字段。作用域跟它们的整体字段一致：整体字段在视觉哈希里，
        // 子字段就也在视觉哈希里。两处不一致的话，改了某个成员会出现
        // "哈希说不用重排、面板却要求重排"这类错位。
        new(FieldNames.StyleFill, "节点", FieldScope.Visual),
        new(FieldNames.StyleStroke, "节点", FieldScope.Visual),
        new(FieldNames.StyleWeight, "节点", FieldScope.Visual),
        new(FieldNames.StyleBorder, "节点", FieldScope.Visual),
        new(FieldNames.StyleRadius, "节点", FieldScope.Visual),
        new(FieldNames.StyleOpacity, "节点", FieldScope.Visual),
        new(FieldNames.StyleBadge, "节点", FieldScope.Visual),

        new(FieldNames.TextFontFamily, "节点", FieldScope.Visual),
        new(FieldNames.TextFontSize, "节点", FieldScope.Visual),
        new(FieldNames.TextFontWeight, "节点", FieldScope.Visual),
        new(FieldNames.TextItalic, "节点", FieldScope.Visual),
        new(FieldNames.TextUnderline, "节点", FieldScope.Visual),
        new(FieldNames.TextStrikethrough, "节点", FieldScope.Visual),
        new(FieldNames.TextFontColor, "节点", FieldScope.Visual),
        new(FieldNames.TextAlign, "节点", FieldScope.Visual),

        // 边
        new(FieldNames.From, "边", FieldScope.Structural),
        new(FieldNames.To, "边", FieldScope.Structural),
        new(FieldNames.FromPort, "边", FieldScope.Structural),
        new(FieldNames.ToPort, "边", FieldScope.Structural),
        new(FieldNames.Label, "边", FieldScope.Visual),
        new(FieldNames.Style, "边", FieldScope.Visual),
        new(FieldNames.Page, "边", FieldScope.Structural),

        // 组合
        new(FieldNames.Label, "组合", FieldScope.Visual),
        new(FieldNames.Parent, "组合", FieldScope.Structural),
        new(FieldNames.Members, "组合", FieldScope.Structural, Atomic: true),
        new(FieldNames.CompositeDirection, "组合", FieldScope.Structural),
        new(FieldNames.Collapsed, "组合", FieldScope.Structural),
        new(FieldNames.Style, "组合", FieldScope.Visual),
        new(FieldNames.LocalLayout, "组合", FieldScope.Structural),

        // 文档级
        new(FieldNames.Kind, "文档", FieldScope.Structural),
        new(FieldNames.Direction, "文档", FieldScope.Structural),
        new(FieldNames.NodeSpacing, "文档", FieldScope.Structural),
        new(FieldNames.LayerSpacing, "文档", FieldScope.Structural),
        new(FieldNames.SameRank, "文档", FieldScope.Structural, Atomic: true),
        new(FieldNames.Order, "文档", FieldScope.Structural, Atomic: true),
        new(FieldNames.Align, "文档", FieldScope.Structural, Atomic: true),
        new(FieldNames.Place, "文档", FieldScope.Structural, Atomic: true),

        // 调色板。整个条目只计外观：换调色板不改变节点尺寸，坐标仍然有效。
        new(FieldNames.PaletteFill, "调色板", FieldScope.Visual),
        new(FieldNames.PaletteStroke, "调色板", FieldScope.Visual),
        new(FieldNames.PaletteText, "调色板", FieldScope.Visual),
        new(FieldNames.PaletteWeight, "调色板", FieldScope.Visual),

        // 文本预设。与调色板同一口径：预设进视觉哈希，它的子字段也只计外观。
        new(FieldNames.PresetFontFamily, "预设", FieldScope.Visual),
        new(FieldNames.PresetFontSize, "预设", FieldScope.Visual),
        new(FieldNames.PresetFontWeight, "预设", FieldScope.Visual),
        new(FieldNames.PresetItalic, "预设", FieldScope.Visual),
        new(FieldNames.PresetUnderline, "预设", FieldScope.Visual),
        new(FieldNames.PresetStrikethrough, "预设", FieldScope.Visual),
        new(FieldNames.PresetFontColor, "预设", FieldScope.Visual),
        new(FieldNames.PresetAlign, "预设", FieldScope.Visual),

        // 画布设置。整个子对象只计外观：网格、背景、纸张尺寸都不改变任何坐标。
        new(FieldNames.CanvasGrid, "画布", FieldScope.Visual),
        new(FieldNames.CanvasGridSize, "画布", FieldScope.Visual),
        new(FieldNames.CanvasPageSize, "画布", FieldScope.Visual),
        new(FieldNames.CanvasOrientation, "画布", FieldScope.Visual),
        new(FieldNames.CanvasBackground, "画布", FieldScope.Visual),
        new(FieldNames.CanvasInfinite, "画布", FieldScope.Visual),
    ];

    private static readonly HashSet<string> ElementLevel =
    [
        FieldNames.NodeElement,
        FieldNames.EdgeElement,
        FieldNames.CompositeElement,
        FieldNames.TagElement,
        FieldNames.ActionElement,
        FieldNames.FontElement,
        FieldNames.TextPresetElement,
        FieldNames.LayerElement,
        FieldNames.PageElement,
        FieldNames.PaletteEntryElement,
    ];

    private static readonly Dictionary<string, FieldDescriptor> ByName = BuildIndex();

    /// <summary>全部已登记的字段。</summary>
    public static IReadOnlyList<FieldDescriptor> All => AllFields;

    /// <summary>已登记的元素级名字。</summary>
    public static IReadOnlyCollection<string> ElementNames => ElementLevel;

    /// <summary>
    /// 取一个字段的元数据。查不到时返回空，调用方据此按"未知字段"处理。
    /// </summary>
    /// <remarks>
    /// 同名字段可能属于多种定义（例如 <c>label</c> 在节点、边、组合上都有），
    /// 作用域一致时合并成一条；不一致的情况在 <see cref="BuildIndex"/> 里会被发现。
    /// </remarks>
    public static FieldDescriptor? Descriptor(string? name) =>
        name is not null && ByName.TryGetValue(name, out var descriptor) ? descriptor : null;

    /// <summary>字段是否已登记。元素级名字也算已登记。</summary>
    public static bool IsKnown(string? name) =>
        name is not null && (ElementLevel.Contains(name) || ByName.ContainsKey(name));

    /// <summary>两个字段能否在冲突时共存。同一个字段不能，不同字段可以。</summary>
    public static bool CanCoexist(string left, string right) =>
        !string.Equals(left, right, StringComparison.Ordinal);

    private static Dictionary<string, FieldDescriptor> BuildIndex()
    {
        var index = new Dictionary<string, FieldDescriptor>(StringComparer.Ordinal);

        foreach (var field in AllFields)
        {
            if (!index.TryGetValue(field.Name, out var existing))
            {
                index[field.Name] = field;
                continue;
            }

            // 同名不同作用域是登记表自身的矛盾：同一个名字在节点上是外观、
            // 在边上是结构，冲突判定与哈希口径就会各按一半理解。
            // 这里直接拒绝，而不是取其一——静默取一个会让问题藏到运行期。
            if (existing.Scope != field.Scope)
            {
                throw new InvalidOperationException(
                    $"字段名 {field.Name} 在{existing.Owner}与{field.Owner}上的作用域不一致"
                    + $"（{existing.Scope} 对 {field.Scope}）。同名必须同义。");
            }
        }

        return index;
    }
}
