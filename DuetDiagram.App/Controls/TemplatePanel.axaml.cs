using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using DuetDiagram.App.Services;
using DuetDiagram.App.ViewModels;

namespace DuetDiagram.App.Controls;

/// <summary>
/// 模板面板：目录里有哪些模板，点一个把它拼进当前文档。
/// </summary>
/// <remarks>
/// <para>
/// 控件在代码里搭，与左栏另外几个面板同一套做法：行数随目录里的文件数变。
/// 清单只在面板接上数据上下文时铺一次，之后每次重铺都是因为模板目录被重新扫过
/// （例如刚存了一份新的进去）。
/// </para>
/// <para>
/// 每颗按钮挂一个自动化标识（<c>template.entry.&lt;模板名&gt;</c>），无头用例按标识去找它们。
/// </para>
/// </remarks>
public sealed partial class TemplatePanel : UserControl
{
    private TemplatePanelViewModel? _built;

    // Rows / Failures / ErrorText / NoteText / EmptyText / FailureHead 六个容器由界面标记的
    // x:Name 生成字段，在 InitializeComponent 里用 FindControl 接住——与调色板面板同一套做法。

    public TemplatePanel() => InitializeComponent();

    /// <summary>面板的数据上下文，按它要的类型取。类型不对时为空。</summary>
    public TemplatePanelViewModel? Model => DataContext as TemplatePanelViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        var model = Model;

        if (ReferenceEquals(_built, model))
        {
            return;
        }

        _built = model;

        Rows.Children.Clear();
        Failures.Children.Clear();

        if (model is null)
        {
            return;
        }

        Build(model);

        // 清单与失败名单一起重铺：两者都来自同一次目录扫描，
        // 分开订阅的话，一次"存为模板"之后失败名单会停在上一轮。
        FieldEditBinder.Watch(model, nameof(TemplatePanelViewModel.Rows), () => Build(model));
        FieldEditBinder.Watch(model, nameof(TemplatePanelViewModel.Error), () =>
        {
            ErrorText.Text = model.Error ?? string.Empty;
            ErrorText.IsVisible = model.HasError;
        });
        FieldEditBinder.Watch(model, nameof(TemplatePanelViewModel.Note), () =>
        {
            NoteText.Text = model.Note ?? string.Empty;
            NoteText.IsVisible = model.HasNote;
        });
    }

    #region 清单

    private void Build(TemplatePanelViewModel model)
    {
        Rows.Children.Clear();
        Failures.Children.Clear();

        foreach (var row in model.Rows)
        {
            Rows.Children.Add(Entry(row));
        }

        foreach (var failure in model.Failures)
        {
            Failures.Children.Add(Failure(failure));
        }

        EmptyText.IsVisible = model.HasNoTemplates;
        FailureHead.IsVisible = model.HasFailures;
    }

    private static Control Entry(TemplateRowViewModel row)
    {
        var title = new TextBlock
        {
            Text = row.Name,
            FontSize = 12,
            Foreground = PanelPalette.Body,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var summary = new TextBlock
        {
            Text = row.Summary,
            FontSize = 10,
            Foreground = PanelPalette.Muted,
        };

        var button = new Button
        {
            Content = new StackPanel { Spacing = 1, Children = { title, summary } },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 3),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };

        AutomationProperties.SetAutomationId(button, $"template.entry.{row.Name}");
        ToolTip.SetTip(button, $"放入模板 {row.Name}（{row.Summary}）。整份算一次操作，撤销按一次全部退回。");

        button.Click += (_, _) => row.Apply();

        return button;
    }

    /// <summary>一个读不出来的文件：文件名加一句原因。</summary>
    private static Control Failure(TemplateLoadFailure failure)
    {
        var title = new TextBlock
        {
            Text = failure.File,
            FontSize = 11,
            Foreground = PanelPalette.Body,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var reason = new TextBlock
        {
            Text = failure.Reason,
            FontSize = 10,
            Foreground = PanelPalette.Error,
            TextWrapping = TextWrapping.Wrap,
        };

        return new StackPanel { Spacing = 1, Children = { title, reason } };
    }

    #endregion

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        Rows = this.FindControl<StackPanel>(nameof(Rows))
            ?? throw new InvalidOperationException("模板面板的界面标记里没有名为 Rows 的容器");
        Failures = this.FindControl<StackPanel>(nameof(Failures))
            ?? throw new InvalidOperationException("模板面板的界面标记里没有名为 Failures 的容器");
        ErrorText = this.FindControl<TextBlock>(nameof(ErrorText))
            ?? throw new InvalidOperationException("模板面板的界面标记里没有名为 ErrorText 的提示");
        NoteText = this.FindControl<TextBlock>(nameof(NoteText))
            ?? throw new InvalidOperationException("模板面板的界面标记里没有名为 NoteText 的提示");
        EmptyText = this.FindControl<TextBlock>(nameof(EmptyText))
            ?? throw new InvalidOperationException("模板面板的界面标记里没有名为 EmptyText 的提示");
        FailureHead = this.FindControl<TextBlock>(nameof(FailureHead))
            ?? throw new InvalidOperationException("模板面板的界面标记里没有名为 FailureHead 的提示");
    }
}
