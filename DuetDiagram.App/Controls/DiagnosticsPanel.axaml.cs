using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DuetDiagram.App.ViewModels;

namespace DuetDiagram.App.Controls;

/// <summary>
/// 性能诊断面板。浮在画布右上角，只读。
/// </summary>
/// <remarks>
/// <para>
/// 控件本身不做任何计算，只把视图模型给的整块文字显示出来。
/// 数字怎么算、多久刷一次都在视图模型里，那样测试不用起窗口也能验它。
/// </para>
/// <para>
/// 关闭按钮与快捷键走的是同一个入口（<see cref="DiagnosticsViewModel.Toggle"/>）。
/// 各写一套开关逻辑的话，两处迟早会不一致——而表现是"快捷键关了但按钮还显示着"，
/// 看不出是哪一处错了。
/// </para>
/// </remarks>
public sealed partial class DiagnosticsPanel : UserControl
{
    public DiagnosticsPanel() => InitializeComponent();

    private void OnClose(object? sender, RoutedEventArgs e) =>
        (DataContext as CanvasViewModel)?.Diagnostics.Toggle();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
