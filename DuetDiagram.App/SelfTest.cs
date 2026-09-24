using System.Buffers.Binary;
using System.Diagnostics;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media.Imaging;
using DuetDiagram.App.Controls;
using DuetDiagram.App.Services;
using DuetDiagram.App.ViewModels;
using DuetDiagram.Render;
using SkiaSharp;
using ArrowStyle = DuetDiagram.Core.Model.ArrowStyle;
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
            var report = RenderFrame(outputPath);
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
            Console.WriteLine($"  SVG 形状/折线/文字 {report.Svg.Shapes,7} / {report.Svg.Polylines} / {report.Svg.Texts}");
            Console.WriteLine($"  视口缩放          {report.Scale,8:0.00}x");
            Console.WriteLine($"  位图尺寸          {report.Bitmap.Width,8} x {report.Bitmap.Height}");
            Console.WriteLine($"  原生绘图库        {skia}");
            Console.WriteLine($"  输出文件          {report.Bitmap.Path}");
            Console.WriteLine($"  SVG 文件          {report.Svg.Path}");

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

        // 逐类核对导出：同一份绘制列表，画布画一遍、导出器写一遍，两边每一类都得对得上。
        // 只核"导出没报错"是不够的——导出另走一条绘制路径时，它自己仍然自洽，
        // 只是画出来的东西与屏幕上不一样，而那种不一样要等人去看图才发现。
        if (report.Svg.Shapes != report.Shapes)
        {
            return $"SVG 里有 {report.Svg.Shapes} 个形状元素，绘制列表里有 {report.Shapes} 条形状指令——"
                + "导出与画布不是同一条绘制路径";
        }

        if (report.Svg.Polylines != report.Polylines)
        {
            return $"SVG 里有 {report.Svg.Polylines} 条折线，绘制列表里有 {report.Polylines} 条——"
                + "导出与画布不是同一条绘制路径";
        }

        if (report.Svg.Texts != report.Texts)
        {
            return $"SVG 里有 {report.Svg.Texts} 段文字，绘制列表里有 {report.Texts} 段——"
                + "导出与画布不是同一条绘制路径";
        }

        // 位图那一边数不出元素，能自动断言的只有"它真是一张 PNG，而且尺寸与导出器说的一致"。
        // 少了这一步，导出器写出一个空文件、或者尺寸算错，退出码仍然是 0。
        return CheckBitmap(report.Bitmap);
    }

    /// <summary>
    /// 核对导出的位图：文件头是 PNG，且里面写的尺寸与导出器报的一致。
    /// </summary>
    /// <remarks>
    /// 尺寸对不上说明留白或缩放的算法与它自己报出来的读数分了岔——那种错不会让文件损坏，
    /// 只会让导出的图比预期大一点或小一点，而看的人多半以为是自己选错了选项。
    /// </remarks>
    private static string? CheckBitmap(BitmapReadings bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return $"导出的位图尺寸是 {bitmap.Width}×{bitmap.Height}";
        }

        // PNG 头：8 字节签名，4 字节块长度，4 字节 "IHDR"，然后宽高各 4 字节（大端）。
        var header = new byte[24];

        using (var stream = File.OpenRead(bitmap.Path))
        {
            if (stream.Read(header, 0, header.Length) != header.Length)
            {
                return $"{bitmap.Path} 短得不像一张 PNG——导出那条路没走通";
            }
        }

        if (!header.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            return $"{bitmap.Path} 不是一张 PNG——导出那条路没走通";
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16));
        var height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20));

        return width == bitmap.Width && height == bitmap.Height
            ? null
            : $"导出器报的是 {bitmap.Width}×{bitmap.Height}，文件里写的是 {width}×{height}——尺寸算了两遍，两遍不一样";
    }

    /// <summary>PNG 文件头的八个字节。</summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

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
    /// 把画布脱屏渲染一遍，并把同一份绘制列表导成位图存盘。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **控件没有挂到窗口上，所以要手工走一遍测量与排布**，否则它的尺寸是零、什么都画不出来。
    /// 顺序必须是先测量再排布再渲染，跳过任何一步都会得到一张空白图而不是报错，
    /// 这种"静默产出错误结果"的行为是脱屏渲染最容易踩的坑。
    /// </para>
    /// <para>
    /// **存盘的那一份来自导出器，不是这块离屏位图。** 自检原来自己写一份存位图的代码，
    /// 而那条路只在自检里跑过；改走导出器之后，流水线上那份证据图验的就是真导出路径。
    /// 离屏渲染这一步留着，是因为"画布把指令消费完了没有"只有它答得了——
    /// 导出器那边数不出元素，位图里也数不出来。
    /// </para>
    /// </remarks>
    private static FrameReport RenderFrame(string outputPath)
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

        canvas.Measure(size);
        canvas.Arrange(new Rect(size));

        using var frame = new RenderTargetBitmap(new PixelSize(DefaultWidth, DefaultHeight), new Vector(96, 96));

        frame.Render(canvas);

        var commands = scene.DrawList.Commands;
        var layout = scene.Document.Layout;

        // 两份产物写在同一个目录下，所以目录只在这里建一次：交给两边各自去建的话，
        // 先跑的那一边会在目录不存在时直接失败。
        var directory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var svg = ExportSvg(scene, outputPath);
        var bitmap = ExportBitmap(scene, outputPath);

        return new FrameReport(
            scene.Document.Nodes.Count,
            scene.Document.Edges.Count,
            layout.SameRank.Count + layout.Order.Count + layout.Align.Count + layout.Place.Count,
            commands.Count,
            canvas.DrawnCommands,
            model.Viewport.Scale,
            commands.OfType<DrawShape>().Count(),
            // 折线与文字按"画得出来的"数：不足两点的折线与空文字画不出任何东西，
            // 导出器也照样跳过它们，两边要数的得是同一件事。
            commands.OfType<DrawPolyline>().Count(line => line.Points.Count >= 2),
            commands.OfType<DrawText>().Count(text => text.Text.Length > 0),
            svg,
            bitmap);
    }

    /// <summary>
    /// 把这一帧的绘制列表导成位图并存盘。
    /// </summary>
    /// <remarks>
    /// 选项取默认那一组：不透明底、按内容外接框裁、一倍。透明底那一档在这里不能用——
    /// 证据图要贴进报告里，透明底在报告的白底上会显示成黑底。
    /// </remarks>
    private static BitmapReadings ExportBitmap(SampleScene scene, string outputPath)
    {
        var export = BitmapExporter.Export(scene.DrawList);

        File.WriteAllBytes(outputPath, export.Png);

        return new BitmapReadings(export.Width, export.Height, outputPath);
    }

    /// <summary>
    /// 把这一帧的绘制列表导成 SVG，并逐类数一遍它写出了什么。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **数的这一份把箭头摘掉了。** 带箭头时折线那一类里混着箭头标记，数不准；
    /// 摘掉之后三类指令与元素是 1:1，逐类对得上这件事才是可判的。
    /// 箭头那一档由渲染层的用例逐种核过。
    /// </para>
    /// <para>
    /// **写出去的那一份是原样的**：带背景、带箭头，与画布上看到的同一份。
    /// 两件事要的输入不同，所以导两次而不是取其一——省掉哪一次都会让另一件事失去意义。
    /// </para>
    /// </remarks>
    private static SvgReadings ExportSvg(SampleScene scene, string outputPath)
    {
        var svgPath = Path.ChangeExtension(outputPath, ".svg");
        File.WriteAllText(svgPath, SvgExporter.Export(scene.DrawList).Svg);

        var bare = scene.DrawList with
        {
            Commands =
            [
                .. scene.DrawList.Commands.Select(command =>
                    command is DrawPolyline line ? line with { Arrow = ArrowStyle.None } : command),
            ],
        };

        var counted = XElement.Parse(
            SvgExporter.Export(bare, new SvgOptions { IncludeBackground = false }).Svg);

        return new SvgReadings(
            counted.Elements().Count(element => IsShape(element.Name.LocalName)),
            counted.Elements().Count(element => element.Name.LocalName == "polyline"),
            counted.Elements().Count(element => element.Name.LocalName == "text"),
            svgPath);
    }

    /// <summary>画出来是一个轮廓的那几种元素。</summary>
    private static bool IsShape(string name) => name is "rect" or "ellipse" or "polygon" or "path";

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
    /// <param name="Svg">同一份列表导出之后各类元素的读数。</param>
    /// <param name="Bitmap">同一份列表导出的位图的读数。</param>
    private sealed record FrameReport(
        int Nodes,
        int Edges,
        int Constraints,
        int Commands,
        int Drawn,
        double Scale,
        int Shapes,
        int Polylines,
        int Texts,
        SvgReadings Svg,
        BitmapReadings Bitmap);

    /// <summary>导出之后各类元素的读数。</summary>
    /// <param name="Shapes">形状元素数。</param>
    /// <param name="Polylines">折线元素数。</param>
    /// <param name="Texts">文字元素数。</param>
    /// <param name="Path">写出去的那份 SVG 在哪。</param>
    private sealed record SvgReadings(int Shapes, int Polylines, int Texts, string Path);

    /// <summary>导出的位图的读数。</summary>
    /// <param name="Width">位图宽度，像素。</param>
    /// <param name="Height">位图高度，像素。</param>
    /// <param name="Path">写出去的那份位图在哪。</param>
    private sealed record BitmapReadings(int Width, int Height, string Path);
}
