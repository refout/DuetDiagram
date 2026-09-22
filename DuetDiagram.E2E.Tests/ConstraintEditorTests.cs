using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DuetDiagram.App;
using DuetDiagram.App.Services;
using DuetDiagram.App.ViewModels;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 布局约束的界面入口：面板上能加能删，加完之后下一次布局真的按约束走。
/// </summary>
/// <remarks>
/// <para>
/// 这一层只验"面板在真实窗口里接上了没有"：点按钮要能写出约束、改完之后坐标要真的变、
/// 归属不同的约束要区别对待。约束怎么校验、怎么合并、怎么进降级矩阵由命令层与布局层
/// 各自的测试管。
/// </para>
/// <para>
/// **按按钮上的字找控件，不按自动化标识。** 标识是界面内部的东西，
/// 而这里的用例说的正是"用户点的是那颗写着'加同层'的按钮"。
/// </para>
/// </remarks>
public sealed class ConstraintEditorTests
{
    #region 列表

    [Fact]
    [Trait("Category", "ConstraintEditor")]
    public async Task The_section_lists_the_constraints_the_document_has()
    {
        // 只能加不能看的话，用户会反复加同一条——他看不到自己已经加过。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            window.Properties.Constraints.Rows.Should().BeEmpty("示例文档里一条约束都没有");

            window.Session.AddConstraint(LayoutConstraintSpec.SameRank(["pass", "fail"]));

            var row = window.Properties.Constraints.Rows.Should().ContainSingle().Subject;

            row.Title.Should().Contain("同层").And.Contain("pass").And.Contain("fail");
            row.OwnerText.Should().Be("人工", "面板上加的是人工约束");
            row.IsRemovable.Should().BeTrue("人工加的能删");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ConstraintEditor")]
    public async Task Only_human_constraints_can_be_removed()
    {
        // 模型刚提的约束被人顺手删掉的话，模型那边不知道自己提的东西没了，
        // 于是下一轮它还会再提一次，而用户会以为删除没生效。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            window.Session.AddConstraint(
                LayoutConstraintSpec.SameRank(["pass", "fail"]),
                ConstraintOwner.Llm);

            var row = window.Properties.Constraints.Rows.Should().ContainSingle().Subject;

            row.OwnerText.Should().Be("模型");
            row.IsRemovable.Should().BeFalse("模型提的约束不归界面管");

            window.Properties.Constraints.Remove(row).IsSuccess.Should().BeFalse("界面删不动它");
            window.Session.Document.Layout.SameRank.Should().ContainSingle("被拒的删除不该改动文档");

            // 界面上也不该给它一颗删除按钮——有按钮却点了没用，比没有按钮更让人困惑。
            RemoveButtons(window).Should().BeEmpty();

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ConstraintEditor")]
    public async Task A_stale_constraint_is_still_listed_and_can_be_removed()
    {
        // 约束指向的节点被删掉之后，那条约束留在文档里。它既不能把界面搞崩，
        // 也不能从列表上悄悄消失——用户要能看到"这条没用了"，然后删掉它。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var session = window.Session;

            session.AddConstraint(LayoutConstraintSpec.SameRank(["pass", "fail"]));

            session.Bus.Execute(new RemoveNodeCommand("fail"));
            session.Reload();
            window.Properties.Refresh();

            var row = window.Properties.Constraints.Rows.Should().ContainSingle().Subject;

            row.Title.Should().Contain("fail", "约束还留在文档里，列表上就该还看得见它");

            RemoveButtons(window).Should().ContainSingle();
            ClickRemove(window, 0);

            session.Document.Layout.SameRank.Should().BeEmpty("按了删除，那条约束就该走");

            window.Close();
        });
    }

    #endregion

    #region 加

    [Fact]
    [Trait("Category", "ConstraintEditor")]
    public async Task Adding_a_same_rank_constraint_from_the_panel_writes_to_the_document()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            window.Session.Select("pass");
            window.Session.Toggle("end");

            var before = window.Session.Document.Version;

            Click(window, "加同层");

            var constraint = window.Session.Document.Layout.SameRank.Should().ContainSingle().Subject;

            constraint.Value.Nodes.Should().BeEquivalentTo(["pass", "end"]);
            constraint.Owner.Should().Be(ConstraintOwner.Human, "面板上加的是人工约束");
            window.Session.Document.Version.Should().BeGreaterThan(before, "改动要经命令层写回文档");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ConstraintEditor")]
    public async Task A_same_rank_constraint_puts_both_nodes_on_one_layer()
    {
        // 验收里那句"改完之后下一次布局确实按约束走"说的就是这一条：
        // 只写进文档而不重排的话，画面上什么都不会变。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            var before = window.Session.Scene.Layout;
            var pass = before.Find("pass")!;
            var end = before.Find("end")!;

            end.Y.Should().NotBeApproximately(pass.Y, 1, "约束之前，结束节点在下一层");

            window.Session.Select("pass");
            window.Session.Toggle("end");
            Click(window, "加同层");

            var after = window.Session.Scene.Layout;

            after.Find("end")!.Y.Should().BeApproximately(
                after.Find("pass")!.Y,
                1,
                "同层约束之后，两个节点落在同一层上");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ConstraintEditor")]
    public async Task An_align_constraint_pulls_the_node_onto_the_other_ones_column()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            var before = window.Session.Scene.Layout;

            before.Find("pass")!.X.Should().NotBeApproximately(
                before.Find("start")!.X,
                1,
                "约束之前，通过节点偏在左边");

            window.Session.Select("start");
            window.Session.Toggle("pass");
            Click(window, "加对齐");

            var after = window.Session.Scene.Layout;

            after.Find("pass")!.X.Should().BeApproximately(
                after.Find("start")!.X,
                1,
                "对齐之后，通过节点落到开始节点那一列上");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ConstraintEditor")]
    public async Task An_order_constraint_is_built_from_the_selection_order()
    {
        // 次序在文档里存的是出边标识，选中的先后就是它要的先后。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            window.Session.Select("check");
            window.Session.Toggle("pass");
            window.Session.Toggle("fail");

            Click(window, "加层内次序");

            var constraint = window.Session.Document.Layout.Order.Should().ContainSingle().Subject;

            constraint.Value.NodeId.Should().Be("check", "主语是第一个选中的那个");
            constraint.Value.Order.Should().Equal(["e2", "e3"], "顺序就是选中的先后");
            constraint.Owner.Should().Be(ConstraintOwner.Human);

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ConstraintEditor")]
    public async Task The_add_buttons_are_greyed_out_until_the_selection_can_carry_them()
    {
        // 让用户点下去再收到一句"选得不够"的话，他会以为是自己点错了。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var constraints = window.Properties.Constraints;

            constraints.CanAddSameRank.Should().BeFalse("没选中时加不了");
            constraints.CanAddOrder.Should().BeFalse();

            window.Session.Select("pass");

            constraints.CanAddSameRank.Should().BeFalse("只选中一个时加不了");
            NamedButton(window, "加同层").IsEnabled.Should().BeFalse();

            window.Session.Toggle("fail");

            constraints.CanAddSameRank.Should().BeTrue("两个节点可以同层");
            constraints.CanAddAlign.Should().BeTrue("两个节点也可以对齐");
            constraints.CanAddOrder.Should().BeFalse("层内次序还要一个主语");
            NamedButton(window, "加同层").IsEnabled.Should().BeTrue();

            window.Session.Select("check");
            window.Session.Toggle("pass");
            window.Session.Toggle("fail");

            constraints.CanAddOrder.Should().BeTrue("主语加两个有边的目标，次序才描述得出来");
            NamedButton(window, "加层内次序").IsEnabled.Should().BeTrue();

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ConstraintEditor")]
    public async Task A_constraint_that_points_at_a_missing_node_is_refused()
    {
        // 约束指向已删除的节点时不能静默留着，也不能崩：走校验路径给一条结构化错误。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var before = window.Session.Document.Version;

            var result = window.Session.AddConstraint(LayoutConstraintSpec.SameRank(["pass", "ghost"]));

            result.IsSuccess.Should().BeFalse("成员里有一个不存在的节点，这条约束写不进去");
            result.Errors.Should().ContainSingle()
                .Which.Code.Should().Be(ErrorCodes.LayoutNodeMissing, "要给出说得清原因的错误码");
            window.Session.Document.Version.Should().Be(before, "被拒的改动不该留下任何痕迹");
            window.Session.Document.Layout.SameRank.Should().BeEmpty();

            window.Close();
        });
    }

    #endregion

    #region 删与撤销

    [Fact]
    [Trait("Category", "ConstraintEditor")]
    public async Task Removing_a_constraint_from_the_panel_takes_it_out()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();

            window.Session.Select("pass");
            window.Session.Toggle("fail");
            Click(window, "加对齐");

            var before = window.Session.Document.Version;

            RemoveButtons(window).Should().ContainSingle("只有人工加的那一条能删");
            ClickRemove(window, 0);

            window.Session.Document.Layout.Align.Should().BeEmpty("按了删除，那条约束就该走");
            window.Session.Document.Version.Should().BeGreaterThan(before, "删除也是一条命令");
            window.Properties.Constraints.Rows.Should().BeEmpty("列表跟着文档走");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ConstraintEditor")]
    public async Task Undo_takes_the_constraint_back_out()
    {
        // 只能加不能撤的话，用户加错一条就只能删，而删是另一条命令，
        // 与"我这一步不想要了"不是一回事。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var session = window.Session;

            session.Select("pass");
            session.Toggle("fail");
            Click(window, "加同层");

            session.Document.Layout.SameRank.Should().ContainSingle();

            session.Bus.Undo().IsSuccess.Should().BeTrue();
            session.Reload();

            session.Document.Layout.SameRank.Should().BeEmpty("撤销把那条约束收回去了");
            window.Properties.Constraints.Rows.Should().BeEmpty();

            window.Close();
        });
    }

    #endregion

    #region 拖动生成的次序

    [Fact]
    [Trait("Category", "ConstraintEditor")]
    public async Task Dropping_a_node_on_its_sibling_orders_it()
    {
        // 落在兄弟节点上说的是"把我排到它旁边"，落定的是一条相对次序；
        // 落在空白处说的才是"把我放这儿"，那才写固定位置。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var session = window.Session;

            var placed = session.Scene.Layout.Find("pass")!;
            var start = new DrawPoint(placed.X, placed.Y);

            session.BeginDrag("pass", additive: false, start);
            session.UpdateDrag(new DrawPoint(start.X + 40, start.Y + 10));

            var commit = session.CommitDrag("fail");

            commit.Kind.Should().Be(DiagramSession.CommitKind.Ordered);
            session.PinnedNodes.Should().BeEmpty("落定的是次序，不是位置");

            var constraint = session.Document.Layout.Order.Should().ContainSingle().Subject;

            constraint.Value.NodeId.Should().Be("check", "两个节点共同的上级才是这条次序的主语");
            constraint.Value.Order.Should().Equal(["e3", "e2"], "通过那条边被排到了失败后面");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "ConstraintEditor")]
    public async Task Dropping_twice_keeps_one_order_constraint()
    {
        // 每拖一次追加一条的话，同一对节点上会积下几十条互相矛盾的次序，
        // 而求解器按列表顺序取第一条——用户后来拖的那几下全都白做。
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var session = window.Session;

            Drop(window, "pass", "fail");
            Drop(window, "pass", "fail");

            var constraint = session.Document.Layout.Order.Should().ContainSingle().Subject;

            constraint.Value.Order.Should().Equal(["e3", "e2"], "第二次落定改的是同一条");

            window.Close();
        });
    }

    /// <summary>把某个节点拖到另一个节点上，走完整条拖动路径。</summary>
    private static void Drop(MainWindow window, string moved, string target)
    {
        var placed = window.Session.Scene.Layout.Find(moved)!;
        var start = new DrawPoint(placed.X, placed.Y);

        window.Session.BeginDrag(moved, additive: false, start);
        window.Session.UpdateDrag(new DrawPoint(start.X + 40, start.Y + 10));
        window.Session.CommitDrag(target);
    }

    #endregion

    #region 找控件

    /// <summary>按按钮上那行字取按钮。</summary>
    private static Button NamedButton(MainWindow window, string label) =>
        window.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => string.Equals(button.Content as string, label, StringComparison.Ordinal));

    /// <summary>面板上的删除按钮，按行序排列。</summary>
    private static IReadOnlyList<Button> RemoveButtons(MainWindow window) =>
        [.. window.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => string.Equals(button.Content as string, "删", StringComparison.Ordinal))];

    private static void Click(MainWindow window, string label) =>
        NamedButton(window, label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void ClickRemove(MainWindow window, int index) =>
        RemoveButtons(window)[index].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    #endregion
}
