using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 一次把一批元素归到同一个图层上。
/// </summary>
/// <remarks>
/// <para>
/// **为什么要有一条批命令。** 单元素的归属走 <c>set-node-field</c> 的 <c>layer</c> 字段就够，
/// 但界面上的「移入」是多选之后的一次操作：逐个发命令的话，撤销要按很多次，
/// 而用户眼里那是同一次操作——一次操作就该进一条历史。
/// </para>
/// <para>
/// 这一组的核心口径是**一次操作进一次历史**：不论点了几个元素，撤销一次就全回去。
/// 另一条是**原子性**：有一个算不出来就整条不改——边算边写的话，
/// 第三个发现节点不在时前两个已经写进去了。
/// </para>
/// </remarks>
public sealed class LayerAssignmentTests
{
    #region 移入

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Assigning_a_layer_moves_every_named_node()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一").IsEffectiveSuccess.Should().BeTrue();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AddNode("c");

        var result = harness.AssignLayer(["a", "b", "c"], "l1");

        result.IsEffectiveSuccess.Should().BeTrue();
        harness.Node("a").Layer.Should().Be("l1");
        harness.Node("b").Layer.Should().Be("l1");
        harness.Node("c").Layer.Should().Be("l1");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void One_assignment_reports_every_element_it_moved()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一").IsEffectiveSuccess.Should().BeTrue();
        harness.AddNode("a");
        harness.AddNode("b");

        var result = harness.AssignLayer(["a", "b"], "l1");

        // 一条命令、两个受影响元素：界面据此知道要重画哪几个，而历史里只多一条。
        result.AffectedIds.Should().BeEquivalentTo("a", "b");
        result.StructuralChanged.Should().BeFalse("归属不改坐标");
        result.VisualChanged.Should().BeTrue("它改的是画在哪一档");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undoing_one_assignment_puts_the_whole_batch_back()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一").IsEffectiveSuccess.Should().BeTrue();
        harness.CreateLayer("l2", "二").IsEffectiveSuccess.Should().BeTrue();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AddNode("c");
        harness.AssignLayer(["a", "b", "c"], "l1").IsEffectiveSuccess.Should().BeTrue();

        var visualBefore = harness.Document.VisualHash;

        harness.AssignLayer(["a", "b", "c"], "l2");
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        // 撤销一次就够，而不是按三次。
        harness.Node("a").Layer.Should().Be("l1");
        harness.Node("b").Layer.Should().Be("l1");
        harness.Node("c").Layer.Should().Be("l1");
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undoing_a_first_assignment_takes_the_nodes_off_the_layer()
    {
        // 起点是"不在任何图层上"，撤销要回到那里——不是回到"某一层"，而是回到没有归属。
        using var harness = new Harness();
        harness.CreateLayer("l1", "一").IsEffectiveSuccess.Should().BeTrue();
        harness.AddNode("a");
        harness.AddNode("b");

        harness.AssignLayer(["a", "b"], "l1");
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Node("a").Layer.Should().BeNull();
        harness.Node("b").Layer.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Assigning_where_everything_already_is_is_a_no_op()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一").IsEffectiveSuccess.Should().BeTrue();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AssignLayer(["a", "b"], "l1").IsEffectiveSuccess.Should().BeTrue();

        var versionBefore = harness.Document.Version;

        // 多选之后移入，选中的元素里常常有几个已经在那一层上。它们不算变更。
        harness.AssignLayer(["a", "b"], "l1").IsNoOp.Should().BeTrue();

        harness.Document.Version.Should().Be(versionBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_partly_unchanged_batch_still_moves_the_rest()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一").IsEffectiveSuccess.Should().BeTrue();
        harness.CreateLayer("l2", "二").IsEffectiveSuccess.Should().BeTrue();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AssignLayer(["a", "b"], "l1").IsEffectiveSuccess.Should().BeTrue();

        var result = harness.AssignLayer(["a", "b"], "l2");

        result.IsEffectiveSuccess.Should().BeTrue();
        result.AffectedIds.Should().BeEquivalentTo("a", "b");
        harness.Node("a").Layer.Should().Be("l2");
        harness.Node("b").Layer.Should().Be("l2");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_duplicated_id_is_counted_once()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一").IsEffectiveSuccess.Should().BeTrue();
        harness.AddNode("a");
        harness.AddNode("b");

        // 重复的标识无害，但结果里不该出现两条一模一样的变更。
        var result = harness.AssignLayer(["a", "a", "b"], "l1");

        result.AffectedIds.Should().BeEquivalentTo("a", "b");
        harness.Node("a").Layer.Should().Be("l1");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Only_the_visual_hash_moves()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一").IsEffectiveSuccess.Should().BeTrue();
        harness.AddNode("a");

        var structuralBefore = harness.Document.StructuralHash;
        var visualBefore = harness.Document.VisualHash;

        harness.AssignLayer(["a"], "l1");

        // 归属在视觉段：它改的是画在哪一档，不改任何坐标，所以不必重排。
        harness.Document.StructuralHash.Should().Be(structuralBefore);
        harness.Document.VisualHash.Should().NotBe(visualBefore);
    }

    #endregion

    #region 失败路径

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Assigning_to_a_missing_layer_is_rejected()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        var before = harness.Snapshot();

        var result = harness.AssignLayer(["a"], "查无此层");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.LayerMissing);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void One_missing_node_stops_the_whole_batch()
    {
        // 这是这一条最要紧的失败路径：边算边写的话，第一个已经进去了，
        // 而失败的命令按约定一个字都不该改。
        using var harness = new Harness();
        harness.CreateLayer("l1", "一").IsEffectiveSuccess.Should().BeTrue();
        harness.AddNode("a");

        var before = harness.Snapshot();

        var result = harness.AssignLayer(["a", "查无此物"], "l1");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.NodeMissing);
        harness.Node("a").Layer.Should().BeNull("整条被拒，排在前面那个也不该动");
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void An_empty_batch_is_rejected()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一").IsEffectiveSuccess.Should().BeTrue();

        // 空的一批多半是调用方把"没选中"当成了"选中了一批"。
        var result = harness.AssignLayer([], "l1");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.InvalidId);
    }

    #endregion
}
