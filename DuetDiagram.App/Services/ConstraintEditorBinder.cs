using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DuetDiagram.App.ViewModels;

namespace DuetDiagram.App.Services;

/// <summary>
/// 搭出布局约束那一节：约束列表、三个加按钮、一句结果。
/// </summary>
/// <remarks>
/// <para>
/// **行是重建的，不增量改。** 约束的条数随文档变，而"哪一行还在不在"只有视图模型知道；
/// 增量改要逐行比对新旧两份列表，比错了的表现是删了一条而界面上少的是另一条。
/// 几十行的东西重建一次的代价看不出来，而面板里那些字段不重建是因为它们有几十个，
/// 且换选中时条数不变——两处的取舍不同，不是因为口径不一致。
/// </para>
/// <para>
/// 三个加按钮常驻，能不能点由可用状态决定。按下去才说"选得不够"的话，
/// 用户会以为是自己点错了；灰着的那一颗配上按钮名，一眼就知道还差什么。
/// </para>
/// </remarks>
internal static class ConstraintEditorBinder
{
    /// <summary>加同层约束的按钮。</summary>
    public const string AddSameRankId = "constraint.add.same-rank";

    /// <summary>加对齐约束的按钮。</summary>
    public const string AddAlignId = "constraint.add.align";

    /// <summary>加层内次序约束的按钮。</summary>
    public const string AddOrderId = "constraint.add.order";

    /// <summary>每一行的删除按钮。各行的标识相同，测试按次序取第几个。</summary>
    public const string RemoveId = "constraint.remove";

    /// <summary>那一句结果。</summary>
    public const string NoteId = "constraint.note";

    /// <summary>搭出这一节的内容。</summary>
    public static Control Create(ConstraintEditorViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var rows = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };

        var empty = new TextBlock
        {
            Text = model.EmptyNote,
            FontSize = 11,
            Foreground = PanelPalette.Muted,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = !model.HasRows,
        };

        var note = new TextBlock
        {
            FontSize = 11,
            Foreground = PanelPalette.Muted,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = model.HasNote,
        };

        AutomationProperties.SetAutomationId(note, NoteId);

        var sameRank = Add(AddSameRankId, "加同层", () => model.AddSameRank());
        var align = Add(AddAlignId, "加对齐", () => model.AddAlign());
        var order = Add(AddOrderId, "加层内次序", () => model.AddOrder());
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(sameRank);
        buttons.Children.Add(align);
        buttons.Children.Add(order);

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
        stack.Children.Add(rows);
        stack.Children.Add(empty);
        stack.Children.Add(buttons);
        stack.Children.Add(note);

        void Apply()
        {
            rows.Children.Clear();

            foreach (var row in model.Rows)
            {
                rows.Children.Add(Row(model, row));
            }

            empty.IsVisible = !model.HasRows;
            note.Text = model.Note;
            note.IsVisible = model.HasNote;
            sameRank.IsEnabled = model.CanAddSameRank;
            align.IsEnabled = model.CanAddAlign;
            order.IsEnabled = model.CanAddOrder;
        }

        Apply();

        // 约束列表、可用状态与那一句结果都由视图模型给出，任何一项变了就整体重搭一次。
        // 逐项订阅的话，新加一个会变的东西而忘了订阅，界面上就是"点了没反应"。
        model.PropertyChanged += (_, _) => Apply();

        return stack;
    }

    private static Control Row(ConstraintEditorViewModel model, ConstraintRowViewModel row)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };

        var title = new TextBlock
        {
            Text = row.Title,
            FontSize = 12,
            Foreground = PanelPalette.Body,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var owner = new TextBlock
        {
            Text = row.OwnerText,
            FontSize = 11,
            Foreground = PanelPalette.Muted,
            Margin = new Thickness(6, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(owner, 1);
        grid.Children.Add(title);
        grid.Children.Add(owner);

        if (!row.IsRemovable)
        {
            // 自动与模型加的约束不给删除按钮，但行本身留着：用户要能看到"这条约束在"，
            // 不然他会以为自己的意图没被记录，然后反复加同一条。
            return grid;
        }

        var remove = new Button
        {
            Content = "删",
            FontSize = 11,
            Padding = new Thickness(8, 1),

            // 只读时按钮留在原地但灰掉，不整个藏起来：藏起来的话，用户会以为
            // 这一行没有删除入口，而不是"这份文档改不动"。
            IsEnabled = model.CanEdit,
        };

        AutomationProperties.SetAutomationId(remove, RemoveId);
        remove.Click += (_, _) => model.Remove(row);

        Grid.SetColumn(remove, 2);
        grid.Children.Add(remove);

        return grid;
    }

    private static Button Add(string id, string text, Action act)
    {
        var button = new Button
        {
            Content = text,
            FontSize = 11,
            Padding = new Thickness(8, 2),
            Margin = new Thickness(0, 0, 6, 0),
        };

        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => act();

        return button;
    }
}
