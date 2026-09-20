namespace DuetDiagram.Core.Bus;

/// <summary>
/// 命令总线选项。只有三个工厂能创建实例，<see cref="RequiresVersionCheck"/> 不可被外部改写。
/// </summary>
public sealed class DiagramCommandBusOptions
{
    private DiagramCommandBusOptions(bool requiresVersionCheck) => RequiresVersionCheck = requiresVersionCheck;

    public bool RequiresVersionCheck { get; private init; }

    /// <summary>GUI 内部：单进程内已有广播与锁，无需版本检查。</summary>
    public static DiagramCommandBusOptions ForGui() => new(false) { RequiresVersionCheck = false };

    /// <summary>MCP：多 agent 并发，必须做乐观并发检查。</summary>
    public static DiagramCommandBusOptions ForMcp() => new(true) { RequiresVersionCheck = true };

    /// <summary>导入：一次性写入，无需版本检查。</summary>
    public static DiagramCommandBusOptions ForImport() => new(false) { RequiresVersionCheck = false };
}
