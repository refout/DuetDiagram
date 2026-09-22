using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Logging;

namespace DuetDiagram.App.Controls;

/// <summary>
/// 右下角的变更边栏：默认折叠只显示摘要，点开看逐字段明细。
/// </summary>
/// <remarks>
/// <para>
/// **明细来自版本日志，不在界面侧另算一份。** 界面自己比较前后两份文档的话，
/// 它会与版本日志对"这次改了什么"给出两个说法，而两者迟早会分叉——
/// 分叉之后用户看到的那一份未必是审计里记的那一份。
/// </para>
/// <para>
/// 它只显示**最近一次**变更。看历史是另一件事，那要一个能翻页的列表，
/// 而这里的用途是"刚才那一下改了什么"，给一屏历史反而要点开才知道最新那条。
/// </para>
/// </remarks>
public sealed partial class DiffSidebar : UserControl
{
    private DiagramSession? _session;
    private TextBlock _header = null!;
    private TextBlock _summary = null!;
    private TextBlock _details = null!;
    private Button _toggle = null!;

    public DiffSidebar()
    {
        InitializeComponent();

        _toggle.Click += (_, _) => SetExpanded(!_details.IsVisible);
    }

    /// <summary>文档那一侧。换一份会话就换一份日志。</summary>
    public DiagramSession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value))
            {
                return;
            }

            if (_session is not null)
            {
                _session.SceneChanged -= Refresh;
            }

            _session = value;

            if (_session is not null)
            {
                _session.SceneChanged += Refresh;
            }

            Refresh();
        }
    }

    private void SetExpanded(bool expanded)
    {
        _details.IsVisible = expanded;
        _toggle.Content = expanded ? "收起" : "展开";
    }

    private void Refresh()
    {
        var entries = _session?.Bus.Context.VersionLog.Snapshot() ?? [];

        if (entries.Count == 0)
        {
            _header.Text = "变更";
            _summary.Text = "暂无变更";
            _details.Text = string.Empty;

            return;
        }

        var last = entries[^1];

        _header.Text = $"变更 · v{last.Version}";
        _summary.Text = Describe(last);
        _details.Text = Details(last);
    }

    private static string Describe(VersionEntry entry)
    {
        var scope = entry.IsBulkChange
            ? $"批量变更 {entry.OriginalChangeCount} 处"
            : $"影响 {Math.Max(entry.Changes.Length, entry.AffectedIds.Length)} 处";

        return $"最近一次：{entry.CommandId}（{SourceLabel(entry.Source)}），{scope}";
    }

    private static string Details(VersionEntry entry)
    {
        if (entry.IsBulkChange)
        {
            return "批量变更不列明细，整份替换更快。";
        }

        if (entry.Changes.Length == 0)
        {
            return string.Join("\n", entry.AffectedIds);
        }

        return string.Join(
            "\n",
            entry.Changes.Select(change =>
                $"{change.ElementId}.{change.Field}: {Show(change.OldValue)} → {Show(change.NewValue)}"));
    }

    private static string Show(string? value) => string.IsNullOrEmpty(value) ? "（空）" : value;

    private static string SourceLabel(ChangeSource source) => source switch
    {
        ChangeSource.Human => "人工",
        ChangeSource.Llm => "LLM",
        ChangeSource.Mcp => "外部代理",
        ChangeSource.Import => "导入",
        ChangeSource.Undo => "撤销",
        ChangeSource.Redo => "重做",
        _ => "系统",
    };

    /// <summary>
    /// 取一次界面标记里那几个带名字的控件。
    /// </summary>
    /// <remarks>
    /// 名字对应的字段由界面标记生成器声明，而给它赋值的那一份 <c>InitializeComponent</c>
    /// 只在类里没有同名方法时才会生成。这里手写了这一个，所以那一份不再生成——
    /// 少掉的正是赋值那一步，字段会一直是空的，而编译期看不出任何异常。
    /// </remarks>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        _header = this.FindControl<TextBlock>(nameof(HeaderText))
            ?? throw new InvalidOperationException("变更边栏的界面标记里没有名为 Header 的文本");
        _summary = this.FindControl<TextBlock>(nameof(SummaryText))
            ?? throw new InvalidOperationException("变更边栏的界面标记里没有名为 Summary 的文本");
        _details = this.FindControl<TextBlock>(nameof(DetailText))
            ?? throw new InvalidOperationException("变更边栏的界面标记里没有名为 Details 的文本");
        _toggle = this.FindControl<Button>(nameof(ToggleButton))
            ?? throw new InvalidOperationException("变更边栏的界面标记里没有名为 Toggle 的按钮");
    }
}
