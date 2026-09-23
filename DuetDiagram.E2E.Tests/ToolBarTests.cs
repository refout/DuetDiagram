using Avalonia.Controls;
using DuetDiagram.App;
using DuetDiagram.App.Services;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 工具栏：档的顺序、每一条点下去都有反馈、只读时写入类条目点不动。
/// </summary>
/// <remarks>
/// <para>
/// 这一层验的是"接上了没有"：按钮在不在、点了走不走命令层、不能点的时候说不说为什么。
/// 每条命令自己怎么校验、怎么改文档由核心层的用例管。
/// </para>
/// <para>
/// **判据写成"要么变了、要么说了话"，不写成"版本加一"。** 有几条合法的条目本来就不改文档
/// （重排、全选），还有几条在某些状态下是合法的无操作（把方向设成它已经是的那个）。
/// 拿"版本加一"当判据的话，那几条会被逼着去改一个不该改的东西。
/// </para>
/// </remarks>
public sealed class ToolBarTests
{
    #region 摆出来的东西

    [Fact]
    [Trait("Category", "ToolBar")]
    public async Task The_tool_bar_shows_every_group_in_a_fixed_order()
    {
        // 档的顺序是用户找东西的位置感，不该随注册顺序变。哪一档里有哪些条目由注册表说了算，
        // 但"档与档的先后"写死在这里——它是界面约定，不是注册表的产物。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            // 写成数组而不是一路逗号分隔：那一路会绑到 params 重载上，
            // 于是末尾那句说明被当成第五个期望元素，报的是"少了一项"。
            MenuGroups.ToolBarOrder.Should().Equal(
                [MenuGroups.Edit, MenuGroups.Align, MenuGroups.Layout, MenuGroups.Export],
                "工具栏上从左到右就是这四档");

            var expected = MenuGroups.ToolBarOrder
                .SelectMany(group => MenuRegistry.Default.On(MenuSurface.ToolBar, group))
                .Select(entry => entry.Id);

            HeadlessFixture.ToolBar(window).Ids.Should().Equal(expected);

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ToolBar")]
    public async Task Every_entry_either_changes_something_or_says_why_not()
    {
        // 点下去什么都不发生的条目是最糟的一种：用户以为程序卡了，然后反复点。
        // 逐条点一遍，每条要么真的改动了什么，要么在状态栏留下一句话。
        await HeadlessFixture.Run(() =>
        {
            var toolbar = MenuGroups.ToolBarOrder
                .SelectMany(group => MenuRegistry.Default.On(MenuSurface.ToolBar, group))
                .ToList();

            toolbar.Should().NotBeEmpty("工具栏上总得有条目");

            foreach (var entry in toolbar)
            {
                // 一条一开一关：留着不关的话，前面那些窗口占着的租约与原生资源会一直攒着，
                // 而后面某一条用例的失败会指向资源耗尽，看不出是这里漏了。
                var window = Seeded();

                try
                {
                    var context = new MenuContext(window);
                    var refusal = entry.Refusal(context);

                    // 先清掉播种时留下的那句话：不清的话，"状态栏有话"这个判据会被它顶掉。
                    window.Status.Clear();

                    var before = Snapshot.Of(window);

                    window.Invoke(entry.Id);

                    var after = Snapshot.Of(window);

                    if (refusal is not null)
                    {
                        after.Should().Be(before, $"「{entry.Label}」被拒了就不该改动任何东西");
                        window.Status.HasMessage.Should().BeTrue($"「{entry.Label}」点不动的时候要说一句为什么");
                        window.Status.Message.Should().Contain(refusal);

                        continue;
                    }

                    var changed = after != before;
                    var spoke = window.Status.HasMessage;

                    (changed || spoke).Should().BeTrue($"「{entry.Label}」点了之后要么变、要么说话，不能什么都不发生");
                }
                finally
                {
                    window.Close();
                }
            }
        });
    }

    [Fact]
    [Trait("Category", "ToolBar")]
    public async Task A_button_that_cannot_be_clicked_carries_its_reason()
    {
        // 只灰掉不说为什么，用户会以为程序坏了。理由挂在提示上，悬停能看到。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            // 没有选中时，删除点不动。
            var delete = HeadlessFixture.ToolBar(window).Find("edit.delete");

            delete.Should().NotBeNull();
            delete!.IsEnabled.Should().BeFalse();
            ToolTip.GetTip(delete).Should().Be("先选中要删的元素");

            window.Session.Select("pass");

            HeadlessFixture.ToolBar(window).Refresh();
            delete.IsEnabled.Should().BeTrue("选中之后就能删了");

            window.Close();
        });
    }

    #endregion

    #region 助手

    /// <summary>
    /// 开一个"每条条目都点得动"的窗口。
    /// </summary>
    /// <remarks>
    /// 选中两个节点、改两次标签、再退回一步，这样撤销与重做两条都亮着；
    /// 再进一次手动布局，这样"重排"点下去能看出它把手动那档解掉了。
    /// 不播种的话，撤销、重做、删除、对齐这几条一开窗口就是灰的，
    /// 而"逐条点一遍"这件事就退化成只点了剩下那几条。
    /// </remarks>
    private static MainWindow Seeded()
    {
        var window = HeadlessFixture.Open();

        window.Session.SetSelection(["pass", "fail"]);
        window.Session.Apply("label", "第一次");
        window.Session.Apply("label", "第二次");
        window.Session.Undo();
        window.Session.EnterManualLayout();

        return window;
    }

    /// <summary>
    /// 点一下之前与之后能观察到的东西。
    /// </summary>
    /// <remarks>
    /// 绘制列表版本也算在内：重排不改文档，但它换了画面上那份东西。
    /// 只看文档版本的话，"重排"会被判成什么都没发生。
    /// </remarks>
    private readonly record struct Snapshot(
        int Version,
        int SceneVersion,
        string Selection,
        bool ManualLayout,
        bool Diagnostics,
        double NodeSpacing,
        double LayerSpacing)
    {
        public static Snapshot Of(MainWindow window)
        {
            var layout = window.Session.Document.Layout;

            return new Snapshot(
                window.Session.Document.Version,
                window.Session.SceneVersion,
                string.Join(',', window.Session.SelectedIds),
                window.Session.ManualLayoutActive,
                window.Model.Diagnostics.IsOpen,
                layout.NodeSpacing,
                layout.LayerSpacing);
        }
    }

    #endregion
}
