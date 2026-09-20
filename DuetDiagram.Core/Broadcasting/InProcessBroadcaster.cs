using System.Collections.Concurrent;
using System.Threading.Channels;

namespace DuetDiagram.Core.Broadcasting;

/// <summary>
/// 进程内广播器。
/// </summary>
/// <remarks>
/// AGENTS.md 约定 12：必须使用**有界** Channel（容量 1024，满时 DropOldest），不得使用无界 Channel。
/// AGENTS.md 约定 13：<see cref="DisposeAsync"/> 等待分发任务结束，超时 2 秒；
/// 超时后**不得**释放 CTS。
/// </remarks>
public sealed class InProcessBroadcaster : IChangeBroadcaster
{
    public const int ChannelCapacity = 1024;

    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(2);

    private readonly Channel<ChangeNotification> _channel;
    private readonly ConcurrentDictionary<Guid, Action<ChangeNotification>> _subscribers = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _dispatch;

    public InProcessBroadcaster()
    {
        _channel = Channel.CreateBounded<ChangeNotification>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        _dispatch = Task.Run(DispatchAsync);
    }

    public int SubscriberCount => _subscribers.Count;

    /// <summary>非阻塞写入。Channel 满时丢弃最旧的一条。</summary>
    public void Enqueue(ChangeNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        _channel.Writer.TryWrite(notification);
    }

    public IDisposable Subscribe(Action<ChangeNotification> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var id = Guid.NewGuid();
        _subscribers[id] = handler;

        return new Subscription(() => _subscribers.TryRemove(id, out _));
    }

    private async Task DispatchAsync()
    {
        try
        {
            await foreach (var notification in _channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                foreach (var (_, handler) in _subscribers)
                {
                    try
                    {
                        handler(notification);
                    }
                    catch (Exception)
                    {
                        // 单个订阅者异常不影响其他订阅者，也不得让分发任务结束。
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭。
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);

        var finished = false;

        try
        {
            await _dispatch.WaitAsync(DisposeTimeout).ConfigureAwait(false);
            finished = true;
        }
        catch (TimeoutException)
        {
            // 分发任务仍在运行：按约定 13 不释放 CTS，避免其被提前释放。
        }
        catch (Exception)
        {
            // 任务已结束（异常在此被观察），可以安全释放。
            finished = true;
        }

        if (finished)
        {
            _cts.Dispose();
        }
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;

        public void Dispose()
        {
            Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
        }
    }
}
