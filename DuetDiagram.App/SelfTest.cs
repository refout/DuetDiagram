using System.Diagnostics;
using Avalonia;
using Avalonia.Media.Imaging;
using DuetDiagram.App.Controls;
using DuetDiagram.App.Services;
using DuetDiagram.App.ViewModels;
using DuetDiagram.Render;
using SkiaSharp;
using LayoutConstraintSpec = DuetDiagram.Core.Model.LayoutConstraintSpec;

namespace DuetDiagram.App;

/// <summary>
/// 无人值守的自检：跑一遍真实的启动与渲染链路，用退出码表达结果。
/// </summary>
/// <remarks>
/// <para>
/// "界面能打开"这件事没法在持续集成里直接断言——打开窗口会一直等用户操作。
/// 自检把同一套启动配置跑一遍、渲染一帧、保存成图片，然后立即退出。这样既能在流水线上验证，
/// 也能顺带测出"启动到出图"这段耗时，而这段耗时正是用户感知到的冷启动时间的主要部分。
/// </para>
/// <para>
/// 除了出图，还单独验证一次原生绘图库能否加载与测量文本。图形绘制走的是界面框架自己的抽象，
/// 而那层抽象背后是否真的把原生库带起来，只有直接调用一次才能确认。
/// </para>
/// <para>
/// **它会核对画布确实把绘制列表消费完了。** 只看"没抛异常"的话，
/// 一份没画出来的列表与一份画在视口之外的列表都算通过，而两者都是空白图。
/// </para>
/// </remarks>
internal static class SelfTest
{
    public const string CommandLineSwitch = "--selftest";

    private const int DefaultWidth = 900;
    private const int DefaultHeight = 600;

    public static int Run(string[] args)
    {
        var outputPath = ReadOption(args, "--out") ?? Path.Combine(Path.GetTempPath(), "duetdiagram-selftest.png");

        try
        {
            // 启动阶段：初始化界面框架与平台后端。自检不创建窗口，因此这里只做到"平台可用"为止。
            var startup = Stopwatch.StartNew();
            Program.BuildAvaloniaApp().SetupWithoutStarting();
            startup.Stop();

            // 渲染阶段计时，与启动阶段分开报，出问题时能看出瓶颈在哪一段。
            var render = Stopwatch.StartNew();
            var pixelSize = RenderFrame(outputPath, out var report);
            render.Stop();

            var skia = ProbeSkia();

            Console.WriteLine("自检概况（首次运行的完整耗时）");
            Console.WriteLine($"  平台就绪          {startup.Elapsed.TotalMilliseconds,8:0.0} ms");
            Console.WriteLine($"  首帧渲染          {render.Elapsed.TotalMilliseconds,8:0.0} ms");
            Console.WriteLine($"  合计              {startup.Elapsed.TotalMilliseconds + render.Elapsed.TotalMilliseconds,8:0.0} ms");
            Console.WriteLine($"  节点 / 连线       {report.Nodes,8} / {report.Edges}");
            Console.WriteLine($"  布局约束          {report.Constraints,8}");
            Console.WriteLine($"  绘制指令          {report.Commands,8}");
            Console.WriteLine($"  画布消费          {report.Drawn,8}");
            Console.WriteLine($"  视口缩放          {report.Scale,8:0.00}x");
            Console.WriteLine($"  位图尺寸          {pixelSize.Width,8} x {pixelSize.Height}");
            Console.WriteLine($"  原生绘图库        {skia}");
            Console.WriteLine($"  输出文件          {outputPath}");

            var failure = Check(report);

            if (failure is not null)
            {
                Console.Error.WriteLine($"自检失败：{failure}");
                return 1;
            }

            Console.WriteLine("自检通过");

            return 0;
        }
        catch (Exception ex)
        {
            // 自检失败时必须把原因完整打出来，否则在流水线上只能看到一个非零退出码。
            Console.Error.WriteLine($"自检失败：{ex.GetType().Name} - {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    /// <summary>
    /// 核对这一帧确实把三种指令都画了，而且画布把它们全部消费掉了。
    /// </summary>
    /// <remarks>
    /// 位图本身没法在这里断言——判断"图上有没有节点"需要看图，那是人做的事。
    /// 能自动断言的是它的前提：列表里有形状、折线与文本，并且每一条都被执行了。
    /// 少了任何一条，那张位图就不可能有节点、边与标签。
    /// </remarks>
    private static string? Check(FrameReport report)
    {
        if (report.Shapes == 0)
        {
            return "绘制列表里没有形状，位图上不会出现节点";
        }

        if (report.Polylines == 0)
        {
            return "绘制列表里没有折线，位图上不会出现连线";
        }

        if (report.Texts == 0)
        {
            return "绘制列表里没有文本，位图上不会出现标签";
        }

        if (report.Constraints < RequiredConstraints)
        {
            return $"文档里只有 {report.Constraints} 条布局约束，至少要有 {RequiredConstraints} 条——"
                + "约束那条路没走通，位图上就看不到按约束排出来的样子";
        }

        if (report.Drawn != report.Commands)
        {
            return $"画布只执行了 {report.Drawn} 条指令，列表里有 {report.Commands} 条——有指令没被消费";
        }

        return null;
    }

    /// <summary>
    /// 自检这一帧要求文档里至少有这么多条约束。
    /// </summary>
    /// <remarks>
    /// 约束是这一帧要验的东西：同层那一条会把本来在下一层的节点拉上来，
    /// 对齐那一条会把并排的两个节点摆到同一列上。两条都不在时画面仍然能画出来，
    /// 退出码也仍然是 0——于是这个自检就变成了一句"能出图"，而约束那段没验到。
    /// </remarks>
    private const int RequiredConstraints = 2;

    /// <summary>
    /// 把画布脱屏渲染成一帧位图并保存。
    /// </summary>
    /// <remarks>
    /// 控件没有挂到窗口上，所以要手工走一遍测量与排布，否则它的尺寸是零、什么都画不出来。
    /// 顺序必须是先测量再排布再渲染，跳过任何一步都会得到一张空白图而不是报错，
    /// 这种"静默产出错误结果"的行为是脱屏渲染最容易踩的坑。
    /// </remarks>
    private static PixelSize RenderFrame(string outputPath, out FrameReport report)
    {
        // 会话而不是"文档走一遍链路"：约束要经命令层写进去。直接往文档里塞约束的话，
        // 这一帧验的只是"引擎认不认约束"，而约束是怎么进文档的那条路
        // （校验、版本、哈希、重排）一段都没走到。
        using var session = new DiagramSession(SampleDiagram.Document());

        ApplyConstraints(session);

        var scene = session.Scene;
        var model = new CanvasViewModel();

        model.Load(scene.DrawList);

        var canvas = new DiagramCanvas { DataContext = model };
        var size = new Size(DefaultWidth, DefaultHeight);
        var pixelSize = new PixelSize(DefaultWidth, DefaultHeight);

        canvas.Measure(size);
        canvas.Arrange(new Rect(size));

        using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        bitmap.Render(canvas);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // 写流并显式指定编码格式。不带编码参数的那条重载已标记为过时，
        // 靠扩展名推断格式既不可控，也无法在将来支持导出 PDF 之类的目标时复用同一条路径。
        // 注意 Avalonia 12 把编码选项从"质量整数"改成了独立的选项类型。
        using var stream = File.Create(outputPath);
        bitmap.Save(stream, new PngBitmapEncoderOptions());

        var commands = scene.DrawList.Commands;
        var layout = scene.Document.Layout;

        report = new FrameReport(
            scene.Document.Nodes.Count,
            scene.Document.Edges.Count,
            layout.SameRank.Count + layout.Order.Count + layout.Align.Count + layout.Place.Count,
            commands.Count,
            canvas.DrawnCommands,
            model.Viewport.Scale,
            commands.OfType<DrawShape>().Count(),
            commands.OfType<DrawPolyline>().Count(),
            commands.OfType<DrawText>().Count());

        return pixelSize;
    }

    /// <summary>
    /// 往示例文档上加两条约束，两条都走命令层。
    /// </summary>
    /// <remarks>
    /// 选这两条是因为它们都会在画面上留下看得见的差别：同层那一条把本来在下一层的
    /// 结束节点拉到与"通过"同一层，对齐那一条把"通过"摆到"校验"那一列上。
    /// 换成两条本来就成立的约束（例如让并排的两个节点同层）也能通过，但画面上看不出任何变化，
    /// 于是"约束生效了没有"这件事就退化成一句空话。
    /// </remarks>
    private static void ApplyConstraints(DiagramSession session)
    {
        session.SetSelection(["pass", "end"]);
        session.AddConstraint(LayoutConstraintSpec.SameRank(["pass", "end"]));

        session.SetSelection(["check", "pass"]);
        session.AddConstraint(LayoutConstraintSpec.Align(["check", "pass"]));
    }

    /// <summary>
    /// 直接调用原生绘图库，确认它能加载并且能测量文本。
    /// </summary>
    /// <remarks>
    /// 返回一行可读的描述而不是布尔值：字体名与测得的宽度都是证据，
    /// 出问题时能立刻分辨是"库没加载"还是"加载了但字体缺失"。
    /// 静默回退到默认字体也会在这里显示出来，而那种情况会让排版结果与预期不符且很难归因。
    /// </remarks>
    private static string ProbeSkia()
    {
        var version = typeof(SKSurface).Assembly.GetName().Version?.ToString() ?? "unknown";

        using var typeface = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
        using var font = new SKFont(typeface, 14);

        // 中英文混排：既验证拉丁字形的测量，也验证非拉丁字形不会退化成零宽。
        const string Sample = "校验 check";
        var width = font.MeasureText(Sample);

        return $"可加载（版本 {version}，字体 {typeface.FamilyName}，\"{Sample}\" 宽度 {width:0.0}）";
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>一帧渲染之后的读数。</summary>
    /// <param name="Nodes">文档里的节点数。</param>
    /// <param name="Edges">文档里的连线数。</param>
    /// <param name="Constraints">文档里的布局约束数。</param>
    /// <param name="Commands">绘制列表里的指令数。</param>
    /// <param name="Drawn">画布实际执行掉的指令数。</param>
    /// <param name="Scale">这一帧用的缩放倍数。</param>
    /// <param name="Shapes">形状指令数。</param>
    /// <param name="Polylines">折线指令数。</param>
    /// <param name="Texts">文本指令数。</param>
    private sealed record FrameReport(
        int Nodes,
        int Edges,
        int Constraints,
        int Commands,
        int Drawn,
        double Scale,
        int Shapes,
        int Polylines,
        int Texts);
}
