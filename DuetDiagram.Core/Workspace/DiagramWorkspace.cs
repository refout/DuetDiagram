using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Time;

namespace DuetDiagram.Core.Workspace;

/// <summary>
/// 一份文档 + 一条命令总线的运行单元。GUI 多窗口共享同一个 Workspace。
/// </summary>
/// <remarks>
/// AGENTS.md 约定 3：<paramref name="ownsBroadcaster"/> 明确广播器所有权。
/// 多个 Workspace 共享 broadcaster 时只有创建者传 true；<see cref="DisposeAsync"/>
/// 仅在拥有所有权时释放它，否则会互相破坏。
/// </remarks>
public sealed class DiagramWorkspace : IAsyncDisposable
{
    private readonly bool _ownsBroadcaster;

    public DiagramWorkspace(DiagramCommandBusContext context, bool ownsBroadcaster = false)
    {
        ArgumentNullException.ThrowIfNull(context);

        Context = context;
        _ownsBroadcaster = ownsBroadcaster;
        CommandBus = new DiagramCommandBus(context);
    }

    public DiagramCommandBusContext Context { get; }

    public DiagramDocument Document => Context.Document;

    public DiagramCommandBus CommandBus { get; }

    public IChangeBroadcaster Broadcaster => Context.Broadcaster;

    /// <summary>创建并持有 broadcaster 的 Workspace（独立宿主、导入器、单窗口场景）。</summary>
    public static DiagramWorkspace CreateOwned(
        DiagramDocument document,
        ISessionProvider session,
        DiagramCommandBusOptions options,
        ITimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);

        var broadcaster = new InProcessBroadcaster();
        var context = DiagramCommandBusContext.Create(
            document,
            session,
            broadcaster,
            options,
            clock);

        return new DiagramWorkspace(context, ownsBroadcaster: true);
    }

    public async ValueTask DisposeAsync()
    {
        CommandBus.Dispose();

        if (_ownsBroadcaster)
        {
            await Broadcaster.DisposeAsync().ConfigureAwait(false);
        }
    }
}
