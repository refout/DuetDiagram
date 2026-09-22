using System.ComponentModel;

namespace DuetDiagram.Llm.Tools;

/// <summary>
/// 内置的八个粗粒度工具。
/// </summary>
/// <remarks>
/// <para>
/// **粗粒度是有意的。** 四十多条命令如果一条一个工具，模型每次调用前都要在一长串名字里挑，
/// 而挑错的代价是一次失败往返。收成八个之后，模型先选"改结构还是改外观"，
/// 再用 <c>action</c> 参数说明具体做什么——这一步它本来就要想清楚。
/// </para>
/// <para>
/// 参数表是**接口**，实现可以分批接上。这一层先把八个工具的名称、说明与参数定死，
/// 因为接口分两次定的话，先接上的那些调用方要跟着改。
/// </para>
/// <para>
/// **折点与固定位置没有 action。** 两者都写在文档之外的人工产物文件里、不走命令总线，
/// 所以它们不该出现在任何一个工具的动作表里：开一个的话，那条 action 要么绕开总线
/// 直接改那份文件（撤销栈与广播都不经过），要么给它们立一条命令。
/// </para>
/// </remarks>
public static class DiagramToolset
{
    #region 工具名

    public const string Read = "diagram_read";
    public const string Edit = "diagram_edit";
    public const string Style = "diagram_style";
    public const string Layout = "diagram_layout";
    public const string Composite = "diagram_composite";
    public const string Export = "diagram_export";
    public const string Validate = "diagram_validate";
    public const string UndoRedo = "diagram_undo_redo";

    #endregion

    #region 说明

    private const string ReadDescription =
        "读取当前图，返回节点、边、布局、锁定、可用样式令牌与最近修改的归一化摘要。"
        + "在需要知道图现在长什么样、或者动手改之前先确认现状时用它。"
        + "摘要里不含原始坐标——坐标每次重排都会变，照着一份会过期的数字去调位置没有意义。";

    private const string EditDescription =
        "按动作修改图的结构：增删节点与边、改标签文字、连线与重连，以及页面、图层、标签、动作与图类型。"
        + "要改的是「图里有什么、谁连着谁」时用它；只改外观用 "
        + Style + "，改布局参数用 " + Layout + "。"
        + "一次调用只做一件事，动作名见 action 参数。结构变更会让已经算出的坐标失效。";

    private const string StyleDescription =
        "按动作改外观：节点形状、样式令牌、文本样式、调色板与画布设置。"
        + "要改的是「看起来什么样」而不是「有什么」时用它；改标签文字属于结构，走 " + Edit + "。"
        + "样式令牌必须已经在调色板里，不在的会被拒绝并列出可用的那些——"
        + "放行未知令牌的话，渲染层会静默按默认样式画，而调用方以为样式生效了。";

    private const string LayoutDescription =
        "按动作调整布局：主方向、同层与层间间距、同层 / 层内次序 / 对齐三类约束，以及一对节点的相对位置。"
        + "用户说「排得不好看」「这两个要并排」时用它。"
        + "它只改布局参数，不搬运坐标；改完之后要不要重排由返回结果里的结构变更标志告诉宿主。";

    private const string CompositeDescription =
        "按动作管理组合：把一批元素收进分组、泳道、子流程或组合框，把成员移进另一个组合，或解散一个组合。"
        + "要表达「这几个是一伙的」时用它。"
        + "一个元素只能属于一个组合，移入会把它从原容器里摘出来；成环与超过嵌套深度上限都会被拒绝。";

    private const string ExportDescription =
        "把当前图导出成文本格式：自有 DSL 或 Mermaid。"
        + "要拿一份能贴进别处、或者交给别人看的文本时用它。"
        + "导出不改变文档，也不进撤销栈。位图与 PDF 走同一个入口但还没有实现，会返回结构化的「尚未支持」。";

    private const string ValidateDescription =
        "整体校验当前文档，返回结构化错误与修复建议。"
        + "改完一批东西之后、或者接了一份外部来的内容之后用它。"
        + "它只报告不修改，修是调用方的事。不经过命令层的输入只有这一条路能发现问题。";

    private const string UndoRedoDescription =
        "撤销或重做。人和模型共用同一条历史栈，所以撤销可能撤掉的是人刚做的那一步。"
        + "调用之前先想清楚要撤的是哪一条，返回结果里会说明实际撤掉了什么。"
        + "重做只在没有新变更时可用——新变更一出现，重做栈就作废了。";

    #endregion

    #region 声明

    /// <summary>八个工具的定义。次序固定，与工具表里那八行一致。</summary>
    public static IReadOnlyList<ToolDescriptor> Create() =>
    [
        ToolDescriptor.Create(ReadDiagram, Read, ReadDescription),
        ToolDescriptor.Create(EditDiagram, Edit, EditDescription),
        ToolDescriptor.Create(StyleDiagram, Style, StyleDescription),
        ToolDescriptor.Create(LayoutDiagram, Layout, LayoutDescription),
        ToolDescriptor.Create(CompositeDiagram, Composite, CompositeDescription),
        ToolDescriptor.Create(ExportDiagram, Export, ExportDescription),
        ToolDescriptor.Create(ValidateDiagram, Validate, ValidateDescription),
        ToolDescriptor.Create(UndoRedoDiagram, UndoRedo, UndoRedoDescription),
    ];

    #endregion

    #region 参数表
    //
    // 参数名与类型就是 schema。约束写在参数上，由 SchemaBuilder 回填进 schema，
    // 于是"约束怎么进去的"与"约束怎么被读出来"读的是同一处声明。
    //
    // 端点参数（from / to）允许带端口，写法是「标识.端口名」。端口写在端点上而不另立参数，
    // 是因为模型描述一条连线时说的本来就是"从 a 的下边到 b 的上边"这一件事；
    // 拆成四个参数之后，端点与端口对不上就成了一个可以表达出来、却没有意义的状态。

    private static Task<ToolResult> ReadDiagram(
        [Description("要读的页面标识。留空表示当前页。")][Pattern(Patterns.DiagramId)] string? pageId = null) =>
        Pending(Read, null);

    private static Task<ToolResult> EditDiagram(
        [Description("要做的事，例如 add-node、remove-node、connect-edge、set-node-field。")][Pattern(Patterns.DiagramId)] string action,
        [Description("这次操作针对的元素标识。")][Pattern(Patterns.DiagramId)] string? id = null,
        [Description("连线的起点，可以带端口，例如 a 或 a.bottom。")][Pattern(Patterns.Endpoint)] string? from = null,
        [Description("连线的终点，写法同 from。")][Pattern(Patterns.Endpoint)] string? to = null,
        [Description("要写的字段名，用于 set-node-field 与 set-edge-field。")] string? field = null,
        [Description("字段要写成的值。")] string? value = null,
        [Description("显示文本：节点标签、页面名、图层名、标签名。")] string? label = null,
        [Description("成员标识，用于打标签。")][Pattern(Patterns.DiagramId)] string[]? memberIds = null,
        [Description("插入位置或次序，从零开始。")] int? index = null) =>
        Pending(Edit, action);

    private static Task<ToolResult> StyleDiagram(
        [Description("要做的事，例如 set-shape、set-style、set-text、set-canvas。")][Pattern(Patterns.DiagramId)] string action,
        [Description("要改的元素标识。")][Pattern(Patterns.DiagramId)] string? id = null,
        [Description("样式令牌名，必须已经在调色板里。")][Pattern(Patterns.DiagramId)] string? token = null,
        [Description("要改的样式成员名，例如 style.fill、text.fontSize。")] string? field = null,
        [Description("成员要写成的值。")] string? value = null) =>
        Pending(Style, action);

    private static Task<ToolResult> LayoutDiagram(
        [Description("要做的事，例如 set-direction、set-spacing、add-constraint、set-place。")][Pattern(Patterns.DiagramId)] string action,
        [Description("约束种类：same-rank、align 或 order。")] string? kind = null,
        [Description("主方向：LR、TB、RL、BT。")] string? direction = null,
        [Description("同层节点间距。")] double? nodeSpacing = null,
        [Description("层与层之间的间距。")] double? layerSpacing = null,
        [Description("要摆位的节点，用于 set-place。")][Pattern(Patterns.DiagramId)] string? id = null,
        [Description("参照节点，用于 set-place。")][Pattern(Patterns.DiagramId)] string? relativeTo = null,
        [Description("相对位置：right-of、left-of、above、below。")] string? relation = null,
        [Description("这条约束归谁：auto、llm 或 human。")] string? owner = null,
        [Description("约束的成员：同层与对齐是节点，层内次序是出边。")][Pattern(Patterns.DiagramId)] string[]? memberIds = null,
        [Description("层内次序的主语节点。")][Pattern(Patterns.DiagramId)] string? subject = null) =>
        Pending(Layout, action);

    private static Task<ToolResult> CompositeDiagram(
        [Description("要做的事：create、dissolve 或 move-into。")][Pattern(Patterns.DiagramId)] string action,
        [Description("组合标识。")][Pattern(Patterns.DiagramId)] string? id = null,
        [Description("组合种类：group、lane、subflow 或 combo。")] string? kind = null,
        [Description("成员标识。")][Pattern(Patterns.DiagramId)] string[]? memberIds = null,
        [Description("移入的目标组合。留空表示搬到顶层。")][Pattern(Patterns.DiagramId)] string? targetId = null,
        [Description("组合的显示名。")] string? label = null) =>
        Pending(Composite, action);

    private static Task<ToolResult> ExportDiagram(
        [Description("导出格式：dsl 或 mermaid。")][Pattern(Patterns.DiagramId)] string format,
        [Description("要导出的页面标识。留空表示当前页。")][Pattern(Patterns.DiagramId)] string? pageId = null) =>
        Pending(Export, format);

    private static Task<ToolResult> ValidateDiagram(
        [Description("校验范围。留空表示整份文档。")] string? scope = null) =>
        Pending(Validate, null);

    private static Task<ToolResult> UndoRedoDiagram(
        [Description("要做的事：undo 或 redo。")][Pattern(Patterns.DiagramId)] string action,
        [Description("撤几步。留空表示一步。")] int? steps = null) =>
        Pending(UndoRedo, action);

    #endregion

    #region 执行体
    //
    // 这一批现在一律返回结构化的「尚未接上」。
    // 逐个接上时替换的是各自的方法体，参数表不动——接口先定死，实现分批填。

    /// <summary>能力还没接上时的统一答复。</summary>
    private static Task<ToolResult> Pending(string tool, string? action) =>
        Task.FromResult(ToolResult.NotSupported(tool, action, "动作分发表还没有接上"));

    #endregion
}
