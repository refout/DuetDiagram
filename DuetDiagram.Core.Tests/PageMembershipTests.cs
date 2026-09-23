using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 元素归在哪一页，以及一页上看得见谁。
/// </summary>
/// <remarks>
/// <para>
/// 这一组的核心口径是**缺省页 = 次序最小的那一页**：没有声明归属、或者归属指向一个
/// 不存在的页面，都归它。布局、渲染、导出与摘要四处都要回答"这一页上有谁"，
/// 而这条口径只有一份——各判一次的话表现是"翻页之后有几个元素赖着不走"。
/// </para>
/// <para>
/// 另一条是**删页不是删元素**：那一页上的元素退回缺省页，撤销之后整份回来。
/// </para>
/// </remarks>
public sealed class PageMembershipTests
{
    #region 归属

    [Fact]
    [Trait("Category", "PageMembership")]
    public void An_element_without_a_page_is_on_the_default_page()
    {
        using var harness = new Harness();
        harness.CreatePage("p1");
        harness.CreatePage("p2");
        harness.AddNode("a");

        PageMembership.EffectivePageId(harness.Document, null).Should().Be("p1");
        PageMembership.Shows(harness.Document, harness.Node("a"), "p1").Should().BeTrue();
        PageMembership.Shows(harness.Document, harness.Node("a"), "p2").Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "PageMembership")]
    public void The_default_page_is_the_one_with_the_smallest_order()
    {
        // 声明顺序与次序字段是反的：缺省页按次序取，不是按声明顺序。
        var document = DiagramDocument.CreateFromContent(
            "d",
            pages: [new PageDef { Id = "later", Order = 5 }, new PageDef { Id = "first", Order = 1 }]);

        PageMembership.Ordered(document).Select(page => page.Id).Should().Equal("first", "later");
        PageMembership.DefaultPageId(document).Should().Be("first");
        PageMembership.EffectivePageId(document, null).Should().Be("first");
    }

    [Fact]
    [Trait("Category", "PageMembership")]
    public void Pages_with_the_same_order_are_broken_by_id()
    {
        // 次序相同是可能的（从文件读进来、或由别的工具写出来的），
        // 那时缺省页不能变成由集合位置决定的偶然结果。
        var document = DiagramDocument.CreateFromContent(
            "d",
            pages: [new PageDef { Id = "b", Order = 3 }, new PageDef { Id = "a", Order = 3 }]);

        PageMembership.Ordered(document).Select(page => page.Id).Should().Equal("a", "b");
    }

    [Fact]
    [Trait("Category", "PageMembership")]
    public void A_page_that_does_not_exist_falls_back_to_the_default()
    {
        using var harness = new Harness();
        harness.CreatePage("p1");
        harness.CreatePage("p2");

        // 悬空引用按缺省页处理，不报错：校验器不查这条引用，
        // 而渲染与布局的立场是"合法的文档里不会有，只需要别崩"。
        PageMembership.EffectivePageId(harness.Document, "查无此页").Should().Be("p1");
    }

    [Fact]
    [Trait("Category", "PageMembership")]
    public void A_document_with_no_pages_shows_everything()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        // 一页都没有时缺省页为空，而传空表示不过滤——单页文档就是这种情形。
        PageMembership.DefaultPageId(harness.Document).Should().BeNull();
        PageMembership.Shows(harness.Document, harness.Node("a"), null).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "PageMembership")]
    public void An_edge_is_only_on_a_page_when_both_ends_are_there()
    {
        using var harness = new Harness();
        harness.CreatePage("p1");
        harness.CreatePage("p2");
        harness.AddNode("a");
        harness.AddNode("b");
        harness.SetField("b", FieldNames.Page, "p2");
        harness.Connect("e", "a", "b");

        // 两端不在同一页的边**哪一页都不画**。挑一页画出来的话，用户在那一页上
        // 会看到一条通向空处的线，而它在另一页上才接得上。
        PageMembership.Shows(harness.Document, harness.Document.Edges[0], "p1").Should().BeFalse();
        PageMembership.Shows(harness.Document, harness.Document.Edges[0], "p2").Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "PageMembership")]
    public void An_edge_with_both_ends_on_the_page_is_shown()
    {
        using var harness = new Harness();
        harness.CreatePage("p1");
        harness.CreatePage("p2");
        harness.AddNode("a");
        harness.AddNode("b");
        harness.SetField("a", FieldNames.Page, "p2");
        harness.SetField("b", FieldNames.Page, "p2");
        harness.Connect("e", "a", "b");

        PageMembership.Shows(harness.Document, harness.Document.Edges[0], "p2").Should().BeTrue();
        PageMembership.Shows(harness.Document, harness.Document.Edges[0], "p1").Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "PageMembership")]
    public void An_edge_following_a_node_that_moved_to_another_page_drops_out()
    {
        using var harness = new Harness();
        harness.CreatePage("p1");
        harness.CreatePage("p2");
        harness.AddNode("a");
        harness.AddNode("b");
        harness.Connect("e", "a", "b");

        // 先把整条边放到 p2，再把一端挪回 p1：这条边因此哪一页都不画。
        // 只判边自己的归属的话，它会留在 p2 上，而它的一端已经不在那儿了。
        harness.SetField("a", FieldNames.Page, "p2");
        harness.SetField("b", FieldNames.Page, "p2");
        harness.SetEdgeField("e", FieldNames.Page, "p2");
        harness.SetField("b", FieldNames.Page, "p1");

        PageMembership.Shows(harness.Document, harness.Document.Edges[0], "p2").Should().BeFalse();
    }

    #endregion

    #region 删除与撤销

    [Fact]
    [Trait("Category", "PageMembership")]
    public void Deleting_a_page_keeps_its_elements_on_the_default_page()
    {
        using var harness = new Harness();
        harness.CreatePage("p1");
        harness.CreatePage("p2");
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AssignPage(["b"], "p2");

        harness.DeletePage("p2").IsEffectiveSuccess.Should().BeTrue();

        // 删页不是删元素：两个节点都还在，第二个退回了缺省页。
        harness.Document.Nodes.Should().HaveCount(2);
        PageMembership.EffectivePageId(harness.Document, harness.Node("b").Page).Should().Be("p1");
        PageMembership.Shows(harness.Document, harness.Node("b"), "p1").Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "PageMembership")]
    public void Undoing_a_deletion_brings_the_page_and_the_membership_back()
    {
        using var harness = new Harness();
        harness.CreatePage("p1");
        harness.CreatePage("p2");
        harness.AddNode("a");
        harness.AssignPage(["a"], "p2");

        var structuralBefore = harness.Document.StructuralHash;

        harness.DeletePage("p2");
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Document.Pages.Select(page => page.Id).Should().Equal("p1", "p2");
        harness.Node("a").Page.Should().Be("p2", "它自己的归属一个字都没动过");
        harness.Document.StructuralHash.Should().Be(structuralBefore);
    }

    [Fact]
    [Trait("Category", "PageMembership")]
    public void The_last_page_cannot_be_deleted()
    {
        using var harness = new Harness();
        harness.CreatePage("p1");

        var result = harness.DeletePage("p1");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.PageRequired);
    }

    [Fact]
    [Trait("Category", "PageMembership")]
    public void Undoing_a_deletion_that_other_elements_joined_does_not_move_them()
    {
        // 删掉一页之后，那一页上的元素退回缺省页；撤销之后页面回来了，
        // 而那些**当时就落在缺口那一页上**的元素该跟着回去——这是撤销一次操作，
        // 不是"把整份文档换回上一个版本"。
        using var harness = new Harness();
        harness.CreatePage("p1");
        harness.CreatePage("p2");
        harness.AddNode("a");
        harness.AssignPage(["a"], "p2");

        harness.DeletePage("p2");
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        PageMembership.Shows(harness.Document, harness.Node("a"), "p2").Should().BeTrue();
    }

    #endregion

    #region 投影

    [Fact]
    [Trait("Category", "PageMembership")]
    public void Projection_keeps_only_that_page()
    {
        using var harness = new Harness();
        harness.CreatePage("p1");
        harness.CreatePage("p2");
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AssignPage(["b"], "p2");
        harness.Connect("e", "a", "b");

        var projected = PageMembership.Project(harness.Document, "p2");

        projected.Nodes.Select(node => node.Id).Should().Equal("b");
        projected.Edges.Should().BeEmpty("那条边的一端不在这一页上");
        projected.Pages.Should().HaveCount(2, "页面集合本身不动——投影是看一份文档的一页，不是删掉别的页");
    }

    [Fact]
    [Trait("Category", "PageMembership")]
    public void Projection_keeps_the_version()
    {
        using var harness = new Harness();
        harness.CreatePage("p1");
        harness.AddNode("a");

        var projected = PageMembership.Project(harness.Document, "p1");

        // 投影是一份视图，不是一次变更。版本号丢了的话，摘要里报的版本会比文档上的小，
        // 而调用方正是按它判断自己手上的副本新不新。
        projected.Version.Should().Be(harness.Document.Version);
    }

    [Fact]
    [Trait("Category", "PageMembership")]
    public void Projection_of_nothing_is_the_document_itself()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        PageMembership.Project(harness.Document, null).Should().BeSameAs(harness.Document);
    }

    #endregion

    #region 变更类型

    [Fact]
    [Trait("Category", "PageMembership")]
    public void Changing_an_elements_page_is_a_structural_change()
    {
        using var harness = new Harness();
        harness.CreatePage("p1");
        harness.CreatePage("p2");
        harness.AddNode("a");

        var structuralBefore = harness.Document.StructuralHash;

        var result = harness.SetField("a", FieldNames.Page, "p2");

        result.IsEffectiveSuccess.Should().BeTrue(
            string.Join('、', result.Errors.Select(error => $"{error.Code}:{error.Payload}")));

        // 布局是按页算的：换了页，这一页上有什么变了，解出来的坐标也就变了。
        // 报成外观变更的话，翻页之后不重排。
        result.StructuralChanged.Should().BeTrue();
        harness.Document.StructuralHash.Should().NotBe(structuralBefore);
    }

    [Fact]
    [Trait("Category", "PageMembership")]
    public void Moving_everything_to_one_page_leaves_that_page_whole()
    {
        using var harness = new Harness();
        harness.CreatePage("p1");
        harness.CreatePage("p2");
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AssignPage(["a", "b"], "p2");

        var document = harness.Document;

        document.Nodes.Should().OnlyContain(node => PageMembership.Shows(document, node, "p2"));
        PageMembership.Project(document, "p1").Nodes.Should().BeEmpty("第一页上什么都没剩下");
    }

    #endregion
}
