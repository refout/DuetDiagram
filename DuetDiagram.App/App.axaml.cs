using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace DuetDiagram.App;

/// <summary>
/// 应用入口。只负责装载主题与创建主窗口，不放任何业务逻辑。
/// </summary>
public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // 自检模式不经过这里：它用独立的启动路径渲染一帧后直接退出，
        // 不创建窗口，因此也不受这个生命周期分支影响。
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
