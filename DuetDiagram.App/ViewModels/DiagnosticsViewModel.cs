using System.ComponentModel;
using System.Globalization;
using System.Text;
using Avalonia.Threading;
using DuetDiagram.Layout;
using DuetDiagram.Render;

namespace DuetDiagram.App.ViewModels;

/// <summary>
/// 性能诊断面板显示什么，以及它开不开。
/// </summary>
/// <remarks>
/// <para>
/// **面板只读。** 它不做任何"自动优化"，也不改渲染档位——诊断工具改了状态之后，
/// 看到的数字就不再是用户实际遇到的那一份了。
/// </para>
/// <para>
/// **关着的时候不做任何事。** 采样点挂在渲染回调里，每帧都会被调一次，
/// 所以关掉之后除了那一次判断之外不能有任何开销。
/// </para>
/// <para>
/// 文字按帧数节流，不是每帧都重算。每帧都换一次文字等于每帧让面板重排一次，
/// 而面板自己就成了开销——它正是用来查开销的。按帧数而不是按时间节流：
/// 面板报的就是帧，按帧数节流在低帧率下也一定会动，按时间则可能一帧都不刷。
/// </para>
/// <para>
/// 面板上只有**一个**文本控件，整块文字一次换掉。拆成一行一个控件的话，
/// 每次刷新要动的控件数与行数一样多，而这一块的每一分开销都记在它自己头上。
/// </para>
/// </remarks>
public sealed class DiagnosticsViewModel : INotifyPropertyChanged
{
    /// <summary>每多少帧重算一次面板上的字。</summary>
    private const int RefreshIntervalFrames = 8;

    /// <summary>标签占多少列。等宽字体下对齐靠它。</summary>
    private const int LabelColumns = 10;

    private readonly DiagnosticsSampler _sampler;

    private bool _isOpen;
    private int _sinceRefresh;
    private LayoutFallbackLevel? _level;
    private int _attempts;

    public DiagnosticsViewModel(DiagnosticsSampler? sampler = null) =>
        _sampler = sampler ?? new DiagnosticsSampler();

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>采样开着没有。渲染回调据此决定要不要取时钟。</summary>
    public bool Enabled => _isOpen;

    /// <summary>面板开着没有。</summary>
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (_isOpen == value)
            {
                return;
            }

            _isOpen = value;
            _sampler.Enabled = value;

            // 打开时立刻刷一次：不刷的话面板先显示上一次的读数，
            // 而那可能是几分钟前的事，看起来像"打开之后数字没变过"。
            _sinceRefresh = 0;
            Raise(nameof(IsOpen));
            Raise(nameof(Text));
        }
    }

    /// <summary>面板上的字。</summary>
    public string Text => Build();

    /// <summary>开关面板。快捷键与面板上的关闭按钮走的是同一个入口。</summary>
    public void Toggle() => IsOpen = !IsOpen;

    /// <summary>
    /// 记一帧。面板关着时立刻返回。
    /// </summary>
    /// <remarks>
    /// 刷新排到队列里而不是当场做：这个调用发生在渲染过程内部，
    /// 当场改绑在界面上的属性会让界面框架抛"渲染过程中作废了视觉对象"。
    /// </remarks>
    public void Record(in DiagnosticsFrame frame)
    {
        if (!_isOpen)
        {
            return;
        }

        _sampler.Record(frame);

        if (++_sinceRefresh < RefreshIntervalFrames)
        {
            return;
        }

        _sinceRefresh = 0;

        Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Background);
    }

    /// <summary>记一段重活的耗时。</summary>
    public void RecordStage(DiagnosticsStage stage, double milliseconds)
    {
        _sampler.RecordStage(stage, milliseconds);

        // 面板关着就不用刷了。重活的记录照留——打开时它会显示出来，
        // 而那恰恰是"打开面板之前就已经发生过"的那一段。
        if (_isOpen)
        {
            Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// 记一次布局，连同它落在哪一级。
    /// </summary>
    /// <remarks>
    /// 级别显示的是枚举名而不是中文。排查"为什么这次布局慢"时，
    /// 第一件要确认的就是它有没有降级，而级别名在各处文档与日志里用的就是这几个词——
    /// 面板另起一套译名，两边就对不上了。
    /// </remarks>
    public void RecordLayout(double milliseconds, LayoutFallbackLevel level, int attempts)
    {
        _level = level;
        _attempts = attempts;

        RecordStage(DiagnosticsStage.Layout, milliseconds);
    }

    /// <summary>
    /// 重算面板上的字。
    /// </summary>
    /// <remarks>
    /// 它由队列调起，不在渲染过程里——<see cref="Record"/> 是从渲染回调里被调的，
    /// 而通知会让绑在面板上的那个文本控件当场作废，界面框架不许在渲染过程中做这件事。
    /// </remarks>
    public void Refresh() => Raise(nameof(Text));

    /// <summary>
    /// 换了一份文档：窗口清空。
    /// </summary>
    /// <remarks>
    /// 旧文档的帧时对新文档没有意义。重活的记录留着——那正是刚量出来的这一次。
    /// </remarks>
    public void Reload()
    {
        _sampler.Clear();
        _sinceRefresh = 0;
        Raise(nameof(Text));
    }

    private string Build()
    {
        var summary = _sampler.Summary();
        var text = new StringBuilder();

        text.Append("性能诊断（Ctrl+Shift+P 开关）").Append('\n');

        if (summary.FrameCount == 0)
        {
            text.Append("还没有样本");

            return text.ToString();
        }

        text.Append("窗口 ").Append(summary.FrameCount).Append(" / ").Append(_sampler.Capacity)
            .Append(" 帧，单位毫秒").Append('\n');

        AppendRow(text, "整帧", summary.Total);
        AppendRow(text, "剔除", summary.Cull);
        AppendRow(text, "光栅化", summary.Raster);

        var latest = summary.Latest;
        var total = latest.Drawn + latest.Culled;

        text.Append(Label("指令")).Append("画 ").Append(latest.Drawn)
            .Append("  剔 ").Append(latest.Culled)
            .Append("  剔除率 ").Append(Rate(total == 0 ? 0 : (double)latest.Culled / total))
            .Append('\n');

        text.Append(Label("档位"))
            .Append(latest.Mode == RenderMode.Virtualized ? "虚拟化" : "即时")
            .Append('\n');

        AppendStages(text);

        return text.ToString();
    }

    private void AppendStages(StringBuilder text)
    {
        if (!_sampler.HasStage(DiagnosticsStage.Layout)
            && !_sampler.HasStage(DiagnosticsStage.DrawList)
            && !_sampler.HasStage(DiagnosticsStage.Hash))
        {
            return;
        }

        text.Append('\n').Append("重活（最近一次）").Append('\n');

        if (_sampler.Stage(DiagnosticsStage.Layout) is { } layout)
        {
            text.Append(Label("布局")).Append(Milliseconds(layout));

            if (_level is { } level)
            {
                text.Append("  级别 ").Append(level.ToString())
                    .Append("  尝试 ").Append(_attempts).Append(" 次");
            }

            text.Append('\n');
        }

        AppendStage(text, "绘制列表", DiagnosticsStage.DrawList);
        AppendStage(text, "哈希", DiagnosticsStage.Hash);
    }

    private void AppendStage(StringBuilder text, string label, DiagnosticsStage stage)
    {
        if (_sampler.Stage(stage) is { } value)
        {
            text.Append(Label(label)).Append(Milliseconds(value)).Append('\n');
        }
    }

    private static void AppendRow(StringBuilder text, string label, DiagnosticsStatistics statistics)
    {
        text.Append(Label(label))
            .Append("中位 ").Append(Number(statistics.Median))
            .Append("  95 分位 ").Append(Number(statistics.P95))
            .Append("  最大 ").Append(Number(statistics.Max))
            .Append('\n');
    }

    /// <summary>
    /// 把标签补齐到固定列数。
    /// </summary>
    /// <remarks>
    /// 一个中日韩字符在等宽字体下占两列，按字符个数补空格的话，
    /// 两个字的标签与四个字的标签会差出两列，整张表就歪了。
    /// </remarks>
    private static string Label(string text)
    {
        var columns = 0;

        foreach (var character in text)
        {
            columns += character > 0x7f ? 2 : 1;
        }

        return text.PadRight(text.Length + Math.Max(0, LabelColumns - columns));
    }

    private static string Milliseconds(double value) =>
        Number(value) + " ms";

    private static string Number(double value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>百分比。不用百分号格式串：不变文化会在数字与百分号之间插一个空格。</summary>
    private static string Rate(double value) =>
        (value * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
