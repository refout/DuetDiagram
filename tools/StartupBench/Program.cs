using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;

namespace StartupBench;

/// <summary>
/// 冷启动取证：每轮起一个全新的界面进程，量「进程创建 → 首帧可交互」。
/// </summary>
/// <remarks>
/// <para>
/// <b>量的是墙上时钟，不是界面自己报的总数。</b> 界面那个总数从它的入口算起，
/// 而用户等的是从双击图标那一刻算起——两者之间还隔着运行时装起来、把入口方法编译出来这一段。
/// 所以装置在起进程之前看表，看到界面的收工标记再停表，同时把界面自己的读数也读回来，
/// 两者的差就是「运行时那一段」。
/// </para>
/// <para>
/// <b>每轮都是新进程。</b> 同一个进程里开第二个窗口量到的是热路径——绘制列表与字体都在缓存里，
/// 而冷启动要回答的正是这些缓存还是空的时候要等多久。
/// </para>
/// <para>
/// <b>逐段读数由界面自己写文件。</b> 从外面只能看到「窗口出现了」这一个外部迹象，
/// 拿它当终点的话，量到的是装置轮询的准确度，而不是界面的速度。
/// </para>
/// <para>
/// <b>第一轮与其余各轮分开报。</b> 第一轮的磁盘缓存与即时编译状态与后面几轮不同，
/// 把两者平均掉会把「第一次打开要多久」这个最要紧的读数抹平。
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// 判据的默认值：进程创建 → 首帧可交互不超过这么多毫秒。超过它退出码 1。
    /// </summary>
    /// <remarks>
    /// 这是规格里那条判据，取在参考机器上。共享的 CI 运行器比参考机器慢三成上下，
    /// 拿这个数去卡它，量到的是「今天这台机器有多忙」而不是「产品有没有退化」——
    /// 所以允许用 <c>--budget</c> 换一个判据值，报告里记的始终是这一档。
    /// </remarks>
    private const int DefaultBudgetMilliseconds = 2000;

    private const int DefaultRuns = 5;

    private const int DefaultNodes = 200;

    /// <summary>观察项用的节点数。它不计入判据，只为看清「大图要等多久」。</summary>
    private const int ObservationNodes = 1000;

    private const int RunTimeoutMilliseconds = 60_000;

    /// <summary>界面上打开打点的开关。与界面那一侧必须一致。</summary>
    private const string ProbeSwitch = "--startup-probe";

    /// <summary>界面收工时打到标准输出那一行的开头。与界面那一侧必须一致。</summary>
    private const string MarkerPrefix = "startup-probe";

    /// <summary>时间点的先后次序。相邻两点之差就是一段。</summary>
    private static readonly string[] MarkOrder =
    [
        "main", "read", "platform", "document", "layout", "session", "scene", "window", "opened", "frame",
    ];

    /// <summary>段的显示名，按它的终点时间点命名。</summary>
    private static readonly (string Mark, string Label)[] Segments =
    [
        ("read", "反序列化"),
        ("platform", "平台"),
        ("document", "文档"),
        ("layout", "首布局"),
        ("session", "会话"),
        ("scene", "面板"),
        ("window", "画布"),
        ("opened", "上屏"),
        ("frame", "首渲染"),
    ];

    public static int Main(string[] args)
    {
        var runs = ReadInt(args, "--runs", DefaultRuns);
        var nodes = ReadInt(args, "--nodes", DefaultNodes);
        var budget = ReadInt(args, "--budget", DefaultBudgetMilliseconds);
        var configuration = ReadOption(args, "--configuration") ?? "Release";
        var skipBig = args.Contains("--no-big", StringComparer.Ordinal);

        Console.WriteLine("冷启动取证");
        Console.WriteLine($"判据：进程创建 → 首帧可交互 ≤ {budget:0} ms");
        Console.WriteLine($"每个场景跑 {runs} 轮，每轮起一个全新的界面进程；逐段耗时由界面自己写文件");
        Console.WriteLine();

        if (FindRoot() is not { } root)
        {
            Console.Error.WriteLine("找不到仓库根：从装置所在目录往上找不到解决方案文件");

            return 2;
        }

        if (ResolveApp(root, configuration) is not { } app)
        {
            Console.Error.WriteLine($"找不到界面程序：先 dotnet build DuetDiagram.slnx -c {configuration}");

            return 2;
        }

        Console.WriteLine($"运行环境：{DescribeEnvironment()}");
        Console.WriteLine($"界面程序：{app.FileName}");
        Console.WriteLine();

        var fixtures = Path.Combine(Path.GetTempPath(), "duet-startup-fixtures");
        Directory.CreateDirectory(fixtures);

        var scenarios = new List<Scenario>
        {
            new("默认文档", "不读文件，直接造一份示例文档", [], runs, Judged: true),
            new("打开文档", $"打开一份 {nodes} 个节点的文档，走读文件与反序列化那一条路",
                ["--open", Generate(fixtures, nodes)], runs, Judged: true),
        };

        if (!skipBig && nodes != ObservationNodes)
        {
            scenarios.Add(new(
                "打开大文档",
                $"打开一份 {ObservationNodes} 个节点的文档（观察项，不计入判据）",
                ["--open", Generate(fixtures, ObservationNodes)],
                Runs: 1,
                Judged: false));
        }

        Console.WriteLine($"文档语料：{fixtures}");
        Console.WriteLine();

        var verdicts = new List<string>();
        var deviceFailures = new List<string>();
        var observations = new List<string>();

        foreach (var scenario in scenarios)
        {
            var results = new List<RunResult>();

            for (var round = 1; round <= scenario.Runs; round++)
            {
                var result = RunOnce(app, root, scenario.Arguments);

                if (!result.Ok)
                {
                    deviceFailures.Add($"{scenario.Name} 第 {round} 轮：{result.Failure}");
                    break;
                }

                results.Add(result);
            }

            if (results.Count == 0)
            {
                Console.WriteLine($"场景 {scenario.Name}：量不了");
                Console.WriteLine();

                continue;
            }

            Report(scenario, results);

            var median = Median(results.Select(result => result.Total));
            var first = results[0].Total;

            if (scenario.Judged)
            {
                if (median > budget)
                {
                    verdicts.Add($"{scenario.Name} 中位数 {median:0.0} ms，超过 {budget:0} ms");
                }
                else if (first > budget)
                {
                    verdicts.Add($"{scenario.Name} 第一轮 {first:0.0} ms，超过 {budget:0} ms（中位数 {median:0.0} ms 达标）");
                }
            }
            else
            {
                observations.Add($"{scenario.Name} {ObservationNodes} 节点：第一轮 {first:0.0} ms，中位数 {median:0.0} ms");
            }
        }

        if (observations.Count > 0)
        {
            Console.WriteLine("观察项（不计入判据）");

            foreach (var line in observations)
            {
                Console.WriteLine($"  {line}");
            }

            Console.WriteLine();
        }

        if (deviceFailures.Count > 0)
        {
            foreach (var failure in deviceFailures)
            {
                Console.Error.WriteLine($"装置不可信：{failure}");
            }

            return 2;
        }

        if (verdicts.Count > 0)
        {
            foreach (var verdict in verdicts)
            {
                Console.Error.WriteLine($"不达标：{verdict}");
            }

            return 1;
        }

        Console.WriteLine($"达标：进程创建到首帧可交互不超过 {budget:0} ms");

        return 0;
    }

    /// <summary>
    /// 起一个界面进程，量到它把首帧画完并上屏为止。
    /// </summary>
    /// <remarks>
    /// 表在起进程之前开始走，看到收工标记那一刻停。标记本身要经管道回到这里，
    /// 所以读数里含管道与线程调度那几毫秒——它与两秒的判据不在一个量级上，不另做补偿。
    /// </remarks>
    private static RunResult RunOnce(Launch app, string root, string[] arguments)
    {
        var probeFile = Path.Combine(Path.GetTempPath(), $"duet-startup-{Guid.NewGuid():N}.txt");

        var info = new ProcessStartInfo(app.FileName)
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in app.Prefix)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.ArgumentList.Add(ProbeSwitch);
        info.ArgumentList.Add(probeFile);

        var clock = Stopwatch.StartNew();
        var markerTicks = 0L;

        using var process = new Process { StartInfo = info };

        process.OutputDataReceived += (_, line) =>
        {
            if (line.Data is null || !line.Data.StartsWith(MarkerPrefix, StringComparison.Ordinal))
            {
                return;
            }

            Interlocked.CompareExchange(ref markerTicks, clock.ElapsedTicks, 0);
        };

        try
        {
            if (!process.Start())
            {
                return RunResult.Device("进程起不来");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return RunResult.Device($"起进程失败：{exception.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(RunTimeoutMilliseconds))
        {
            TryKill(process);

            return RunResult.Device($"等进程退出超时（{RunTimeoutMilliseconds} ms）");
        }

        // 再走一遍：上一句可能在异步读取把最后几行交出来之前就返回了，
        // 而收工标记恰好是最后几行之一。
        process.WaitForExit();

        var exitCode = process.ExitCode;

        if (markerTicks == 0)
        {
            return RunResult.Device($"没等到收工标记（退出码 {exitCode}）");
        }

        var wall = markerTicks * 1000.0 / Stopwatch.Frequency;
        var marks = ReadProbe(probeFile);

        TryDelete(probeFile);

        if (marks is null)
        {
            return RunResult.Device("界面没有写出逐段读数");
        }

        if (marks.TryGetValue("failure", out var failure))
        {
            return RunResult.Device($"界面自己报的量不成：{failure}");
        }

        var inside = marks.TryGetValue("total", out var total) ? total : wall;

        return new RunResult(
            Ok: true,
            Failure: null,
            Host: Math.Max(0, wall - inside),
            Total: wall,
            Segments: Split(marks),
            DrawnCommands: marks.TryGetValue("drawn_commands", out var drawn) ? (int)drawn : 0,
            ExitCode: exitCode);
    }

    /// <summary>相邻两个时间点之差就是一段。缺的点跳过，于是缺的那一段自然消失。</summary>
    private static Dictionary<string, double> Split(IReadOnlyDictionary<string, double> marks)
    {
        var segments = new Dictionary<string, double>(StringComparer.Ordinal);
        var previous = 0.0;
        var has = false;

        foreach (var mark in MarkOrder)
        {
            if (!marks.TryGetValue(mark, out var value))
            {
                continue;
            }

            var label = LabelOf(mark);

            if (has && label is not null)
            {
                segments[label] = value - previous;
            }

            previous = value;
            has = true;
        }

        return segments;
    }

    /// <summary>一个时间点对应的段名。它不是任何一段的终点时给空。</summary>
    private static string? LabelOf(string mark)
    {
        foreach (var (candidate, label) in Segments)
        {
            if (string.Equals(candidate, mark, StringComparison.Ordinal))
            {
                return label;
            }
        }

        return null;
    }

    private static void Report(Scenario scenario, IReadOnlyList<RunResult> results)
    {
        var median = Median(results.Select(result => result.Total));
        var medianRun = results.OrderBy(result => Math.Abs(result.Total - median)).First();
        var first = results[0];

        Console.WriteLine($"场景 {scenario.Name}：{scenario.Note}");
        Console.WriteLine($"  {Pad("轮次", 6)}{Cell("运行时", 10)}{Cell("总计", 10)}");

        for (var index = 0; index < results.Count; index++)
        {
            Console.WriteLine(
                $"  {Pad((index + 1).ToString(CultureInfo.InvariantCulture), 6)}"
                + $"{Cell($"{results[index].Host:0.0}", 10)}{Cell($"{results[index].Total:0.0}", 10)}");
        }

        Console.WriteLine();
        Console.WriteLine($"  {Pad("段", 10)}{Cell("第一轮", 12)}{Cell("中位数那一轮", 14)}");
        Console.WriteLine($"  {new string('─', 40)}");
        Console.WriteLine($"  {Pad("运行时", 10)}{Cell($"{first.Host:0.0}", 12)}{Cell($"{medianRun.Host:0.0}", 14)}");

        foreach (var (_, label) in Segments)
        {
            var firstValue = first.Segments.TryGetValue(label, out var a) ? a : (double?)null;
            var medianValue = medianRun.Segments.TryGetValue(label, out var b) ? b : (double?)null;

            Console.WriteLine(
                $"  {Pad(label, 10)}{Cell(firstValue is null ? "—" : $"{firstValue:0.0}", 12)}"
                + $"{Cell(medianValue is null ? "—" : $"{medianValue:0.0}", 14)}");
        }

        Console.WriteLine($"  {Pad("总计", 10)}{Cell($"{first.Total:0.0}", 12)}{Cell($"{medianRun.Total:0.0}", 14)}");

        var commands = string.Join("，", results.Select(result => result.DrawnCommands));
        var exits = results.Where(result => result.ExitCode != 0).Select(result => result.ExitCode).Distinct().ToList();

        Console.WriteLine();
        Console.WriteLine($"  首帧消费掉的绘制指令条数：{commands}");
        Console.WriteLine(
            $"  第一轮 {first.Total:0.0} ms；中位数 {median:0.0} ms"
            + $"（最小 {results.Min(result => result.Total):0.0}，最大 {results.Max(result => result.Total):0.0}）");

        if (exits.Count > 0)
        {
            Console.WriteLine($"  界面退出码不是零：{string.Join("，", exits)}");
        }

        Console.WriteLine();
    }

    /// <summary>
    /// 造一份排成网格的文档，写在磁盘上供界面打开。
    /// </summary>
    /// <remarks>
    /// 与帧率基准用的是同一种形态——真实流程图与架构图最常见的样子，也是布局压力最大的样子。
    /// 文件已存在就直接用：几轮之间共用一个文件，量的才是启动，不是造文件。
    /// </remarks>
    private static string Generate(string directory, int nodeCount)
    {
        var path = Path.Combine(directory, $"startup-{nodeCount}.json");

        if (File.Exists(path))
        {
            return path;
        }

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

        var document = DiagramDocument.CreateFromContent(
            $"startup-{nodeCount}",
            DiagramKind.Flowchart,
            Direction.TB,
            nodes: nodes,
            edges: edges);

        File.WriteAllText(path, DiagramSerializer.SerializeFull(document));

        return path;
    }

    private static Dictionary<string, double>? ReadProbe(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var marks = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var line in File.ReadAllLines(path))
        {
            var separator = line.IndexOf('=');

            if (separator <= 0)
            {
                continue;
            }

            var name = line[..separator];
            var value = line[(separator + 1)..];

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                marks[name] = number;
            }
            else
            {
                marks[name] = 0;
            }
        }

        return marks;
    }

    private static string DescribeEnvironment()
    {
        var processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");

        return string.Join(
            " / ",
            RuntimeInformation.OSDescription,
            $"{Environment.ProcessorCount} 逻辑核",
            RuntimeInformation.FrameworkDescription,
            string.IsNullOrWhiteSpace(processor) ? RuntimeInformation.ProcessArchitecture.ToString() : processor);
    }

    /// <summary>从装置所在目录往上找仓库根。按当前目录找的话，换个地方调用就找不到了。</summary>
    private static string? FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DuetDiagram.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>
    /// 找界面程序。
    /// </summary>
    /// <remarks>
    /// 找到的是原生宿主（可执行文件）而不是用 dotnet 去跑那一份程序集：用 dotnet 跑的话，
    /// 墙上时钟里会多出「找一个已经装好的运行时」那一段，而用户双击图标走的是原生宿主。
    /// 只有在没有原生宿主时才退回用 dotnet 跑程序集。
    /// </remarks>
    private static Launch? ResolveApp(string root, string configuration)
    {
        var bin = Path.Combine(root, "DuetDiagram.App", "bin", configuration);

        if (!Directory.Exists(bin))
        {
            return null;
        }

        var executable = OperatingSystem.IsWindows() ? "DuetDiagram.App.exe" : "DuetDiagram.App";

        foreach (var framework in Directory.GetDirectories(bin))
        {
            var host = Path.Combine(framework, executable);

            if (File.Exists(host))
            {
                return new Launch(host, []);
            }
        }

        foreach (var framework in Directory.GetDirectories(bin))
        {
            var assembly = Path.Combine(framework, "DuetDiagram.App.dll");

            if (File.Exists(assembly))
            {
                return new Launch("dotnet", [assembly]);
            }
        }

        return null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException
            or System.ComponentModel.Win32Exception)
        {
            // 进程可能已经自己退了。这里只是收尾，收不掉也没什么可做的。
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 临时文件删不掉不影响结论，下一次系统清理会带走它。
        }
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(value => value).ToList();

        return sorted.Count == 0
            ? 0
            : sorted.Count % 2 == 1
                ? sorted[sorted.Count / 2]
                : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2;
    }

    /// <summary>名字左对齐到固定显示宽度。中文按两格算，否则列会对不齐。</summary>
    private static string Pad(string text, int width)
    {
        var actual = text.Sum(character => character > 0x7F ? 2 : 1);

        return text + new string(' ', Math.Max(0, width - actual));
    }

    /// <summary>
    /// 数字右对齐到固定显示宽度。
    /// </summary>
    /// <remarks>
    /// 不能用格式化里的宽度：它按字符个数算，而中文字符在终端上占两格，
    /// 于是带中文的表头会比别的行短一截。
    /// </remarks>
    private static string Cell(string text, int width)
    {
        var actual = text.Sum(character => character > 0x7F ? 2 : 1);

        return new string(' ', Math.Max(0, width - actual)) + text;
    }

    private static int ReadInt(string[] args, string name, int fallback)
    {
        var index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value)
            ? value
            : fallback;
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);

        if (index < 0 || index + 1 >= args.Length)
        {
            return null;
        }

        var value = args[index + 1];

        return value.StartsWith("--", StringComparison.Ordinal) ? null : value;
    }

    /// <summary>一个场景：怎么起进程、跑几轮、算不算判据。</summary>
    private sealed record Scenario(string Name, string Note, string[] Arguments, int Runs, bool Judged);

    /// <summary>怎么起界面：可执行文件，以及要垫在命令行前面的那几项。</summary>
    private sealed record Launch(string FileName, string[] Prefix);

    /// <summary>一轮的读数。</summary>
    /// <param name="Ok">这一轮量成了没有。</param>
    /// <param name="Failure">没量成的原因。</param>
    /// <param name="Host">进程创建到界面入口那一段，由墙上时钟减去界面自己报的总数得到。</param>
    /// <param name="Total">进程创建到首帧可交互。</param>
    /// <param name="Segments">逐段耗时。</param>
    /// <param name="DrawnCommands">首帧消费掉的绘制指令条数。</param>
    /// <param name="ExitCode">界面进程的退出码。</param>
    private sealed record RunResult(
        bool Ok,
        string? Failure,
        double Host,
        double Total,
        IReadOnlyDictionary<string, double> Segments,
        int DrawnCommands,
        int ExitCode)
    {
        public static RunResult Device(string failure) =>
            new(false, failure, 0, 0, new Dictionary<string, double>(StringComparer.Ordinal), 0, 0);
    }
}
