using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 页面、标签、动作三条线各自的增删命令。
/// </summary>
/// <remarks>
/// <para>
/// 这一组最容易写错的是**两个变更标志**。三样东西的哈希口径互不相同：
/// 页面与标签只进视觉哈希，动作**两个哈希都不进**。所以"新建一个动作"报的是
/// 两个标志都假——而那不是无操作：文档内容确实变了，版本照推、历史照进、广播照发，
/// 只是宿主那一侧既不用重排也不用重绘。
/// </para>
/// <para>
/// 另一条容易写错的是**撤销时的位置**。页面集合的位置是加入顺序，
/// 删掉中间那一页再撤销，必须插回原来那一格；一律追加到末尾的话，
/// 次序与删除前不同，而两个哈希都按标识排序后遍历，这个错在哈希上看不出来。
/// </para>
/// </remarks>
public sealed class PageTagActionCommandTests
{
    #region 页面

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Creating_a_page_gives_it_the_next_order()
    {
        using var harness = new Harness();

        harness.CreatePage("p1", "第一页").IsEffectiveSuccess.Should().BeTrue();
        harness.CreatePage("p2", "第二页").IsEffectiveSuccess.Should().BeTrue();

        harness.Document.Pages.Should().HaveCount(2);
        harness.Document.Pages[0].Order.Should().Be(0);
        harness.Document.Pages[1].Order.Should().Be(1);
        harness.Document.Pages[1].Name.Should().Be("第二页");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_new_page_is_a_visual_change_only()
    {
        using var harness = new Harness();

        var result = harness.CreatePage("p1", "第一页");

        // 页面进的是视觉哈希：加一页不改变任何坐标。报成结构变更的话，
        // 新建一页就要把整张图重排一遍。
        result.StructuralChanged.Should().BeFalse();
        result.VisualChanged.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_page_id_that_collides_with_a_node_is_rejected()
    {
        using var harness = new Harness();
        harness.AddNode("x");

        var before = harness.Snapshot();

        // 九个集合共用一个命名空间。
        var result = harness.CreatePage("x", "页面");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.DuplicateId);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Deleting_a_page_removes_it()
    {
        using var harness = new Harness();
        harness.CreatePage("p1", "第一页").IsEffectiveSuccess.Should().BeTrue();
        harness.CreatePage("p2", "第二页").IsEffectiveSuccess.Should().BeTrue();

        harness.DeletePage("p1").IsEffectiveSuccess.Should().BeTrue();

        harness.Document.Pages.Select(p => p.Id).Should().Equal("p2");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void The_last_page_cannot_be_deleted()
    {
        using var harness = new Harness();
        harness.CreatePage("p1", "唯一的一页").IsEffectiveSuccess.Should().BeTrue();

        var before = harness.Snapshot();

        // 页面集合为空之后渲染层无页面可画，而那不是一次"删掉了一个东西"能解释的状态。
        var result = harness.DeletePage("p1");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.PageRequired);
        harness.Document.Pages.Should().HaveCount(1);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Deleting_a_missing_page_is_rejected()
    {
        using var harness = new Harness();
        harness.CreatePage("p1", "第一页").IsEffectiveSuccess.Should().BeTrue();

        var before = harness.Snapshot();

        var result = harness.DeletePage("查无此页");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.PageMissing);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undoing_a_page_creation_puts_the_content_back()
    {
        using var harness = new Harness();
        harness.CreatePage("p1", "第一页").IsEffectiveSuccess.Should().BeTrue();

        var visualBefore = harness.Document.VisualHash;

        harness.CreatePage("p2", "第二页");
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Document.Pages.Select(p => p.Id).Should().Equal("p1");
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undoing_a_page_deletion_puts_it_back_where_it_was()
    {
        using var harness = new Harness();
        harness.CreatePage("p1", "一").IsEffectiveSuccess.Should().BeTrue();
        harness.CreatePage("p2", "二").IsEffectiveSuccess.Should().BeTrue();
        harness.CreatePage("p3", "三").IsEffectiveSuccess.Should().BeTrue();

        var orderBefore = harness.Document.Pages.Select(p => p.Id).ToArray();
        var visualBefore = harness.Document.VisualHash;

        harness.DeletePage("p2").IsEffectiveSuccess.Should().BeTrue();
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        // 插回原来那一格，而不是追加到末尾。两个哈希都按标识排序后遍历，
        // 所以位置错了哈希照样对得上——这一条只能在集合顺序上断言。
        harness.Document.Pages.Select(p => p.Id).Should().Equal(orderBefore);
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    #endregion

    #region 标签

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_tag_records_its_members_across_collections()
    {
        using var harness = new Harness();
        harness.AddNode("a").IsEffectiveSuccess.Should().BeTrue();
        harness.AddNode("b").IsEffectiveSuccess.Should().BeTrue();
        harness.Connect("e1", "a", "b").IsEffectiveSuccess.Should().BeTrue();

        // 标签是跨集合的：成员里可以同时有节点与边。
        var result = harness.AddTag("t1", ["a", "e1"], label: "待确认", color: "warn");

        result.IsEffectiveSuccess.Should().BeTrue();
        harness.Tag("t1").Members.Should().Equal("a", "e1");
        harness.Tag("t1").Color.Should().Be("warn");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_new_tag_is_a_visual_change_only()
    {
        using var harness = new Harness();
        harness.AddNode("a").IsEffectiveSuccess.Should().BeTrue();

        var result = harness.AddTag("t1", ["a"], label: "待确认");

        // 标签进的是视觉哈希：打标签不改变任何坐标。
        result.StructuralChanged.Should().BeFalse();
        result.VisualChanged.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_tag_whose_member_is_missing_is_rejected()
    {
        using var harness = new Harness();

        var before = harness.Snapshot();

        // 成员列表挂在标签自己身上，被点名的元素上没有回指字段，
        // 所以一个不存在的成员不会被任何别的地方发现。
        var result = harness.AddTag("t1", ["查无此物"]);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.TagMemberMissing);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_tag_id_that_collides_with_a_node_is_rejected()
    {
        using var harness = new Harness();
        harness.AddNode("x");

        var before = harness.Snapshot();

        var result = harness.AddTag("x", []);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.DuplicateId);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Removing_a_tag_leaves_nothing_dangling()
    {
        using var harness = new Harness();
        harness.AddNode("a").IsEffectiveSuccess.Should().BeTrue();
        harness.AddTag("t1", ["a"], label: "待确认").IsEffectiveSuccess.Should().BeTrue();

        var visualWithTag = harness.Document.VisualHash;

        harness.RemoveTag("t1").IsEffectiveSuccess.Should().BeTrue();

        // 关系是单向的：成员列表挂在标签自己身上，元素上没有回指字段。
        // 所以删掉标签之后，文档里既没有悬空引用，也没有"元素还挂着标签"这种状态。
        harness.Document.Tags.Should().BeEmpty();
        harness.Document.Nodes.Should().HaveCount(1);
        DiagramValidator.Validate(harness.Document).Should().BeEmpty();

        // 整份集合换回去就还原了，别处没有要摘的。
        harness.Bus.Undo().IsSuccess.Should().BeTrue();
        harness.Document.VisualHash.Should().Be(visualWithTag);
        harness.Tag("t1").Members.Should().Equal("a");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Removing_a_missing_tag_is_rejected()
    {
        using var harness = new Harness();

        var result = harness.RemoveTag("查无此签");

        // 不存在时报错而不是无操作：那通常说明调用方手上那份列表已经过期。
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.TagMissing);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undoing_a_tag_creation_puts_the_content_back()
    {
        using var harness = new Harness();
        harness.AddNode("a").IsEffectiveSuccess.Should().BeTrue();
        harness.AddTag("t1", ["a"], label: "第一个").IsEffectiveSuccess.Should().BeTrue();

        var visualBefore = harness.Document.VisualHash;

        harness.AddTag("t2", ["a"], label: "第二个");
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Document.Tags.Select(t => t.Id).Should().Equal("t1");
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    #endregion

    #region 动作

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_action_changes_neither_hash()
    {
        using var harness = new Harness();
        harness.AddNode("a").IsEffectiveSuccess.Should().BeTrue();

        var structuralBefore = harness.Document.StructuralHash;
        var visualBefore = harness.Document.VisualHash;
        var versionBefore = harness.Document.Version;

        var result = harness.AddAction("act1", "click", "open-url", "a");

        // 动作两个哈希都不进：它不影响坐标，也不影响像素，只影响交互。
        // 报成视觉变更会让宿主白白重绘一次，而画面上一个像素都不会变。
        result.StructuralChanged.Should().BeFalse();
        result.VisualChanged.Should().BeFalse();

        harness.Document.StructuralHash.Should().Be(structuralBefore);
        harness.Document.VisualHash.Should().Be(visualBefore);

        // 两个标志都假不等于无操作：文档内容确实变了，版本照推、历史照进。
        result.IsEffectiveSuccess.Should().BeTrue();
        harness.Document.Version.Should().Be(versionBefore + 1);
        harness.Action("act1").Target.Should().Be("a");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_action_with_a_missing_target_is_rejected()
    {
        using var harness = new Harness();

        var before = harness.Snapshot();

        var result = harness.AddAction("act1", "click", "open-url", "查无此物");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.ActionTargetMissing);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_action_may_have_no_target()
    {
        using var harness = new Harness();

        // 目标为空表示这个动作不作用于具体元素。整体校验器也不查这一档。
        harness.AddAction("act1", "ready", "run-command").IsEffectiveSuccess.Should().BeTrue();

        harness.Action("act1").Target.Should().BeNull();
        DiagramValidator.Validate(harness.Document).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_action_id_that_collides_with_a_node_is_rejected()
    {
        using var harness = new Harness();
        harness.AddNode("x");

        var result = harness.AddAction("x", "click", "open-url");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.DuplicateId);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Removing_a_missing_action_is_rejected()
    {
        using var harness = new Harness();

        var result = harness.RemoveAction("查无此作");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.ActionMissing);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undoing_an_action_creation_puts_the_content_back()
    {
        using var harness = new Harness();
        harness.AddNode("a").IsEffectiveSuccess.Should().BeTrue();
        harness.AddAction("act1", "click", "open-url", "a").IsEffectiveSuccess.Should().BeTrue();

        var actionsBefore = harness.Document.Actions.ToArray();

        harness.AddAction("act2", "dblclick", "open-url", "a");
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        // 动作不进任何哈希，所以"内容回去了"只能比集合本身。
        // 也不能比整份序列化——撤销同样推进版本号，那一处差异与内容无关。
        harness.Document.Actions.Should().Equal(actionsBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Removing_an_action_and_undoing_puts_it_back()
    {
        using var harness = new Harness();
        harness.AddNode("a").IsEffectiveSuccess.Should().BeTrue();
        harness.AddAction("act1", "click", "open-url", "a").IsEffectiveSuccess.Should().BeTrue();

        var actionsBefore = harness.Document.Actions.ToArray();

        harness.RemoveAction("act1").IsEffectiveSuccess.Should().BeTrue();
        harness.Document.Actions.Should().BeEmpty();

        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Document.Actions.Should().Equal(actionsBefore);
    }

    #endregion

    #region Memento 往返

    [Fact]
    [Trait("Category", "MementoRegistration")]
    public void The_new_mementos_survive_a_serialization_round_trip()
    {
        using var harness = new Harness();
        harness.CreatePage("p1", "第一页").IsEffectiveSuccess.Should().BeTrue();
        harness.AddNode("a").IsEffectiveSuccess.Should().BeTrue();
        harness.AddTag("t1", ["a"], label: "待确认").IsEffectiveSuccess.Should().BeTrue();
        harness.AddAction("act1", "click", "open-url", "a").IsEffectiveSuccess.Should().BeTrue();

        // 三个 memento 各自装着一个集合。走一趟序列化才能确认多态标签与集合元素类型
        // 都登记全了——漏登记在单机运行时不报错，只有原生编译之后才暴露。
        var pages = (PageMemento)RoundTrip(new PageMemento { PreviousPages = [.. harness.Document.Pages] });
        var tags = (TagMemento)RoundTrip(new TagMemento { PreviousTags = [.. harness.Document.Tags] });
        var actions = (ActionMemento)RoundTrip(new ActionMemento { PreviousActions = [.. harness.Document.Actions] });

        pages.PreviousPages.Should().BeEquivalentTo(harness.Document.Pages);
        tags.PreviousTags.Should().BeEquivalentTo(harness.Document.Tags);
        tags.PreviousTags[0].Members.Should().Equal("a");
        actions.PreviousActions.Should().BeEquivalentTo(harness.Document.Actions);
        actions.PreviousActions[0].Target.Should().Be("a");
    }

    private static CommandMemento RoundTrip(CommandMemento memento) =>
        DiagramSerializer.DeserializeMemento(DiagramSerializer.SerializeMemento(memento));

    #endregion
}
