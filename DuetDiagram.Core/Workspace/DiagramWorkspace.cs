using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Time;

namespace DuetDiagram.Core.Workspace;

/// <summary>
/// 一份文档加一条命令总线的运行单元，是宿主拿到手的入口对象。
/// </summary>
/// <remarks>
/// <para>
/// 多窗口共处一个进程时，各窗口共享同一个工作区实例：文档只有一份，
/// 变更通过广播器通知到每个窗口，所有窗口看到的版本号天然一致，不存在版本冲突。
/// </para>
/// <para>
/// 生命周期上最关键的是广播器归谁释放。<see cref="ownsBroadcaster"/> 就是这个约定的载体：
/// 广播器可能是这个工作区创建的，也可能是外部传进来给多个工作区共用的。
/// 释放时只能由创建者动手，否则先关掉的那个窗口会把广播器一起销毁，
/// 剩下还在用的窗口就再也收不到任何通知，而且症状是"界面莫名不再刷新"，很难定位。
/// </para>
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

    /// <summary>广播器取自上下文，与命令总线用的是同一个实例。</summary>
    public IChangeBroadcaster Broadcaster => Context.Broadcaster;

    /// <summary>
    /// 创建一个自带广播器、并负责释放它的工作区。
    /// 适用于独立宿主、导入器等"这个工作区独占这份文档"的场景。
    /// </summary>
    /// <remarks>
    /// 这里把创建与所有权设成一体，是为了避免调用方自己 new 一个广播器之后
    /// 忘记传所有权标志——那样会泄漏一个后台投递任务，而且没有任何编译期提示。
    /// </remarks>
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

    /// <summary>
    /// 释放工作区。顺序是先关命令总线再考虑广播器。
    /// </summary>
    /// <remarks>
    /// 命令总线的门锁要先释放掉，之后就不会再有新的变更进来，
    /// 这时再去关广播器才不会出现"正在投递最后一条通知时被销毁"的竞态。
    /// 广播器只在拥有所有权时释放，共享场景留给创建者处理。
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        CommandBus.Dispose();

        if (_ownsBroadcaster)
        {
            await Broadcaster.DisposeAsync().ConfigureAwait(false);
        }
    }
}
