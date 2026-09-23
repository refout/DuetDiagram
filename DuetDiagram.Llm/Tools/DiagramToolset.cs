using System.ComponentModel;
using System.Text.Json;
using DuetDiagram.Llm.Context;

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
/// 参数表是**接口**，实现可以分批接上。参数先定死，因为接口分两次定的话，
/// 先接上的那些调用方要跟着改。
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
        + "它只改布局参数，不搬运坐标；改完之后要不要重排由返回结果里的结构变更标志告诉宿主。"
        + "相对位置这一类约束求解器还不消费它，设完之后坐标不会变。";

    private const string CompositeDescription =
        "按动作管理组合：把一批元素收进分组、泳道、子流程或组合框，把成员移进另一个组合，或解散一个组合。"
        + "要表达「这几个是一伙的」时用它。"
        + "一个元素只能属于一个组合，移入会把它从原容器里摘出来；成环与超过嵌套深度上限都会被拒绝。"
        + "新建的组合一律在顶层，要嵌套就再发一次移入。";

    private const string ExportDescription =
        "把当前图导出成文本格式。现在能导出 Mermaid。"
        + "要拿一份能贴进别处、或者交给别人看的文本时用它。"
        + "导出不改变文档，也不进撤销栈。"
        + "导出必然有损——IR 的表达力强于目标格式，写不出来的东西在返回的 dropped 里逐类列出，"
        + "报告为空不等于无损。自有 DSL 的导出方向还没有实现，位图与 PDF 走同一个入口"
        + "但还没有排到，这几样会返回结构化的「尚未支持」。";

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

    /// <summary>
    /// 八个工具的定义。次序固定，与工具表里那八行一致。
    /// </summary>
    /// <remarks>
    /// 声明是实例方法而不是静态方法，因为执行体要读到那一份文档。
    /// 把上下文当成声明方法的一个参数是不行的：参数表从签名推导，多一个参数
    /// 就会多一条模型要填的 schema，而它根本不是模型能提供的东西。
    /// </remarks>
    public static IReadOnlyList<ToolDescriptor> Create(DiagramToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var declarations = new Declarations(context);

        return
        [
            ToolDescriptor.Create(declarations.Read, Read, ReadDescription),
            ToolDescriptor.Create(declarations.Edit, Edit, EditDescription),
            ToolDescriptor.Create(declarations.Style, Style, StyleDescription),
            ToolDescriptor.Create(declarations.Layout, Layout, LayoutDescription),
            ToolDescriptor.Create(declarations.Composite, Composite, CompositeDescription),
            ToolDescriptor.Create(declarations.Export, Export, ExportDescription),
            ToolDescriptor.Create(declarations.Validate, Validate, ValidateDescription),
            ToolDescriptor.Create(declarations.UndoRedo, UndoRedo, UndoRedoDescription),
        ];
    }

    #endregion

    #region 参数表与执行体

    /// <summary>
    /// 八个工具的签名与执行体，都绑在这一份上下文上。
    /// </summary>
    /// <remarks>
    /// 参数名与类型就是 schema。约束写在参数上，由 <see cref="SchemaBuilder"/> 回填进 schema，
    /// 于是"约束怎么进去的"与"约束怎么被读出来"读的是同一处声明。
    /// 端点参数（from / to）允许带端口，写法是「标识.端口名」：模型描述一条连线时说的
    /// 本来就是"从 a 的下边到 b 的上边"这一件事，拆成四个参数之后，
    /// 端点与端口对不上就成了一个可以表达出来、却没有意义的状态。
    /// </remarks>
    private sealed class Declarations(DiagramToolContext context)
    {
        private readonly DiagramToolContext _context = context;

        #region diagram_read

        public Task<ToolResult> Read(
            [Description("要读的页面标识。留空表示整份文档。")][Pattern(Patterns.DiagramId)] string? pageId = null)
        {
            // 点名的页面不在文档里要如实说：与元素自己的归属字段不同，
            // 那一条指向不存在的页面按缺省页处理（文档内部的引用），
            // 而这里点的是调用方要读的东西——认下来按别的页回，它拿到的是一张不是它要的图。
            if (ActionDispatch.PageMissing(_context, pageId) is { } missing)
            {
                return Task.FromResult(missing);
            }

            var summary = SummaryBuilder.Build(_context.ToSummaryInput(pageId));
            var payload = new SummaryPayload(
                SummaryFormatter.Format(summary, _context.Clock.UtcNow),
                summary);

            return Task.FromResult(ToolResult.Ok(
                JsonSerializer.SerializeToElement(payload, SummaryJsonContext.Default.SummaryPayload),
                $"读到 {summary.Nodes.Count} 个节点、{summary.Edges.Count} 条边"));
        }

        #endregion

        #region diagram_edit

        public Task<ToolResult> Edit(
            [Description("要做的事，例如 add-node、remove-node、connect-edge、set-node-field。")][Pattern(Patterns.DiagramId)] string action,
            [Description("这次操作针对的元素标识。")][Pattern(Patterns.DiagramId)] string? id = null,
            [Description("连线的起点，可以带端口，例如 a 或 a.bottom。")][Pattern(Patterns.Endpoint)] string? from = null,
            [Description("连线的终点，写法同 from。")][Pattern(Patterns.Endpoint)] string? to = null,
            [Description("要写的字段名，用于 set-node-field 与 set-edge-field。")] string? field = null,
            [Description("字段要写成的值。set-kind 也用它，填图类型名；两个图层开关（set-layer-visible / set-layer-locked）填 true 或 false。")] string? value = null,
            [Description("显示文本：节点标签、边标签、页面名、图层名、标签名。")] string? label = null,
            [Description("成员标识，用于打标签。")][Pattern(Patterns.DiagramId)] string[]? memberIds = null,
            [Description("插入位置或次序，从零开始。")] int? index = null,
            [Description("触发事件名，用于 add-action，例如 click。")] string? @event = null,
            [Description("动作类型名，用于 add-action，例如 open-url。")] string? kind = null,
            [Description("动作作用的对象标识，用于 add-action。")][Pattern(Patterns.DiagramId)] string? targetId = null) =>
            Task.FromResult(EditTool.Run(_context, new EditArguments(
                action, id, from, to, field, value, label, memberIds, index, @event, kind, targetId)));

        #endregion

        #region diagram_style

        public Task<ToolResult> Style(
            [Description("要做的事，例如 set-shape、set-style、set-text、set-canvas。")][Pattern(Patterns.DiagramId)] string action,
            [Description("要改的元素标识。")][Pattern(Patterns.DiagramId)] string? id = null,
            [Description("样式令牌名，必须已经在调色板里。用于 set-style 与调色板那三个动作。")][Pattern(Patterns.DiagramId)] string? token = null,
            [Description("要改的样式成员名，例如 style.fill、text.fontSize、canvas.grid。")] string? field = null,
            [Description("成员要写成的值。")] string? value = null) =>
            Task.FromResult(StyleTool.Run(_context, new StyleArguments(action, id, token, field, value)));

        #endregion

        #region diagram_layout

        public Task<ToolResult> Layout(
            [Description("要做的事，例如 set-direction、set-spacing、add-constraint、remove-constraint、set-place。")][Pattern(Patterns.DiagramId)] string action,
            [Description("约束种类：same-rank、align 或 order。")] string? kind = null,
            [Description("主方向：LR、TB、RL、BT。")] string? direction = null,
            [Description("同层节点间距。")] double? nodeSpacing = null,
            [Description("层与层之间的间距。")] double? layerSpacing = null,
            [Description("要摆位的节点，用于 set-place。")][Pattern(Patterns.DiagramId)] string? id = null,
            [Description("参照节点，用于 set-place。")][Pattern(Patterns.DiagramId)] string? relativeTo = null,
            [Description("相对位置：right-of、left-of、above、below。留空表示清除这一条。")] string? relation = null,
            [Description("这条约束归谁：auto 或 llm。留空按 llm 算。")] string? owner = null,
            [Description("约束的成员：同层与对齐是节点，层内次序是出边。")][Pattern(Patterns.DiagramId)] string[]? memberIds = null,
            [Description("层内次序的主语节点。")][Pattern(Patterns.DiagramId)] string? subject = null) =>
            Task.FromResult(LayoutTool.Run(_context, new LayoutArguments(
                action, kind, direction, nodeSpacing, layerSpacing, id, relativeTo, relation, owner, memberIds, subject)));

        #endregion

        #region diagram_composite

        public Task<ToolResult> Composite(
            [Description("要做的事：create、dissolve 或 move-into。")][Pattern(Patterns.DiagramId)] string action,
            [Description("组合标识。")][Pattern(Patterns.DiagramId)] string? id = null,
            [Description("组合种类：group、lane、subflow 或 combo。")] string? kind = null,
            [Description("成员标识。")][Pattern(Patterns.DiagramId)] string[]? memberIds = null,
            [Description("移入的目标组合。留空表示搬到顶层。")][Pattern(Patterns.DiagramId)] string? targetId = null,
            [Description("组合的显示名。")] string? label = null) =>
            Task.FromResult(CompositeTool.Run(_context, new CompositeArguments(
                action, id, kind, memberIds, targetId, label)));

        #endregion

        #region diagram_export

        public Task<ToolResult> Export(
            [Description("导出格式：mermaid、dsl、svg、png 或 pdf。现在只有 mermaid 接上了。")][Pattern(Patterns.DiagramId)] string format,
            [Description("要导出的页面标识。留空表示当前页。")][Pattern(Patterns.DiagramId)] string? pageId = null) =>
            Task.FromResult(ExportTool.Run(_context, new ExportArguments(format, pageId)));

        #endregion

        #region diagram_validate

        public Task<ToolResult> Validate(
            [Description("校验范围。留空表示整份文档。")] string? scope = null) =>
            Task.FromResult(ValidateTool.Run(_context, new ValidateArguments(scope)));

        #endregion

        #region diagram_undo_redo

        public Task<ToolResult> UndoRedo(
            [Description("要做的事：undo 或 redo。")][Pattern(Patterns.DiagramId)] string action,
            [Description("撤几步。留空表示一步。")] int? steps = null) =>
            Task.FromResult(HistoryTool.Run(_context, new HistoryArguments(action, steps)));

        #endregion
    }

    #endregion
}
