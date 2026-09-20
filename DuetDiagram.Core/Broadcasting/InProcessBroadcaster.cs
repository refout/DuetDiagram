using System.Collections.Concurrent;
using System.Threading.Channels;

namespace DuetDiagram.Core.Broadcasting;

/// <summary>
/// 进程内广播器：生产者写入通道即返回，后台任务负责投递给订阅者。
/// </summary>
/// <remarks>
/// <para>
/// 通道是**有界**的，容量 1024，写满时丢弃最旧的一条。
/// 这两个参数合起来决定了背压策略：有界保证内存不会无限增长，
/// 丢弃最旧而不是阻塞或拒绝，保证生产者永远不会被拖慢。
/// 对变更通知来说丢旧的是安全的——订阅者后续会收到更新的版本号，
/// 它可以用版本日志把丢掉的那些补回来；卡住编辑操作才是不可接受的。
/// </para>
/// <para>
/// 单个订阅者抛异常必须被吞掉，只影响它自己。一个写坏的界面回调不应该让
/// 其它窗口收不到变更，更不应该让后台投递任务整个结束。
/// </para>
/// <para>
/// 关闭时先取消，再等后台任务结束，最多等 2 秒。
/// 超时之后**不释放**取消令牌：投递任务可能还在使用它，
/// 提前释放会让它拿到已释放的对象并抛异常。宁可留一个小的托管对象给垃圾回收，
/// 也不要制造出难以复现的并发异常。
/// </para>
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

        // 后台投递任务从这里开始跑，一直读到令牌被取消为止。
        _dispatch = Task.Run(DispatchAsync);
    }

    public int SubscriberCount => _subscribers.Count;

    /// <summary>
    /// 非阻塞写入。返回值的丢失情况无需调用方关心。
    /// </summary>
    /// <remarks>
    /// 用带返回值的写入尝试而不是异步写入：通道满时异步写入会让调用方挂起等待，
    /// 而这里要的正是"绝不等待"。丢弃由通道的满模式策略处理。
    /// </remarks>
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

        // 用标识而不是回调本身做键：同一个方法被订阅两次是合法用法，两次都应生效。
        return new Subscription(() => _subscribers.TryRemove(id, out _));
    }

    private async Task DispatchAsync()
    {
        try
        {
            await foreach (var notification in _channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                // 直接遍历并发字典。投递期间有人订阅或退订都能容忍：
                // 并发字典的枚举是安全的快照语义，最坏情况是这一轮漏掉或多投一次，
                // 而通知本身是幂等的（只带版本号），多投一次不会造成状态错误。
                foreach (var (_, handler) in _subscribers)
                {
                    try
                    {
                        handler(notification);
                    }
                    catch (Exception)
                    {
                        // 故意吞掉：一个订阅者出问题不能影响其它订阅者，也不能让投递任务结束。
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭路径。
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
            // 投递任务没在时限内结束。此时保持取消令牌存活，避免它被使用中的代码访问到已释放对象。
        }
        catch (Exception)
        {
            // 任务已经结束（异常在这里被观察到）。既然结束了就可以安全释放令牌。
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

        /// <summary>
        /// 用交换保证只退订一次。重复释放是常见写法（例如同时被 using 和手动调用），
        /// 不做保护的话第二次调用会对着已经移除的键做无意义操作，或者更糟——重复执行副作用。
        /// </summary>
        public void Dispose()
        {
            Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
        }
    }
}
