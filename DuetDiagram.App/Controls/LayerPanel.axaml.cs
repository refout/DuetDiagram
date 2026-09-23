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
/// 图层面板：列表、两个开关、次序，以及归属入口。
/// </summary>
/// <remarks>
/// <para>
/// 控件在代码里搭，与属性面板同一套做法：行数随文档里图层的个数变，
/// 一份界面标记写不出"每层一行"，而写死几行的话加减图层时得多改一处。
/// </para>
/// <para>
/// **行只在图层增删时重建。** 换选中、挪次序、开关某一层都只把新值推进已有控件——
/// 重建会让用户正在输入的那个框失焦，而挪次序时正需要它保持焦点。
/// </para>
/// <para>
/// 每个控件挂一个自动化标识（<c>layer.visible.&lt;图层标识&gt;</c> 这类），
/// 无头用例按标识去找它们，不用猜视觉树里的位置。
/// </para>
/// </remarks>
public sealed partial class LayerPanel : UserControl
{
    /// <summary>列表上方那一行表头，标明每列是什么。</summary>
    private static readonly string[] Header = ["显示", "锁定", "名字", "元素"];

    private LayerPanelViewModel? _built;

    public LayerPanel() => InitializeComponent();

    /// <summary>面板的数据上下文，按它要的类型取。类型不对时为空。</summary>
    public LayerPanelViewModel? Model => DataContext as LayerPanelViewModel;

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

        // 行只在图层增删时换一批，所以这里只盯那一个属性。
        FieldEditBinder.Watch(model, nameof(LayerPanelViewModel.Rows), () => BuildRows(model));
    }

    #region 列表

    private void BuildRows(LayerPanelViewModel model)
    {
        Rows.Children.Clear();
        Rows.Children.Add(HeaderRow());

        foreach (var row in model.Rows)
        {
            Rows.Children.Add(LayerRow(model, row));
        }
    }

    /// <summary>
    /// 表头。
    /// </summary>
    /// <remarks>
    /// 两个开关没有文字（一行的宽度放不下），不说一句的话用户得逐个悬停去试。
    /// 表头与行用同一套列宽，所以它们是对齐的。
    /// </remarks>
    private static Control HeaderRow()
    {
        var grid = NewGrid();

        for (var column = 0; column < Header.Length; column++)
        {
            var text = new TextBlock
            {
                Text = Header[column],
                FontSize = 11,
                Foreground = PanelPalette.Muted,
            };

            Grid.SetColumn(text, column);
            grid.Children.Add(text);
        }

        return grid;
    }

    private static Control LayerRow(LayerPanelViewModel model, LayerRowViewModel row)
    {
        var grid = NewGrid();

        var visible = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
        };

        AutomationProperties.SetAutomationId(visible, $"layer.visible.{row.Id}");
        ToolTip.SetTip(visible, "这一层画不画。藏起来只是不画，位置还占着——整张图不会跟着重排。");

        var locked = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
        };

        AutomationProperties.SetAutomationId(locked, $"layer.locked.{row.Id}");
        ToolTip.SetTip(locked, "这一层改不改得动。锁着的那一层照常画出来，但点不中、改不了。");

        var name = new TextBlock
        {
            FontSize = 12,
            Foreground = PanelPalette.Body,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var count = new TextBlock
        {
            FontSize = 11,
            Foreground = PanelPalette.Muted,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var up = Stepper(model, row, delta: -1);
        var down = Stepper(model, row, delta: +1);

        Place(grid, visible, 0);
        Place(grid, locked, 1);
        Place(grid, name, 2);
        Place(grid, count, 3);
        Place(grid, up, 4);
        Place(grid, down, 5);

        // 开关的回写：用户点一下之后由命令层去改，改完文档那边回流过来。
        // 回流推的是同一个值，所以这里要比一下再赋——不比的话会把用户刚点上的那一下弹回去。
        visible.IsCheckedChanged += (_, _) =>
        {
            if (visible.IsChecked == row.Visible)
            {
                return;
            }

            model.ToggleVisible(row.Id);
        };

        locked.IsCheckedChanged += (_, _) =>
        {
            if (locked.IsChecked == row.Locked)
            {
                return;
            }

            model.ToggleLocked(row.Id);
        };

        // 当前图层带一个记号。只在别处标出来的话，用户看不出「移入」会落到哪一层。
        // 名字与记号一起渲染：分成两个订阅的话，换当前图层时只更新其中一个。
        Bind(
            row,
            () =>
            {
                name.Text = row.IsCurrent ? $"● {row.Name}" : row.Name;
                name.FontWeight = row.IsCurrent ? FontWeight.SemiBold : FontWeight.Normal;
                name.Foreground = row.IsCurrent ? PanelPalette.Body : PanelPalette.Label;
            },
            nameof(LayerRowViewModel.Name),
            nameof(LayerRowViewModel.IsCurrent));

        Bind(
            row,
            () =>
            {
                visible.IsChecked = row.Visible;
                visible.IsEnabled = !model.HasReadOnlyNote;
            },
            nameof(LayerRowViewModel.Visible));

        Bind(
            row,
            () =>
            {
                locked.IsChecked = row.Locked;
                locked.IsEnabled = !model.HasReadOnlyNote;
            },
            nameof(LayerRowViewModel.Locked));

        Bind(row, () => count.Text = row.ElementCountText, nameof(LayerRowViewModel.ElementCountText));

        return grid;
    }

    /// <summary>往上或往下挪一格的那个按钮。</summary>
    private static Button Stepper(LayerPanelViewModel model, LayerRowViewModel row, int delta)
    {
        var button = new Button
        {
            Content = delta < 0 ? "↑" : "↓",
            FontSize = 11,
            Padding = new Thickness(6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        // 按钮作用的是"这一行"，但改的是当前图层——点之前先把它定为当前图层，
        // 否则用户点第 3 行的箭头，动的是第 1 行。
        AutomationProperties.SetAutomationId(button, delta < 0 ? $"layer.up.{row.Id}" : $"layer.down.{row.Id}");
        ToolTip.SetTip(button, delta < 0 ? "往上挪一位（画到更上面）" : "往下挪一位（画到更下面）");

        button.Click += (_, _) =>
        {
            model.Select(row.Id);

            if (delta < 0)
            {
                model.MoveUp();
            }
            else
            {
                model.MoveDown();
            }
        };

        Bind(
            row,
            () => button.IsEnabled = !model.HasReadOnlyNote
                && (delta < 0 ? row.CanMoveUp : row.CanMoveDown),
            delta < 0 ? nameof(LayerRowViewModel.CanMoveUp) : nameof(LayerRowViewModel.CanMoveDown));

        return button;
    }

    #endregion

    #region 底部

    private void BuildFooter(LayerPanelViewModel model)
    {
        var newName = Box("layer.new-name", "新图层的名字，可以留空之后再改。");
        var create = Button("layer.new", "新建");
        create.Click += (_, _) => model.Create(newName.Text);

        var renameName = Box("layer.rename-name", "把当前图层改成这个名字。");
        var rename = Button("layer.rename", "改名");
        rename.Click += (_, _) => model.Rename(renameName.Text);

        var note = new TextBlock
        {
            FontSize = 12,
            Foreground = PanelPalette.Label,
            TextWrapping = TextWrapping.Wrap,
        };

        var assign = Button("layer.assign", model.AssignLabel);
        ToolTip.SetTip(assign, "把选中的元素都归到当前图层上。一次操作进一条历史，撤销一次就全回去。");
        assign.Click += (_, _) => model.AssignSelection();

        Footer.Children.Add(RowOf(newName, create));
        Footer.Children.Add(RowOf(renameName, rename));
        Footer.Children.Add(note);
        Footer.Children.Add(assign);

        // 新建那一行按只读门决定可不可点。文档改不动时发出去也是被拒，
        // 但先把按钮禁掉，用户不用点了才知道。
        Bind(model, () => create.IsEnabled = !model.HasReadOnlyNote, nameof(LayerPanelViewModel.ReadOnlyNote));

        // 改名框跟着当前图层走：预填当前那一层的名字，用户接着改。
        // 不预填的话，用户得先把整名字敲一遍，而多数时候改的只是一个字。
        Bind(
            model,
            () =>
            {
                renameName.Text = model.CurrentLayerName ?? string.Empty;
                rename.IsEnabled = model.HasCurrentLayer && !model.HasReadOnlyNote;
            },
            nameof(LayerPanelViewModel.CurrentLayerName),
            nameof(LayerPanelViewModel.HasCurrentLayer));

        Bind(model, () => note.Text = model.CurrentLayerNote, nameof(LayerPanelViewModel.CurrentLayerNote));

        Bind(model, () => assign.Content = model.AssignLabel, nameof(LayerPanelViewModel.AssignLabel));

        Bind(model, () => assign.IsEnabled = model.CanAssign, nameof(LayerPanelViewModel.CanAssign));
    }

    private static TextBox Box(string id, string tip)
    {
        var box = new TextBox
        {
            FontSize = 12,
            Padding = new Thickness(6, 3),
            PlaceholderText = "名字",
        };

        AutomationProperties.SetAutomationId(box, id);
        ToolTip.SetTip(box, tip);

        return box;
    }

    private static Button Button(string id, string content)
    {
        var button = new Button
        {
            Content = content,
            FontSize = 12,
            Padding = new Thickness(8, 2),
        };

        AutomationProperties.SetAutomationId(button, id);

        return button;
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

    #endregion

    #region 搭行

    private static Grid NewGrid() => new()
    {
        ColumnDefinitions = new ColumnDefinitions("24,24,*,Auto,24,24"),
    };

    private static void Place(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    /// <summary>
    /// 立刻铺一遍，然后盯住那几个属性。
    /// </summary>
    /// <remarks>
    /// 先把当前值推一遍是必须的：订阅只管之后的改动，而控件是刚 new 出来的。
    /// 不推的话，界面要等到那一行第一次变化才显示内容。
    /// </remarks>
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
            ?? throw new InvalidOperationException("图层面板的界面标记里没有名为 Rows 的容器");
        Footer = this.FindControl<StackPanel>(nameof(Footer))
            ?? throw new InvalidOperationException("图层面板的界面标记里没有名为 Footer 的容器");
    }

    #endregion
}
