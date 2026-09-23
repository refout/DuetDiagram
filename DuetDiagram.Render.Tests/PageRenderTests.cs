using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 翻页对画面的影响：这一页上只剩这一页的东西。
/// </summary>
/// <remarks>
/// <para>
/// 坐标一律手写（见 <see cref="Layouts"/>），与绘制列表那一组同一套做法。
/// </para>
/// <para>
/// 这一层的判据是"哪几个元素出笔"。布局那边只把这一页的节点交给引擎，
/// 所以绘制列表拿到的坐标本来就只有这一页的——但**过滤不能只靠布局**：
/// 别页的元素若仍在文档里，绘制列表照样会给它们出笔，而它们的坐标是上一轮的残留。
/// </para>
/// </remarks>
public sealed class PageRenderTests
{
    [Fact]
    [Trait("Category", "PageRender")]
    public void Only_the_current_pages_nodes_are_drawn()
    {
        var document = TwoPages();

        var list = Build(
            document,
            "p2",
            [Layouts.Node("b", 40, 60), Layouts.Node("c", 40, 200)]);

        list.Commands.Select(command => command.ElementId).Should().Contain("b");
        list.Commands.Select(command => command.ElementId).Should().NotContain("a");
    }

    [Fact]
    [Trait("Category", "PageRender")]
    public void Without_a_page_everything_is_drawn()
    {
        // 单页文档与旧调用点走这条路：不传页就不过滤，与从前逐字节相同。
        var document = TwoPages();

        var list = Build(
            document,
            null,
            [Layouts.Node("a", 40, 60), Layouts.Node("b", 40, 200), Layouts.Node("c", 40, 340)]);

        list.Commands.Select(command => command.ElementId).Distinct(StringComparer.Ordinal)
            .Should().Contain(["a", "b", "c"]);
    }

    [Fact]
    [Trait("Category", "PageRender")]
    public void An_edge_between_pages_is_drawn_on_neither()
    {
        var document = TwoPages();

        var layout = Layouts.Result(
            [Layouts.Node("a", 40, 60), Layouts.Node("b", 40, 200)],
            [Layouts.Vertical("e1", Layouts.Node("a", 40, 60), Layouts.Node("b", 40, 200))],
            200,
            300);

        foreach (var page in new[] { "p1", "p2" })
        {
            var list = SceneBuilder.Build(document, layout, Theme.Default, new FakeTextMeasurer(), page);

            list.Commands.Select(command => command.ElementId).Should().NotContain(
                "e1",
                $"{page}：两端不在同一页的边哪一页都不画——画出来是一根通向空处的线");
        }
    }

    [Fact]
    [Trait("Category", "PageRender")]
    public void An_edge_without_a_page_follows_its_ends()
    {
        // 两端都在第二页、而边自己没声明归属：它归第二页，不是缺省页。
        // 按缺省页算的话，它两页都不画——一条两端都在、却哪儿都看不见的边。
        var document = DiagramDocument.CreateFromContent(
            "d",
            pages: [new PageDef { Id = "p1", Order = 0 }, new PageDef { Id = "p2", Order = 1 }],
            nodes:
            [
                new NodeDef { Id = "b", Label = "乙", Page = "p2" },
                new NodeDef { Id = "c", Label = "丙", Page = "p2" },
            ],
            edges: [new EdgeDef { Id = "e1", From = "b", To = "c" }]);

        var layout = Layouts.Result(
            [Layouts.Node("b", 40, 60), Layouts.Node("c", 40, 200)],
            [Layouts.Vertical("e1", Layouts.Node("b", 40, 60), Layouts.Node("c", 40, 200))],
            200,
            300);

        var list = SceneBuilder.Build(document, layout, Theme.Default, new FakeTextMeasurer(), "p2");

        list.Commands.Select(command => command.ElementId).Should().Contain("e1");
    }

    [Fact]
    [Trait("Category", "PageRender")]
    public void A_composite_whose_members_are_all_elsewhere_is_not_drawn()
    {
        var document = DiagramDocument.CreateFromContent(
            "d",
            pages: [new PageDef { Id = "p1", Order = 0 }, new PageDef { Id = "p2", Order = 1 }],
            nodes:
            [
                new NodeDef { Id = "a", Label = "甲", Page = "p1" },
                new NodeDef { Id = "b", Label = "乙", Page = "p2" },
            ],
            composites: [new GroupDef { Id = "g", Label = "组", Members = ["a"] }]);

        var layout = Layouts.Result([Layouts.Node("b", 40, 60)], [], 200, 300);
        var list = SceneBuilder.Build(document, layout, Theme.Default, new FakeTextMeasurer(), "p2");

        // 成员全在别的页面上，框算不出来，也就不画——与成员全被藏起来时同一处置。
        list.Commands.Select(command => command.ElementId).Should().NotContain("g");
    }

    [Fact]
    [Trait("Category", "PageRender")]
    public void Off_page_nodes_cannot_be_hit()
    {
        var document = TwoPages();

        var list = Build(document, "p2", [Layouts.Node("b", 40, 60)]);

        // 别的页面上的节点本来就没有指令，并进这份名单是为了让这一份**单独**
        // 就能回答"谁能被点中"。边不进名单：它一进去，同一页上的边也会变得点不中。
        list.Blocked.Should().Contain("a");
        list.Blocked.Should().NotContain("b");
    }

    #region 辅助

    /// <summary>两页，各放一个节点，两端跨页的边一条。</summary>
    private static DiagramDocument TwoPages() => DiagramDocument.CreateFromContent(
        "d",
        pages: [new PageDef { Id = "p1", Order = 0 }, new PageDef { Id = "p2", Order = 1 }],
        nodes:
        [
            new NodeDef { Id = "a", Label = "甲", Page = "p1" },
            new NodeDef { Id = "b", Label = "乙", Page = "p2" },
            new NodeDef { Id = "c", Label = "丙", Page = "p2" },
        ],
        edges: [new EdgeDef { Id = "e1", From = "a", To = "b" }]);

    private static DrawList Build(DiagramDocument document, string? pageId, PlacedNode[] nodes) =>
        SceneBuilder.Build(
            document,
            Layouts.Result(nodes, [], 400, 400),
            Theme.Default,
            new FakeTextMeasurer(),
            pageId);

    #endregion
}
