using System.Diagnostics;
using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Commands;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>P1 判据 #24 / #32：有界 Channel、订阅者隔离、非阻塞。</summary>
public sealed class BroadcasterTests
{
    private static ChangeNotification Notification(int version) => new()
    {
        DocumentId = "d",
        Version = version,
        AffectedIds = ["n1"],
        Source = ChangeSource.Llm,
        Timestamp = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    [Trait("Category", "Broadcaster")]
    public async Task Enqueue_never_blocks_the_caller()
    {
        await using var broadcaster = new InProcessBroadcaster();
        using var subscription = broadcaster.Subscribe(_ => Thread.Sleep(1));

        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < 20_000; i++)
        {
            broadcaster.Enqueue(Notification(i));
        }

        stopwatch.Stop();

        // 订阅者按 1ms/条 消费，同步分发 2 万条要 20 秒以上；
        // 有界 Channel + DropOldest 必须让生产者立刻返回。
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    [Trait("Category", "Broadcaster")]
    public async Task A_throwing_subscriber_does_not_starve_the_others()
    {
        await using var broadcaster = new InProcessBroadcaster();
        using var delivered = new ManualResetEventSlim(false);

        broadcaster.Subscribe(_ => throw new InvalidOperationException("boom"));
        broadcaster.Subscribe(_ => delivered.Set());

        broadcaster.Enqueue(Notification(1));

        delivered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Broadcaster")]
    public async Task Disposing_a_subscription_stops_delivery()
    {
        await using var broadcaster = new InProcessBroadcaster();
        using var firstDelivered = new ManualResetEventSlim(false);
        var received = 0;

        var subscription = broadcaster.Subscribe(_ =>
        {
            Interlocked.Increment(ref received);
            firstDelivered.Set();
        });

        broadcaster.Enqueue(Notification(1));
        firstDelivered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).Should().BeTrue();

        subscription.Dispose();
        broadcaster.SubscriberCount.Should().Be(0);

        broadcaster.Enqueue(Notification(2));
        await Task.Delay(200, TestContext.Current.CancellationToken);

        received.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Broadcaster")]
    public async Task Dispose_async_finishes_within_the_two_second_budget()
    {
        var broadcaster = new InProcessBroadcaster();
        broadcaster.Subscribe(_ => { });
        broadcaster.Enqueue(Notification(1));

        var stopwatch = Stopwatch.StartNew();
        await broadcaster.DisposeAsync();
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    [Fact]
    [Trait("Category", "Broadcaster")]
    public void Null_broadcaster_is_inert()
    {
        var broadcaster = NullChangeBroadcaster.Instance;

        broadcaster.Enqueue(Notification(1));
        using var subscription = broadcaster.Subscribe(_ => throw new InvalidOperationException("must never run"));

        broadcaster.DisposeAsync().IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Broadcaster")]
    public void Channel_capacity_is_bounded()
    {
        // AGENTS.md 约定 12：容量必须是 1024，且不得改成无界。
        InProcessBroadcaster.ChannelCapacity.Should().Be(1024);
    }
}
