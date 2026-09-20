using System.Diagnostics;
using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Commands;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 广播器的三项硬要求：写入不阻塞、订阅者互不影响、关闭有上限。
/// </summary>
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

        // 订阅者每条睡 1 毫秒，等于每秒最多消费一千条。
        using var subscription = broadcaster.Subscribe(_ => Thread.Sleep(1));

        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < 20_000; i++)
        {
            broadcaster.Enqueue(Notification(i));
        }

        stopwatch.Stop();

        // 两万条按订阅者的速度要二十秒以上。写入方必须在毫秒级返回：
        // 它站在命令执行的路径上，被拖住等于冻结整个编辑操作。
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

        // 第一个订阅者抛异常不能阻止第二个收到通知，也不能让后台投递循环结束。
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

        // 退订之后不能再收到任何投递。界面窗口关闭后回调还打到已销毁的控件上，
        // 是这类订阅机制最典型的崩溃来源。
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

        // 关闭有等待上限，不能让一个卡住的订阅者把关闭流程无限拖住。
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    [Fact]
    [Trait("Category", "Broadcaster")]
    public void No_op_broadcaster_is_inert()
    {
        var broadcaster = NullChangeBroadcaster.Instance;

        broadcaster.Enqueue(Notification(1));

        // 订阅了也不该收到任何东西——它存在的意义就是让调用方省掉空值判断。
        using var subscription = broadcaster.Subscribe(_ => throw new InvalidOperationException("must never run"));

        broadcaster.DisposeAsync().IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Broadcaster")]
    public void Channel_capacity_is_bounded()
    {
        // 有界容量是"内存占用可预测"的唯一保证。改成无界之后，
        // 一个不消费的订阅者就能把内存吃光，而且症状是缓慢增长、极难定位。
        InProcessBroadcaster.ChannelCapacity.Should().Be(1024);
    }
}
