using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DuetDiagram.App.Controls;
using DuetDiagram.App.Interaction;
using DuetDiagram.App.Services;
using DuetDiagram.App.ViewModels;
using DuetDiagram.Core.Commands;
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

    /// <summary>属性面板的宽度与高度。宽度与主窗口里那一栏一致。</summary>
    private const double PropertyPanelWidth = 300;

    private const double PropertyPanelHeight = 800;

    /// <summary>切换选中之前先空跑几次，把首次用到的字形与控件模板预热掉。</summary>
    private const int WarmupSwitches = 10;

    /// <summary>
    /// 单次处理一帧拖拽输入允许花多少毫秒。
    /// </summary>
    /// <remarks>
    /// 判中位，不判最大。拖动中每一帧都要把选中节点（加相连边）的绘制指令整体挪一下，
    /// 那一笔里混着系统调度与垃圾回收的噪声，单次尖峰否掉整套设计不合理；
    /// 而中位若到了这个数，说明常态本身已经超标。最大照样报出来让人看见尾巴。
    /// 这跟"拖动中不调布局、不发命令"是同一件事：要量的就是那一下偏移处理，
    /// 不是整条布局链路。
    /// </remarks>
    private const double MaximumDragMilliseconds = 16;

    /// <summary>
    /// 单次切换选中允许花多少毫秒。
    /// </summary>
    /// <remarks>
    /// 判的是中位，不是最大值。单次切换里混着垃圾回收与系统调度的噪声，
    /// 取最大值等于让一次停顿否掉整套设计；而中位若到了这个数，
    /// 说明常态本身就已经超标了。最大值照常报出来，让读的人看见尾巴有多长。
    /// </remarks>
    private const double MaximumSwitchMilliseconds = 100;

    public static int Run(int nodeCount, int frameCount, bool diagnostics, bool highlight)
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

        // 高亮全开：给一批节点挂上三种手段。两边都挂，量的是"高亮带来的开销"，
        // 只挂一边的话，两边的差值里混进了"挂没挂高亮"而不是"剔除省了多少"。
        var highlighted = highlight ? MarkNodes(plainHost, culledHost, scene.DrawList, nodeCount) : 0;

        var plain = Measure(plainHost, pixelSize, frameCount);
        var culled = Measure(culledHost, pixelSize, frameCount);

        var cullRate = culledHost.Model.Mode.CullRate;

        Console.WriteLine($"  平台就绪          {startup.Elapsed.TotalMilliseconds,8:0.0} ms");
        Console.WriteLine($"  造文档到绘制列表  {build.Elapsed.TotalMilliseconds,8:0.0} ms");
        Console.WriteLine($"  节点 / 连线       {scene.Document.Nodes.Count,8} / {scene.Document.Edges.Count}");
        Console.WriteLine($"  内容范围          {scene.DrawList.Width,8:0} × {scene.DrawList.Height:0}");
        Console.WriteLine($"  绘制指令          {scene.DrawList.Commands.Count,8}");

        if (highlight)
        {
            Console.WriteLine($"  高亮标记          {highlighted,8} 个节点（脉冲 + 角标 + 虚线轮廓）");
        }

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
    /// 属性面板切换选中的耗时。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 测的是"换一个选中元素"这件事从头到尾花多久：重读每个字段、把新值推进控件、
    /// 跑完排在队列里的那次重排。它对应的是用户连点节点时的体感。
    /// </para>
    /// <para>
    /// **面板只建一次，切换时只换值。** 每次换选中重建一遍的话，连续点选会明显卡顿，
    /// 而连续点选正是用户在找元素时的常态。这条设计对不对，靠"一次冷建"那个对照数字看：
    /// 它比单次切换高出一个量级的话，说明切换确实没有在重建。
    /// </para>
    /// </remarks>
    public static int RunPanel(int nodeCount, int switchCount)
    {
        Console.WriteLine("属性面板切换测量");
        Console.WriteLine($"节点 {nodeCount} 个，切换 {switchCount} 次");
        Console.WriteLine();

        var startup = Stopwatch.StartNew();
        Program.BuildAvaloniaApp().SetupWithoutStarting();
        startup.Stop();

        var build = Stopwatch.StartNew();
        var document = Graph(nodeCount);
        build.Stop();

        // 面板要读文档、要按命令层写回去，所以它拿的是一整个会话，不是一份绘制列表。
        using var session = new DiagramSession(document);

        var cold = Stopwatch.StartNew();
        var panel = new PropertyPanelViewModel(session);
        var view = new PropertyPanel { DataContext = panel };
        var root = new Grid();

        root.Children.Add(view);

        var size = new Size(PropertyPanelWidth, PropertyPanelHeight);

        root.Measure(size);
        root.Arrange(new Rect(size));
        cold.Stop();

        // 再建一份，用来把"第一次用到某种控件"的那笔开销摘出去。
        // 文本框、下拉、三态勾选框的模板都是进程里第一次出现时才准备的，
        // 那一笔算进"建一次面板"里会让这个对照数字虚高一个量级。
        var rebuild = Stopwatch.StartNew();
        var spare = new PropertyPanel { DataContext = new PropertyPanelViewModel(session) };

        spare.Measure(size);
        spare.Arrange(new Rect(size));
        rebuild.Stop();

        var ids = document.Nodes.Select(node => node.Id).ToList();
        var elapsed = new List<double>(switchCount);
        var stopwatch = new Stopwatch();

        for (var index = 0; index < WarmupSwitches; index++)
        {
            Switch(session, root, size, ids[index % ids.Count], stopwatch);
        }

        for (var index = 0; index < switchCount; index++)
        {
            elapsed.Add(Switch(session, root, size, ids[index % ids.Count], stopwatch));
        }

        elapsed.Sort();

        var median = elapsed[elapsed.Count / 2];
        var p95 = elapsed[Math.Min(elapsed.Count - 1, (int)(elapsed.Count * 0.95))];
        var max = elapsed[^1];

        Console.WriteLine($"  平台就绪          {startup.Elapsed.TotalMilliseconds,8:0.0} ms");
        Console.WriteLine($"  造文档            {build.Elapsed.TotalMilliseconds,8:0.0} ms");
        Console.WriteLine($"  节点 / 连线       {document.Nodes.Count,8} / {document.Edges.Count}");
        Console.WriteLine($"  分节 / 字段       {panel.Sections.Count,8} / {panel.Sections.Sum(s => s.Fields.Count)}");
        Console.WriteLine($"  冷建一次面板      {cold.Elapsed.TotalMilliseconds,8:0.00} ms  （含控件模板第一次准备）");
        Console.WriteLine($"  再建一次面板      {rebuild.Elapsed.TotalMilliseconds,8:0.00} ms  （对照：切换不该接近这个数）");
        Console.WriteLine();
        Console.WriteLine($"  单次切换  中位 {median,8:0.000} ms");
        Console.WriteLine($"  单次切换  95 分位 {p95,8:0.000} ms");
        Console.WriteLine($"  单次切换  最大 {max,8:0.000} ms");
        Console.WriteLine();

        if (median <= MaximumSwitchMilliseconds)
        {
            Console.WriteLine($"达标：单次切换中位不超过 {MaximumSwitchMilliseconds:0} 毫秒");

            return 0;
        }

        Console.Error.WriteLine(
            $"不达标：属性面板单次切换的中位是 {median:0.000} 毫秒，超过 {MaximumSwitchMilliseconds:0} 毫秒");

        return 1;
    }

    /// <summary>
    /// 换一次选中，量它花了多久。
    /// </summary>
    /// <remarks>
    /// 队列要跑一遍：面板上的字是排到队列里换的，不跑的话量到的只是"排了个队"。
    /// 排在队列里的还有一次重排，那也是用户实际要等的一段。
    /// </remarks>
    private static double Switch(
        DiagramSession session,
        Grid root,
        Size size,
        string nodeId,
        Stopwatch stopwatch)
    {
        stopwatch.Restart();

        session.Select(nodeId);
        Dispatcher.UIThread.RunJobs();
        root.Measure(size);
        root.Arrange(new Rect(size));

        stopwatch.Stop();

        return stopwatch.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// 拖拽中单帧处理输入（应用临时偏移）的耗时。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 量的是"一帧里把选中节点连同相连边整体挪一下偏移"这件事，不是整条布局链路——
    /// 拖拽中不调布局、不发命令，那条链路只在松手那一刻跑一次。预览偏移由画布的
    /// <see cref="CanvasViewModel.BeginFrame"/> 在每帧开头套上去，所以这里就是反复调用它
    /// 并量耗时。首帧要建剔除索引、首次准备字形，那些都不能算进稳态帧时，先预热掉。
    /// </para>
    /// <para>
    /// 输入是一份真实的千节点图与真实的绘制列表：手画的矩形量不出命中与相连边重画的开销，
    /// 而那两样恰恰是拖拽预览要做的全部。每帧只挪被拖动的几个节点，整份列表不能被拷一遍，
    /// 所以单帧耗时与节点总数基本无关，只与"这一拖波及了多少元素"有关。
    /// </para>
    /// </remarks>
    public static int RunDrag(int nodeCount, int samples)
    {
        Console.WriteLine("节点拖拽输入处理测量");
        Console.WriteLine($"节点 {nodeCount} 个，采样 {samples} 次");
        Console.WriteLine();

        var startup = Stopwatch.StartNew();
        Program.BuildAvaloniaApp().SetupWithoutStarting();
        startup.Stop();

        using var measurer = new SkiaTextMeasurer();
        var document = Graph(nodeCount);
        using var session = new DiagramSession(document);
        var model = new CanvasViewModel();
        model.Load(session.Scene.DrawList);
        model.Resize(DefaultWidth, DefaultHeight);

        // 选一份内容里第一个节点开拖。够大的图才覆盖真实场景：
        // 千节点上松手那一档的预算卡的是"整条链路"，这里卡的只是"一帧偏移"，
        // 但图小了剔除与拷贝的开销也小，量出来的数字没有意义。
        var target = document.Nodes[0].Id;
        var placed = session.Scene.Layout.Find(target)
            ?? throw new InvalidOperationException($"布局结果里没有 {target}");
        var startDoc = new DrawPoint(placed.X, placed.Y);

        var preview = session.BeginDrag(target, additive: false, startDoc)
            ?? throw new InvalidOperationException("节点应当可以拖");
        model.BeginDrag(preview.NodeIds, preview.EdgeIds);

        for (var index = 0; index < WarmupFrames; index++)
        {
            var warm = new DrawPoint(startDoc.X + (index + 1), startDoc.Y + (index + 1));
            model.UpdateDrag(session.UpdateDrag(warm));
            model.BeginFrame();
        }

        var elapsed = new List<double>(samples);
        var stopwatch = new Stopwatch();

        for (var index = 0; index < samples; index++)
        {
            // 每帧把指针挪一点点，模拟拖动中连续收到的指针事件。
            var current = new DrawPoint(startDoc.X + ((index + 1) * 0.5), startDoc.Y + ((index + 1) * 0.5));
            model.UpdateDrag(session.UpdateDrag(current));

            stopwatch.Restart();
            model.BeginFrame();
            stopwatch.Stop();

            elapsed.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        elapsed.Sort();

        var median = elapsed[elapsed.Count / 2];
        var max = elapsed[^1];

        Console.WriteLine($"  平台就绪          {startup.Elapsed.TotalMilliseconds,8:0.0} ms");
        Console.WriteLine($"  节点 / 连线       {document.Nodes.Count,8} / {document.Edges.Count}");
        Console.WriteLine($"  单帧处理  中位 {median,8:0.000} ms");
        Console.WriteLine($"  单帧处理  最大 {max,8:0.000} ms");
        Console.WriteLine();

        if (median <= MaximumDragMilliseconds)
        {
            Console.WriteLine($"达标：单帧处理拖拽输入不超过 {MaximumDragMilliseconds:0} 毫秒");

            return 0;
        }

        Console.Error.WriteLine(
            $"不达标：拖拽单帧处理的中位是 {median:0.000} 毫秒，超过 {MaximumDragMilliseconds:0} 毫秒");

        return 1;
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

    /// <summary>高亮测量时标记多少个节点。够多才量得出叠加层的开销，又不至于盖满整屏。</summary>
    private const int HighlightedNodes = 30;

    private static readonly IReadOnlySet<HighlightKind> HighlightKinds =
        new HashSet<HighlightKind> { HighlightKind.Pulse, HighlightKind.Badge, HighlightKind.Outline };

    /// <summary>
    /// 给前若干节点挂上三种高亮手段，返回挂了几个。
    /// </summary>
    /// <remarks>
    /// 相位固定：这里量的是"把高亮画出来"的开销，脉冲相位每帧变化只多一次三角函数，
    /// 对帧时的影响远小于光栅化那几条指令。两个宿主共用同一份指令——它们只读。
    /// </remarks>
    private static int MarkNodes(Host plain, Host culled, DrawList list, int nodeCount)
    {
        var count = Math.Min(HighlightedNodes, nodeCount);
        var highlights = new List<ElementHighlight>(count);

        for (var index = 0; index < count; index++)
        {
            highlights.Add(new ElementHighlight($"n{index}", ChangeSource.Human, HighlightKinds));
        }

        var commands = Highlight.Build(highlights, list.Commands, Theme.Default, 0.25);

        plain.Model.SetHighlights(commands);
        culled.Model.SetHighlights(commands);

        return count;
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
