using Avalonia;
using DuetDiagram.App.Services;

namespace DuetDiagram.App;

/// <summary>
/// 程序入口。默认启动图形界面；带自检开关时改为渲染一帧后退出。
/// </summary>
/// <remarks>
/// 提供自检入口是因为"界面能启动"这件事无法在无人值守的环境里直接断言：
/// 打开窗口会一直等用户操作。自检把同一条渲染链路跑一遍后立即退出并用退出码表达结果，
/// 这样就可以放进持续集成，也能用来测量启动到出图这一段耗时。
/// </remarks>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains(SelfTest.CommandLineSwitch, StringComparer.Ordinal))
        {
            return SelfTest.Run(args);
        }

        if (args.Contains(FrameBenchmarkSwitch, StringComparer.Ordinal))
        {
            return FrameBenchmark.Run(
                ReadInt(args, "--nodes", 1000),
                ReadInt(args, "--frames", 60),
                args.Contains(DiagnosticsSwitch, StringComparer.Ordinal),
                args.Contains(HighlightSwitch, StringComparer.Ordinal));
        }

        if (args.Contains(PropertyPanelBenchmarkSwitch, StringComparer.Ordinal))
        {
            return FrameBenchmark.RunPanel(
                ReadInt(args, "--nodes", 200),
                ReadInt(args, "--switches", 200));
        }

        if (args.Contains(DragBenchmarkSwitch, StringComparer.Ordinal))
        {
            return FrameBenchmark.RunDrag(
                ReadInt(args, "--nodes", 1000),
                ReadInt(args, "--samples", 200));
        }

        if (ReadOption(args, OpenSwitch) is { } path)
        {
            // 文档在起界面之前读进来。读不出来就在这一步收场——界面起来之后才发现开不了，
            // 用户看到的是一个空窗口，而错误只写在控制台上。
            try
            {
                App.Startup = DocumentLaunch.File(path);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException
                or UnauthorizedAccessException or ArgumentException)
            {
                Console.Error.WriteLine($"打不开 {path}：{exception.Message}");

                return OpenFailedExitCode;
            }
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    /// <summary>从命令行打开一份文档的开关。</summary>
    public const string OpenSwitch = "--open";

    /// <summary>文档打不开时的退出码。</summary>
    /// <remarks>
    /// 与"跑通了但结果不对"分开：自检那几条路径用它自己的退出码表达结论，
    /// 而这里说的是"连窗口都没起来"，两种情况混用一个码之后，
    /// 脚本分不出该去看日志还是该去看界面。
    /// </remarks>
    public const int OpenFailedExitCode = 2;

    /// <summary>进入帧率测量模式的开关。</summary>
    public const string FrameBenchmarkSwitch = "--benchmark-frames";

    /// <summary>帧率测量时顺带把诊断面板开一遍，跟关着的时候比。</summary>
    public const string DiagnosticsSwitch = "--diagnostics";

    /// <summary>帧率测量时把变更高亮全开，量高亮带来的开销。</summary>
    public const string HighlightSwitch = "--highlight";

    /// <summary>进入属性面板切换测量的开关。</summary>
    public const string PropertyPanelBenchmarkSwitch = "--benchmark-panel";

    /// <summary>进入拖拽单帧输入处理测量的开关。</summary>
    public const string DragBenchmarkSwitch = "--benchmark-drag";

    private static int ReadInt(string[] args, string name, int fallback)
    {
        var index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value)
            ? value
            : fallback;
    }

    /// <summary>取一个"开关后面跟一个值"的选项。没给或后面没值时给空。</summary>
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

    /// <summary>
    /// 组装应用。自检路径也复用这个方法，保证两条路径用的是同一套配置。
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
