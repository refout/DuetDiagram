using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 图层的那两个开关：藏起来、锁上。
/// </summary>
/// <remarks>
/// <para>
/// **两个开关是两件事，用例也分开验。** 可见性管"画不画"，锁定管"改不改得动"。
/// 合成一个开关的话，「我想看着它但别动它」这个最常见的诉求就表达不出来，
/// 而合成之后不会有哪条用例变红——两条各自都自洽。
/// </para>
/// <para>
/// 分类按问的问题切：<c>LayerVisibility</c> 那一组问的是"这两个开关各自的作用范围"，
/// <c>Atomicity</c> 那一组问的是"失败改不改文档、没变推不推版本、撤销回不回得去"。
/// </para>
/// <para>
/// 两个开关改的都是 <see cref="LayerDef"/> 上的一个布尔，而图层的三个字段
/// （可见、锁定、次序）都在**视觉哈希**段里，所以两条命令都只报外观变更。
/// 报成结构变更的话，每点一次眼睛图标都会触发一次全图重排。
/// </para>
/// </remarks>
public sealed class LayerVisibilityTests
{
    #region 可见性：作用范围

    [Fact]
    [Trait("Category", "LayerVisibility")]
    public void Hiding_a_layer_flips_only_that_layers_visibility()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一");
        harness.CreateLayer("l2", "二");

        var result = harness.SetLayerVisible("l1", false);

        result.IsEffectiveSuccess.Should().BeTrue();
        harness.Layer("l1").Visible.Should().BeFalse();
        harness.Layer("l2").Visible.Should().BeTrue("另一个图层与这次改动无关");
    }

    [Fact]
    [Trait("Category", "LayerVisibility")]
    public void Hiding_a_layer_is_a_visual_change_only()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一");

        var structuralBefore = harness.Document.StructuralHash;

        var result = harness.SetLayerVisible("l1", false);

        // 藏起来只是不画，不改坐标：被藏起来的元素仍然参与布局，连线仍然绕着它走。
        // 报成结构变更的话，藏一个元素会让整张图重排——而用户只是把它藏起来了。
        result.StructuralChanged.Should().BeFalse();
        result.VisualChanged.Should().BeTrue();
        harness.Document.StructuralHash.Should().Be(structuralBefore);
    }

    [Fact]
    [Trait("Category", "LayerVisibility")]
    public void Showing_a_hidden_layer_brings_it_back()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一");
        harness.SetLayerVisible("l1", false).IsEffectiveSuccess.Should().BeTrue();

        harness.SetLayerVisible("l1", true).IsEffectiveSuccess.Should().BeTrue();

        harness.Layer("l1").Visible.Should().BeTrue();
    }

    #endregion

    #region 锁定：作用范围

    [Fact]
    [Trait("Category", "LayerVisibility")]
    public void Locking_a_layer_flips_only_that_layers_lock()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一");
        harness.CreateLayer("l2", "二");

        var result = harness.SetLayerLocked("l1", true);

        result.IsEffectiveSuccess.Should().BeTrue();
        harness.Layer("l1").Locked.Should().BeTrue();
        harness.Layer("l2").Locked.Should().BeFalse("另一个图层与这次改动无关");
    }

    [Fact]
    [Trait("Category", "LayerVisibility")]
    public void Locking_a_layer_is_a_visual_change_only()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一");

        var structuralBefore = harness.Document.StructuralHash;

        var result = harness.SetLayerLocked("l1", true);

        // 锁上什么都不改画面，只是"点不中、改不了"。坐标当然也不用重算。
        result.StructuralChanged.Should().BeFalse();
        result.VisualChanged.Should().BeTrue();
        harness.Document.StructuralHash.Should().Be(structuralBefore);
    }

    #endregion

    #region 两个开关互不干涉

    [Fact]
    [Trait("Category", "LayerVisibility")]
    public void Hiding_a_layer_leaves_its_lock_alone()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一");
        harness.SetLayerLocked("l1", true).IsEffectiveSuccess.Should().BeTrue();

        harness.SetLayerVisible("l1", false).IsEffectiveSuccess.Should().BeTrue();

        harness.Layer("l1").Locked.Should().BeTrue("藏起来与锁上是两件事");
    }

    [Fact]
    [Trait("Category", "LayerVisibility")]
    public void Locking_a_layer_leaves_its_visibility_alone()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一");
        harness.SetLayerVisible("l1", false).IsEffectiveSuccess.Should().BeTrue();

        harness.SetLayerLocked("l1", true).IsEffectiveSuccess.Should().BeTrue();

        harness.Layer("l1").Visible.Should().BeFalse("锁上不会顺带把它放出来");
    }

    [Fact]
    [Trait("Category", "LayerVisibility")]
    public void Flipping_one_layer_leaves_the_others_untouched()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一");
        harness.CreateLayer("l2", "二");
        harness.CreateLayer("l3", "三");

        var othersBefore = new[] { harness.Layer("l2"), harness.Layer("l3") };

        harness.SetLayerVisible("l1", false);
        harness.SetLayerLocked("l1", true);

        // 整份集合换掉是实现方式（图层是不可变记录），但结果里只有被点名的那个变了。
        new[] { harness.Layer("l2"), harness.Layer("l3") }.Should().Equal(othersBefore);
    }

    #endregion

    #region 原子性：可见性

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Hiding_a_missing_layer_is_rejected()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一");

        var before = harness.Snapshot();

        var result = harness.SetLayerVisible("查无此物", false);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.LayerMissing);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Hiding_an_already_hidden_layer_is_a_no_op()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一");
        harness.SetLayerVisible("l1", false).IsEffectiveSuccess.Should().BeTrue();

        var versionBefore = harness.Document.Version;

        // 面板上的眼睛图标点到一个已经是那个值的状态时会提交一次。
        harness.SetLayerVisible("l1", false).IsNoOp.Should().BeTrue();

        harness.Document.Version.Should().Be(versionBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undoing_a_hide_puts_the_visibility_back()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一");

        var visualBefore = harness.Document.VisualHash;

        harness.SetLayerVisible("l1", false);
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Layer("l1").Visible.Should().BeTrue();
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    #endregion

    #region 原子性：锁定

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Locking_a_missing_layer_is_rejected()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一");

        var before = harness.Snapshot();

        var result = harness.SetLayerLocked("查无此物", true);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.LayerMissing);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Locking_an_already_locked_layer_is_a_no_op()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一");
        harness.SetLayerLocked("l1", true).IsEffectiveSuccess.Should().BeTrue();

        var versionBefore = harness.Document.Version;

        harness.SetLayerLocked("l1", true).IsNoOp.Should().BeTrue();

        harness.Document.Version.Should().Be(versionBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undoing_a_lock_puts_the_flag_back()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一");

        var visualBefore = harness.Document.VisualHash;

        harness.SetLayerLocked("l1", true);
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Layer("l1").Locked.Should().BeFalse();
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undoing_one_switch_does_not_disturb_the_other()
    {
        using var harness = new Harness();
        harness.CreateLayer("l1", "一");
        harness.SetLayerVisible("l1", false).IsEffectiveSuccess.Should().BeTrue();

        harness.SetLayerLocked("l1", true);

        // 撤销的是锁定那一次。整份集合换回去，换的是"锁定之前"的那一份，
        // 而那一份里可见性已经是隐藏了——所以两个开关各自的撤销互不牵连。
        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Layer("l1").Locked.Should().BeFalse();
        harness.Layer("l1").Visible.Should().BeFalse();
    }

    #endregion
}
