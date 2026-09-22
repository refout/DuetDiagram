using System.Diagnostics;
using Avalonia;
using Avalonia.Media.Imaging;
using DuetDiagram.App.Controls;
using DuetDiagram.App.ViewModels;
using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using DuetDiagram.Render;
using Size = Avalonia.Size;

namespace DuetDiagram.App;

/// <summary>
/// 帧率基线测量。
/// </summary>
/// <remarks>
/// <para>
/// 测的是**光栅化一帧的耗时**，由它反推可达到的帧率上限。之所以不在真实窗口里数帧：
/// 那样要依赖桌面会话、受合成器与垂直同步影响，数字噪声大且不可复现。
/// 光栅化耗时是帧率的主导因素，也是后续优化真正要压的部分。
/// </para>
/// <para>
/// **输入是一份真实的绘制列表**，不是一堆手工画的矩形。手画的矩形量不出文本度量与
/// 折线路由的开销，而千节点图上恰恰是那两样占大头——按矩形测出来的数字会明显偏乐观。
/// 整条链路是：造文档、量尺寸、求解布局、翻译成绘制列表、画布画出来。
/// </para>
/// <para>
/// **两种模式各测一遍，视口与绘制列表完全相同。** 只测一种的话，
/// 那个数字说明不了剔除有没有用；而两次测量之间只要有一处不同，比较就失去意义。
/// </para>
/// <para>
/// 每帧新建一块目标位图。这块分配两种模式都要付，所以比较不受影响；
/// 真实渲染画到窗口的后备缓冲上、不重新分配，因此这里报出的绝对帧率略偏保守。
/// 偏保守是好事：达标线是按真实渲染定的。
/// </para>
/// </remarks>
internal static class FrameBenchmark
{
    private const int DefaultWidth = 1920;

    private const int DefaultHeight = 1080;

    private const int WarmupFrames = 10;

    /// <summary>
    /// 千节点下必须达到的帧率。
    /// </summary>
    /// <remarks>
    /// 判的是**虚拟化那一档**，不是两种模式都要达标。这个尺寸上应用跑的就是虚拟化——
    /// 要求回退档也达标等于说阈值可以取无穷大，那样虚拟化本身就没有存在的理由了。
    /// 回退档的数字照常报出来，它是"虚拟化省下了多少"的对照。
    /// </remarks>
    private const double MinimumFps = 30;

    /// <summary>剔除率必须高于这个比例。取"高于"而不是"不低于"：八成是及格线，不是目标。</summary>
    private const double MinimumCullRate = 0.8;

    /// <summary>
    /// 视口里只留内容面积的多少分之一。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 剔除率由它决定：留下十二分之一，再扣掉预取边距多取的那一圈，剩下大约八成六。
    /// 这对应一个用户真会用的倍数——千节点的图上，屏幕上摆得下几十个节点。
    /// </para>
    /// <para>
    /// 分母不能再小。预取边距是固定的文档尺寸，图越小、倍数越大，它占的比例越高；
    /// 留到八分之一时那一圈就把剔除率压到八成以下，而剔除率本身并没有变差，
    /// 变的是这个测量点选在了哪儿。
    /// </para>
    /// </remarks>
    private const double VisibleFraction = 1.0 / 12;

    public static int Run(int nodeCount, int frameCount)
    {
        Console.WriteLine("帧率基线测量");
        Console.WriteLine($"节点 {nodeCount} 个，画布 {DefaultWidth}×{DefaultHeight}，测量 {frameCount} 帧");
        Console.WriteLine();

        var startup = Stopwatch.StartNew();
        Program.BuildAvaloniaApp().SetupWithoutStarting();
        startup.Stop();

        var build = Stopwatch.StartNew();
        var scene = Build(nodeCount);
        build.Stop();

        var size = new Size(DefaultWidth, DefaultHeight);
        var pixelSize = new PixelSize(DefaultWidth, DefaultHeight);

        // 阈值调到无穷大就是"永远走回退档"。用它当对照，而不是另写一条不剔除的绘制路径：
        // 另写一条的话，两条路径迟早会在别处分叉，量出来的差值就不再是剔除带来的。
        var immediate = Prepare(scene.DrawList, size, new CullingPolicy(threshold: int.MaxValue));
        var virtualized = Prepare(scene.DrawList, size, CullingPolicy.Default);

        var plain = Measure(immediate, pixelSize, frameCount);
        var culled = Measure(virtualized, pixelSize, frameCount);

        var cullRate = virtualized.Mode.CullRate;

        Console.WriteLine($"  平台就绪          {startup.Elapsed.TotalMilliseconds,8:0.0} ms");
        Console.WriteLine($"  造文档到绘制列表  {build.Elapsed.TotalMilliseconds,8:0.0} ms");
        Console.WriteLine($"  节点 / 连线       {scene.Document.Nodes.Count,8} / {scene.Document.Edges.Count}");
        Console.WriteLine($"  内容范围          {scene.DrawList.Width,8:0} × {scene.DrawList.Height:0}");
        Console.WriteLine($"  绘制指令          {scene.DrawList.Commands.Count,8}");
        Console.WriteLine();
        Console.WriteLine($"  回退档   单帧平均 {plain.Mean,8:0.00} ms  → 约 {1000 / plain.Mean,6:0.0} 帧每秒");
        Console.WriteLine($"  回退档   单帧中位 {plain.Median,8:0.00} ms  → 约 {1000 / plain.Median,6:0.0} 帧每秒");
        Console.WriteLine($"  虚拟化   单帧平均 {culled.Mean,8:0.00} ms  → 约 {1000 / culled.Mean,6:0.0} 帧每秒");
        Console.WriteLine($"  虚拟化   单帧中位 {culled.Median,8:0.00} ms  → 约 {1000 / culled.Median,6:0.0} 帧每秒");
        Console.WriteLine($"  剔除率            {cullRate,8:P1}  （{scene.DrawList.Commands.Count} 条里只画 {virtualized.FrameCommands.Count} 条）");
        Console.WriteLine();

        var failures = Check(plain, culled, cullRate);

        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            {
                Console.Error.WriteLine($"不达标：{failure}");
            }

            return 1;
        }

        Console.WriteLine($"达标：虚拟化不低于 {MinimumFps:0} 帧每秒，剔除率高于 {MinimumCullRate:P0}，且比回退档快");

        return 0;
    }

    /// <summary>
    /// 逐条核对判据。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 全部列出来再一起报，而不是遇到第一条就返回：一次运行里能看出有几项不达标，
    /// 比反复跑几遍去凑齐更省事。
    /// </para>
    /// <para>
    /// "比回退档快"也要判。少了这一条，一个剔掉得很少、索引开销又压过收益的实现
    /// 仍然能靠帧率与剔除率各自达标——两个数字分开看都没问题，合起来是"白忙一场"。
    /// </para>
    /// </remarks>
    private static List<string> Check(FrameTiming plain, FrameTiming culled, double cullRate)
    {
        var failures = new List<string>();

        if (1000 / culled.Mean < MinimumFps)
        {
            failures.Add($"虚拟化的平均帧率低于 {MinimumFps:0} 帧每秒");
        }

        if (cullRate <= MinimumCullRate)
        {
            failures.Add($"剔除率没有超过 {MinimumCullRate:P0}");
        }

        if (culled.Mean >= plain.Mean)
        {
            failures.Add($"虚拟化没有比回退档快（{culled.Mean:0.00} ms 对 {plain.Mean:0.00} ms）");
        }

        return failures;
    }

    /// <summary>把一份绘制列表装进画布，并把视口放大到只看得见内容的一小块。</summary>
    /// <remarks>
    /// 倍数由内容范围反算，不写死：写死的话换一份节点尺寸不同的图，剔除率就跟着漂。
    /// 反算要的是"看得见的文档面积正好是内容的多少分之一"——
    /// 看得见的文档面积等于窗口面积除以倍数的平方，所以倍数开的是这个比值的平方根。
    /// 把分数乘在分子上就把方向搞反了，算出来的倍数小得离谱，视口里装得下整张图，
    /// 于是剔除率是零而画面看起来一切正常。
    /// </remarks>
    private static CanvasViewModel Prepare(DrawList list, Size size, CullingPolicy policy)
    {
        var model = new CanvasViewModel(culling: policy);

        model.Load(list);
        model.Resize(size.Width, size.Height);

        var content = list.Width * list.Height;
        var wanted = content <= 0
            ? model.Viewport.Scale
            : Math.Sqrt(size.Width * size.Height / (VisibleFraction * content));

        model.ZoomAt(wanted / model.Viewport.Scale, size.Width / 2, size.Height / 2);

        return model;
    }

    private static FrameTiming Measure(CanvasViewModel model, PixelSize pixelSize, int frameCount)
    {
        var canvas = new DiagramCanvas { DataContext = model };

        canvas.Measure(new Size(pixelSize.Width, pixelSize.Height));
        canvas.Arrange(new Rect(0, 0, pixelSize.Width, pixelSize.Height));

        // 预热：即时编译、字形缓存与模式切换的首帧成本不能算进稳态帧率。
        // 模式切换那一帧本来就要多做一次建索引，混进测量会把均值拉高。
        for (var i = 0; i < WarmupFrames; i++)
        {
            using var warmup = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
            warmup.Render(canvas);
        }

        var elapsed = new List<double>(frameCount);
        var stopwatch = new Stopwatch();

        for (var i = 0; i < frameCount; i++)
        {
            stopwatch.Restart();

            using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
            bitmap.Render(canvas);

            stopwatch.Stop();
            elapsed.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        elapsed.Sort();

        return new FrameTiming(elapsed.Average(), elapsed[elapsed.Count / 2]);
    }

    /// <summary>造一份示例文档并跑完整条链路。</summary>
    private static SampleScene Build(int nodeCount)
    {
        using var measurer = new SkiaTextMeasurer();

        return SampleDiagram.Build(Graph(nodeCount), Theme.Default, measurer);
    }

    /// <summary>
    /// 排成网格的图：每层若干并列节点，层间连接同序号节点。
    /// </summary>
    /// <remarks>
    /// 这是真实流程图与架构图最常见的形态，也是布局压力最大的形态之一：
    /// 层数决定分层阶段的工作量，每层宽度决定层内排序与坐标分配的工作量。
    /// 连线不写标签，因为标签的排版成本已经在节点标签上量到了。
    /// </remarks>
    private static DiagramDocument Graph(int nodeCount)
    {
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(nodeCount)));
        var rows = (int)Math.Ceiling((double)nodeCount / columns);

        var nodes = new List<NodeDef>(nodeCount);

        for (var index = 0; index < nodeCount; index++)
        {
            nodes.Add(new NodeDef { Id = $"n{index}", Label = $"节点 {index}" });
        }

        var edges = new List<EdgeDef>();

        for (var row = 0; row + 1 < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var from = (row * columns) + column;
                var to = ((row + 1) * columns) + column;

                if (to >= nodeCount)
                {
                    continue;
                }

                edges.Add(new EdgeDef { Id = $"e{from}", From = $"n{from}", To = $"n{to}" });
            }
        }

        return DiagramDocument.CreateFromContent(
            $"bench-{nodeCount}",
            DiagramKind.Flowchart,
            Direction.TB,
            nodes: nodes,
            edges: edges);
    }

    /// <summary>一种模式的帧时。</summary>
    /// <param name="Mean">平均毫秒数。</param>
    /// <param name="Median">中位毫秒数。</param>
    private sealed record FrameTiming(double Mean, double Median);
}
