using System.Diagnostics;
using System.Runtime;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Layout;
using DuetDiagram.Render;

namespace MemBudget;

/// <summary>
/// 内存预算取证：逐档量进程的真实工作集，并核两处「有上限」的设计。
/// </summary>
/// <remarks>
/// <para>
/// <b>量的是进程工作集，不是垃圾回收的已提交字节。</b> 已提交字节只说明托管堆要了多少地址空间，
/// 它既不含原生字体、位图与布局引擎的内部结构，也不说明这些页有没有真的落在物理内存里。
/// 用户在意的是「这个进程占了我多少内存」，那是工作集。
/// </para>
/// <para>
/// <b>档与档之间只比增量。</b> 绝对读数里含运行时自己那几十兆，把绝对值当结论的话，
/// 换一台机器、换一个运行时补丁，同一个实现的「内存占用」就会变，而它一行代码都没动。
/// 所以每一档都先记基线、再记峰值，判的是增量与最终的峰值。
/// </para>
/// <para>
/// <b>峰值靠后台采样，不靠操作前后各读一次。</b> 布局那一段的峰值出现在中途——分配完中间结构、
/// 还没把它们丢掉的那一刻。前后各读一次读到的是两头都安静的值，恰好把要量的那一段漏掉。
/// </para>
/// <para>
/// <b>两处「有上限」单独核，不并进总分里。</b> 总数好看不能说明它们有界：
/// 一个每次换文档都多留一份索引的实现，在千节点上多出来的那点内存会被运行时自己的开销盖住，
/// 而它在大文档上迟早会暴露。所以那两处用「重复做同一件事、看它还涨不涨」来判，
/// 判据是「不再增长」，不是「涨得不多」。
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>工作集的判据。超过它退出码 1。</summary>
    private const long BudgetBytes = 500L * 1024 * 1024;

    private const int DefaultNodes = 1000;

    private const int DefaultBigNodes = 5000;

    /// <summary>滚动多少次。视口从内容左上角扫到右下角，覆盖索引要走的各条分支。</summary>
    private const int ScrollCount = 200;

    /// <summary>版本日志每批写多少条。两批都远超日志自己的上限。</summary>
    private const int LogBatch = 500;

    /// <summary>索引重建多少次来核它可回收。够多才看得出累积。</summary>
    private const int RebuildCount = 20;

    /// <summary>每条日志带多少字段变更与波及元素。两者都压在降级阈值之下。</summary>
    private const int ChangesPerEntry = 50;

    private const int AffectedPerEntry = 20;

    /// <summary>采样间隔。布局那一段是几百毫秒到几秒，两毫秒一次足够密。</summary>
    private const int SampleMilliseconds = 2;

    /// <summary>
    /// 第二遍之后还允许涨多少算「不再增长」。
    /// </summary>
    /// <remarks>
    /// 不取零：托管堆的读数是「强制回收之后」的值，而强制回收之后仍有字符串驻留、
    /// 即时编译后的代码页、垃圾回收自己的段这些不会退回去的东西。取零会让这一条在
    /// 任何机器上都不成立，于是判据形同虚设。两兆是这段噪声的量级。
    /// </remarks>
    private const long SteadyToleranceBytes = 2L * 1024 * 1024;

    public static int Main(string[] args)
    {
        var nodes = ReadInt(args, "--nodes", DefaultNodes);
        var bigNodes = ReadInt(args, "--big", DefaultBigNodes);

        Console.WriteLine("内存预算取证");
        Console.WriteLine($"千节点场景 {nodes} 个节点，大文档 {bigNodes} 个节点，判据 < {Mb(BudgetBytes):0} MB");
        Console.WriteLine($"滚动 {ScrollCount} 次核视口索引，版本日志每批写 {LogBatch} 条核日志上限");
        Console.WriteLine();

        using var watcher = new Watcher();

        var stages = new List<Stage>();
        var invariants = new List<string>();

        // 各档的产物要跨档传下去（文档给布局、绘制列表给索引），所以在这里先声明。
        DiagramDocument? bigDocument = null;
        DiagramDocument? loadedDocument = null;
        string bigJson = string.Empty;
        DrawList? bigDrawList = null;
        DrawList? sceneDrawList = null;
        CullingIndex? index = null;
        var bigLayout = "未试";

        stages.Add(Step("空载", watcher, () => { }));

        stages.Add(Step("大文档 IR", watcher, () => bigDocument = Graph(bigNodes)));

        stages.Add(Step("序列化", watcher, () => bigJson = DiagramSerializer.SerializeFull(bigDocument!)));

        stages.Add(Step("载入（反序列化）", watcher, () => loadedDocument = DiagramSerializer.DeserializeFull(bigJson)));

        // 大文档排不排得出来本身是个结论，所以这里接住异常照常报，不让它把整轮测量打断。
        stages.Add(Step("大文档布局", watcher, () =>
        {
            try
            {
                bigDrawList = Compose(loadedDocument!, LayoutBudgets.ManualRelayout);
                bigLayout = "成功";
            }
            catch (LayoutFailedException failure)
            {
                bigLayout = Describe(failure);
            }
        }));

        stages.Add(Step("千节点布局 + 绘制列表", watcher, () =>
            sceneDrawList = Compose(Graph(nodes), null)));

        stages.Add(Step("建视口索引", watcher, () => index = new CullingIndex(sceneDrawList!)));

        stages.Add(Step($"滚动 {ScrollCount} 次", watcher, () => Scroll(index!, ScrollCount)));

        // 剔除率是「最近一次查询」的读数，所以在滚动那一档之后立刻取，
        // 后面重建索引会把读数重置成零。
        var cullRate = index!.CullRate;
        var elementCount = index.ElementCount;

        var log = new VersionLog();

        stages.Add(Step($"版本日志前 {LogBatch} 条", watcher, () => WriteLog(log, 0, LogBatch)));

        var heapAfterFirstBatch = SettledHeap();

        stages.Add(Step($"版本日志再 {LogBatch} 条", watcher, () => WriteLog(log, LogBatch, LogBatch)));

        var heapAfterSecondBatch = SettledHeap();

        // 视口索引：换一份新的，把旧的丢掉，反复若干次。索引是只读的，
        // 换掉之后旧的应当整个可回收——留得住的话，每换一次文档就多占一份。
        var heapBeforeRebuild = SettledHeap();

        stages.Add(Step($"重建索引 {RebuildCount} 次", watcher, () =>
        {
            for (var round = 0; round < RebuildCount; round++)
            {
                index = new CullingIndex(sceneDrawList!);
            }
        }));

        var heapAfterRebuild = SettledHeap();

        Report(stages);
        Console.WriteLine();

        Check("版本日志条数封顶", log.Count == VersionLogLimits.MaxEntries,
            $"写了两批共 {LogBatch * 2} 条之后队列里是 {log.Count} 条，上限 {VersionLogLimits.MaxEntries} 条",
            invariants);

        Check("日志写满之后不再增长", heapAfterSecondBatch - heapAfterFirstBatch <= SteadyToleranceBytes,
            $"托管堆在第二批前后是 {Mb(heapAfterSecondBatch):0.0} MB 对 {Mb(heapAfterFirstBatch):0.0} MB，"
            + $"涨了 {Mb(heapAfterSecondBatch - heapAfterFirstBatch):0.00} MB",
            invariants);

        Check("换索引不累积", heapAfterRebuild - heapBeforeRebuild <= SteadyToleranceBytes,
            $"托管堆在重建 {RebuildCount} 次前后是 {Mb(heapAfterRebuild):0.0} MB 对 {Mb(heapBeforeRebuild):0.0} MB，"
            + $"涨了 {Mb(heapAfterRebuild - heapBeforeRebuild):0.00} MB",
            invariants);

        var scale = new List<string>
        {
            $"大文档   节点 {loadedDocument!.Nodes.Count}，连线 {loadedDocument.Edges.Count}，"
            + $"JSON {bigJson.Length / 1024.0 / 1024.0:0.0} MB 字符",
            $"大文档布局 {bigLayout}",
            $"千节点   绘制指令 {sceneDrawList!.Commands.Count} 条，元素 {elementCount} 个，"
            + $"末次查询剔除率 {cullRate:P1}",
        };

        if (bigDrawList is not null)
        {
            scale.Add($"大文档绘制列表 {bigDrawList.Commands.Count} 条，内容 {bigDrawList.Width:0} × {bigDrawList.Height:0}");
        }

        foreach (var line in scale)
        {
            Console.WriteLine($"  {line}");
        }

        Console.WriteLine();

        var peak = stages.Max(stage => stage.Peak);
        var process = Process.GetCurrentProcess();

        Console.WriteLine($"  峰值工作集 {Mb(peak):0.0} MB，判据 < {Mb(BudgetBytes):0.0} MB");
        Console.WriteLine($"  进程自己的峰值读数 {Mb(process.PeakWorkingSet64):0.0} MB（含装置本身与运行时）");
        Console.WriteLine();

        if (invariants.Count > 0)
        {
            foreach (var invariant in invariants)
            {
                Console.Error.WriteLine($"装置不可信：{invariant}");
            }

            return 2;
        }

        if (peak >= BudgetBytes)
        {
            Console.Error.WriteLine($"不达标：峰值工作集 {Mb(peak):0.0} MB，超过 {Mb(BudgetBytes):0.0} MB");

            return 1;
        }

        Console.WriteLine($"达标：峰值工作集不超过 {Mb(BudgetBytes):0.0} MB，且两处上限都成立");

        return 0;
    }

    /// <summary>
    /// 把一次布局失败写成人能读的一行。
    /// </summary>
    /// <remarks>
    /// 逐级印出「试到哪一级、花了多久、是不是超时」——只印一句「全部失败」的话，
    /// 读的人分不清是引擎算不出来，还是每一级都撞在时限上，而这两种的修法完全不同。
    /// </remarks>
    private static string Describe(LayoutFailedException failure)
    {
        var levels = failure.Payload.Attempts.Select(attempt =>
            $"{attempt.Level} {(attempt.TimedOut ? "超时" : "报错")} {attempt.Elapsed.TotalMilliseconds:0} ms");

        return $"失败（{string.Join("，", levels)}）";
    }

    /// <summary>
    /// 跑一档，记下基线、峰值与操作后的常驻值。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 峰值取「采样到的最大」与「操作结束那一刻的读数」中的大者：操作短于一个采样间隔时
    /// 采样器可能一次都没醒，那样峰值会是零。
    /// </para>
    /// <para>
    /// 每一档开始前先强制回收一次，让各档的基线可比。不回收的话，
    /// 上一档留下的垃圾会算进这一档的起点，于是「这一档花了多少内存」里混着上一档的欠账，
    /// 表上还会出现「增量是负的」这种读不懂的行。
    /// </para>
    /// </remarks>
    private static Stage Step(string name, Watcher watcher, Action action)
    {
        _ = SettledHeap();

        var baseline = Environment.WorkingSet;

        watcher.Reset();
        action();
        var peak = Math.Max(watcher.Peak, Environment.WorkingSet);

        return new Stage(name, baseline, peak);
    }

    /// <summary>
    /// 强制回收之后读托管堆。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这一对读数用来判「重复做同一件事还涨不涨」。工作集会被原生分配与内存整理搅动，
    /// 而这里要看的只是托管对象有没有被留住，所以用托管堆，且每次都先强制回收。
    /// </para>
    /// <para>
    /// 大对象堆要顺带压实一次。默认不压实的话，一次大分配留下的空段会一直挂着，
    /// 读数比真实的存活对象高出一截——而那一截会随上一档做了什么而变，
    /// 于是同一个检查在两轮里给出差一倍的两个数。
    /// </para>
    /// </remarks>
    private static long SettledHeap()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        Collect();

        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private static void Collect()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
    }

    /// <summary>滚动一遍：视口从内容左上角扫到右下角。</summary>
    private static void Scroll(CullingIndex index, int count)
    {
        var width = index.Source.Width;
        var height = index.Source.Height;
        var policy = CullingPolicy.Default;

        for (var step = 0; step < count; step++)
        {
            var progress = (double)step / count;
            var viewport = new SpatialRect(
                progress * Math.Max(0, width - ViewportWidth),
                progress * Math.Max(0, height - ViewportHeight),
                ViewportWidth,
                ViewportHeight);

            index.Visible(policy.VisibleArea(viewport));
        }
    }

    private const double ViewportWidth = 1920;

    private const double ViewportHeight = 1080;

    /// <summary>
    /// 往版本日志里写若干条。
    /// </summary>
    /// <remarks>
    /// 每条的规模都压在两个降级阈值之下，这样量到的是**队列长度**这个上限，
    /// 而不是「一条被裁成了批量变更所以本来就小」。裁掉明细那条路径由它自己的用例守。
    /// </remarks>
    private static void WriteLog(VersionLog log, int offset, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var version = offset + index + 1;
            var changes = new FieldChange[ChangesPerEntry];
            var affected = new string[AffectedPerEntry];

            for (var slot = 0; slot < ChangesPerEntry; slot++)
            {
                changes[slot] = new FieldChange
                {
                    ElementId = $"n{slot}",
                    Field = "label",
                    OldValue = $"旧值 {slot}",
                    NewValue = $"新值 {version}-{slot}",
                };
            }

            for (var slot = 0; slot < AffectedPerEntry; slot++)
            {
                affected[slot] = $"n{slot}";
            }

            log.Record(new VersionEntry
            {
                Version = version,
                CommandId = $"cmd-{version}",
                Source = ChangeSource.Llm,
                Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(version),
                AffectedIds = affected,
                Changes = changes,
            });
        }
    }

    /// <summary>造一份文档并走完整条链路，返回绘制列表。</summary>
    /// <remarks>
    /// 预算由调用方给。千节点那一档传空，走的是产品默认的「结构变更」那一档——
    /// 那一档才是用户改一下文档实际会等的时长，量出来的内存才对得上真实路径。
    /// 大文档那一档要给更长的预算，理由印在输出里也写在报告里。
    /// </remarks>
    private static DrawList Compose(DiagramDocument document, TimeSpan? budget)
    {
        using var measurer = new SkiaTextMeasurer();

        return SceneComposer.Compose(document, Theme.Default, measurer, budget: budget).DrawList;
    }

    /// <summary>
    /// 排成网格的图：每层若干并列节点，层间连接同序号节点。
    /// </summary>
    /// <remarks>
    /// 与帧率基准用的是同一种形态——真实流程图与架构图最常见的样子，
    /// 也是布局压力最大的样子：层数决定分层阶段的工作量，每层宽度决定层内排序的工作量。
    /// 连线不写标签，标签的排版成本已经在节点标签上量到了。
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
            $"mem-{nodeCount}",
            DiagramKind.Flowchart,
            Direction.TB,
            nodes: nodes,
            edges: edges);
    }

    private static void Report(IReadOnlyList<Stage> stages)
    {
        Console.WriteLine($"  {Pad("档位")}{Cell("基线", 10)}{Cell("峰值", 10)}{Cell("增量", 10)}");
        Console.WriteLine($"  {new string('─', 56)}");

        foreach (var stage in stages)
        {
            Console.WriteLine(
                $"  {Pad(stage.Name)}{Cell($"{Mb(stage.Baseline):0.0} MB", 10)}"
                + $"{Cell($"{Mb(stage.Peak):0.0} MB", 10)}{Cell($"{Mb(stage.Peak - stage.Baseline):0.0} MB", 10)}");
        }

        Console.WriteLine();
        Console.WriteLine("  每档开始前强制回收一次，所以基线是「上一档的垃圾已经收掉」之后的值。");
        Console.WriteLine("  增量是这一档把进程推到多高，不是这一档新分配了多少——两者在高分配低存活的操作上差很多。");
    }

    private static void Check(string name, bool holds, string detail, List<string> failures)
    {
        Console.WriteLine($"  [{(holds ? "成立" : "不成立")}] {name}：{detail}");

        if (!holds)
        {
            failures.Add($"{name}不成立——{detail}");
        }
    }

    /// <summary>档位名左对齐到固定显示宽度。中文按两格算，否则列会对不齐。</summary>
    private static string Pad(string text)
    {
        var actual = text.Sum(character => character > 0x7F ? 2 : 1);

        return text + new string(' ', Math.Max(0, 26 - actual));
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

    private static double Mb(long bytes) => bytes / 1024.0 / 1024.0;

    private static int ReadInt(string[] args, string name, int fallback)
    {
        var index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value)
            ? value
            : fallback;
    }

    /// <summary>一档的读数。</summary>
    /// <param name="Name">档位名。</param>
    /// <param name="Baseline">这一档开始前的工作集。</param>
    /// <param name="Peak">这一档里采样到的最大工作集。</param>
    private sealed record Stage(string Name, long Baseline, long Peak);

    /// <summary>
    /// 后台按固定间隔采工作集，记下这一段里的最大值。
    /// </summary>
    /// <remarks>
    /// 峰值出现在操作的中途，所以只能在旁边看着采。采样本身要便宜——用环境变量那一个读数，
    /// 它是一次系统调用，不建对象、不排序；换成每次都取一份性能计数器的话，
    /// 采样器自己就会出现在被采的这段内存里。
    /// </remarks>
    private sealed class Watcher : IDisposable
    {
        private readonly Thread _thread;
        private long _peak;
        private volatile bool _running = true;

        public Watcher()
        {
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "mem-watch",
            };

            _thread.Start();
        }

        public long Peak => Interlocked.Read(ref _peak);

        public void Reset() => Interlocked.Exchange(ref _peak, 0);

        public void Dispose()
        {
            _running = false;
            _thread.Join(TimeSpan.FromSeconds(1));
        }

        private void Loop()
        {
            while (_running)
            {
                Bump(Environment.WorkingSet);

                Thread.Sleep(SampleMilliseconds);
            }
        }

        private void Bump(long value)
        {
            long current;

            while (value > (current = Interlocked.Read(ref _peak)))
            {
                if (Interlocked.CompareExchange(ref _peak, value, current) == current)
                {
                    return;
                }
            }
        }
    }
}
