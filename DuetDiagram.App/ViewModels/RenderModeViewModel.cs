using System.ComponentModel;
using Avalonia.Threading;
using DuetDiagram.Render;

namespace DuetDiagram.App.ViewModels;

/// <summary>
/// 渲染模式那一格显示什么。
/// </summary>
/// <remarks>
/// <para>
/// 单独一个对象而不是往画布的状态里再塞几个属性：状态栏与将来的诊断面板都读它，
/// 而它们关心的只是"现在走哪一档、剔掉了多少"，与视口、绘制列表无关。
/// </para>
/// <para>
/// 剔除比例是**数据**，不是给状态栏看的。状态栏只显示模式名——
/// 比例每帧都在变，把它放状态栏等于每帧重排一次那一行文字。
/// </para>
/// <para>
/// **刷新是从渲染过程里调过来的**（换档的判定就发生在那一帧开始的时候），
/// 所以这里不能当场通知——理由写在 <see cref="Update"/> 上。
/// </para>
/// </remarks>
public sealed class RenderModeViewModel : INotifyPropertyChanged
{
    private RenderMode _mode = RenderMode.Immediate;
    private bool _switching;
    private double _cullRate;
    private string _notified = Name(RenderMode.Immediate);

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>这一帧实际在用的模式。</summary>
    public RenderMode Mode => _mode;

    /// <summary>切换在路上。界面据此可以显示一个过渡状态。</summary>
    public bool IsSwitching => _switching;

    /// <summary>最近一帧剔掉的指令比例，取值 0 到 1。</summary>
    public double CullRate => _cullRate;

    /// <summary>状态栏上显示的那一格。</summary>
    public string Text => _switching
        ? $"{Name(_mode)}（切换中）"
        : Name(_mode);

    /// <summary>
    /// 一帧开始时刷新。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **通知要等这一帧画完再发。** 调用发生在渲染过程里，而这一格绑在状态栏上：
    /// 当场通知会让状态栏在渲染过程中被作废，界面框架直接抛
    /// "渲染过程中作废了视觉对象"。把通知排到队列里，等当前这一帧结束再执行。
    /// </para>
    /// <para>
    /// 排队之前先比一次：模式名几乎不变，不该每帧都往队列里塞一条。
    /// 通知只是"重新读一次"的信号，读到的总是当时的值，所以偶尔多发一条也不会读错。
    /// </para>
    /// </remarks>
    internal void Update(RenderMode mode, bool switching, double cullRate)
    {
        _mode = mode;
        _switching = switching;
        _cullRate = cullRate;

        if (_notified == Text)
        {
            return;
        }

        _notified = Text;

        Dispatcher.UIThread.Post(
            () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text))),
            DispatcherPriority.Background);
    }

    private static string Name(RenderMode mode) => mode == RenderMode.Virtualized ? "虚拟化" : "即时";
}
