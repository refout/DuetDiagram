using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using DuetDiagram.App.ViewModels;
using DuetDiagram.Render;

namespace DuetDiagram.App;

/// <summary>
/// 主窗口。画布铺满，底下一行状态栏，右上角浮着性能诊断面板。
/// </summary>
/// <remarks>
/// 这一轮只到"把图显示出来、能缩放平移、能看诊断数字"为止。菜单栏、工具栏、
/// 图层面板与属性面板都还没有，它们各自依赖的东西（命令层接线、属性绑定）还没做。
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
            var scene = SampleDiagram.Build(Model.Theme, measurer);

            Model.Diagnostics.RecordLayout(
                scene.LayoutMilliseconds,
                scene.Result.AppliedLevel,
                scene.Result.Attempts.Count);
            Model.Diagnostics.RecordStage(DiagnosticsStage.DrawList, scene.DrawListMilliseconds);

            Model.Load(scene.DrawList);
        }

        DataContext = Model;
    }

    /// <summary>画布、状态栏与诊断面板共用的状态。</summary>
    public CanvasViewModel Model { get; }

    /// <summary>
    /// 诊断面板的开关。
    /// </summary>
    /// <remarks>
    /// 键盘消息只发给有焦点的控件，而画布在指针按下时会把焦点收过去。
    /// 所以这里不能假设焦点在窗口上——好在按键事件是从焦点控件往上冒泡的，
    /// 画布不认 P 键，它就冒到这儿了。
    /// </remarks>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || e.Key != Key.P || !e.KeyModifiers.HasFlag(KeyModifiers.Control)
            || !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            return;
        }

        Model.Diagnostics.Toggle();
        e.Handled = true;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
