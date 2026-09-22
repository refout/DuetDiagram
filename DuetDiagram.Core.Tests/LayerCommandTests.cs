using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 图层命令：新建、改名、挪位。
/// </summary>
/// <remarks>
/// <para>
/// 这一组的核心口径是**次序字段才是次序**。图层集合在视觉哈希里是按标识排序后遍历的，
/// 所以在集合里换个位置进不了任何哈希、也不改变任何坐标——那会是一条效果不可观测的命令。
/// 真正决定叠放次序的是每个图层自己的次序字段，所以三条用例里凡是谈"次序"的，
/// 断言的都是那个字段而不是集合下标。
/// </para>
/// <para>
/// 图层现在只进视觉哈希：渲染层还没有读它，所以没有坐标要重算，
/// 三条命令报的都是"纯外观变更"。
/// </para>
/// </remarks>
public sealed class LayerCommandTests
{
    #region 新建

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Creating_a_layer_gives_it_the_next_order()
    {
        using var harness = new Harness();

        harness.CreateLayer("l1", "底层").IsEffectiveSuccess.Should().BeTrue();
        harness.CreateLayer("l2", "上层").IsEffectiveSuccess.Should().BeTrue();

        harness.Document.Layers.Should().HaveCount(2);
        harness.Document.Layers[0].Order.Should().Be(0);
        harness.Document.Layers[1].Order.Should().Be(1);
        harness.Document.Layers[1].Name.Should().Be("上层");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_new_layer_is_a_visual_change_only()
    {
        using var harness = new Harness();

        var result = harness.CreateLayer("l1", "底层");

        // 渲染层还没有读图层，所以没有坐标要重算。报成结构变更的话，
        // 每加一个图层都会触发一次全图重排。
        result.StructuralChanged.Should().BeFalse();
        result.VisualChanged.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_layer_id_that_collides_with_a_node_is_rejected()
    {
        using var harness = new Harness();
        harness.AddNode("x");

        var before = harness.Snapshot();

        // 九个集合共用一个命名空间。
        var result = harness.CreateLayer("x", "图层");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.DuplicateId);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undoing_a_creation_puts_the_content_back()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "底层").IsEffectiveSuccess.Should().BeTrue();

        var visualBefore = harness.Document.VisualHash;

        harness.CreateLayer("l2", "上层");
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Document.Layers.Select(l => l.Id).Should().Equal("l1");
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    #endregion

    #region 改名

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Renaming_a_layer_keeps_its_order_and_id()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "旧名字").IsEffectiveSuccess.Should().BeTrue();

        var result = harness.RenameLayer("l1", "新名字");

        result.IsEffectiveSuccess.Should().BeTrue();
        harness.Document.Layers[0].Name.Should().Be("新名字");

        // 标识与次序都不动：标识是引用它的那个字段写的东西，改它会让所有引用一起失效；
        // 次序有它自己的那条命令。
        harness.Document.Layers[0].Id.Should().Be("l1");
        harness.Document.Layers[0].Order.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Renaming_a_missing_layer_is_rejected()
    {
        using var harness = new Harness();

        var result = harness.RenameLayer("查无此物", "新名字");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.LayerMissing);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Renaming_to_the_same_name_is_a_no_op()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "名字").IsEffectiveSuccess.Should().BeTrue();

        var versionBefore = harness.Document.Version;

        // 面板上的输入框失焦就会提交一次。每次失焦都推进版本、都广播一遍，是白付的代价。
        harness.RenameLayer("l1", "名字").IsNoOp.Should().BeTrue();

        harness.Document.Version.Should().Be(versionBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undoing_a_rename_puts_the_content_back()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "旧名字").IsEffectiveSuccess.Should().BeTrue();

        var visualBefore = harness.Document.VisualHash;

        harness.RenameLayer("l1", "新名字");
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Document.Layers[0].Name.Should().Be("旧名字");
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    #endregion

    #region 挪位

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Moving_a_layer_renumbers_the_order_field()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一").IsEffectiveSuccess.Should().BeTrue();
        harness.CreateLayer("l2", "二").IsEffectiveSuccess.Should().BeTrue();
        harness.CreateLayer("l3", "三").IsEffectiveSuccess.Should().BeTrue();

        harness.ReorderLayer("l3", 0).IsEffectiveSuccess.Should().BeTrue();

        // 次序看的是字段而不是集合下标。按次序排出来应当是 l3、l1、l2。
        InOrder(harness).Should().Equal("l3", "l1", "l2");

        // 次序值重排成连续的 0、1、2。留下空档的话，下一次挪位时"第几位"就有两种读法。
        harness.Document.Layers.Select(l => l.Order).Order().Should().Equal(0, 1, 2);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Moving_a_layer_down_the_order_also_works()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一").IsEffectiveSuccess.Should().BeTrue();
        harness.CreateLayer("l2", "二").IsEffectiveSuccess.Should().BeTrue();
        harness.CreateLayer("l3", "三").IsEffectiveSuccess.Should().BeTrue();

        harness.ReorderLayer("l1", 2).IsEffectiveSuccess.Should().BeTrue();

        InOrder(harness).Should().Equal("l2", "l3", "l1");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_out_of_range_index_is_clamped_rather_than_rejected()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一").IsEffectiveSuccess.Should().BeTrue();
        harness.CreateLayer("l2", "二").IsEffectiveSuccess.Should().BeTrue();

        // 索引可能来自一个在请求发出之后就已经过期的界面状态。夹紧到一个仍然合理的位置，
        // 比让整条命令失败更符合预期。
        harness.ReorderLayer("l1", 99).IsEffectiveSuccess.Should().BeTrue();

        InOrder(harness).Should().Equal("l2", "l1");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Moving_a_layer_to_where_it_already_is_is_a_no_op()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一").IsEffectiveSuccess.Should().BeTrue();
        harness.CreateLayer("l2", "二").IsEffectiveSuccess.Should().BeTrue();

        var versionBefore = harness.Document.Version;

        harness.ReorderLayer("l1", 0).IsNoOp.Should().BeTrue();

        harness.Document.Version.Should().Be(versionBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Moving_a_missing_layer_is_rejected()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一").IsEffectiveSuccess.Should().BeTrue();

        var before = harness.Snapshot();

        var result = harness.ReorderLayer("查无此物", 0);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.LayerMissing);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undoing_a_reorder_puts_every_layer_back()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一").IsEffectiveSuccess.Should().BeTrue();
        harness.CreateLayer("l2", "二").IsEffectiveSuccess.Should().BeTrue();
        harness.CreateLayer("l3", "三").IsEffectiveSuccess.Should().BeTrue();

        var visualBefore = harness.Document.VisualHash;
        var ordersBefore = harness.Document.Layers.Select(l => (l.Id, l.Order)).ToArray();

        harness.ReorderLayer("l3", 0);
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        // 重排一次会改到每一个图层的次序，所以只还原被点名的那个是不够的。
        harness.Document.Layers.Select(l => (l.Id, l.Order)).Should().Equal(ordersBefore);
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    #endregion

    #region 辅助

    /// <summary>按次序字段排好的图层标识。次序相同的用标识断掉并列，与命令里的口径一致。</summary>
    private static string[] InOrder(Harness harness) =>
        [.. harness.Document.Layers
            .OrderBy(l => l.Order)
            .ThenBy(l => l.Id, StringComparer.Ordinal)
            .Select(l => l.Id)];

    #endregion
}
