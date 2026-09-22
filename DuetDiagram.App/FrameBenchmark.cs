using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DuetDiagram.App.Controls;
using DuetDiagram.App.ViewModels;
using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using DuetDiagram.Render;
using Size = Avalonia.Size;
using Thickness = Avalonia.Thickness;

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
/// 画布装在一个只有它一个可见子元素的容器里。容器是为了让诊断面板能叠上来——
/// 面板不进容器的话，"开着面板"这件事在这里就没法量。面板隐藏时不参与渲染，
/// 所以基线数字与只画画布时几乎一样。
/// </para>
/// <para>
/// 目标位图整段复用，不每帧新建。真实渲染画到窗口的后备缓冲上、也不重新分配；
/// 每帧新建的话，一段下来要分配上吉字节，垃圾回收的代价随时间爬升，
/// 而后量到的那一遍总是更吃亏——比较就不再公平。
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

    /// <summary>开着诊断面板时，单帧耗时最多允许比关着时高多少。</summary>
    private const double MaximumPanelCost = 0.1;

    /// <summary>
    /// 面板代价每轮量多少帧。与主测量同一个长度。
    /// </summary>
    /// <remarks>
    /// 窗口不能短。面板要判的那点开销远小于一次垃圾回收或一次系统调度，
    /// 窗口一短，一次停顿就能把那一轮的比值拉到几倍上去，中位再稳也架不住多数轮都被污染。
    /// </remarks>
    private const int PanelFrames = 120;

    /// <summary>
    /// 面板代价量多少轮，取各轮比值的中位。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 比的是**每一轮里关、开两遍的比值**，不是"关着的总中位对开着的总中位"。
    /// 后者把两边的样本混在一起，机器在整段测量里慢下来这件事会全部落到先量的那一边。
    /// </para>
    /// <para>
    /// 每一轮内部取的是**窗口里的中位帧时**而不是均值。均值会被一次停顿整个拽走，
    /// 而那个停顿与面板无关——它在关着的那一遍里同样会发生。
    /// </para>
    /// <para>取奇数轮，中位就落在某一轮真实样本上，不是两轮插出来的。</para>
    /// </remarks>
    private const int PanelRounds = 7;

    public static int Run(int nodeCount, int frameCount, bool diagnostics)
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
        var plainHost = Mount(Prepare(scene.DrawList, size, new CullingPolicy(threshold: int.MaxValue)));
        var culledHost = Mount(Prepare(scene.DrawList, size, CullingPolicy.Default));

        var plain = Measure(plainHost, pixelSize, frameCount);
        var culled = Measure(culledHost, pixelSize, frameCount);

        var cullRate = culledHost.Model.Mode.CullRate;

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
        Console.WriteLine($"  剔除率            {cullRate,8:P1}  （{scene.DrawList.Commands.Count} 条里只画 {culledHost.Model.FrameCommands.Count} 条）");
        Console.WriteLine();

        var failures = Check(plain, culled, cullRate);

        if (diagnostics)
        {
            failures.AddRange(CheckPanel(culledHost, pixelSize));
        }

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
    /// 开一次诊断面板，跟关着的时候比一比。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 用的是同一个宿主、同一份绘制列表、同一个视口，只把面板开起来再测一遍——
    /// 换个新宿主的话，两边差的是"冷热"而不是"面板"。
    /// </para>
    /// <para>
    /// 关着时面板整个不在视觉树上，一条指令都不画；开着时每帧往采样器里写一条，
    /// 每若干帧重排一次文字。两边的差别就是这两件事。显隐与开关一起设，
    /// 真实窗口里这两者由一处绑定联动。
    /// </para>
    /// <para>
    /// 面板上的字每八帧才换一次，而那一次换字是排到队列里等这一帧画完再做的。
    /// 所以这里每帧跑一次队列，让那次换字真的发生——不跑的话，排着的通知永远不执行，
    /// 量到的是"排队的开销"而不是"换字的开销"。
    /// </para>
    /// </remarks>
    private static List<string> CheckPanel(Host host, PixelSize pixelSize)
    {
        var costs = new double[PanelRounds];

        for (var round = 0; round < PanelRounds; round++)
        {
            host.Model.Diagnostics.IsOpen = false;
            host.Panel.IsVisible = false;
            var closed = Measure(host, pixelSize, PanelFrames).Median;

            host.Model.Diagnostics.IsOpen = true;
            host.Panel.IsVisible = true;
            var open = Measure(host, pixelSize, PanelFrames).Median;

            costs[round] = (open / closed) - 1;
        }

        var cost = Median(costs);

        Console.WriteLine("  面板代价          逐轮，开对比关（%）");

        for (var round = 0; round < PanelRounds; round++)
        {
            Console.WriteLine($"    {round + 1,2}  {costs[round],8:P1}");
        }

        Console.WriteLine($"    中位  {cost,8:P1}   判据不超过 {MaximumPanelCost:P0}");
        Console.WriteLine();

        return cost <= MaximumPanelCost
            ? []
            : [$"开着诊断面板的帧率比关着时低了一成以上（中位 {cost:P1}）"];
    }

    /// <summary>一组样本的中位数。样本数是奇数，所以取到的是一个真实样本，不是插出来的。</summary>
    private static double Median(double[] values)
    {
        var sorted = (double[])values.Clone();

        Array.Sort(sorted);

        return sorted[sorted.Length / 2];
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

    /// <summary>
    /// 把画布与诊断面板装进一个容器。
    /// </summary>
    /// <remarks>
    /// 面板的显隐由 <see cref="CheckPanel"/> 每次测量前显式设置，这里不挂绑定：
    /// 程序集要能原生编译，而反射式绑定在裁剪与原生编译下都不可用，
    /// 编译器直接把这一条当成错误拦下来。真实窗口里那一处绑定由端到端用例覆盖。
    /// </remarks>
    private static Host Mount(CanvasViewModel model)
    {
        var canvas = new DiagramCanvas { DataContext = model };
        var panel = new DiagnosticsPanel
        {
            DataContext = model,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12),
        };

        var root = new Grid();

        root.Children.Add(canvas);
        root.Children.Add(panel);

        return new Host(root, canvas, panel, model);
    }

    private static FrameTiming Measure(Host host, PixelSize pixelSize, int frameCount)
    {
        var size = new Size(pixelSize.Width, pixelSize.Height);

        host.Root.Measure(size);
        host.Root.Arrange(new Rect(size));

        // 一块 1920×1080 的位图是八兆。每帧新建的话，一段下来要分配上吉字节，
        // 垃圾回收的代价随时间爬升，而后量到的那一遍总是更吃亏。
        using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));

        // 预热：即时编译、字形缓存与模式切换的首帧成本不能算进稳态帧率。
        // 模式切换那一帧本来就要多做一次建索引，混进测量会把均值拉高。
        for (var i = 0; i < WarmupFrames; i++)
        {
            Render(host, bitmap);
        }

        var elapsed = new List<double>(frameCount);
        var stopwatch = new Stopwatch();

        for (var i = 0; i < frameCount; i++)
        {
            stopwatch.Restart();

            Render(host, bitmap);

            stopwatch.Stop();
            elapsed.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        elapsed.Sort();

        return new FrameTiming(elapsed.Average(), elapsed[elapsed.Count / 2]);
    }

    private static void Render(Host host, RenderTargetBitmap bitmap)
    {
        bitmap.Render(host.Root);

        // 面板每若干帧排一次换字，而那一次换字排在队列里。不跑队列的话它永远不执行，
        // 量到的就是"排队"而不是"换字"。
        Dispatcher.UIThread.RunJobs();
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

    /// <summary>要渲染的那一叠东西，以及它们的共同状态。</summary>
    /// <param name="Root">容器。渲染的是它，不是画布——面板得叠在画布上。</param>
    /// <param name="Canvas">画布。</param>
    /// <param name="Panel">诊断面板。</param>
    /// <param name="Model">画布与面板共用的状态。</param>
    private sealed record Host(
        Grid Root,
        DiagramCanvas Canvas,
        DiagnosticsPanel Panel,
        CanvasViewModel Model);
}
