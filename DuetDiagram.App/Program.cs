using Avalonia;

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
                args.Contains(DiagnosticsSwitch, StringComparer.Ordinal));
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    /// <summary>进入帧率测量模式的开关。</summary>
    public const string FrameBenchmarkSwitch = "--benchmark-frames";

    /// <summary>帧率测量时顺带把诊断面板开一遍，跟关着的时候比。</summary>
    public const string DiagnosticsSwitch = "--diagnostics";

    private static int ReadInt(string[] args, string name, int fallback)
    {
        var index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value)
            ? value
            : fallback;
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
