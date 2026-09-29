using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using DuetDiagram.App.Interaction;
using DuetDiagram.App.Services;
using DuetDiagram.App.ViewModels;

namespace DuetDiagram.App.Controls;

/// <summary>
/// 形状面板：一份形状清单，点一个就把选中的节点换成它，没选中就新建一个。
/// </summary>
/// <remarks>
/// <para>
/// 控件在代码里搭，与图层面板、调色板面板同一套做法。清单本身是静态的（形状库是编译期定下的），
/// 所以这里只在数据上下文换人时铺一次；之后每次重铺都是因为"哪一行是当前形状"变了。
/// </para>
/// <para>
/// **每一格既能点也能拖。** 点走 <see cref="ShapeRowViewModel.Apply"/>（换成这个形状，
/// 或者没选中时新建）；拖到画布上则由画布接落点，在落点新建——
/// 两件事的落点一个来自"用户在看哪一块"，一个来自"他松手在哪"，所以不是同一条路。
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
        ToolTip.SetTip(button, $"{row.Name}——选中了节点就换成这个形状，没选中就新建一个；也可以拖到画布上，在落点新建。");

        // 按钮上画的是一块形状预览加一个形状名，名字要说的是"按下去做什么"：
        // 只念形状名的话，用户听不出这一下会改掉画布上的什么。
        AccessibleName.Set(
            button,
            $"用「{row.Name}」形状",
            row.IsCurrent
                ? "选中的节点现在就是这一种形状。"
                : "选中了节点就换成这个形状，没选中就新建一个；也可以拖到画布上，在落点新建。");

        var dragged = false;

        button.Click += (_, _) =>
        {
            // 这一按已经起过一次拖：拖到画布上那一下已经在落点新建了节点，
            // 松手时若按钮还报一次点击，就会紧接着把新节点又改一遍形状。
            if (dragged)
            {
                return;
            }

            row.Apply();
        };

        StartDrag(button, row, () => dragged = true, () => dragged = false);

        return button;
    }

    /// <summary>
    /// 让这一格能起拖：拖到画布上，就在落点新建一个这个形状的节点。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **按下只记一笔，真的起拖要等指针移开一段。** 不设门槛的话，一次普通的点击
    /// （手指按下去时总会抖一两个像素）也会被当成拖拽，于是点一下什么也建不出来。
    /// </para>
    /// <para>
    /// 门槛比系统认定的拖拽距离略大：小一点的话，手抖仍会误起拖；
    /// 大一点则要拖出明显一段才开始，用户会以为没反应。
    /// </para>
    /// </remarks>
    /// <param name="button">起拖的那颗按钮。</param>
    /// <param name="row">这一格对应的形状。</param>
    /// <param name="began">真的起拖了。</param>
    /// <param name="reset">这一按翻篇了（下一次按下）。</param>
    private static void StartDrag(Button button, ShapeRowViewModel row, Action began, Action reset)
    {
        PointerPressedEventArgs? pressed = null;
        var origin = default(Point);

        button.PointerPressed += (_, e) =>
        {
            reset();

            if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
            {
                pressed = null;
                return;
            }

            pressed = e;
            origin = e.GetPosition(button);
        };

        button.PointerMoved += async (_, e) =>
        {
            if (pressed is not { } start)
            {
                return;
            }

            var moved = e.GetPosition(button) - origin;

            if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold)
            {
                return;
            }

            // 先清掉再起拖：指针移动会连续来很多次，不清的话一次按下会起出好几趟拖。
            pressed = null;
            began();

            await DragDrop.DoDragDropAsync(start, ShapeDrag.Pack(row.Name), DragDropEffects.Copy);
        };
    }

    /// <summary>认定"这是在拖而不是在点"的位移，单位与画布坐标一致。</summary>
    private const double DragThreshold = 4;

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
