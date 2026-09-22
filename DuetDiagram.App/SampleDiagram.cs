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
    /// 度量器由调用方传进来并在用完后释放：它按字体家族缓存字体对象，
    /// 而字体对象持有原生资源。让这个方法自己 new 一个再丢掉，
    /// 就是把"什么时候释放原生资源"这件事交给垃圾回收决定。
    /// </remarks>
    public static SampleScene Build(DiagramDocument document, Theme theme, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(measurer);

        var job = LayoutRequestFactory.FromDocument(
            document,
            node => SceneBuilder.MeasureNode(node, theme, measurer));

        var layout = new LayoutCoordinator(new ConstraintLayoutEngine()).Compute(job);

        return new SampleScene(document, layout.Layout, SceneBuilder.Build(document, layout.Layout, theme, measurer));
    }
}

/// <summary>示例场景的三个阶段产物。自检要按它们报数，所以一并给出来。</summary>
/// <param name="Document">文档。</param>
/// <param name="Layout">布局结果。</param>
/// <param name="DrawList">绘制列表。</param>
internal sealed record SampleScene(DiagramDocument Document, EngineLayoutResult Layout, DrawList DrawList);
