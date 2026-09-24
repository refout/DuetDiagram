using System.ComponentModel;
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
/// 形状面板：一份形状清单，点一个就把选中的节点换成它。
/// </summary>
/// <remarks>
/// <para>
/// 控件在代码里搭，与图层面板、调色板面板同一套做法。清单本身是静态的（形状库是编译期定下的），
/// 所以这里只在数据上下文换人时铺一次；之后每次重铺都是因为"哪一行是当前形状"变了。
/// </para>
/// <para>
/// 每颗按钮挂一个自动化标识（<c>shape.entry.&lt;形状名&gt;</c>），无头用例按标识去找它们。
/// </para>
/// </remarks>
public sealed partial class ShapeLibraryPanel : UserControl
{
    private ShapeLibraryPanelViewModel? _built;

    // Rows / ErrorText 两个容器由界面标记的 x:Name 生成字段，
    // 在 InitializeComponent 里用 FindControl 接住——与调色板面板同一套做法。

    public ShapeLibraryPanel() => InitializeComponent();

    /// <summary>面板的数据上下文，按它要的类型取。类型不对时为空。</summary>
    public ShapeLibraryPanelViewModel? Model => DataContext as ShapeLibraryPanelViewModel;

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

        if (model is null)
        {
            return;
        }

        BuildRows(model);

        FieldEditBinder.Watch(model, nameof(ShapeLibraryPanelViewModel.Rows), () => BuildRows(model));
        FieldEditBinder.Watch(model, nameof(ShapeLibraryPanelViewModel.Error), () =>
        {
            ErrorText.Text = model.Error;
            ErrorText.IsVisible = model.HasError;
        });
    }

    #region 清单

    private void BuildRows(ShapeLibraryPanelViewModel model)
    {
        Rows.Children.Clear();

        foreach (var row in model.Rows)
        {
            Rows.Children.Add(Entry(row));
        }
    }

    private static Control Entry(ShapeRowViewModel row)
    {
        // 预览画的是形状库给的那份几何，与画布走同一个渲染器——预览里看到的形状
        // 就是画布上会画出来的形状。
        var preview = new ShapePreview
        {
            Shape = row.Shape,
            Width = 30,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var name = new TextBlock
        {
            Text = row.IsCurrent ? $"● {row.Name}" : row.Name,
            FontSize = 11,
            FontWeight = row.IsCurrent ? FontWeight.SemiBold : FontWeight.Normal,
            Foreground = row.IsCurrent ? PanelPalette.Body : PanelPalette.Label,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var button = new Button
        {
            Content = new StackPanel { Spacing = 2, Children = { preview, name } },
            Width = 66,
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(row.IsCurrent ? 1 : 0),
            BorderBrush = PanelPalette.Body,
        };

        AutomationProperties.SetAutomationId(button, $"shape.entry.{row.Name}");
        ToolTip.SetTip(button, $"{row.Name}——把选中的节点换成这个形状。");

        button.Click += (_, _) => row.Apply();

        return button;
    }

    #endregion

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        Rows = this.FindControl<WrapPanel>(nameof(Rows))
            ?? throw new InvalidOperationException("形状面板的界面标记里没有名为 Rows 的容器");
        ErrorText = this.FindControl<TextBlock>(nameof(ErrorText))
            ?? throw new InvalidOperationException("形状面板的界面标记里没有名为 ErrorText 的提示");
    }
}
