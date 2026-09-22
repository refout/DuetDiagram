using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DuetDiagram.App.ViewModels;
using DuetDiagram.Render;

namespace DuetDiagram.App;

/// <summary>
/// 主窗口。左边是画布，底下一行状态栏。
/// </summary>
/// <remarks>
/// 这一轮只到"把图显示出来并且能缩放平移"为止。菜单栏、工具栏、图层面板与属性面板
/// 都还没有，它们各自依赖的东西（命令层接线、属性绑定）还没做。
/// </remarks>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Model = new CanvasViewModel();

        // 度量器用完就放：它按字体家族缓存字体对象，而字体对象持有原生资源。
        using (var measurer = new SkiaTextMeasurer())
        {
            Model.Load(SampleDiagram.Build(Model.Theme, measurer).DrawList);
        }

        DataContext = Model;
    }

    /// <summary>画布与状态栏共用的状态。</summary>
    public CanvasViewModel Model { get; }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
