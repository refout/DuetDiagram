using System.Globalization;
using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Concurrency;
using DuetDiagram.Core.Diagnostics;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Core.Time;
using ModelContextProtocol.Protocol;

namespace DuetDiagram.Mcp.Server;

/// <summary>
/// 两条传输共用的那一份会话：文档、命令总线、变更广播，以及调用方随请求带上来的声明。
/// </summary>
/// <remarks>
/// <para>
/// 标准输入输出与网络两条传输挂在同一个进程里的是同一件事：八个工具作用在某一份文档上，
/// 而那份文档的每一次改动都走命令总线。把它拆成两份实现的话，最要紧的那一点——
/// 「每条请求都重写一次声明，包括这次没声明那一种」——会在其中一份里被漏掉，
/// 而漏掉的后果是「一次没声明的调用继承了上一条请求的版本」，两边都不报错。
/// </para>
/// <para>
/// 声明放在随异步流走的槽里，不放在字段上：它随每条请求变，而工具是按会话建一次、
/// 之后一直复用的。放字段上的话，第二条请求会拿着第一条的声明去比对。
/// </para>
/// </remarks>
public sealed class SessionCore : IAsyncDisposable
{
    private readonly AsyncLocal<SessionState?> _declared = new();
    private readonly AsyncLocal<PermissionSet?> _permissions = new();

    private SessionCore(DiagramCommandBus bus, InProcessBroadcaster broadcaster)
    {
        Bus = bus;
        Broadcaster = broadcaster;
        Ack = new SessionAck(bus.Context.Document.Id, bus.Context.Document.Version, bus.Context.Document.StructuralHash);
    }

    /// <summary>命令总线。八个工具的每一条改动都从这里走。</summary>
    public DiagramCommandBus Bus { get; }

    /// <summary>这份文档的变更广播。变化源端点订的就是它。</summary>
    public InProcessBroadcaster Broadcaster { get; }

    /// <summary>会话建立时回给调用方的那一份状态。</summary>
    public SessionAck Ack { get; }

    /// <summary>这份会话正在编辑的文档。与总线管着的是同一个对象。</summary>
    public DiagramDocument Document => Bus.Context.Document;

    /// <summary>这一条请求声明的会话状态。没声明时为空。</summary>
    public SessionState? Declared => _declared.Value;

    /// <summary>
    /// 建一份会话。
    /// </summary>
    /// <param name="transport">这一份会话挂在哪条传输上，只进会话标识，用来分辨审计记录。</param>
    /// <param name="documentPath">要编辑的文档文件。为空表示从一张空图开始。</param>
    /// <param name="diagnostics">应用日志出口。为空表示不记。</param>
    public static SessionCore Create(
        string transport,
        string? documentPath = null,
        IDiagnosticsSink? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);

        var document = Load(documentPath);

        // 带版本检查的模式要求有一个真的广播器：这个模式意味着存在多个写入方，
        // 而变更必须真的能被送出去。空实现过不了这道检查，这正是那条约束的用意。
        var broadcaster = new InProcessBroadcaster();

        var bus = new DiagramCommandBus(DiagramCommandBusContext.Create(
            document,
            new SimpleSessionProvider(
                "mcp-agent",
                SessionIds.Mcp(transport, Environment.ProcessId.ToString(CultureInfo.InvariantCulture))),
            broadcaster,
            DiagramCommandBusOptions.ForMcp(),
            SystemTimeProvider.Instance,
            diagnostics));

        return new SessionCore(bus, broadcaster);
    }

    /// <summary>
    /// 记下这条请求自带的会话状态，并把它放进本次调用的槽里。
    /// </summary>
    /// <remarks>
    /// 每条请求都重写一次，**包括「这次没声明」那一种**：留着上一条的值的话，
    /// 一次没声明的调用会继承上一条请求的版本，而那条版本可能是另一个调用方报的。
    /// 那会变成一次「照着别人的版本改」的写入，两边都不报错。
    /// </remarks>
    /// <returns>这一次读到的声明，供调用方写进自己的日志。</returns>
    public SessionState? Observe(JsonRpcMessage message)
    {
        var state = message is JsonRpcRequest request ? SessionState.FromRequest(request.Params) : null;

        if (message is JsonRpcRequest)
        {
            _declared.Value = state;
        }

        return state;
    }

    /// <summary>本次调用要报给命令总线的版本声明。</summary>
    public VersionCheckRequest? DeclaredVersion() => _declared.Value?.ToVersionCheck();

    /// <summary>
    /// 这一次调用所属主体能改哪些图层。
    /// </summary>
    /// <remarks>
    /// 没有主体时是不受限：标准输入输出那条通路下这个进程只服务一个客户端，
    /// 它由客户端作为子进程拉起来，不存在"这一份凭据与那一份凭据"的分别。
    /// </remarks>
    public PermissionSet Permissions => _permissions.Value ?? PermissionSet.Full;

    /// <summary>
    /// 记下这一条请求所属的主体。
    /// </summary>
    /// <remarks>
    /// 与声明同一个做法：**每条请求重写一次**。传输层是无状态的，上一条请求的主体留着不放的话，
    /// 一次没带凭据的调用会继承上一条请求的图层范围——而那份范围可能是另一个调用方的，
    /// 于是它要么被多挡一次，要么被少挡一次，两边都不报错。
    /// </remarks>
    public void Scope(PermissionSet permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        _permissions.Value = permissions;
    }

    public async ValueTask DisposeAsync()
    {
        Bus.Dispose();

        await Broadcaster.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 读一份文档。给了路径就从文件读，没给就从一张空图开始。
    /// </summary>
    /// <remarks>
    /// 路径要由调用方先过工作区那道关：这一层只负责读，不判断该不该读。
    /// 判断混进来之后，两条传输各判一次，而两处判据迟早不一样。
    /// </remarks>
    private static DiagramDocument Load(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? new DiagramDocument("mcp-document")
            : DiagramSerializer.DeserializeFull(File.ReadAllText(Path.GetFullPath(path)));
}
