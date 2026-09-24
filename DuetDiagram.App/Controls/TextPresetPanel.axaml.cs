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
/// 文本预设面板：预设清单、成员编辑器，以及新建、删除与应用到选中。
/// </summary>
/// <remarks>
/// <para>
/// 控件在代码里搭，与调色板面板、图层面板同一套做法：行数随文档里预设的个数变。
/// 行只在预设增删时重建；成员编辑器只在换选中时重建——文档一变就重建的话，
/// 用户正在输入的那个框会失焦。
/// </para>
/// <para>
/// 每个可点的控件挂一个自动化标识（<c>textpreset.entry.&lt;标识&gt;</c> 这类），
/// 无头用例按标识去找它们。
/// </para>
/// </remarks>
public sealed partial class TextPresetPanel : UserControl
{
    private TextPresetPanelViewModel? _built;
    private string? _editorFor;
    private StackPanel? _editor;

    // Rows / Footer / ErrorText 三个容器由界面标记的 x:Name 生成字段，
    // 在 InitializeComponent 里用 FindControl 接住——与调色板面板同一套做法。

    public TextPresetPanel() => InitializeComponent();

    /// <summary>面板的数据上下文，按它要的类型取。类型不对时为空。</summary>
    public TextPresetPanelViewModel? Model => DataContext as TextPresetPanelViewModel;

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

        FieldEditBinder.Watch(model, nameof(TextPresetPanelViewModel.Rows), () => BuildRows(model));

        // 选中换行时不重算清单，但"哪一行亮着"要重铺——记号画在行的名字上。
        FieldEditBinder.Watch(model, nameof(TextPresetPanelViewModel.SelectedId), () => BuildRows(model));
        FieldEditBinder.Watch(model, nameof(TextPresetPanelViewModel.EditorFields), () => RebuildEditor(model));
        FieldEditBinder.Watch(model, nameof(TextPresetPanelViewModel.Error), () =>
        {
            ErrorText.Text = model.Error ?? string.Empty;
            ErrorText.IsVisible = model.HasError;
        });
    }

    #region 清单

    private void BuildRows(TextPresetPanelViewModel model)
    {
        Rows.Children.Clear();

        foreach (var row in model.Rows)
        {
            Rows.Children.Add(PresetRow(model, row));
        }
    }

    private Control PresetRow(TextPresetPanelViewModel model, TextPresetRowViewModel row)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        var name = new TextBlock
        {
            FontSize = 12,
            Foreground = PanelPalette.Body,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var summary = new TextBlock
        {
            FontSize = 11,
            Foreground = PanelPalette.Muted,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(name, 0);
        Grid.SetColumn(summary, 1);
        grid.Children.Add(name);
        grid.Children.Add(summary);

        summary.Margin = new Thickness(8, 0, 0, 0);

        AutomationProperties.SetAutomationId(grid, $"textpreset.entry.{row.Id}");
        ToolTip.SetTip(grid, $"{row.Name}（{row.Summary}）——点一下选中它，下面就能改它的成员。");

        grid.Cursor = new Cursor(StandardCursorType.Hand);
        grid.PointerPressed += (_, _) => model.Select(row.Id);

        // 行不是可通知对象：清单的每一行本来就随 Rows / SelectedId 整批重铺，
        // 不必为"选中记号"一件事给行加一层通知。
        var selected = string.Equals(model.SelectedId, row.Id, StringComparison.Ordinal);

        name.Text = selected ? $"● {row.Name}" : row.Name;
        name.FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal;
        summary.Text = row.Summary;

        return grid;
    }

    #endregion

    #region 底部：新建、编辑器、应用、删除

    private void BuildFooter(TextPresetPanelViewModel model)
    {
        Footer.Children.Clear();

        var newName = new TextBox
        {
            FontSize = 12,
            Padding = new Thickness(6, 3),
            PlaceholderText = "新预设名",
        };

        AutomationProperties.SetAutomationId(newName, "textpreset.new-name");
        ToolTip.SetTip(newName, "要新建的预设名。不许为空。样式建好之后在下面逐个成员填。");

        var define = new Button
        {
            Content = "新建",
            FontSize = 12,
            Padding = new Thickness(8, 2),
        };

        AutomationProperties.SetAutomationId(define, "textpreset.define");

        define.Click += (_, _) =>
        {
            model.NewName = newName.Text ?? string.Empty;
            model.Define();

            // 成功的话清空输入框；失败的话留着，用户多半只想改一个字再试。
            newName.Text = model.NewName;
        };

        FieldEditBinder.Watch(model, nameof(TextPresetPanelViewModel.HasReadOnlyNote), () =>
            define.IsEnabled = !model.HasReadOnlyNote);

        Footer.Children.Add(RowOf(newName, define));

        // 编辑器：选中一个预设才出现。整节收起而不是摆一排灰着的框——
        // 那会让用户以为面板坏了。
        var editor = new StackPanel { Spacing = 4 };

        AutomationProperties.SetAutomationId(editor, "textpreset.editor");

        Footer.Children.Add(editor);

        FieldEditBinder.Watch(
            model,
            nameof(TextPresetPanelViewModel.EditorFields),
            () => RebuildEditor(model));

        var apply = new Button
        {
            Content = "应用到选中",
            FontSize = 12,
            Padding = new Thickness(8, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        AutomationProperties.SetAutomationId(apply, "textpreset.apply");
        ToolTip.SetTip(apply,
            "把这个预设应用到画布上选中的节点。多选也算一次操作，撤销按一次全部还原。"
            + "预设里没声明的成员保持元素自己现在的值——是叠加，不是替换。");

        apply.Click += (_, _) => model.ApplyToSelection();

        FieldEditBinder.Watch(model, nameof(TextPresetPanelViewModel.HasSelection), () =>
            apply.IsEnabled = model.HasSelection && !model.HasReadOnlyNote);
        FieldEditBinder.Watch(model, nameof(TextPresetPanelViewModel.HasReadOnlyNote), () =>
            apply.IsEnabled = model.HasSelection && !model.HasReadOnlyNote);

        Footer.Children.Add(apply);

        var remove = new Button
        {
            Content = "删除选中",
            FontSize = 12,
            Padding = new Thickness(8, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        AutomationProperties.SetAutomationId(remove, "textpreset.remove");
        ToolTip.SetTip(remove,
            "删掉选中的预设。已经应用过的样式原样长在各节点上——应用是按值抄过去的，"
            + "所以删除不需要先问「谁在用」。");

        remove.Click += (_, _) => model.RemoveSelected();

        FieldEditBinder.Watch(model, nameof(TextPresetPanelViewModel.HasSelection), () =>
            remove.IsEnabled = model.HasSelection && !model.HasReadOnlyNote);
        FieldEditBinder.Watch(model, nameof(TextPresetPanelViewModel.HasReadOnlyNote), () =>
            remove.IsEnabled = model.HasSelection && !model.HasReadOnlyNote);

        Footer.Children.Add(remove);

        _editor = editor;

        // 初次接线时先铺一遍：订阅只管之后的改动。
        RebuildEditor(model);
    }

    private void RebuildEditor(TextPresetPanelViewModel model)
    {
        if (_editor is null)
        {
            return;
        }

        // 换选中才重建。文档变了（改的是别的预设）只推值、不重建——
        // 重建会让用户正在输入的那个框失焦。
        if (ReferenceEquals(_editorFor, model.SelectedId))
        {
            return;
        }

        _editorFor = model.SelectedId;
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

        AutomationProperties.SetAutomationId(box, $"textpreset.{field.Field}");

        // 与调色板编辑器同一套提交时机：回车或焦点离开。
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

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        Rows = this.FindControl<StackPanel>(nameof(Rows))
            ?? throw new InvalidOperationException("文本预设面板的界面标记里没有名为 Rows 的容器");
        Footer = this.FindControl<StackPanel>(nameof(Footer))
            ?? throw new InvalidOperationException("文本预设面板的界面标记里没有名为 Footer 的容器");
        ErrorText = this.FindControl<TextBlock>(nameof(ErrorText))
            ?? throw new InvalidOperationException("文本预设面板的界面标记里没有名为 ErrorText 的提示");
    }
}
