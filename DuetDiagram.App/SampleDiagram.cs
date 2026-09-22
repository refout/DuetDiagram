using System.Diagnostics;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Sidecar;
using DuetDiagram.Layout;
using DuetDiagram.Render;

namespace DuetDiagram.App;

/// <summary>
/// 从一份文档到绘制列表的整条链路，外加主程序启动时用的那份示例文档。
/// </summary>
/// <remarks>
/// <para>
/// 这条链路存在的理由与当初那个手画示意图的控件一样：确认整条链路能在当前技术栈上跑通。
/// 区别是这次走的是真链路——量尺寸、求解布局、翻译成绘制列表，
/// 而画布只认最后那一步的产物。手画示意图验证不了中间任何一段。
/// </para>
/// <para>
/// **链路的每一步只有这一份实现。** 帧率测量也要走这条路：另写一份的话，
/// 两边迟早会在引擎、约束或主题上分叉，而量出来的数字仍然像模像样。
/// </para>
/// <para>
/// 文档由调用方给出。主程序启动时给的是那份写死的示例——这一轮要验的是"画得出来"，
/// 从文件读会把失败原因混进"路径对不对""格式对不对"这些问题里；
/// 帧率测量给的是一份上千节点的生成图，那份文档写不到代码里。
/// </para>
/// </remarks>
public static class SampleDiagram
{
    /// <summary>主程序启动时画的那份示例文档。</summary>
    public static DiagramDocument Document() => DiagramDocument.CreateFromContent(
        "sample",
        DiagramKind.Flowchart,
        Direction.TB,
        nodes:
        [
            new NodeDef { Id = "start", Label = "开始", Shape = NodeShape.Stadium },
            new NodeDef { Id = "check", Label = "校验", Shape = NodeShape.Diamond },
            new NodeDef { Id = "pass", Label = "通过" },
            new NodeDef { Id = "fail", Label = "失败" },
            new NodeDef { Id = "end", Label = "结束", Shape = NodeShape.Stadium },
        ],
        edges:
        [
            new EdgeDef { Id = "e1", From = "start", To = "check" },
            new EdgeDef { Id = "e2", From = "check", To = "pass", Label = "是" },
            new EdgeDef { Id = "e3", From = "check", To = "fail", Label = "否" },
            new EdgeDef { Id = "e4", From = "pass", To = "end" },
            new EdgeDef { Id = "e5", From = "fail", To = "end" },
        ]);

    /// <summary>示例文档走完整条链路。</summary>
    public static SampleScene Build(Theme theme, ITextMeasurer measurer) =>
        Build(Document(), theme, measurer);

    /// <summary>
    /// 给定文档走完整条链路，给出可画的绘制列表。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 度量器由调用方传进来并在用完后释放：它按字体家族缓存字体对象，
    /// 而字体对象持有原生资源。让这个方法自己 new 一个再丢掉，
    /// 就是把"什么时候释放原生资源"这件事交给垃圾回收决定。
    /// </para>
    /// <para>
    /// 两段重活的耗时在这里量，而不是由调用方在外面量：布局与绘制列表构建
    /// 是这里面的两步，从外面看它们是一件事。要分开就得把整条链路拆成两个公开方法，
    /// 而那样调用方迟早会只调其中一个。
    /// </para>
    /// </remarks>
    public static SampleScene Build(DiagramDocument document, Theme theme, ITextMeasurer measurer) =>
        Build(document, theme, measurer, null);

    /// <summary>
    /// 带固定位置的同一链路。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 固定坐标来自人工产物（<see cref="UserSidecar.PinnedNodes"/>），不进 IR——
    /// 同一份语义在不同机器上不该因为某人拖过而变成不同的文档内容。
    /// 这里把它从 <see cref="Anchor"/> 翻成布局引擎认的 <see cref="LayoutPoint"/>，
    /// 转完即丢，不放任何字段：调用方每次都显式传，才不会把一份过期的固定位置
    /// 默默沿用进下一次解算。
    /// </para>
    /// <para>
    /// 引擎可注入，默认用约束布局引擎。注入的用途只有一个：让"布局彻底失败"这条路径
    /// 能被真正走到——拿一个会失败的引擎，比在真实引擎上构造一份它解不出的图可靠得多。
    /// </para>
    /// <para>
    /// 预算也由调用方给。自动重排与用户主动点的重试该等多久是两回事：
    /// 前者超过一帧就是卡顿，后者用户已经在等结果了，多等一会儿换更好的布局是划算的。
    /// 写死一个的话，两个场景里必有一个拿到不合适的值。
    /// </para>
    /// </remarks>
    public static SampleScene Build(
        DiagramDocument document,
        Theme theme,
        ITextMeasurer measurer,
        IReadOnlyDictionary<string, Anchor>? pinnedNodes,
        ILayoutEngine? engine = null,
        TimeSpan? budget = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(measurer);

        var layoutPins = pinnedNodes is null
            ? null
            : new Dictionary<string, LayoutPoint>(StringComparer.Ordinal);

        if (pinnedNodes is not null)
        {
            foreach (var (id, anchor) in pinnedNodes)
            {
                layoutPins![id] = new LayoutPoint(anchor.X, anchor.Y);
            }
        }

        var job = LayoutRequestFactory.FromDocument(
            document,
            node => SceneBuilder.MeasureNode(node, theme, measurer),
            layoutPins);

        var layoutWatch = Stopwatch.StartNew();
        var result = new LayoutCoordinator(engine ?? new ConstraintLayoutEngine()).Compute(job, budget);
        layoutWatch.Stop();

        var drawListWatch = Stopwatch.StartNew();
        var drawList = SceneBuilder.Build(document, result.Layout, theme, measurer);
        drawListWatch.Stop();

        return new SampleScene(
            document,
            result,
            drawList,
            layoutWatch.Elapsed.TotalMilliseconds,
            drawListWatch.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// 沿用上一次的坐标，只按当前文档重画一遍绘制列表。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 手动布局模式走这条：位置冻在最近一次成功的布局上，不再问引擎。
    /// 标签、样式这些不进坐标的东西仍然是文档里的最新值——只冻坐标，
    /// 不连内容一起冻。两者一起冻的话，用户改了标签却看不到变化，会以为编辑没生效。
    /// </para>
    /// <para>
    /// **上一次布局里没有的元素画不出来。** 这是这一模式的本意：不重排就不会有它的位置。
    /// 绘制列表按标识逐个取坐标，取不到的直接跳过，不会画出一个零坐标的方块。
    /// </para>
    /// </remarks>
    public static SampleScene Rebuild(
        DiagramDocument document,
        Theme theme,
        ITextMeasurer measurer,
        SampleScene previous)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(measurer);
        ArgumentNullException.ThrowIfNull(previous);

        var watch = Stopwatch.StartNew();
        var drawList = SceneBuilder.Build(document, previous.Result.Layout, theme, measurer);
        watch.Stop();

        return previous with
        {
            Document = document,
            DrawList = drawList,
            DrawListMilliseconds = watch.Elapsed.TotalMilliseconds,
        };
    }
}

/// <summary>
/// 示例场景的各阶段产物。
/// </summary>
/// <remarks>
/// 布局那一步的结果整份带出来，不只带坐标：降级落在哪一级、试了几次都在里面，
/// 而诊断面板要显示它们。只留坐标的话，调用方就再也问不到那两件事了。
/// </remarks>
/// <param name="Document">文档。</param>
/// <param name="Result">布局结果，含所应用的降级级别与尝试记录。</param>
/// <param name="DrawList">绘制列表。</param>
/// <param name="LayoutMilliseconds">求解布局用的毫秒数。</param>
/// <param name="DrawListMilliseconds">构建绘制列表用的毫秒数。</param>
public sealed record SampleScene(
    DiagramDocument Document,
    LayoutResult Result,
    DrawList DrawList,
    double LayoutMilliseconds,
    double DrawListMilliseconds)
{
    /// <summary>布局给出的坐标、折线与内容范围。</summary>
    public EngineLayoutResult Layout => Result.Layout;
}
