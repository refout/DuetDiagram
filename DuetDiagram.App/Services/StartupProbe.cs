using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DuetDiagram.App.Services;

/// <summary>
/// 冷启动打点：从进程起来到首帧可交互，逐段记时，把读数写成一份文件。
/// </summary>
/// <remarks>
/// <para>
/// <b>终点是首帧画完且窗口已经上屏，不是 main 返回。</b> main 返回时窗口可能还没建起来，
/// 那时读到的只是"进程起来了"；用户等的是"图出来了、能点了"。所以收工要两个条件都满足：
/// 画布真的消费掉一份非空绘制列表，并且窗口已经上屏。
/// </para>
/// <para>
/// <b>同名打点只记第一次。</b> 布局与渲染在启动过程中会被叫很多次，后到的覆盖掉先到的，
/// 量出来的就成了最后一帧而不是首帧——而首帧恰恰是唯一有意义的那一帧。
/// </para>
/// <para>
/// <b>读数由界面自己写文件，装置只负责起进程与读文件。</b> 让装置从外面猜"什么时候算首帧"
/// 只能靠轮询窗口句柄之类的外部迹象，那样量到的是猜测的准确度。界面自己知道自己在哪一步。
/// </para>
/// <para>
/// <b>超时按"量不了"收场。</b> 界面起不来时进程会一直挂着，没有超时的话装置只能从外面杀它，
/// 拿到的是一份空文件，看不出卡在哪一段。
/// </para>
/// </remarks>
internal static class StartupProbe
{
    /// <summary>打开打点的开关。后面跟一个文件路径。</summary>
    public const string Switch = "--startup-probe";

    /// <summary>收工时打到标准输出那一行的开头。装置靠它记下墙上时钟的那一点。</summary>
    public const string MarkerPrefix = "startup-probe";

    /// <summary>等多久还没收工就认输。</summary>
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(30);

    private static readonly Stopwatch Clock = new();
    private static readonly List<Tick> Marks = [];
    private static readonly object Gate = new();

    private static string? _output;
    private static Timer? _watchdog;
    private static bool _done;

    // 首帧那一刻的旁证：这一帧消费掉多少条绘制指令、画布多大。
    // 判据之外还要能自证——只报一个总时长的话，读的人没法判断它是"画完了"还是"空窗口上屏了"。
    private static int _drawnCommands;
    private static double _canvasWidth;
    private static double _canvasHeight;
    private static double _windowWidth;
    private static double _windowHeight;

    /// <summary>开始打点。</summary>
    /// <param name="path">读数写到哪个文件。</param>
    public static void Begin(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _output = path;
        Clock.Start();
        Mark("main");

        _watchdog = new Timer(_ => Abort("等首帧超时"), null, GiveUpAfter, System.Threading.Timeout.InfiniteTimeSpan);
    }

    /// <summary>记一个点。同名只记第一次。</summary>
    public static void Mark(string name)
    {
        if (_output is null)
        {
            return;
        }

        lock (Gate)
        {
            if (_done || Marks.Any(mark => string.Equals(mark.Name, name, StringComparison.Ordinal)))
            {
                return;
            }

            Marks.Add(new Tick(name, Clock.Elapsed.TotalMilliseconds));
        }
    }

    /// <summary>
    /// 接上窗口的上屏事件。
    /// </summary>
    /// <remarks>
    /// 在窗口的构造函数里接。放到外面接的话，事件可能在接上之前就发过了，
    /// 而漏掉它的表现是"等首帧超时"——那种失败看起来像界面起不来，实际只是没人在听。
    /// </remarks>
    public static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (_output is null)
        {
            return;
        }

        window.Opened += (_, _) =>
        {
            Mark("opened");
            FinishIfReady(window);
        };
    }

    /// <summary>首帧画完了。窗口也已经上屏时收工。</summary>
    /// <param name="window">画这一帧的窗口，收工时由它关掉。</param>
    /// <param name="drawnCommands">这一帧消费掉的绘制指令条数。</param>
    /// <param name="width">画布宽度。</param>
    /// <param name="height">画布高度。</param>
    /// <remarks>
    /// <b>上屏之前画的帧不算。</b> 窗口还没出来时画的那一帧，尺寸与内容都未必是最终的，
    /// 把它当终点会得到一个偏小的读数，而它偏小的原因跟实现快慢无关。
    /// </remarks>
    public static void Frame(Window? window, int drawnCommands, double width, double height)
    {
        if (_output is null || _done || !Has("opened"))
        {
            return;
        }

        lock (Gate)
        {
            _drawnCommands = drawnCommands;
            _canvasWidth = width;
            _canvasHeight = height;
            _windowWidth = window?.ClientSize.Width ?? 0;
            _windowHeight = window?.ClientSize.Height ?? 0;
        }

        Mark("frame");
        FinishIfReady(window);
    }

    /// <summary>这一轮量不成：写清原因并按"量不了"的退出码收场。</summary>
    /// <remarks>
    /// 与"量出来超标"分开：那种是结论，这种是装置没跑起来。混成一个码之后，
    /// 脚本分不出该去改实现还是该去看装置。
    /// </remarks>
    public static void Fail(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (_output is null || !Claim())
        {
            return;
        }

        Clock.Stop();
        Write(reason, Clock.Elapsed.TotalMilliseconds);
        Report(reason);
    }

    private static void FinishIfReady(Window? window)
    {
        lock (Gate)
        {
            if (_done || !Has("opened") || !Has("frame"))
            {
                return;
            }

            _done = true;
        }

        Clock.Stop();
        _watchdog?.Dispose();

        Write(failure: null, Clock.Elapsed.TotalMilliseconds);
        Report(failure: null);

        // 关窗口要回界面线程：这一步可能是从渲染里被叫到的，而渲染那一段正在用的东西
        // 不能在关窗口时被拆掉。
        if (window is not null)
        {
            Dispatcher.UIThread.Post(() => window.Close(), DispatcherPriority.Background);
        }
    }

    private static void Abort(string reason)
    {
        if (_output is null || !Claim())
        {
            return;
        }

        Clock.Stop();
        Write(reason, Clock.Elapsed.TotalMilliseconds);
        Report(reason);

        // 兜底那一条：界面线程可能已经卡在一个起不来的原生调用里，
        // 那时排队等它没有任何意义，只能从这里收场。
        Environment.Exit(2);
    }

    /// <summary>把"这一轮结束了"这件事抢下来。抢不到说明别人已经收过工。</summary>
    private static bool Claim()
    {
        lock (Gate)
        {
            if (_done)
            {
                return false;
            }

            _done = true;

            return true;
        }
    }

    private static bool Has(string name) =>
        Marks.Any(mark => string.Equals(mark.Name, name, StringComparison.Ordinal));

    private static void Report(string? failure)
    {
        var line = failure is null
            ? $"{MarkerPrefix} total={Format(Clock.Elapsed.TotalMilliseconds)}ms file={_output}"
            : $"{MarkerPrefix} failed={failure} file={_output}";

        Console.Out.WriteLine(line);

        // 输出被重定向到管道时不是行缓冲的。不显式冲一下，装置要等到进程退出才看得到这一行，
        // 而它记的正是"看到这一行的那一刻"。
        Console.Out.Flush();
    }

    private static void Write(string? failure, double total)
    {
        var text = new StringBuilder("startup-probe/v1\n");

        lock (Gate)
        {
            foreach (var mark in Marks)
            {
                text.Append(mark.Name).Append('=').AppendLine(Format(mark.Milliseconds));
            }

            text.Append("total=").AppendLine(Format(total));
            text.Append("drawn_commands=").AppendLine(_drawnCommands.ToString(CultureInfo.InvariantCulture));
            text.Append("canvas_width=").AppendLine(Format(_canvasWidth));
            text.Append("canvas_height=").AppendLine(Format(_canvasHeight));
            text.Append("window_width=").AppendLine(Format(_windowWidth));
            text.Append("window_height=").AppendLine(Format(_windowHeight));
        }

        if (failure is not null)
        {
            text.Append("failure=").AppendLine(failure);
        }

        File.WriteAllText(_output!, text.ToString());
    }

    private static string Format(double milliseconds) =>
        milliseconds.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>一个时间点：名字与它距进程入口的毫秒数。</summary>
    private readonly record struct Tick(string Name, double Milliseconds);
}
