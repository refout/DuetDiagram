using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using DuetDiagram.App;
using DuetDiagram.App.Controls;
using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 渲染模式换档：什么时候换、换档那一帧会不会卡、换档前后画面对不对得上。
/// </summary>
/// <remarks>
/// <para>
/// 这一层盯的是"换档在真实窗口里接上了没有"。判据本身（什么元素数走哪一档、
/// 预取边距扩多少）由渲染层的单元测试管，这里只验它在窗口里跑起来的样子。
/// </para>
/// <para>
/// **两轮跑。** 第一轮把即时编译、字形缓存与索引构造的钱花掉，第二轮才是量到的那些帧。
/// 混在一起的话，换档帧与稳态帧的比值里混着启动成本，而那个比值正是要说明
/// "换档有没有多做事情"——被启动成本搅过之后，它什么也说明不了。
/// </para>
/// </remarks>
public sealed class ModeSwitchTests
{
    /// <summary>元素数要跨过默认阈值，不然根本不会换档。</summary>
    private const int NodeCount = 600;

    /// <summary>预热帧数。</summary>
    private const int WarmupFrames = 8;

    /// <summary>稳态帧数。换档那一帧要跟它们的中间值比。</summary>
    private const int SteadyFrames = 8;

    /// <summary>
    /// 换档那一帧允许是稳态帧耗时的几倍。
    /// </summary>
    /// <remarks>
    /// 单帧的耗时在无头环境里抖动不小，拿某一个邻居帧当分母会把测试变成掷骰子，
    /// 所以分母取若干稳态帧的中间值。两倍是个宽裕的上限：真把建索引挤进换档帧的话，
    /// 那一帧会是稳态的十倍上下，远不是两倍能兜住的。
    /// </remarks>
    private const double SwitchBudget = 2;

    /// <summary>放大到适配倍数的几倍。适配时整张图都在视口里，剔除无从谈起。</summary>
    private const double Zoom = 4;

    private static readonly Lazy<DrawList> Scene = new(Build);

    private readonly ITestOutputHelper _output;

    public ModeSwitchTests(ITestOutputHelper output) => _output = output;

    #region 换档那一帧不做额外的事

    [Fact]
    [Trait("Category", "ModeSwitch")]
    public async Task The_frame_that_switches_does_no_extra_work()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            WarmUp(window, canvas);
            Prepare(window, canvas);

            var arming = Time(window, canvas);

            window.Model.Mode.Mode.Should().Be(
                RenderMode.Immediate,
                "判定那一帧还在用旧档，新档下一帧才生效");
            window.Model.Mode.IsSwitching.Should().BeTrue("预备做完了，只等下一帧换过来");

            var switching = Time(window, canvas);

            window.Model.Mode.Mode.Should().Be(RenderMode.Virtualized, "这一帧换过来了");
            window.Model.Mode.IsSwitching.Should().BeFalse("换档只花一帧，不该拖到第三帧");

            var steady = new List<double>(SteadyFrames);

            for (var index = 0; index < SteadyFrames; index++)
            {
                steady.Add(Time(window, canvas));
            }

            steady.Sort();

            var median = steady[steady.Count / 2];

            // 判据是比值，但比值本身要留个凭据：只写"通过"的话，读报告的人
            // 没法判断它是在余量充足的情况下通过，还是压着线过去的。
            _output.WriteLine(
                $"判定帧 {arming:0.00} ms，换档帧 {switching:0.00} ms，"
                + $"稳态中位 {median:0.00} ms（{SteadyFrames} 帧，最快 {steady[0]:0.00} ms，"
                + $"最慢 {steady[^1]:0.00} ms）");

            switching.Should().BeLessThan(
                SwitchBudget * median,
                "换档那一帧不该是卡的那一帧。建索引排在判定帧上，换档帧只换一份要画的东西；"
                + "挤在一起的话，表现就是转一下视图卡一下");

            switching.Should().BeLessThan(
                SwitchBudget * arming,
                "判定帧做的是重活（建索引加画满屏），换档帧比它还慢一倍说明重活挪到了换档帧上");

            window.Close();
        });
    }

    #endregion

    #region 换档前后画面一致

    [Fact]
    [Trait("Category", "ModeSwitch")]
    public async Task Switching_does_not_change_a_single_pixel()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            WarmUp(window, canvas);
            Prepare(window, canvas);

            var before = HeadlessFixture.Frame(window, canvas);

            before.Should().NotBeNull("无头模式用的是真实的绘图后端，抓不到帧说明后端没起来");

            window.Model.Mode.Mode.Should().Be(RenderMode.Immediate, "第一帧还在旧档上");

            var drawnBefore = canvas.DrawnCommands;

            var after = HeadlessFixture.Frame(window, canvas);

            after.Should().NotBeNull();
            window.Model.Mode.Mode.Should().Be(RenderMode.Virtualized, "第二帧换过来了");

            canvas.DrawnCommands.Should().BeLessThan(
                drawnBefore,
                "换档之后确实少画了。一样多的话，这一条测的是\"两帧都画了全部\"，"
                + "而剔除到底有没有生效就没人验过");

            FirstDifference(before!, after!, RegionOf(canvas, window)).Should().BeNull(
                "剔除只是少画，不是画得不一样。差一个像素就说明被剔掉的东西原本露在视口里——"
                + "那种缺陷在平移时表现成\"边缘的元素一闪一闪\"，看不出规律");

            window.Close();
        });
    }

    #endregion

    #region 装置

    /// <summary>
    /// 画布在窗口位图里占的那块区域。
    /// </summary>
    /// <remarks>
    /// 比对要限定在画布上。状态栏在换档时本来就会变——它要报的就是模式名——
    /// 把整张窗口位图拿来比，等于要求状态栏不许说自己换了档。
    /// </remarks>
    private static PixelRect RegionOf(DiagramCanvas canvas, Window window)
    {
        var origin = HeadlessFixture.ToWindow(canvas, window, new Point(0, 0));

        return new PixelRect(
            (int)Math.Round(origin.X),
            (int)Math.Round(origin.Y),
            (int)Math.Round(canvas.Bounds.Width),
            (int)Math.Round(canvas.Bounds.Height));
    }

    /// <summary>
    /// 装上一份跨过阈值的大图，并把视口放大到只看得见一小块。
    /// </summary>
    /// <remarks>
    /// 适配到内容上时整张图都在视口里，剔除无从谈起——那时换档照样会发生，
    /// 但"画面一致"那一条会退化成"两帧都画了全部"，什么也没验到。
    /// </remarks>
    private static void Prepare(MainWindow window, DiagramCanvas canvas)
    {
        var model = window.Model;

        model.Load(Scene.Value);
        model.ZoomAt(Zoom, canvas.Bounds.Width / 2, canvas.Bounds.Height / 2);
    }

    /// <summary>跑一轮，把首帧成本花掉。</summary>
    private static void WarmUp(MainWindow window, DiagramCanvas canvas)
    {
        Prepare(window, canvas);

        for (var index = 0; index < WarmupFrames; index++)
        {
            HeadlessFixture.Frame(window, canvas);
        }
    }

    /// <summary>渲染一帧并量它花了多久。</summary>
    private static double Time(MainWindow window, DiagramCanvas canvas)
    {
        var stopwatch = Stopwatch.StartNew();

        HeadlessFixture.Frame(window, canvas);

        stopwatch.Stop();

        return stopwatch.Elapsed.TotalMilliseconds;
    }

    /// <summary>两张位图在给定区域里第一处不同的位置。完全相同时为空。</summary>
    private static string? FirstDifference(WriteableBitmap before, WriteableBitmap after, PixelRect region)
    {
        var left = Pixels(before);
        var right = Pixels(after);

        if (left.Length != right.Length)
        {
            return $"两张图大小不同：{left.Length} 字节对 {right.Length} 字节";
        }

        var columns = before.PixelSize.Width;
        var pixels = columns * before.PixelSize.Height;
        var bytesPerPixel = pixels == 0 ? 4 : left.Length / pixels;
        var stride = columns * bytesPerPixel;

        for (var row = region.Y; row < region.Bottom; row++)
        {
            var start = (row * stride) + (region.X * bytesPerPixel);

            for (var offset = 0; offset < region.Width * bytesPerPixel; offset++)
            {
                if (left[start + offset] != right[start + offset])
                {
                    return $"第 {row} 行第 {region.X + (offset / bytesPerPixel)} 列";
                }
            }
        }

        return null;
    }

    private static byte[] Pixels(WriteableBitmap frame)
    {
        using var locked = frame.Lock();

        var bytes = new byte[locked.RowBytes * locked.Size.Height];

        Marshal.Copy(locked.Address, bytes, 0, bytes.Length);

        return bytes;
    }

    /// <summary>
    /// 造一份网格图。
    /// </summary>
    /// <remarks>
    /// 排成网格而不是一条链：链只占一条线，放大之后视口要么套住整条链要么完全错过它，
    /// 剔除率会在一与零之间跳。网格铺满一片区域，视口落在哪儿都能剔掉大部分。
    /// </remarks>
    private static DiagramDocument Graph()
    {
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(NodeCount)));
        var rows = (int)Math.Ceiling((double)NodeCount / columns);

        var nodes = new List<NodeDef>(NodeCount);

        for (var index = 0; index < NodeCount; index++)
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

                if (to >= NodeCount)
                {
                    continue;
                }

                edges.Add(new EdgeDef { Id = $"e{from}", From = $"n{from}", To = $"n{to}" });
            }
        }

        return DiagramDocument.CreateFromContent(
            $"mode-switch-{NodeCount}",
            DiagramKind.Flowchart,
            Direction.TB,
            nodes: nodes,
            edges: edges);
    }

    /// <summary>
    /// 造文档、求解布局、翻译成绘制列表。
    /// </summary>
    /// <remarks>
    /// 走的是应用里那条链路，只是换了一份更大的文档：另拼一份绘制列表的话，
    /// 元素与指令的配比就和真实图不一样，而剔除率、绘制耗时都跟这个配比有关。
    /// </remarks>
    private static DrawList Build()
    {
        using var measurer = new SkiaTextMeasurer();

        var document = Graph();
        var job = LayoutRequestFactory.FromDocument(
            document,
            node => SceneBuilder.MeasureNode(node, Theme.Default, measurer));
        var layout = new LayoutCoordinator(new ConstraintLayoutEngine()).Compute(job).Layout;

        return SceneBuilder.Build(document, layout, Theme.Default, measurer);
    }

    #endregion
}
