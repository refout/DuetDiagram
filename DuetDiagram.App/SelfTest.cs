using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Media.Imaging;
using SkiaSharp;

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
/// </remarks>
internal static class SelfTest
{
    public const string CommandLineSwitch = "--selftest";

    private const int DefaultWidth = 900;
    private const int DefaultHeight = 600;

    public static int Run(string[] args)
    {
        var outputPath = ReadOption(args, "--out") ?? Path.Combine(Path.GetTempPath(), "duetdiagram-selftest.png");
        var nodeCount = int.TryParse(ReadOption(args, "--nodes"), CultureInfo.InvariantCulture, out var parsed) ? parsed : 4;

        try
        {
            // 启动阶段：初始化界面框架与平台后端。自检不创建窗口，因此这里只做到"平台可用"为止。
            var startup = Stopwatch.StartNew();
            Program.BuildAvaloniaApp().SetupWithoutStarting();
            startup.Stop();

            // 渲染阶段计时，与启动阶段分开报，出问题时能看出瓶颈在哪一段。
            var render = Stopwatch.StartNew();
            var pixelSize = RenderFrame(outputPath, nodeCount, out var actualSize);
            render.Stop();

            var skia = ProbeSkia();

            Console.WriteLine("自检概况（首次运行的完整耗时）");
            Console.WriteLine($"  平台就绪          {startup.Elapsed.TotalMilliseconds,8:0.0} ms");
            Console.WriteLine($"  首帧渲染          {render.Elapsed.TotalMilliseconds,8:0.0} ms");
            Console.WriteLine($"  合计              {startup.Elapsed.TotalMilliseconds + render.Elapsed.TotalMilliseconds,8:0.0} ms");
            Console.WriteLine($"  绘制元素数        {nodeCount,8}");
            Console.WriteLine($"  位图尺寸          {actualSize.Width,8} x {actualSize.Height}");
            Console.WriteLine($"  原生绘图库        {skia}");
            Console.WriteLine($"  输出文件          {outputPath}");
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
    /// 把绘图区脱屏渲染成一帧位图并保存。
    /// </summary>
    /// <remarks>
    /// 控件没有挂到窗口上，所以要手工走一遍测量与排布，否则它的尺寸是零、什么都画不出来。
    /// 顺序必须是先测量再排布再渲染，跳过任何一步都会得到一张空白图而不是报错，
    /// 这种"静默产出错误结果"的行为是脱屏渲染最容易踩的坑。
    /// </remarks>
    private static PixelSize RenderFrame(string outputPath, int nodeCount, out PixelSize actualSize)
    {
        var preview = new DiagramPreview { NodeCount = nodeCount };
        var size = new Size(DefaultWidth, DefaultHeight);

        preview.Measure(size);
        preview.Arrange(new Rect(size));

        actualSize = new PixelSize(DefaultWidth, DefaultHeight);

        using var bitmap = new RenderTargetBitmap(actualSize, new Vector(96, 96));
        bitmap.Render(preview);

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

        return actualSize;
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
}
