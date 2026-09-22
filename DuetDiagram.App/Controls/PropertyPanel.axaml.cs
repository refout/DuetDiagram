using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using DuetDiagram.App.Services;
using DuetDiagram.App.ViewModels;

namespace DuetDiagram.App.Controls;

/// <summary>
/// 选中元素的属性面板。
/// </summary>
/// <remarks>
/// <para>
/// 六节与二十几个字段都由视图模型给出，控件在代码里搭（见 <see cref="FieldEditBinder"/>）。
/// 这一层只做两件事：把分节按顺序摆出来，以及在换选中时**不重建**它们——
/// 换选中只是把新值推进已有控件。重建的话，连续点选时面板会明显卡顿，
/// 而连续点选正是用户在找元素时的常态。
/// </para>
/// <para>
/// 换数据上下文时才重建。那是"换了一份文档"，与换选中是两回事。
/// </para>
/// </remarks>
public sealed partial class PropertyPanel : UserControl
{
    private PropertyPanelViewModel? _built;

    public PropertyPanel() => InitializeComponent();

    /// <summary>面板的数据上下文，按它要的类型取。类型不对时为空。</summary>
    public PropertyPanelViewModel? Model => DataContext as PropertyPanelViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        var model = Model;

        if (ReferenceEquals(_built, model))
        {
            return;
        }

        _built = model;

        Build(model);
    }

    private void Build(PropertyPanelViewModel? model)
    {
        Body.Children.Clear();

        if (model is null)
        {
            return;
        }

        foreach (var section in model.Sections)
        {
            Body.Children.Add(Section(section, model));
        }
    }

    private static Control Section(PropertySectionViewModel section, PropertyPanelViewModel model)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };

        stack.Children.Add(new TextBlock
        {
            Text = section.Title,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = PanelPalette.Label,
        });

        // 布局约束这一节不是一串字段，也不是一段文字，而是一个能加能删的列表。
        // 按标题认它，标题本身是常量——写成字面量的话，改一次标题编辑器就悄悄挂不上，
        // 而界面上只是少了一块，看起来像"这一轮没做"。
        if (string.Equals(section.Title, PropertyFieldCatalog.ConstraintSection, StringComparison.Ordinal))
        {
            stack.Children.Add(ConstraintEditorBinder.Create(model.Constraints));
            return stack;
        }

        if (section.IsReadOnly)
        {
            stack.Children.Add(ReadOnly(section));
            return stack;
        }

        foreach (var field in section.Fields)
        {
            stack.Children.Add(FieldEditBinder.Create(field));
        }

        return stack;
    }

    /// <summary>
    /// 只读的那几节。
    /// </summary>
    /// <remarks>
    /// 做成一段灰字，与上面那些带边框的输入框一眼分得开。长得一样的话，
    /// 用户会在上面点半天，然后以为面板坏了——而实际上这几节这一轮本来就不能改。
    /// 一段文字而不是一串行控件：行的条数随选中的元素变，做成控件列表就得在
    /// 每次换选中时增删控件，那正是这个面板要避免的事。
    /// </remarks>
    private static Control ReadOnly(PropertySectionViewModel section)
    {
        var text = new TextBlock
        {
            Text = section.Text,
            FontSize = 12,
            Foreground = PanelPalette.Muted,
            TextWrapping = TextWrapping.Wrap,
        };

        FieldEditBinder.Watch(section, nameof(PropertySectionViewModel.Text), () => text.Text = section.Text);

        return text;
    }

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

        Body = this.FindControl<StackPanel>(nameof(Body))
            ?? throw new InvalidOperationException("属性面板的界面标记里没有名为 Body 的容器");
    }
}
