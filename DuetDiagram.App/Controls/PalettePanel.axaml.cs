using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using DuetDiagram.App.Services;
using DuetDiagram.App.ViewModels;

namespace DuetDiagram.App.Controls;

/// <summary>
/// 调色板面板：条目清单、真渲染的预览、成员编辑器，以及新建与删除。
/// </summary>
/// <remarks>
/// <para>
/// 控件在代码里搭，与图层面板、页签同一套做法：行数随文档里条目的个数变。
/// 行只在条目增删时重建；成员编辑器只在换选中时重建——文档一变就重建的话，
/// 用户正在输入的那个框会失焦。
/// </para>
/// <para>
/// 每个可点的控件挂一个自动化标识（<c>palette.entry.&lt;令牌名&gt;</c> 这类），
/// 无头用例按标识去找它们。
/// </para>
/// </remarks>
public sealed partial class PalettePanel : UserControl
{
    private PalettePanelViewModel? _built;
    private string? _editorFor;
    private StackPanel? _editor;

    // Rows / Footer / ErrorText 三个容器由界面标记的 x:Name 生成字段，
    // 在 InitializeComponent 里用 FindControl 接住——与图层面板同一套做法。

    public PalettePanel() => InitializeComponent();

    /// <summary>面板的数据上下文，按它要的类型取。类型不对时为空。</summary>
    public PalettePanelViewModel? Model => DataContext as PalettePanelViewModel;

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
        Footer.Children.Clear();

        if (model is null)
        {
            return;
        }

        BuildRows(model);
        BuildFooter(model);

        FieldEditBinder.Watch(model, nameof(PalettePanelViewModel.Rows), () => BuildRows(model));

        // 选中换行时不重算清单，但"哪一行亮着"要重铺——记号画在行的名字上。
        FieldEditBinder.Watch(model, nameof(PalettePanelViewModel.SelectedName), () => BuildRows(model));
        FieldEditBinder.Watch(model, nameof(PalettePanelViewModel.EditorFields), () => RebuildEditor(model));
        FieldEditBinder.Watch(model, nameof(PalettePanelViewModel.Error), () =>
        {
            ErrorText.Text = model.Error;
            ErrorText.IsVisible = model.HasError;
        });
    }

    #region 清单

    private void BuildRows(PalettePanelViewModel model)
    {
        Rows.Children.Clear();

        foreach (var row in model.Rows)
        {
            Rows.Children.Add(EntryRow(model, row));
        }
    }

    private Control EntryRow(PalettePanelViewModel model, PaletteRowViewModel row)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

        // 预览色块的颜色由主题解析出来（与画布同一条路径），这里只负责把它画出来。
        // 边框用条目自己的描边色与粗细——粗细只在预览上看得出变化。
        var swatch = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(3),
            Background = Brush(row.Fill),
            BorderBrush = Brush(row.Stroke),
            BorderThickness = new Thickness(Math.Max(1, row.Weight)),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var name = new TextBlock
        {
            FontSize = 12,
            Foreground = PanelPalette.Body,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var users = new TextBlock
        {
            FontSize = 11,
            Foreground = PanelPalette.Muted,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(swatch, 0);
        Grid.SetColumn(name, 1);
        Grid.SetColumn(users, 2);
        grid.Children.Add(swatch);
        grid.Children.Add(name);
        grid.Children.Add(users);

        name.Margin = new Thickness(8, 0, 0, 0);
        users.Margin = new Thickness(8, 0, 0, 0);

        AutomationProperties.SetAutomationId(grid, $"palette.entry.{row.Name}");
        ToolTip.SetTip(grid, $"{row.Name}——点一下选中它，下面就能改它的成员。");

        // 当前行带一个记号，与图层面板同一套读法。
        grid.Cursor = new Cursor(StandardCursorType.Hand);
        grid.PointerPressed += (_, _) => model.Select(row.Name);

        // 行不是可通知对象：清单的每一行本来就随 Rows / SelectedName 整批重铺，
        // 不必为"选中记号"一件事给行加一层通知。
        var selected = string.Equals(model.SelectedName, row.Name, StringComparison.Ordinal);

        name.Text = selected ? $"● {row.Name}" : row.Name;
        name.FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal;
        users.Text = row.ReferrersNote;

        return grid;
    }

    #endregion

    #region 底部：新建、编辑器、删除

    private void BuildFooter(PalettePanelViewModel model)
    {
        Footer.Children.Clear();

        var newName = new TextBox
        {
            FontSize = 12,
            Padding = new Thickness(6, 3),
            PlaceholderText = "新令牌名",
        };

        AutomationProperties.SetAutomationId(newName, "palette.new-name");
        ToolTip.SetTip(newName, "要新建的令牌名。不许为空，也不许与已有的重名。");

        var define = new Button
        {
            Content = "新建",
            FontSize = 12,
            Padding = new Thickness(8, 2),
        };

        AutomationProperties.SetAutomationId(define, "palette.define");

        define.Click += (_, _) =>
        {
            model.NewName = newName.Text ?? string.Empty;
            model.Define();

            // 成功的话清空输入框；失败的话留着，用户多半只想改一个字再试。
            newName.Text = model.NewName;
        };

        FieldEditBinder.Watch(model, nameof(PalettePanelViewModel.HasReadOnlyNote), () =>
            define.IsEnabled = !model.HasReadOnlyNote);

        Footer.Children.Add(RowOf(newName, define));

        // 编辑器：选中一个条目才出现。整节收起而不是摆一排灰着的框——
        // 那会让用户以为调色板坏了。
        var editor = new StackPanel { Spacing = 4 };

        AutomationProperties.SetAutomationId(editor, "palette.editor");

        Footer.Children.Add(editor);

        FieldEditBinder.Watch(
            model,
            nameof(PalettePanelViewModel.EditorFields),
            () => RebuildEditor(model));

        var remove = new Button
        {
            Content = "删除选中",
            FontSize = 12,
            Padding = new Thickness(8, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        AutomationProperties.SetAutomationId(remove, "palette.remove");
        ToolTip.SetTip(remove, "删掉选中的条目。还有元素在用它的时候会先把名单列出来。");

        remove.Click += (_, _) => model.RemoveSelected();

        FieldEditBinder.Watch(model, nameof(PalettePanelViewModel.HasSelection), () =>
            remove.IsEnabled = model.HasSelection && !model.HasReadOnlyNote);
        FieldEditBinder.Watch(model, nameof(PalettePanelViewModel.HasReadOnlyNote), () =>
            remove.IsEnabled = model.HasSelection && !model.HasReadOnlyNote);

        Footer.Children.Add(remove);

        _editor = editor;

        // 初次接线时先铺一遍：订阅只管之后的改动。
        RebuildEditor(model);
    }

    private void RebuildEditor(PalettePanelViewModel model)
    {
        if (_editor is null)
        {
            return;
        }

        // 换选中才重建。文档变了（改的是别的条目）只推值、不重建——
        // 重建会让用户正在输入的那个框失焦。
        if (ReferenceEquals(_editorFor, model.SelectedName))
        {
            return;
        }

        _editorFor = model.SelectedName;
        _editor.Children.Clear();

        if (!model.EditorVisible)
        {
            return;
        }

        foreach (var field in model.EditorFields)
        {
            _editor.Children.Add(EditorRow(field));
        }
    }

    private Control EditorRow(PaletteFieldViewModel field)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("64,*") };

        var label = new TextBlock
        {
            Text = field.Label,
            FontSize = 12,
            Foreground = PanelPalette.Label,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var box = new TextBox
        {
            Text = field.Value ?? string.Empty,
            FontSize = 12,
            Padding = new Thickness(6, 3),
            PlaceholderText = "没有设置",
        };

        AutomationProperties.SetAutomationId(box, $"palette.{field.Field}");

        // 与属性面板的文本框同一套提交时机：回车或焦点离开。
        box.LostFocus += (_, _) => field.Commit(box.Text is { Length: > 0 } text ? text : null);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                field.Commit(box.Text is { Length: > 0 } text ? text : null);
                e.Handled = true;
            }
        };

        Grid.SetColumn(label, 0);
        Grid.SetColumn(box, 1);
        grid.Children.Add(label);
        grid.Children.Add(box);

        return grid;
    }

    #endregion

    /// <summary>把主题解析出来的颜色串变成一把刷子。解析不了时给透明——画一块红出来更吓人。</summary>
    private static IBrush Brush(string color)
    {
        try
        {
            return new SolidColorBrush(Color.Parse(color));
        }
        catch (FormatException)
        {
            return Brushes.Transparent;
        }
    }

    private static Control RowOf(Control left, Control right)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        right.Margin = new Thickness(6, 0, 0, 0);

        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);

        return grid;
    }

    /// <summary>立刻铺一遍，然后盯住那几个属性。与图层面板同一个理由：订阅只管之后的改动。</summary>
    private static void Bind(INotifyPropertyChanged source, Action apply, params string[] properties)
    {
        apply();

        foreach (var property in properties)
        {
            FieldEditBinder.Watch(source, property, apply);
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        Rows = this.FindControl<StackPanel>(nameof(Rows))
            ?? throw new InvalidOperationException("调色板面板的界面标记里没有名为 Rows 的容器");
        Footer = this.FindControl<StackPanel>(nameof(Footer))
            ?? throw new InvalidOperationException("调色板面板的界面标记里没有名为 Footer 的容器");
        ErrorText = this.FindControl<TextBlock>(nameof(ErrorText))
            ?? throw new InvalidOperationException("调色板面板的界面标记里没有名为 ErrorText 的提示");
    }
}
