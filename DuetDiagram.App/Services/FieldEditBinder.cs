using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DuetDiagram.App.ViewModels;

namespace DuetDiagram.App.Services;

/// <summary>
/// 面板上共用的一小套颜色与尺寸。
/// </summary>
/// <remarks>
/// 放在一处而不是各文件各写一遍：面板上几十个控件用同一套取值，
/// 散开写之后改一次颜色要翻好几个文件，而漏掉的那一处表现成"这一块颜色不对"，
/// 看起来像特意那么设计的。
/// </remarks>
internal static class PanelPalette
{
    /// <summary>字段名与分节标题。</summary>
    public static readonly IBrush Label = Brush.Parse("#5b6675");

    /// <summary>正文。</summary>
    public static readonly IBrush Body = Brush.Parse("#2b3440");

    /// <summary>只读内容与说明。</summary>
    public static readonly IBrush Muted = Brush.Parse("#7a8492");

    /// <summary>字段上的错误。</summary>
    public static readonly IBrush Error = Brush.Parse("#b4232a");

    /// <summary>字段名那一列的宽度。固定列宽，值那一列才能对齐。</summary>
    public const double LabelWidth = 92;
}

/// <summary>
/// 把字段与编辑它的控件接起来。
/// </summary>
/// <remarks>
/// <para>
/// 每个字段给出一整行：名字、编辑器、错误提示。名字那一列固定宽度，
/// 值那一列因此是对齐的——对不齐时用户得逐行去找，而这一屏上字段有二十几个。
/// </para>
/// <para>
/// **控件在这里搭而不是在界面标记里。** 字段表在编译期看不到，一行一个模板的话，
/// 要么给每种控件各写一套模板再由框架按类型挑，要么在一个模板里塞五种控件、
/// 靠显隐挑出一种——前者要靠类型匹配，后者每行都白白多出四个从不出现的控件。
/// 搭出来还有一个好处：提交的时机（失焦、回车、选中项变了）集中在这一处，
/// 而不是散在若干份模板里。
/// </para>
/// <para>
/// 值的推送是单向的：字段的值被换掉时推进控件。控件自己不往回写，
/// 只在用户明确改完时报告一次——每敲一个字都往回写的话，输入 <c>1.5</c> 的过程中
/// 会先写进去一个 <c>1.</c>，那一段不合法，界面会当场弹一条错误。
/// </para>
/// </remarks>
internal static class FieldEditBinder
{
    /// <summary>选择控件里代表"没有设置"的那一项。</summary>
    private const string NoValue = "（没有设置）";

    /// <summary>取值不一致时的那一句提示。</summary>
    private const string MixedValue = "多个值";

    /// <summary>搭出一行：字段名、编辑器、错误提示、取值不一致的提示。</summary>
    public static Control Create(PropertyFieldViewModel field)
    {
        ArgumentNullException.ThrowIfNull(field);

        var error = new TextBlock
        {
            FontSize = 11,
            Foreground = PanelPalette.Error,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        // 取值不一致的提示挂在编辑器下面，与错误提示分开：两者可以同时成立
        // （改了一次没写进去，而那几个元素本来就取值不一），合成一条就少说了一半。
        var mixed = new TextBlock
        {
            Text = MixedValue,
            FontSize = 11,
            Foreground = PanelPalette.Muted,
            IsVisible = field.IsMixed,
        };

        var body = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };

        var editor = Editor(field);

        // 自动化 ID 让无头测试能按字段名找到控件，不用去猜它在视觉树里的位置。
        AutomationProperties.SetAutomationId(editor, field.Field);

        ApplyReadOnly(field, editor);

        body.Children.Add(editor);
        body.Children.Add(mixed);
        body.Children.Add(error);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions($"{PanelPalette.LabelWidth},*") };
        var label = new TextBlock
        {
            Text = field.Label,
            FontSize = 12,
            Foreground = PanelPalette.Label,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        Grid.SetColumn(body, 1);
        row.Children.Add(label);
        row.Children.Add(body);

        Watch(field, nameof(PropertyFieldViewModel.Error), () =>
        {
            error.Text = field.Error;
            error.IsVisible = field.HasError;
        });

        Watch(field, nameof(PropertyFieldViewModel.IsMixed), () => mixed.IsVisible = field.IsMixed);
        Watch(field, nameof(PropertyFieldViewModel.IsReadOnly), () => ApplyReadOnly(field, editor));

        return row;
    }

    /// <summary>
    /// 把只读状态推到编辑器上。
    /// </summary>
    /// <remarks>
    /// 文本框用只读档而不是禁用档：禁用的框连选都选不中，用户没法把里面的 id 或标签
    /// 复制出来贴到别处；只读的框还能选中、还能复制，只是改不动。勾选框与下拉没有
    /// 这一档，只能整体禁用。
    /// </remarks>
    private static void ApplyReadOnly(PropertyFieldViewModel field, Control editor)
    {
        if (editor is TextBox box)
        {
            box.IsReadOnly = field.IsReadOnly;

            return;
        }

        editor.IsEnabled = !field.IsReadOnly;
    }

    /// <summary>
    /// 盯住一个属性，它被换掉时把新值推进控件。
    /// </summary>
    /// <remarks>
    /// 用订阅而不是绑定：程序集要能原生编译，而界面标记里的绑定要编译期就能解析出类型，
    /// 这些控件是在代码里搭的，标记看不见它们。
    /// </remarks>
    public static void Watch(INotifyPropertyChanged source, string property, Action apply)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(apply);

        source.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, property, StringComparison.Ordinal))
            {
                apply();
            }
        };
    }

    private static Control Editor(PropertyFieldViewModel field) => field.Editor switch
    {
        PropertyEditor.Multiline => Multiline(field),
        PropertyEditor.Flag => Flag(field),
        PropertyEditor.Choice => Choice(field),
        _ => SingleLine(field),
    };

    #region 文本

    private static Control SingleLine(PropertyFieldViewModel field)
    {
        var box = new TextBox
        {
            Text = field.Value ?? string.Empty,
            FontSize = 12,
            Padding = new Thickness(6, 3),
        };

        box.LostFocus += (_, _) => Commit(field, box.Text);

        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            Commit(field, box.Text);
            e.Handled = true;
        };

        Watch(field, nameof(PropertyFieldViewModel.Value), () =>
        {
            var text = field.Value ?? string.Empty;

            // 值没变时不动控件：动一下会把光标顶到末尾，用户正改在中间的话光标就跳了。
            if (!string.Equals(box.Text, text, StringComparison.Ordinal))
            {
                box.Text = text;
            }
        });

        return box;
    }

    private static Control Multiline(PropertyFieldViewModel field)
    {
        var box = new TextBox
        {
            Text = field.Value ?? string.Empty,
            FontSize = 12,
            Padding = new Thickness(6, 3),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 64,
        };

        // 多行框不在回车时提交：那一键在这里是换行。
        box.LostFocus += (_, _) => Commit(field, box.Text);

        Watch(field, nameof(PropertyFieldViewModel.Value), () =>
        {
            var text = field.Value ?? string.Empty;

            if (!string.Equals(box.Text, text, StringComparison.Ordinal))
            {
                box.Text = text;
            }
        });

        return box;
    }

    /// <summary>
    /// 把框里的字报上去。
    /// </summary>
    /// <remarks>
    /// 与当前值相同就不报。失焦与回车都会走到这里，用户按回车提交之后又点了别处，
    /// 那就是两次同样的提交——第二次白走一遍校验与审计，还多出一条"没有变化"的记录。
    /// </remarks>
    private static void Commit(PropertyFieldViewModel field, string? text)
    {
        if (string.Equals(text, field.Value ?? string.Empty, StringComparison.Ordinal))
        {
            return;
        }

        field.Commit(text);
    }

    #endregion

    #region 开关

    /// <summary>
    /// 三态开关。
    /// </summary>
    /// <remarks>
    /// 三态而不是两态：这个字段的空值表示"没有设置"，与"设成假"是两回事——
    /// 设成假是明确要求它不斜体，没有设置则跟着样式令牌走。
    /// 两态的勾选框分不出这两者，用户取消勾选之后会得到"明确要求不斜体"，
    /// 而样式令牌里写着斜体的那些节点从此不再跟随令牌。
    /// </remarks>
    private static Control Flag(PropertyFieldViewModel field)
    {
        var box = new CheckBox
        {
            IsThreeState = true,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetTip(box, "三态：勾上是真，空框是假，实心方框表示没有设置（跟随样式令牌）。");

        Sync(field, box);

        box.IsCheckedChanged += (_, _) =>
        {
            var value = box.IsChecked switch
            {
                true => "true",
                false => "false",
                _ => null,
            };

            // 跟着外面同步过来的那一次不是用户改的。不比一下的话，每次换选中
            // 都会给文档发一条"改成原值"的命令，而它本身什么也不做。
            if (!string.Equals(value, field.Value, StringComparison.Ordinal))
            {
                field.Commit(value);
            }
        };

        return box;
    }

    private static void Sync(PropertyFieldViewModel field, CheckBox box)
    {
        void Apply()
        {
            var wanted = field.IsMixed ? null : FlagValue(field.Value);

            if (box.IsChecked != wanted)
            {
                box.IsChecked = wanted;
            }

            // 只有"确实没有设置"才把那一句显示出来。取值不一致时下面另有一行提示，
            // 在这里再写一句"没有设置"就把两件事说成了一件事。
            box.Content = !field.IsMixed && field.Value is null ? NoValue : null;
        }

        Apply();

        Watch(field, nameof(PropertyFieldViewModel.Value), Apply);
        Watch(field, nameof(PropertyFieldViewModel.IsMixed), Apply);
    }

    private static bool? FlagValue(string? text) =>
        bool.TryParse(text, out var value) ? value : null;

    #endregion

    #region 选择

    private static Control Choice(PropertyFieldViewModel field)
    {
        var combo = new ComboBox
        {
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        void Repopulate()
        {
            var items = new List<string>();

            if (field.AllowEmpty)
            {
                items.Add(NoValue);
            }

            items.AddRange(field.Choices);

            // 自定义取值：当前值不在可选取值里时补一项，而不是显示成空。
            // 样式令牌那一格的值可以指向任何串（认不出的令牌按元素自己的样式兜底），
            // 显示成空的话，用户会以为这个字段本来就没设过。
            if (field.Spec.AllowCustom
                && field.Value is { } custom
                && !items.Contains(custom, StringComparer.Ordinal))
            {
                items.Add(custom);
            }

            combo.ItemsSource = items;
            Sync(field, combo);
        }

        Repopulate();

        // 选项长在文档里的字段（样式令牌）在重读时会换一批取值，控件要跟着换。
        Watch(field, nameof(PropertyFieldViewModel.Choices), Repopulate);

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not string selected)
            {
                return;
            }

            var value = selected == NoValue ? null : selected;

            if (!string.Equals(value, field.Value, StringComparison.Ordinal))
            {
                field.Commit(value);
            }
        };

        return combo;
    }

    private static void Sync(PropertyFieldViewModel field, ComboBox combo)
    {
        void Apply()
        {
            var wanted = ChoiceValue(field);

            if (!string.Equals(combo.SelectedItem as string, wanted, StringComparison.Ordinal))
            {
                combo.SelectedItem = wanted;
            }
        }

        Apply();

        Watch(field, nameof(PropertyFieldViewModel.Value), Apply);
        Watch(field, nameof(PropertyFieldViewModel.IsMixed), Apply);
    }

    /// <summary>
    /// 当前值在下拉里对应哪一项。
    /// </summary>
    /// <remarks>
    /// 取值不一致时给空，让下拉显示成空的。显示成"没有设置"的话，
    /// 用户会以为这几个元素在这个字段上都没值，而实际上是各不一样。
    /// 值不在可选取值里时也给空，不硬塞一项——塞进去的那一项会一直留在下拉里，
    /// 看起来像这个字段本来就有这么一个取值。
    /// </remarks>
    private static string? ChoiceValue(PropertyFieldViewModel field)
    {
        if (field.IsMixed)
        {
            return null;
        }

        if (field.Value is null)
        {
            return field.AllowEmpty ? NoValue : null;
        }

        // 允许自定义取值的字段，值不在选项里也照常显示——下拉里会补上当前值那一项。
        return field.Spec.AllowCustom
            || field.Choices.Contains(field.Value, StringComparer.Ordinal)
            ? field.Value
            : null;
    }

    #endregion
}
