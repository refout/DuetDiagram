namespace DuetDiagram.Core.Broadcasting;

/// <summary>
/// 空广播器。用于 GUI 单窗口或导入等无订阅者场景。
/// </summary>
/// <remarks>
/// AGENTS.md 约定 10：MCP 模式（RequiresVersionCheck=true）下构造函数必须用
/// <c>is NullChangeBroadcaster</c> 判据拒绝本类型，而不是判 null。
/// </remarks>
public sealed class NullChangeBroadcaster : IChangeBroadcaster
{
    public static NullChangeBroadcaster Instance { get; } = new();

    private NullChangeBroadcaster()
    {
    }

    public void Enqueue(ChangeNotification notification)
    {
    }

    public IDisposable Subscribe(Action<ChangeNotification> handler) => NullSubscription.Instance;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed class NullSubscription : IDisposable
    {
        public static NullSubscription Instance { get; } = new();

        private NullSubscription()
        {
        }

        public void Dispose()
        {
        }
    }
}
