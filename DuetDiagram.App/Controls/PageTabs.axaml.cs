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
/// 画布上方的标签栏：一格一页，加上新建与删页。
/// </summary>
/// <remarks>
/// <para>
/// 页签在代码里搭，与属性面板、图层面板同一套做法：页数随文档变，
/// 一份界面标记写不出"每页一格"，而写死几格的话加减页面时得多改一处。
/// </para>
/// <para>
/// **页签只在增删页面时重建。** 翻页只换"哪一格亮着"——重建的话，
/// 每点一次整条栏会闪一下。
/// </para>
/// </remarks>
public sealed partial class PageTabs : UserControl
{
    private PageTabsViewModel? _built;

    public PageTabs() => InitializeComponent();

    /// <summary>标签栏的数据上下文，按它要的类型取。类型不对时为空。</summary>
    public PageTabsViewModel? Model => DataContext as PageTabsViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        var model = Model;

        if (ReferenceEquals(_built, model))
        {
            return;
        }

        _built = model;

        Tabs.Children.Clear();
        Actions.Children.Clear();

        if (model is null)
        {
            return;
        }

        BuildTabs(model);
        BuildActions(model);

        // 页签只在增删页面时换一批，所以这里只盯那一个属性。
        FieldEditBinder.Watch(model, nameof(PageTabsViewModel.Tabs), () => BuildTabs(model));
    }

    #region 页签

    private void BuildTabs(PageTabsViewModel model)
    {
        Tabs.Children.Clear();

        foreach (var tab in model.Tabs)
        {
            Tabs.Children.Add(Tab(model, tab));
        }
    }

    private static Control Tab(PageTabsViewModel model, PageTabViewModel tab)
    {
        var button = new Button
        {
            FontSize = 12,
            Padding = new Thickness(10, 3),
        };

        AutomationProperties.SetAutomationId(button, $"page.tab.{tab.Id}");
        ToolTip.SetTip(button, "看这一页。翻页会重新算一次布局——每一页上的元素不同，坐标也不同。");

        // 当前的这一格点不动：再点一下什么都不该发生，而不让它可点最省事——
        // 不必在点击里再判一次"是不是已经在看这一页了"。
        Bind(
            tab,
            () =>
            {
                var title = string.IsNullOrWhiteSpace(tab.Name) ? tab.Id : tab.Name;

                button.Content = tab.IsCurrent ? $"● {title}" : title;
                button.FontWeight = tab.IsCurrent ? FontWeight.SemiBold : FontWeight.Normal;
                button.IsEnabled = !tab.IsCurrent;
            },
            nameof(PageTabViewModel.IsCurrent),
            nameof(PageTabViewModel.Name));

        button.Click += (_, _) => model.Select(tab.Id);

        return button;
    }

    #endregion

    #region 新建与删页

    private void BuildActions(PageTabsViewModel model)
    {
        var name = new TextBox
        {
            FontSize = 12,
            Padding = new Thickness(6, 3),
            Width = 110,
            PlaceholderText = "新页的名字",
        };

        AutomationProperties.SetAutomationId(name, "page.new-name");
        ToolTip.SetTip(name, "新的一页叫什么，可以留空——留空时标签上显示页面标识。");

        var create = Button("page.new", "新建");
        create.Click += (_, _) => model.Create(name.Text);

        var delete = Button("page.delete", "删掉这一页");
        ToolTip.SetTip(delete, "删页不是删元素：这一页上的元素退回缺省页（次序最小的那一页）而不是跟着删。");
        delete.Click += (_, _) => model.DeleteCurrent();

        Actions.Children.Add(name);
        Actions.Children.Add(create);
        Actions.Children.Add(delete);

        Bind(model, () => create.IsEnabled = !model.HasReadOnlyNote, nameof(PageTabsViewModel.ReadOnlyNote));

        // 删最后一页是不行的：页面集合为空之后渲染层无页面可画。
        // 按钮先禁掉，用户不用点了才知道。
        Bind(model, () => delete.IsEnabled = model.CanDelete, nameof(PageTabsViewModel.CanDelete));

        Bind(
            model,
            () =>
            {
                var current = model.CurrentPageName ?? "（没有页面）";
                var count = model.CurrentElementCount;

                Note.Text = $"{current} · {count} 个元素";
            },
            nameof(PageTabsViewModel.CurrentPageName),
            nameof(PageTabsViewModel.CurrentElementCount));
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

    #endregion

    #region 搭格

    /// <summary>
    /// 立刻铺一遍，然后盯住那几个属性。
    /// </summary>
    /// <remarks>
    /// 先把当前值推一遍是必须的：订阅只管之后的改动，而控件是刚 new 出来的。
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

        Tabs = this.FindControl<StackPanel>(nameof(Tabs))
            ?? throw new InvalidOperationException("标签栏的界面标记里没有名为 Tabs 的容器");
        Actions = this.FindControl<StackPanel>(nameof(Actions))
            ?? throw new InvalidOperationException("标签栏的界面标记里没有名为 Actions 的容器");
        Note = this.FindControl<TextBlock>(nameof(Note))
            ?? throw new InvalidOperationException("标签栏的界面标记里没有名为 Note 的文本");
    }

    #endregion
}
