namespace DuetDiagram.Core.Broadcasting;

/// <summary>
/// 什么都不做的广播器。用于单窗口界面、导入等确定没有订阅者的场景。
/// </summary>
/// <remarks>
/// <para>
/// 它存在的意义是省掉到处写空值判断。宿主总是能拿到一个可用的广播器实例，
/// 不需要在每个调用点判断"这次有没有广播器"。
/// </para>
/// <para>
/// 但它**不能**用来糊弄需要跨进程通知的场景。多进程协作时，变更必须真的发出去，
/// 否则另一个进程永远不知道文档变了。所以命令总线在启用版本检查的模式下，
/// 会专门识别出这个类型并拒绝启动——注意判断依据必须是"是不是这个类型"，
/// 而不是"是不是空引用"，因为这里永远不是空引用，只用空值判断等于没有这道防线。
/// </para>
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
