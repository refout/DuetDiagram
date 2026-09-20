using DuetDiagram.Core.Commands;

namespace DuetDiagram.Core.Broadcasting;

/// <summary>
/// 一条变更通知。
/// </summary>
/// <remarks>
/// 通知本身不携带变更内容，只有一个版本号和受影响元素清单。这是有意的：
/// 通知会分发给多个订阅者，把完整内容塞进来会让内存放大；
/// 需要细节的订阅者拿着版本号去查版本日志即可。
/// </remarks>
public sealed record ChangeNotification
{
    public required string DocumentId { get; init; }

    /// <summary>
    /// 单调递增的版本号，也是**唯一**可用于排序的字段。
    /// 时间戳只用于日志和审计：多进程场景下各机器的时钟可能不同步，
    /// 拿时间戳排序会得到错误的先后关系。
    /// </summary>
    public required int Version { get; init; }

    public string[] AffectedIds { get; init; } = [];

    public required ChangeSource Source { get; init; }

    /// <summary>仅用于日志与审计，不要用于排序。</summary>
    public required DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// 变更广播器。文档每次成功变更后由命令总线调用一次。
/// </summary>
/// <remarks>
/// 接口只有两个动作，但都对实现有硬性要求：
/// <see cref="Enqueue"/> 必须立即返回，绝不能因为订阅者处理慢而阻塞调用方——
/// 调用方是命令总线，阻塞它等于冻结整个编辑操作。
/// <see cref="Subscribe"/> 返回的句柄释放后必须彻底停止投递，
/// 否则界面窗口关掉之后回调还会打到已经销毁的控件上。
/// </remarks>
public interface IChangeBroadcaster : IAsyncDisposable
{
    void Enqueue(ChangeNotification notification);

    /// <summary>订阅。释放返回的句柄即取消订阅。</summary>
    IDisposable Subscribe(Action<ChangeNotification> handler);
}
