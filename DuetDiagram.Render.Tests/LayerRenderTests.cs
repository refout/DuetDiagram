using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 图层对画面的影响：谁画在谁上面、谁不画、谁画了但点不中。
/// </summary>
/// <remarks>
/// <para>
/// 坐标一律手写（见 <see cref="Layouts"/>），这样断言落在"拿到坐标之后怎么排"上，
/// 而不是落在布局引擎的当前输出上。
/// </para>
/// <para>
/// 这一组的核心口径是**两份判断必须来自同一处**：画出来的那份与点得中的那份。
/// 各算各的话，表现是"点得中一个看不见的东西"，而两边各自都自洽。
/// </para>
/// </remarks>
public sealed class LayerRenderTests
{
    #region 不画

    [Fact]
    [Trait("Category", "LayerRender")]
    public void A_hidden_layer_draws_nothing()
    {
        var document = Document(
            [new LayerDef { Id = "l1", Visible = false }],
            [Node("a", "l1"), Node("b")]);

        var list = Build(document, [Layouts.Node("a", 40, 60), Layouts.Node("b", 40, 200)]);

        // 藏起来的那一层一条指令都不出。空的框、空的标签都不该留下。
        list.Commands.Select(c => c.ElementId).Should().NotContain("a");
        list.Commands.Select(c => c.ElementId).Should().Contain("b");
    }

    [Fact]
    [Trait("Category", "LayerRender")]
    public void A_visible_layer_draws_its_nodes()
    {
        var document = Document(
            [new LayerDef { Id = "l1", Visible = true }],
            [Node("a", "l1")]);

        var list = Build(document, [Layouts.Node("a", 40, 60)]);

        list.Commands.Select(c => c.ElementId).Should().Contain("a");
    }

    [Fact]
    [Trait("Category", "LayerRender")]
    public void A_hidden_layer_does_not_shrink_the_drawing()
    {
        var document = Document(
            [new LayerDef { Id = "l1", Visible = false }],
            [Node("a", "l1")]);

        // 被藏起来的元素仍然参与布局（坐标由调用方给的就是那份完整布局的结果），
        // 绘制列表的画布尺寸也照旧——变小的话，把一层藏起来会让视图跟着缩放一下。
        var layout = Layouts.Result([Layouts.Node("a", 40, 60)], [], 400, 300);

        var list = SceneBuilder.Build(document, layout, Theme.Default, new FakeTextMeasurer());

        list.Width.Should().Be(400);
        list.Height.Should().Be(300);
    }

    [Fact]
    [Trait("Category", "LayerRender")]
    public void An_edge_touching_a_hidden_node_is_not_drawn()
    {
        var document = Document(
            [new LayerDef { Id = "l1", Visible = false }],
            [Node("a", "l1"), Node("b")],
            [new EdgeDef { Id = "e", From = "a", To = "b" }]);

        var layout = Layouts.Result(
            [Layouts.Node("a", 40, 60), Layouts.Node("b", 40, 200)],
            [Layouts.Vertical("e", Layouts.Node("a", 40, 60), Layouts.Node("b", 40, 200))],
            200,
            300);

        var list = SceneBuilder.Build(document, layout, Theme.Default, new FakeTextMeasurer());

        // 一条线连着看不见的东西，画出来是一根悬空的线——用户会去找它另一头在哪。
        list.Commands.Select(c => c.ElementId).Should().NotContain("e");
    }

    [Fact]
    [Trait("Category", "LayerRender")]
    public void A_composite_whose_members_are_all_hidden_is_not_drawn()
    {
        var document = Document(
            [new LayerDef { Id = "l1", Visible = false }],
            [Node("a", "l1")],
            composites: [new GroupDef { Id = "g", Label = "组", Members = ["a"] }]);

        var list = Build(document, [Layouts.Node("a", 40, 60)]);

        // 成员全被藏起来，框就算不出来（算得出来的话，用户会看到一个空荡荡的大框，
        // 而看不出它是为了谁留的）。
        list.Commands.Select(c => c.ElementId).Should().NotContain("g");
    }

    #endregion

    #region 前后

    [Fact]
    [Trait("Category", "LayerRender")]
    public void Nodes_are_drawn_in_layer_order()
    {
        var document = Document(
            [new LayerDef { Id = "low", Order = 0 }, new LayerDef { Id = "high", Order = 1 }],
            [Node("a", "low"), Node("b", "high")]);

        var list = Build(document, [Layouts.Node("a", 40, 60), Layouts.Node("b", 40, 200)]);

        // 后画的在上面。次序值大的那一层要排在后面。
        IndexOf(list, "a").Should().BeLessThan(IndexOf(list, "b"));
    }

    [Fact]
    [Trait("Category", "LayerRender")]
    public void A_lower_order_draws_below_even_when_declared_later()
    {
        var document = Document(
            [new LayerDef { Id = "high", Order = 5 }, new LayerDef { Id = "low", Order = 1 }],
            [Node("a", "low"), Node("b", "high")]);

        var list = Build(document, [Layouts.Node("a", 40, 60), Layouts.Node("b", 40, 200)]);

        // 声明顺序不决定前后，次序字段才决定。看声明顺序的话，面板上挪一下次序
        // 什么都不会发生——而用户以为自己在控制前后关系。
        IndexOf(list, "a").Should().BeLessThan(IndexOf(list, "b"));
    }

    [Fact]
    [Trait("Category", "LayerRender")]
    public void Equal_orders_are_broken_by_id_so_the_result_is_deterministic()
    {
        var document = Document(
            [new LayerDef { Id = "b", Order = 3 }, new LayerDef { Id = "a", Order = 3 }],
            [Node("na", "a"), Node("nb", "b")]);

        var list = Build(document, [Layouts.Node("nb", 40, 60), Layouts.Node("na", 40, 200)]);

        // 次序相同是可能的（从文件读进来、或由别的工具写出来的），那时"谁在上面"
        // 不能变成由集合位置决定的偶然结果。
        IndexOf(list, "na").Should().BeLessThan(IndexOf(list, "nb"));
    }

    [Fact]
    [Trait("Category", "LayerRender")]
    public void Elements_without_a_layer_draw_below_every_declared_layer()
    {
        var document = Document(
            [new LayerDef { Id = "l1", Order = 0 }],
            [Node("loose"), Node("a", "l1")]);

        var list = Build(document, [Layouts.Node("loose", 40, 60), Layouts.Node("a", 40, 200)]);

        // 缺省层固定在最底下。随图层次序浮动的话，每加一个图层都会有一批没有归属的
        // 元素莫名其妙地换前后——而"我加了一层"与"原来那批东西跑到后面去了"
        // 在用户看来是两件不相干的事。
        IndexOf(list, "loose").Should().BeLessThan(IndexOf(list, "a"));
    }

    [Fact]
    [Trait("Category", "LayerRender")]
    public void A_node_pointing_at_a_missing_layer_draws_in_the_default_layer()
    {
        var document = Document(
            [new LayerDef { Id = "l1" }],
            [Node("a", "查无此层"), Node("b", "l1")]);

        var list = Build(document, [Layouts.Node("a", 40, 60), Layouts.Node("b", 40, 200)]);

        // 合法文档里不会有这种引用，渲染只需要别崩，并且给出一个说得通的位置。
        IndexOf(list, "a").Should().BeLessThan(IndexOf(list, "b"));
    }

    [Fact]
    [Trait("Category", "LayerRender")]
    public void A_document_without_layers_still_draws_composites_then_edges_then_nodes()
    {
        var document = Document(
            [],
            [Node("a"), Node("b")],
            [new EdgeDef { Id = "e", From = "a", To = "b" }],
            [new GroupDef { Id = "g", Label = "组", Members = ["a"] }]);

        var layout = Layouts.Result(
            [Layouts.Node("a", 40, 60), Layouts.Node("b", 40, 200)],
            [Layouts.Vertical("e", Layouts.Node("a", 40, 60), Layouts.Node("b", 40, 200))],
            200,
            300);

        var list = SceneBuilder.Build(document, layout, Theme.Default, new FakeTextMeasurer());

        // 没有图层时全部元素同属缺省层，出笔次序与从前逐字节相同——
        // 快照那一组用例靠的就是这件事。
        list.Commands.Select(c => c.ElementId).Should().Equal("g", "g", "e", "a", "a", "b", "b");
    }

    #endregion

    #region 点不中

    [Fact]
    [Trait("Category", "LayerRender")]
    public void A_locked_layer_is_drawn_but_cannot_be_hit()
    {
        var document = Document(
            [new LayerDef { Id = "l1", Locked = true }],
            [Node("a", "l1")]);

        var list = Build(document, [Layouts.Node("a", 40, 60)]);
        var inside = new DrawPoint(100, 82);

        // 照常画出来。
        list.Commands.Select(c => c.ElementId).Should().Contain("a");

        // 但点不中。锁定的意思是"看着别动"，所以看得见、选不上。
        HitTester.Hit(list.Commands, inside, 0, list.Blocked).Should().BeNull();

        // 把名单拿掉就点得中——这说明挡住它的是这份名单，而不是几何算错了。
        HitTester.Hit(list.Commands, inside).Should().Be("a");
    }

    [Fact]
    [Trait("Category", "LayerRender")]
    public void A_hidden_layer_cannot_be_hit_because_it_is_not_drawn()
    {
        var document = Document(
            [new LayerDef { Id = "l1", Visible = false }],
            [Node("a", "l1")]);

        var list = Build(document, [Layouts.Node("a", 40, 60)]);

        HitTester.Hit(list.Commands, new DrawPoint(100, 82), 0, list.Blocked).Should().BeNull();
    }

    [Fact]
    [Trait("Category", "LayerRender")]
    public void The_blocked_list_answers_who_can_be_hit_on_its_own()
    {
        var document = Document(
            [new LayerDef { Id = "l1", Locked = true }, new LayerDef { Id = "l2", Visible = false }],
            [Node("locked", "l1"), Node("hidden", "l2"), Node("open")]);

        var list = Build(
            document,
            [Layouts.Node("locked", 40, 60), Layouts.Node("hidden", 40, 200), Layouts.Node("open", 40, 340)]);

        // 锁着的与藏起来的都在里面。藏起来的那些本来就没有指令，放进来是为了让
        // 这一份**单独**就能回答"谁能被点中"，读的人不必再知道"不可见的没指令"这件事。
        list.Blocked.Should().BeEquivalentTo(["locked", "hidden"]);
        list.Blocked.Should().NotContain("open");
    }

    #endregion

    #region 辅助

    private static DiagramDocument Document(
        IReadOnlyList<LayerDef> layers,
        IReadOnlyList<NodeDef> nodes,
        IReadOnlyList<EdgeDef>? edges = null,
        IReadOnlyList<CompositeDef>? composites = null) =>
        DiagramDocument.CreateFromContent(
            "d",
            layers: layers,
            nodes: nodes,
            edges: edges,
            composites: composites);

    private static NodeDef Node(string id, string? layer = null) =>
        new() { Id = id, Label = id, Layer = layer };

    private static DrawList Build(DiagramDocument document, PlacedNode[] nodes) =>
        SceneBuilder.Build(
            document,
            Layouts.Result(nodes, [], 400, 400),
            Theme.Default,
            new FakeTextMeasurer());

    private static int IndexOf(DrawList list, string elementId) =>
        list.Commands.ToList().FindIndex(command => command.ElementId == elementId);

    #endregion
}
