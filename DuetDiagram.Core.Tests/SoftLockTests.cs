using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Concurrency;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Time;
using DuetDiagram.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 文档级软锁：拿锁、续期、到期自动释放。
/// </summary>
/// <remarks>
/// <para>
/// 这一组里最要紧的是"每二十九秒动一次"那一条。到期判据写成"拿到锁的时刻加三十秒"的话，
/// 一个正在慢慢想下一步的 agent 会在两次操作之间被判成过期，而它的锁被别人拿走——
/// 表现是两个 agent 同时以为自己在改这份文档。
/// </para>
/// <para>
/// 时钟用可推进的那一个，不靠真的等三十秒：等出来的用例跑得慢，而且时长一改就全红。
/// </para>
/// </remarks>
public sealed class SoftLockTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-23T10:00:00Z");

    #region 拿锁

    [Fact]
    [Trait("Category", "SoftLock")]
    public void The_first_acquire_succeeds()
    {
        var clock = new ManualTimeProvider(Start);
        var softLock = new SoftLock(clock: clock);

        softLock.TryAcquire("agent-a", out var heldBy).Should().BeTrue();

        heldBy.Should().BeNull("拿得到就没什么要解释的");
        softLock.Holder.Should().Be("agent-a");
        softLock.IsFree.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "SoftLock")]
    public void A_second_subject_is_refused_and_told_who_has_it()
    {
        // 拿不到时必须报出是谁拿着：宿主据此决定是排队还是告诉用户"另一个人在改"。
        // 只回一个假的话，它只能给用户一句"改不了"，而用户无从知道在等谁。
        var clock = new ManualTimeProvider(Start);
        var softLock = new SoftLock(clock: clock);

        softLock.TryAcquire("agent-a", out _).Should().BeTrue();

        softLock.TryAcquire("agent-b", out var heldBy).Should().BeFalse();
        heldBy.Should().Be("agent-a");
        softLock.Holder.Should().Be("agent-a", "被拒的那一次不该把锁抢过去");
    }

    [Fact]
    [Trait("Category", "SoftLock")]
    public void The_same_subject_can_take_it_again()
    {
        // 宿主重连之后会重新拿一次，而它并没有失去这个锁。判成失败的话，
        // 那个 agent 会以为有人跟它抢，实际上没有。
        var clock = new ManualTimeProvider(Start);
        var softLock = new SoftLock(clock: clock);

        softLock.TryAcquire("agent-a", out _).Should().BeTrue();
        softLock.TryAcquire("agent-a", out _).Should().BeTrue();

        softLock.Holder.Should().Be("agent-a");
    }

    #endregion

    #region 到期

    [Fact]
    [Trait("Category", "SoftLock")]
    public void An_idle_lock_expires_on_its_own()
    {
        // 释放是懒的：到期的锁在下一个来拿的人看来就是空的，不另起一条清理线程。
        var clock = new ManualTimeProvider(Start);
        var softLock = new SoftLock(clock: clock);

        softLock.TryAcquire("agent-a", out _).Should().BeTrue();

        clock.Advance(TimeSpan.FromSeconds(31));

        softLock.IsFree.Should().BeTrue();
        softLock.Holder.Should().BeNull();

        softLock.TryAcquire("agent-b", out var heldBy).Should().BeTrue();
        heldBy.Should().BeNull();
        softLock.Holder.Should().Be("agent-b");
    }

    [Fact]
    [Trait("Category", "SoftLock")]
    public void A_holder_that_keeps_working_keeps_the_lock()
    {
        // 这一条是本组最要紧的一条：每二十九秒动一次的 agent 不能被判成过期。
        // 判据是"最后一次操作"，不是"拿到锁的时刻"。
        var clock = new ManualTimeProvider(Start);
        var softLock = new SoftLock(clock: clock);

        softLock.TryAcquire("agent-a", out _).Should().BeTrue();

        for (var round = 0; round < 5; round++)
        {
            clock.Advance(TimeSpan.FromSeconds(29));

            softLock.Renew("agent-a").Should().BeTrue($"第 {round + 1} 次报活时锁还在它手里");
        }

        clock.Advance(TimeSpan.FromSeconds(29));

        softLock.Holder.Should().Be("agent-a", "它每隔二十九秒动一次，从来没闲过三十秒");
        softLock.TryAcquire("agent-b", out var heldBy).Should().BeFalse();
        heldBy.Should().Be("agent-a");
    }

    [Fact]
    [Trait("Category", "SoftLock")]
    public void Renewing_after_the_lock_expired_does_not_bring_it_back()
    {
        // 续得上的话，两个主体会同时以为自己在改这份文档——而先来的那一个已经在改了。
        // 它要重新走一次拿锁，那时拿得到就拿得到、拿不到就知道有人接手了。
        var clock = new ManualTimeProvider(Start);
        var softLock = new SoftLock(clock: clock);

        softLock.TryAcquire("agent-a", out _).Should().BeTrue();
        clock.Advance(TimeSpan.FromSeconds(31));

        softLock.Renew("agent-a").Should().BeFalse();
        softLock.Holder.Should().BeNull("续期失败不该把锁又写回去");
    }

    [Fact]
    [Trait("Category", "SoftLock")]
    public void An_expired_holder_can_take_the_lock_again()
    {
        var clock = new ManualTimeProvider(Start);
        var softLock = new SoftLock(clock: clock);

        softLock.TryAcquire("agent-a", out _).Should().BeTrue();
        clock.Advance(TimeSpan.FromSeconds(31));

        softLock.TryAcquire("agent-a", out _).Should().BeTrue();
        softLock.Holder.Should().Be("agent-a");
    }

    [Fact]
    [Trait("Category", "SoftLock")]
    public void The_ttl_comes_from_the_options()
    {
        // 时长写死在实现里的话，用例只能靠真的等三十秒来验过期。
        var clock = new ManualTimeProvider(Start);
        var softLock = new SoftLock(new SoftLockOptions { Ttl = TimeSpan.FromSeconds(2) }, clock);

        softLock.TryAcquire("agent-a", out _).Should().BeTrue();

        clock.Advance(TimeSpan.FromSeconds(3));

        softLock.IsFree.Should().BeTrue();
    }

    #endregion

    #region 释放

    [Fact]
    [Trait("Category", "SoftLock")]
    public void The_holder_can_release_it()
    {
        var clock = new ManualTimeProvider(Start);
        var softLock = new SoftLock(clock: clock);

        softLock.TryAcquire("agent-a", out _).Should().BeTrue();
        softLock.Release("agent-a").Should().BeTrue();

        softLock.IsFree.Should().BeTrue();
        softLock.TryAcquire("agent-b", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "SoftLock")]
    public void A_non_holder_cannot_release_it()
    {
        // 让非持有者放得掉的话，一个拼错的主体名会把别人的锁解掉，而那位正在写。
        var clock = new ManualTimeProvider(Start);
        var softLock = new SoftLock(clock: clock);

        softLock.TryAcquire("agent-a", out _).Should().BeTrue();

        softLock.Release("agent-b").Should().BeFalse();
        softLock.Holder.Should().Be("agent-a");
    }

    [Fact]
    [Trait("Category", "SoftLock")]
    public void Releasing_twice_is_harmless()
    {
        var clock = new ManualTimeProvider(Start);
        var softLock = new SoftLock(clock: clock);

        softLock.TryAcquire("agent-a", out _).Should().BeTrue();
        softLock.Release("agent-a").Should().BeTrue();

        softLock.Release("agent-a").Should().BeFalse("锁已经不在它手上了");
        softLock.IsFree.Should().BeTrue();
    }

    #endregion

    #region 与工作区

    [Fact]
    [Trait("Category", "SoftLock")]
    public async Task The_workspace_carries_one()
    {
        // 软锁的粒度是文档，而工作区正是"一份文档加一条总线"那个运行单元。
        // 挂在别处的话，同一个进程里两个窗口会各自拿到一把锁，而它们看的是同一份文档。
        var clock = new ManualTimeProvider(Start);

        await using var workspace = NewWorkspace(clock);

        workspace.SoftLock.Ttl.Should().Be(SoftLockOptions.DefaultTtl);

        workspace.SoftLock.TryAcquire("agent-a", out _).Should().BeTrue();
        workspace.SoftLock.Holder.Should().Be("agent-a");
    }

    [Fact]
    [Trait("Category", "SoftLock")]
    public async Task The_workspace_lock_uses_the_bus_clock()
    {
        // 时钟取上下文里那一个：用例注入可推进的时钟之后，"刚过三十秒"这类场景才构造得出来。
        var clock = new ManualTimeProvider(Start);

        await using var workspace = NewWorkspace(clock);

        workspace.SoftLock.TryAcquire("agent-a", out _).Should().BeTrue();

        clock.Advance(TimeSpan.FromSeconds(31));

        workspace.SoftLock.IsFree.Should().BeTrue();
    }

    /// <summary>造一个只带时钟的工作区。软锁不碰文档，所以文档是空的也无妨。</summary>
    private static DiagramWorkspace NewWorkspace(ITimeProvider clock) =>
        DiagramWorkspace.CreateOwned(
            new DiagramDocument("lock-doc"),
            new SimpleSessionProvider("tester", SessionIds.Gui("w1")),
            DiagramCommandBusOptions.ForGui(),
            clock);

    #endregion
}
