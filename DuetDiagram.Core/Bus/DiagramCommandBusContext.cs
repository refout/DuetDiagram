using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Diagnostics;
using DuetDiagram.Core.History;
using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Time;

namespace DuetDiagram.Core.Bus;

/// <summary>
/// 命令总线的全部依赖。
/// </summary>
/// <remarks>
/// 方案 §4.9 列出 11 项依赖。本轮垂直切片实现 8 项；
/// <c>Sidecar</c> / <c>Layout</c> / <c>Renderer</c> 未实现（见 AGENTS.md「与方案的已知差异」），
/// 因此「结构变更触发重布局，否则重绘」这步目前由 <c>CommandResult</c> 的两个布尔标志外化给宿主。
/// </remarks>
public sealed record DiagramCommandBusContext
{
    public required DiagramDocument Document { get; init; }

    public required HistoryStack History { get; init; }

    public required VersionLog VersionLog { get; init; }

    public required AuditLog AuditLog { get; init; }

    public required ISessionProvider Session { get; init; }

    public required IChangeBroadcaster Broadcaster { get; init; }

    public required DiagramCommandBusOptions Options { get; init; }

    public ITimeProvider Clock { get; init; } = SystemTimeProvider.Instance;

    public IDiagnosticsSink Diagnostics { get; init; } = NullDiagnosticsSink.Instance;

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
