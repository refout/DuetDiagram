namespace DuetDiagram.Core.Bus;

/// <summary>
/// 命令总线的行为开关。目前只有一项，但它的取值决定了整个冲突处理路径是否启用。
/// </summary>
/// <remarks>
/// <para>
/// 之所以用工厂方法而不是公开构造函数加可写属性：这个开关必须与调用场景严格对应，
/// 用错了会造成很难查的问题。同一个进程内的界面操作去走版本检查，会在每次操作时
/// 因为本地版本恰好匹配而白白多做一次计算；反过来，多个进程同时改一份文档却不做版本检查，
/// 后写的会静默覆盖先写的，而且没有任何迹象表明发生了数据丢失。
/// 收敛成三个具名工厂之后，调用点的意图一眼可见，也不会误传参数。
/// </para>
/// <para>
/// 属性用私有 setter，让外部无法在构造之后再翻转它。
/// </para>
/// </remarks>
public sealed class DiagramCommandBusOptions
{
    private DiagramCommandBusOptions(bool requiresVersionCheck) => RequiresVersionCheck = requiresVersionCheck;

    /// <summary>
    /// 为真时，每条命令都必须携带版本声明，且版本不匹配会被拒绝。
    /// </summary>
    public bool RequiresVersionCheck { get; private init; }

    /// <summary>单进程界面：文档只被本进程修改，不存在并发写入。</summary>
    public static DiagramCommandBusOptions ForGui() => new(false) { RequiresVersionCheck = false };

    /// <summary>外部代理接入：可能有多个连接同时写同一份文档，必须做乐观并发检查。</summary>
    public static DiagramCommandBusOptions ForMcp() => new(true) { RequiresVersionCheck = true };

    /// <summary>批量导入：一次性写入，期间不对外开放，无需版本检查。</summary>
    public static DiagramCommandBusOptions ForImport() => new(false) { RequiresVersionCheck = false };
}
