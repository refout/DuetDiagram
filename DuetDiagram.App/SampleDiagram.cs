using System.Diagnostics;
using DuetDiagram.Core.Model;
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
internal static class SampleDiagram
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
    public static SampleScene Build(DiagramDocument document, Theme theme, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(measurer);

        var job = LayoutRequestFactory.FromDocument(
            document,
            node => SceneBuilder.MeasureNode(node, theme, measurer));

        var layoutWatch = Stopwatch.StartNew();
        var result = new LayoutCoordinator(new ConstraintLayoutEngine()).Compute(job);
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
internal sealed record SampleScene(
    DiagramDocument Document,
    LayoutResult Result,
    DrawList DrawList,
    double LayoutMilliseconds,
    double DrawListMilliseconds)
{
    /// <summary>布局给出的坐标、折线与内容范围。</summary>
    public EngineLayoutResult Layout => Result.Layout;
}
