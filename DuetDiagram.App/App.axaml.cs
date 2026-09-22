using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DuetDiagram.App.Services;

namespace DuetDiagram.App;

/// <summary>
/// 应用入口。只负责装载主题与创建主窗口，不放任何业务逻辑。
/// </summary>
public sealed class App : Application
{
    /// <summary>
    /// 这次启动要开哪份文档。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 由命令行入口在起界面之前摆好。摆在这里而不是让窗口自己去解析命令行，
    /// 是因为"文件读不出来"要在起界面之前就变成一句错误与一个非零退出码——
    /// 界面起来之后才发现开不了，用户看到的是一个窗口，而错误只写在控制台上，
    /// 那种窗口看起来像已经打开了文档，只是里面什么都没有。
    /// </para>
    /// <para>
    /// 没摆时开一份示例文档，这也是测试与自检走的路径。
    /// </para>
    /// </remarks>
    public static DocumentLaunch? Startup { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // 自检模式不经过这里：它用独立的启动路径渲染一帧后直接退出，
        // 不创建窗口，因此也不受这个生命周期分支影响。
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = Startup is { } startup ? new MainWindow(startup) : new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
