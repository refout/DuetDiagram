using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 渲染模式的切换器。
/// </summary>
/// <remarks>
/// 这一层测的是**时机**：判定、预备、生效分在三帧上，而"切换那一帧不做额外的事"
/// 这件事只能通过"哪一帧该返回真"来验。验不了的话，表现是转一下视图就卡一下，
/// 而那种卡顿在功能测试里完全看不出来。
/// </remarks>
public sealed class ModeSwitchTests
{
    #region 不切换

    [Fact]
    [Trait("Category", "ModeSwitch")]
    public void A_small_document_stays_in_immediate_mode()
    {
        var mode = new ModeSwitch(CullingPolicy.Default);

        mode.Current.Should().Be(RenderMode.Immediate);
        mode.Request(10).Should().BeFalse();
        mode.Commit().Should().BeFalse();

        mode.IsSwitching.Should().BeFalse();
        mode.SwitchCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "ModeSwitch")]
    public void Requesting_the_same_mode_twice_arms_nothing_the_second_time()
    {
        // 这一条盯的是"预备那件事不会每帧重做一遍"。
        var mode = new ModeSwitch(CullingPolicy.Default);

        mode.Request(1000).Should().BeTrue("第一次判定出一个新模式，预备要做一次");
        mode.Request(1000).Should().BeFalse("目标已经对准了，不必再预备一遍");
        mode.Request(1200).Should().BeFalse("还在同一档里");
    }

    #endregion

    #region 切换的时机

    [Fact]
    [Trait("Category", "ModeSwitch")]
    public void The_switch_takes_effect_on_the_frame_after_it_is_armed()
    {
        var mode = new ModeSwitch(CullingPolicy.Default);

        // 第一帧：判定出该换成虚拟化，这一帧做预备。
        mode.Request(1000).Should().BeTrue();
        mode.Current.Should().Be(RenderMode.Immediate, "预备那一帧还在用旧模式");
        mode.IsSwitching.Should().BeTrue();

        // 第二帧：预备好的生效。
        mode.Commit().Should().BeTrue();
        mode.Current.Should().Be(RenderMode.Virtualized);
        mode.IsSwitching.Should().BeFalse();
        mode.SwitchCount.Should().Be(1);

        // 第三帧起无事发生。
        mode.Request(1000).Should().BeFalse();
        mode.Commit().Should().BeFalse();
        mode.SwitchCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "ModeSwitch")]
    public void The_arming_frame_is_the_only_one_that_reports_work()
    {
        // 这是"切换那一帧不做额外的事"的可判定形式：
        // 返回真的那一帧是预备帧，切换帧上 Request 与 Commit 各返回一次假与一次真，
        // 而调用方只把真当成"要干活"。
        var mode = new ModeSwitch(CullingPolicy.Default);

        var workFrames = 0;

        for (var frame = 0; frame < 4; frame++)
        {
            if (mode.Request(1000))
            {
                workFrames++;
            }

            mode.Commit();
        }

        workFrames.Should().Be(1, "整段过程里只有一帧需要为新模式做准备");
    }

    [Fact]
    [Trait("Category", "ModeSwitch")]
    public void Going_back_below_the_threshold_switches_back()
    {
        var mode = new ModeSwitch(CullingPolicy.Default);

        mode.Request(1000).Should().BeTrue();
        mode.Commit();

        mode.Current.Should().Be(RenderMode.Virtualized);

        mode.Request(100).Should().BeTrue();
        mode.Commit().Should().BeTrue();

        mode.Current.Should().Be(RenderMode.Immediate);
        mode.SwitchCount.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "ModeSwitch")]
    public void A_change_of_target_while_a_switch_is_in_flight_replaces_the_target()
    {
        // 元素数在两帧之间又跨了回来：目标被改掉，而当前模式始终没动过。
        var mode = new ModeSwitch(CullingPolicy.Default);

        mode.Request(1000).Should().BeTrue();
        mode.Target.Should().Be(RenderMode.Virtualized);

        mode.Request(10).Should().BeTrue();
        mode.Target.Should().Be(RenderMode.Immediate);

        mode.Commit().Should().BeFalse("目标被改回了当前模式，这一帧什么都不用换");
        mode.SwitchCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "ModeSwitch")]
    public void A_switch_starts_from_the_mode_it_was_told_to()
    {
        var mode = new ModeSwitch(CullingPolicy.Default, RenderMode.Virtualized);

        mode.Current.Should().Be(RenderMode.Virtualized);
        mode.Request(10).Should().BeTrue();
        mode.Commit().Should().BeTrue();
        mode.Current.Should().Be(RenderMode.Immediate);
    }

    [Fact]
    [Trait("Category", "ModeSwitch")]
    public void A_custom_threshold_moves_the_boundary()
    {
        var mode = new ModeSwitch(new CullingPolicy(threshold: 10));

        mode.Request(9).Should().BeFalse();
        mode.Request(10).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "ModeSwitch")]
    public void A_missing_policy_is_rejected()
    {
        var act = () => new ModeSwitch(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion
}
