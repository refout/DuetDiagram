using System.Text.Json.Nodes;
using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Mcp.Server;

/// <summary>
/// 文档当前的样子：停在第几版、结构哈希是什么、最近一次改动碰了谁。
/// </summary>
/// <param name="Version">当前版本号。排序与比较只看它。</param>
/// <param name="StructuralHash">当前结构哈希。没变过时为空。</param>
/// <param name="AffectedIds">最近一次改动碰过的元素。</param>
/// <param name="Timestamp">最近一次改动的时刻。只用于日志。</param>
public sealed record ChangeNotice(
    int Version,
    string StructuralHash,
    string[] AffectedIds,
    DateTimeOffset Timestamp)
{
    public JsonObject ToJson() => new()
    {
        ["version"] = Version,
        ["structuralHash"] = StructuralHash,
        ["affectedIds"] = new JsonArray([.. AffectedIds.Select(id => JsonValue.Create(id))]),
    };
}

/// <summary>
/// 变化源：已连接的代理靠它知道文档什么时候变了。
/// </summary>
/// <remarks>
/// <para>
/// **它不是协议那条推送。** 协议那条路在当前版本下要么不支持（无状态模式明文写着
/// 服务端主动发的消息不支持），要么得踩过时与实验性的接口。所以这一条做成一条普通的
/// 只读端点，形状就是长轮询——这正是传输那一节说的那件事。
/// </para>
/// <para>
/// **只保存最新的一份状态，不保存通知历史。** 于是断线重连按版本号补差：
/// 调用方报上它见过的最后一版，服务端要么立刻告诉它现在是第几版，要么等。
/// 存历史的话，一个断了一小时的调用方会把这一小时里的每一条都收一遍，
/// 而它真正需要的只是"现在是第几版"。
/// </para>
/// <para>
/// 版本号与结构哈希在应答的那一刻一起从文档上读。分开读的话，两份值可能来自不同的版本
/// ——而调用方拿到它们是要拿去比对并据此决定重不重读的，对不上比不读还糟。
/// </para>
/// </remarks>
public sealed class ChangeFeed : IDisposable
{
    private readonly DiagramDocument _document;
    private readonly IDisposable _subscription;
    private readonly Lock _gate = new();

    private TaskCompletionSource _signal = NewSignal();
    private string[] _affected = [];
    private DateTimeOffset _timestamp;

    public ChangeFeed(DiagramDocument document, IChangeBroadcaster broadcaster)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(broadcaster);

        _document = document;
        _subscription = broadcaster.Subscribe(OnChange);
    }

    /// <summary>当前这一刻的样子。</summary>
    public ChangeNotice Current()
    {
        lock (_gate)
        {
            return new ChangeNotice(_document.Version, _document.StructuralHash, [.. _affected], _timestamp);
        }
    }

    /// <summary>
    /// 等到版本号超过 <paramref name="since"/> 的那一刻。
    /// </summary>
    /// <remarks>
    /// 已经有更新的版本就立刻回，不等到超时：调用方报的版本落在后面时，
    /// 它要的答案现在就有，让它干等一轮纯粹是白花的时间。
    /// </remarks>
    /// <param name="since">调用方见过的最后一版。</param>
    /// <param name="timeout">最多等多久。</param>
    /// <param name="cancellationToken">调用方断开时用它停下。</param>
    /// <returns>新的一刻；等到超时都没有变化时为空。</returns>
    public async Task<ChangeNotice?> WaitAsync(int since, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = TimeProvider.System.GetUtcNow() + timeout;

        // 唤醒它的可能是一条与这份文档无关的变更，所以醒来之后要再看一眼；
        // 那时要么真的有了新版本，要么接着等。直接回空会让调用方以为"这一轮没变"。
        while (true)
        {
            if (Current() is { } ready && ready.Version > since)
            {
                return ready;
            }

            var left = deadline - TimeProvider.System.GetUtcNow();

            if (left <= TimeSpan.Zero)
            {
                return null;
            }

            Task signal;

            lock (_gate)
            {
                signal = _signal.Task;
            }

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(left);

            try
            {
                await signal.WaitAsync(budget.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // 等到超时。调用方自己断开那一种不在这里吞掉：那一条要往外传，
                // 好让传输层知道这个响应已经没人要了。
                return null;
            }
        }
    }

    public void Dispose() => _subscription.Dispose();

    private void OnChange(ChangeNotification notification)
    {
        TaskCompletionSource signal;

        lock (_gate)
        {
            _affected = [.. notification.AffectedIds];
            _timestamp = notification.Timestamp;

            signal = _signal;
            _signal = NewSignal();
        }

        signal.TrySetResult();
    }

    /// <summary>
    /// 一个只用来唤醒等待者的信号。
    /// </summary>
    /// <remarks>
    /// 每次唤醒之后换一个新的，而不是复位同一个：复位那一步与"又来了一个变更"之间
    /// 有一个窗口，落在窗口里的那一条会被丢掉，而表现是"改了却没人被通知"。
    /// 延续不排在原地跑，免得订阅回调在广播器的分发线程上等一整条应答。
    /// </remarks>
    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
