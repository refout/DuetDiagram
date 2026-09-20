using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Diagnostics;
using DuetDiagram.Core.History;
using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Time;

namespace DuetDiagram.Core.Bus;

/// <summary>
/// 命令总线的全部外部依赖，集中成一个对象。
/// </summary>
/// <remarks>
/// <para>
/// 把它们打包是有实际好处的：总线的构造函数只需一个参数，
/// 而共享这些依赖的多个总线（例如同一个界面进程里几个窗口共用一份日志与广播器）可以共用同一个上下文。
/// </para>
/// <para>
/// 可空的那几项都是"可选的横切关注点"，缺省时用无操作实现兜底：
/// 时钟缺省读系统时间，诊断出口缺省丢弃消息。这样最小用例只需要提供文档、会话、广播器和选项。
/// </para>
/// </remarks>
public sealed record DiagramCommandBusContext
{
    public required DiagramDocument Document { get; init; }

    public required HistoryStack History { get; init; }

    public required VersionLog VersionLog { get; init; }

    public required AuditLog AuditLog { get; init; }

    /// <summary>当前操作主体。命令上下文里没声明会话时，总线从这里取值兜底。</summary>
    public required ISessionProvider Session { get; init; }

    public required IChangeBroadcaster Broadcaster { get; init; }

    public required DiagramCommandBusOptions Options { get; init; }

    public ITimeProvider Clock { get; init; } = SystemTimeProvider.Instance;

    public IDiagnosticsSink Diagnostics { get; init; } = NullDiagnosticsSink.Instance;

    /// <summary>
    /// 组装上下文。日志类依赖可以显式传入，这样多个总线就能共用同一份历史与日志，
    /// 不传则各自新建一份。
    /// </summary>
    public static DiagramCommandBusContext Create(
        DiagramDocument document,
        ISessionProvider session,
        IChangeBroadcaster broadcaster,
        DiagramCommandBusOptions options,
        ITimeProvider? clock = null,
        IDiagnosticsSink? diagnostics = null,
        HistoryStack? history = null,
        VersionLog? versionLog = null,
        AuditLog? auditLog = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(broadcaster);
        ArgumentNullException.ThrowIfNull(options);

        return new DiagramCommandBusContext
        {
            Document = document,
            History = history ?? new HistoryStack(),
            VersionLog = versionLog ?? new VersionLog(),
            AuditLog = auditLog ?? new AuditLog(),
            Session = session,
            Broadcaster = broadcaster,
            Options = options,
            Clock = clock ?? SystemTimeProvider.Instance,
            Diagnostics = diagnostics ?? NullDiagnosticsSink.Instance,
        };
    }
}
