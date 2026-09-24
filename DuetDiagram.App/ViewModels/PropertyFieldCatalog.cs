using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Shapes;

namespace DuetDiagram.App.ViewModels;

/// <summary>一个字段在面板上用哪种控件编辑。</summary>
public enum PropertyEditor
{
    /// <summary>不给控件。见 <see cref="PropertyFieldCatalog.Hidden"/>。</summary>
    None,

    /// <summary>单行文本。</summary>
    Text,

    /// <summary>多行文本。</summary>
    Multiline,

    /// <summary>数值。</summary>
    Number,

    /// <summary>开关。空值表示"没有设置"，与"设成假"不是一回事。</summary>
    Flag,

    /// <summary>从一组固定取值里选一个。</summary>
    Choice,
}

/// <summary>一个字段的界面描述。</summary>
/// <param name="Field">字段名，取自字段表。</param>
/// <param name="Label">界面上显示的名字。</param>
/// <param name="Section">归到哪一节。</param>
/// <param name="Editor">用哪种控件。</param>
/// <param name="Choices">可选取值。只对 <see cref="PropertyEditor.Choice"/> 有意义。</param>
/// <param name="AllowEmpty">
/// 允许"没有设置"这一档。只对 <see cref="PropertyEditor.Choice"/> 有意义——
/// 其余控件把值清空就等于"没有设置"，不需要额外的一档。
/// 必填字段给的是假：给出一档选了就被拒的选项，用户会以为界面坏了。
/// </param>
public sealed record PropertyFieldSpec(
    string Field,
    string Label,
    string Section,
    PropertyEditor Editor,
    IReadOnlyList<string>? Choices = null,
    bool AllowEmpty = true,
    bool AllowCustom = false);

/// <summary>
/// 面板上每个字段的界面描述，以及它与字段表的对应关系。
/// </summary>
/// <remarks>
/// <para>
/// **字段名的唯一来源仍然是字段表。** 这里只补界面需要而字段表不关心的那几样：
/// 显示名、归到哪一节、用哪种控件。两边各抄一份字段名的话，
/// 改了字段名之后面板会静默失联——那个控件还在，只是改不动文档。
/// </para>
/// <para>
/// 所以这张表在构建时做两件事：字段表里每个节点字段都要有描述（没有就抛异常），
/// 描述里每个字段都要在字段表里（没有也抛异常）。抛在启动时而不是等用户点到那个控件——
/// 后者的表现是"点了一下没反应"，而没有人会去查代码。
/// </para>
/// </remarks>
internal static class PropertyFieldCatalog
{
    /// <summary>
    /// 布局约束那一节的标题。
    /// </summary>
    /// <remarks>
    /// 单独一个常量，因为面板靠它决定哪一节挂约束编辑器。写成字面量的话，
    /// 改一次标题就会让编辑器悄悄挂不上，而界面上只是少了一块，看起来像"这一轮没做"。
    /// </remarks>
    public const string ConstraintSection = "布局约束";

    /// <summary>
    /// 七个分节的标题与顺序。
    /// </summary>
    /// <remarks>
    /// 一节可以没有字段、只有一段只读内容（见 <c>PropertySectionViewModel.IsReadOnly</c>）：
    /// 图层与端口、动作这三节就是这样。它们的取值要么是运行期才有的（图层标识），
    /// 要么还没有编辑界面（端口、动作），给不出一个可选取值的列表。
    /// </remarks>
    public static IReadOnlyList<string> Sections { get; } =
    [
        "形状",
        "样式与调色板",
        "文本与字体",
        ConstraintSection,
        "图层",
        "端口",
        "动作与链接",
    ];

    /// <summary>
    /// 登记在字段表里、但这一轮不在面板上出现的字段。
    /// </summary>
    /// <remarks>
    /// 每个都要写清为什么不出现。留着空档不解释的话，下一个人分不清
    /// "还没做"与"有意不做"，而两者该有的处置完全不同。
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Hidden { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FieldNames.Parent] = "归属组合。入口在分组操作里，面板上改它等于绕开成员列表的一致性校验",
            [FieldNames.Layer] = "图层归属。改它的入口在图层面板上（那里才看得到全部图层，也多选之后一次移入）；这里只在「图层」那一节里把当前归属显示出来",
            [FieldNames.Page] = "页面归属。改它的入口在画布上方的标签栏上——那里才看得到全部页面，而归属是按「在某一页上画东西」发生的",
            [FieldNames.Style] = "样式整体。面板按成员逐个编辑，见样式与文本的子字段",
            [FieldNames.Text] = "文本样式整体。面板按成员逐个编辑，见样式与文本的子字段",
            [FieldNames.Ports] = "端口整体。入口在连线交互里，那里才看得到连线的出入点",
            [FieldNames.Meta] = "宿主自定义数据。键值对没有通用编辑界面，它的含义只有宿主知道",
        };

    private static readonly PropertyFieldSpec[] AllFields =
    [
        // 形状是必填的：节点总得有个形状，所以这一档不给"没有设置"。
        // 取值表来自形状库，不来自枚举反射：加一个形状只改形状库一处，
        // 而枚举反射那条路还会漏掉"注册表里有、枚举里没有"这类不一致。
        new(FieldNames.Shape, "形状", "形状", PropertyEditor.Choice, ShapeNames(), AllowEmpty: false),

        // 样式令牌的取值长在文档的调色板里，选项由属性面板在每次重读时推进来；
        // 允许自定义取值——认不出的令牌不报错，渲染按元素自己的样式兜底，
        // 所以下拉里当前值不在选项里时补一项显示，而不是显示成"没有设置"。
        new(FieldNames.StyleToken, "样式令牌", "样式与调色板", PropertyEditor.Choice, AllowCustom: true),
        new(FieldNames.StyleFill, "填充", "样式与调色板", PropertyEditor.Text),
        new(FieldNames.StyleStroke, "描边", "样式与调色板", PropertyEditor.Text),
        new(FieldNames.StyleBorder, "边框线型", "样式与调色板", PropertyEditor.Choice, Names<LineStyle>()),
        new(FieldNames.StyleWeight, "描边粗细", "样式与调色板", PropertyEditor.Number),
        new(FieldNames.StyleRadius, "圆角半径", "样式与调色板", PropertyEditor.Number),
        new(FieldNames.StyleOpacity, "不透明度", "样式与调色板", PropertyEditor.Number),
        new(FieldNames.StyleBadge, "角标", "样式与调色板", PropertyEditor.Text),

        new(FieldNames.Label, "标签", "文本与字体", PropertyEditor.Text),
        new(FieldNames.Desc, "说明", "文本与字体", PropertyEditor.Multiline),
        new(FieldNames.TextFontFamily, "字体", "文本与字体", PropertyEditor.Text),
        new(FieldNames.TextFontSize, "字号", "文本与字体", PropertyEditor.Number),
        new(FieldNames.TextFontWeight, "字重", "文本与字体", PropertyEditor.Choice, Names<FontWeight>()),
        new(FieldNames.TextFontColor, "文字颜色", "文本与字体", PropertyEditor.Text),
        new(FieldNames.TextAlign, "水平对齐", "文本与字体", PropertyEditor.Choice, Names<TextAlign>()),
        new(FieldNames.TextItalic, "斜体", "文本与字体", PropertyEditor.Flag),
        new(FieldNames.TextUnderline, "下划线", "文本与字体", PropertyEditor.Flag),
        new(FieldNames.TextStrikethrough, "删除线", "文本与字体", PropertyEditor.Flag),
        new(FieldNames.RichText, "富文本", "文本与字体", PropertyEditor.Flag),

        // 数学排版也是必填的：它决定标签按哪一种排版走，没有"没设置"这一档。
        new(FieldNames.MathMode, "数学排版", "文本与字体", PropertyEditor.Choice, Names<MathMode>(), AllowEmpty: false),
    ];

    private static readonly Dictionary<string, PropertyFieldSpec> ByField = BuildIndex();

    /// <summary>面板上出现的全部字段，按分节与登记顺序。</summary>
    public static IReadOnlyList<PropertyFieldSpec> All => AllFields;

    /// <summary>取一个字段的界面描述。没有描述时为空。</summary>
    public static PropertyFieldSpec? Find(string field) =>
        ByField.TryGetValue(field, out var spec) ? spec : null;

    private static IReadOnlyList<string> Names<TEnum>()
        where TEnum : struct, Enum => Enum.GetNames<TEnum>();

    /// <summary>
    /// 形状名，按形状库给出的顺序。
    /// </summary>
    /// <remarks>
    /// 与其它几档的 <see cref="Names{TEnum}"/> 分开：形状的取值表来自形状库，
    /// 而库与枚举的一致性由注册表在构造时校验，不靠"两边都从枚举反射"来保证。
    /// </remarks>
    private static IReadOnlyList<string> ShapeNames() =>
        [.. ShapeRegistry.Default.All.Select(definition => definition.Name)];

    /// <summary>
    /// 建索引，并把描述表与字段表对齐。
    /// </summary>
    /// <remarks>
    /// 两边都要查：字段表里有而这里没有，说明新加的字段在面板上不可见；
    /// 这里有的而字段表里没有，说明这个控件改不动文档。两种都是静默故障，
    /// 而静默故障要等到有人点到它才会发现。
    /// </remarks>
    private static Dictionary<string, PropertyFieldSpec> BuildIndex()
    {
        var index = new Dictionary<string, PropertyFieldSpec>(StringComparer.Ordinal);

        foreach (var spec in AllFields)
        {
            if (!index.TryAdd(spec.Field, spec))
            {
                throw new InvalidOperationException($"字段 {spec.Field} 在面板描述表里出现了两次");
            }

            if (!NodeFieldValue.IsWritable(spec.Field))
            {
                throw new InvalidOperationException(
                    $"字段 {spec.Field} 有界面描述，却不在节点可写的字段表里。"
                    + "那个控件改不动文档，而界面上看不出任何异常。");
            }

            if (spec.Editor == PropertyEditor.None)
            {
                throw new InvalidOperationException($"字段 {spec.Field} 标了不显示，却又出现在描述表里");
            }

            // 允许自定义取值的字段例外：它的选项长在文档里（样式令牌 → 调色板条目），
            // 由属性面板在每次重读时推进来，构造时给不出、也用不着给。
            if (spec.Editor == PropertyEditor.Choice && !spec.AllowCustom
                && spec.Choices is not { Count: > 0 })
            {
                throw new InvalidOperationException($"字段 {spec.Field} 是选择控件，却没有给可选取值");
            }

            if (spec.Editor != PropertyEditor.Choice && !spec.AllowEmpty)
            {
                throw new InvalidOperationException(
                    $"字段 {spec.Field} 标了不允许空值，但它不是选择控件——"
                    + "其余控件把内容清空就等于没有值，标了也不会生效。");
            }
        }

        foreach (var field in NodeFieldValue.Writable)
        {
            if (!index.ContainsKey(field) && !Hidden.ContainsKey(field))
            {
                throw new InvalidOperationException(
                    $"字段 {field} 已登记在字段表里，面板既没有它的描述，也没有说明为什么不显示");
            }
        }

        foreach (var field in Hidden.Keys)
        {
            if (!NodeFieldValue.IsWritable(field))
            {
                throw new InvalidOperationException($"隐藏清单里的 {field} 不在节点可写的字段表里");
            }

            if (index.ContainsKey(field))
            {
                throw new InvalidOperationException($"字段 {field} 既在隐藏清单里，又有界面描述");
            }
        }

        return index;
    }
}
