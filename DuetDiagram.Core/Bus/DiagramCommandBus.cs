using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.History;
using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;

namespace DuetDiagram.Core.Bus;

/// <summary>
/// 命令总线：所有修改文档的路径在这里汇合，之后走同一套校验、执行、记账、广播流程。
/// </summary>
/// <remarks>
/// <para>
/// 汇合点是"能力对等"的落点——总线之下没有任何代码知道这次变更来自界面、内置 AI 还是外部代理，
/// 只有一个来源标记用于审计与高亮。
/// </para>
/// <para>
/// 并发控制用一把异步门锁。所有执行路径（执行、撤销、重做）都先拿锁再动文档，
/// 因此文档的修改天然串行，版本号递增与哈希更新之间不会被打断。
/// </para>
/// <para>
/// 嵌套检测用执行上下文局部变量记录当前命令的深度。它有两个特性需要知道：
/// 一是这个变量会沿着异步调用链自动传递，所以命令里若另起一个任务再调用执行，
/// 新任务同样会看到非零深度并被拒绝；二是同一个总线实例上多个并发任务各自持有自己的深度副本，
/// 互不干扰。第一条是有意为之的保守策略：命令内重入几乎没有正当理由，
/// 而漏检会导致锁重入死锁，代价远大于偶尔误报。
/// </para>
/// </remarks>
public sealed class DiagramCommandBus : IDisposable
{
    public const string NestedExecuteMessage =
        "Nested Execute is not allowed. A command must not trigger another command; " +
        "AsyncLocal propagates into Task.Run, so this check also fires across threads. " +
        "Schedule the second command from the host instead.";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AsyncLocal<int> _depth = new();

    public DiagramCommandBus(DiagramCommandBusContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 需要版本检查的模式意味着存在多个写入方，此时变更必须真的能被广播出去。
        // 判据必须是"是不是那个空实现类型"，而不是"是不是空引用"——空实现永远是有效对象，
        // 只判空引用相当于这道防线不存在，而后果是另一个进程静默地永远收不到变更。
        if (context.Options.RequiresVersionCheck && context.Broadcaster is NullChangeBroadcaster)
        {
            throw new ArgumentException(
                "The version-checked mode requires a real broadcaster; " +
                "the no-op implementation cannot carry change notifications across processes.",
                nameof(context));
        }

        Context = context;
    }

    public DiagramCommandBusContext Context { get; }

    public CommandResult Execute(IDiagramCommand command, VersionCheckRequest? check = null)
    {
        EnsureNotNested();

        _gate.Wait();
        _depth.Value++;

        try
        {
            return ExecuteCore(command, check, CancellationToken.None);
        }
        finally
        {
            // 先减深度再放锁。顺序反了的话，另一个线程可能在深度还没归零时就进来并误判为嵌套。
            _depth.Value--;
            _gate.Release();
        }
    }

    public async Task<CommandResult> ExecuteAsync(
        IDiagramCommand command,
        VersionCheckRequest? check = null,
        CancellationToken cancellationToken = default)
    {
        EnsureNotNested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _depth.Value++;

        try
        {
            return ExecuteCore(command, check, cancellationToken);
        }
        finally
        {
            _depth.Value--;
            _gate.Release();
        }
    }

    /// <summary>撤销最近一次操作。人与自动操作共用同一条历史。</summary>
    public CommandResult Undo()
    {
        EnsureNotNested();

        _gate.Wait();
        _depth.Value++;

        try
        {
            return UndoCore();
        }
        finally
        {
            _depth.Value--;
            _gate.Release();
        }
    }

    /// <summary>重做最近一次被撤销的操作。</summary>
    public CommandResult Redo()
    {
        EnsureNotNested();

        _gate.Wait();
        _depth.Value++;

        try
        {
            return RedoCore();
        }
        finally
        {
            _depth.Value--;
            _gate.Release();
        }
    }

    /// <summary>
    /// 检查是否在命令内部又发起了命令。
    /// </summary>
    /// <remarks>
    /// 必须在拿锁**之前**检查。门锁不可重入，真等到拿锁那一步才发现嵌套，
    /// 外层命令会永远等自己持有的锁，表现为整个进程卡死而不是一条清楚的错误。
    /// </remarks>
    private void EnsureNotNested()
    {
        if (_depth.Value > 0)
        {
            throw new InvalidOperationException(NestedExecuteMessage);
        }
    }

    /// <summary>
    /// 执行的核心流程。调用方必须已经持有门锁并把深度加过一。
    /// </summary>
    /// <remarks>
    /// 步骤顺序是有依赖关系的，调整前要看清每一步为后一步准备了什么：
    /// <list type="number">
    /// <item>版本检查：先挡掉注定会冲突的请求，避免白做后面的工作。</item>
    /// <item>取当前时间并解析会话，用它们补齐命令上下文。命令本身是无状态的，
    /// 上下文规范化通过复制实现，所以历史栈里存的是规范化之后的副本。</item>
    /// <item>校验。不通过就记一条"被拒绝"的审计并返回，文档完全没被碰过。</item>
    /// <item>捕获快照。此刻文档还是变更前的状态，这是唯一能算准逆变更的时机。</item>
    /// <item>执行。正常返回失败和抛异常都走同一套回滚；取消单独记录以便与故障区分。</item>
    /// <item>成功之后才推进版本、更新哈希、写两份日志、入历史栈、清空重做栈。</item>
    /// <item>最后广播。放在最末尾，保证订阅者收到通知时文档和日志都已经是最终状态。</item>
    /// </list>
    /// 第 5 步里，"返回失败"与"抛异常"的处理差别只在是否把异常继续往上抛：
    /// 两者都要回滚，因为命令可能在返回失败之前已经改了一部分文档。
    /// </remarks>
    private CommandResult ExecuteCore(IDiagramCommand command, VersionCheckRequest? check, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var document = Context.Document;

        if (Context.Options.RequiresVersionCheck)
        {
            if (check is null)
            {
                return CommandResult.Fail(CommandError.Of(ErrorCodes.ExpectedVersionRequired, command.CommandId));
            }

            if (check.ClientVersion != document.Version)
            {
                // 差异在这里算出来只是为了区分两种错误码：调用方版本更靠前属于参数错误，
                // 其余属于并发冲突。差异本身由协议层在需要时另行索取。
                var diff = Context.VersionLog.BuildDiff(
                    check.ClientVersion,
                    document.Version,
                    check.ClientStructuralHash,
                    document.StructuralHash,
                    () => DiagramSerializer.SerializeFull(document));

                return CommandResult.Conflict(diff);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var timestamp = Context.Clock.UtcNow;
        var sessionId = ResolveSessionId(command.Context.SessionId);

        // 复制出规范化副本：调用方声明的时间戳被覆盖成总线取的当前时间，
        // 会话标识被补齐。原始命令对象不被修改，调用方可以安全地复用它。
        var normalized = command.WithContext(command.Context with
        {
            Timestamp = timestamp,
            SessionId = sessionId,
        });

        var validation = normalized.Validate(document);

        if (!validation.IsValid)
        {
            Record(normalized, AuditKind.Rejected, normalized.Context.Source, timestamp, sessionId, "validation failed", validation.Errors);
            return CommandResult.FailWith("Command validation failed.", validation.Errors);
        }

        var memento = normalized.CaptureMemento(document);

        CommandResult result;

        try
        {
            result = normalized.Apply(document);
        }
        catch (OperationCanceledException)
        {
            // 取消也走回滚：文档可能已经被改了一部分，任其留下会让取消变成一次静默的数据损坏。
            normalized.RestoreMemento(document, memento);
            Record(normalized, AuditKind.Cancelled, normalized.Context.Source, timestamp, sessionId, "apply cancelled", null);
            throw;
        }
        catch (Exception)
        {
            normalized.RestoreMemento(document, memento);

            // 审计日志里只留一个统一的内部分错误码。异常类型名会暴露内部结构，
            // 而且这里本来也看不到异常对象——它已经被丢掉了。
            Record(normalized, AuditKind.Failed, normalized.Context.Source, timestamp, sessionId, "apply threw", [CommandError.Of(ErrorCodes.InternalError)]);
            throw;
        }

        if (!result.IsSuccess)
        {
            // 关键分支：命令"正常地"报告了失败，但它可能在失败前已经改过文档。
            // 不回滚的话，文档就停在一个既不是变更前也不是变更后的中间状态。
            normalized.RestoreMemento(document, memento);
            Record(normalized, AuditKind.Failed, normalized.Context.Source, timestamp, sessionId, result.Message, result.Errors);
            return result;
        }

        if (result.IsNoOp)
        {
            // 合法但无变更。不推进版本、不进历史、不广播：把它当成一次真的变更会让
            // 版本号与实际内容脱节，增量同步那边就会拿到一段空转的区间。
            return result;
        }

        document.Version++;
        UpdateHashes(document, result);

        Context.VersionLog.Record(new VersionEntry
        {
            Version = document.Version,
            CommandId = normalized.CommandId,
            Source = normalized.Context.Source,
            ActorId = normalized.Context.ActorId,
            SessionId = sessionId,
            Timestamp = timestamp,
            AffectedIds = result.AffectedIds,
            Changes = result.FieldChanges,
        });

        Context.History.PushUndo(new HistoryEntry
        {
            Command = normalized,
            Memento = memento,
            Result = result,
            Timestamp = timestamp,
            SessionId = sessionId,
        });

        // 新变更出现后，重做栈里的假设就失效了。
        Context.History.ClearRedo();

        Record(normalized, AuditKind.Executed, normalized.Context.Source, timestamp, sessionId, result.Message, null);
        Broadcast(normalized.Context.Source, result.AffectedIds, timestamp);

        return result;
    }

    /// <summary>撤销。门锁与深度由调用方管理。</summary>
    private CommandResult UndoCore()
    {
        var entry = Context.History.TryPopUndo();

        if (entry is null)
        {
            return CommandResult.NoOp("无可撤销操作");
        }

        var document = Context.Document;
        var timestamp = Context.Clock.UtcNow;
        var sessionId = Context.Session.CurrentSessionId;

        entry.Command.RestoreMemento(document, entry.Memento);

        // 撤销同样推进版本号。版本号描述的是状态序列，撤销产生的是一个新状态，
        // 如果它不占一个版本号，对端就无法从版本区间推算出这段时间发生过撤销。
        document.Version++;
        UpdateHashes(document, entry.Result);

        Context.VersionLog.Record(new VersionEntry
        {
            Version = document.Version,
            CommandId = entry.Command.CommandId,
            Source = ChangeSource.Undo,
            ActorId = Context.Session.CurrentActorId,
            SessionId = sessionId,
            Timestamp = timestamp,

            // 这里用快照里的逆变更，而不是原命令的变更记录：
            // 撤销的效果在语义上正好是原变更的反向，用逆变更描述才对得上。
            AffectedIds = entry.Memento.AffectedIds,
            Changes = entry.Memento.InverseChanges,
        });

        // 弹出的条目原样推进重做栈，这样重做才能复现同一个命令与来源。
        Context.History.PushRedo(entry);

        Record(entry.Command, AuditKind.Undone, ChangeSource.Undo, timestamp, sessionId, $"undo of {entry.Command.CommandId}", null);
        Broadcast(ChangeSource.Undo, entry.Memento.AffectedIds, timestamp);

        return CommandResult.Ok(
            affected: entry.Memento.AffectedIds,
            changes: entry.Memento.InverseChanges,
            structural: entry.Result.StructuralChanged,
            visual: entry.Result.VisualChanged);
    }

    /// <summary>重做。门锁与深度由调用方管理。</summary>
    /// <remarks>
    /// 与执行路径有两点不同，都是刻意的：
    /// 一是快照要重新捕获，因为重做是在**当前**文档状态上重放，
    /// 沿用上次的快照会让下次撤销还原到错误的基线；
    /// 二是失败时必须把条目放回重做栈，否则一次失败重做会让这个操作彻底消失，
    /// 用户再点重做已经没有东西可做了。
    /// </remarks>
    private CommandResult RedoCore()
    {
        var entry = Context.History.TryPopRedo();

        if (entry is null)
        {
            return CommandResult.NoOp("无可重做操作");
        }

        var document = Context.Document;
        var timestamp = Context.Clock.UtcNow;
        var sessionId = Context.Session.CurrentSessionId;

        var memento = entry.Command.CaptureMemento(document);

        CommandResult result;

        try
        {
            result = entry.Command.Apply(document);
        }
        catch (OperationCanceledException)
        {
            entry.Command.RestoreMemento(document, memento);
            Context.History.PushRedo(entry);
            Record(entry.Command, AuditKind.Cancelled, ChangeSource.Redo, timestamp, sessionId, "redo cancelled", null);
            throw;
        }
        catch (Exception)
        {
            entry.Command.RestoreMemento(document, memento);
            Context.History.PushRedo(entry);
            Record(entry.Command, AuditKind.Failed, ChangeSource.Redo, timestamp, sessionId, "redo threw", [CommandError.Of(ErrorCodes.InternalError)]);
            throw;
        }

        if (!result.IsSuccess)
        {
            // 文档状态和重做栈要一起恢复。只恢复其中一个的话，
            // 要么文档脏了，要么这个操作凭空消失，两种情况用户都无法理解。
            entry.Command.RestoreMemento(document, memento);
            Context.History.PushRedo(entry);
            Record(entry.Command, AuditKind.Failed, ChangeSource.Redo, timestamp, sessionId, result.Message, result.Errors);
            return result;
        }

        document.Version++;
        UpdateHashes(document, result);

        Context.VersionLog.Record(new VersionEntry
        {
            Version = document.Version,
            CommandId = entry.Command.CommandId,
            Source = ChangeSource.Redo,
            ActorId = Context.Session.CurrentActorId,
            SessionId = sessionId,
            Timestamp = timestamp,
            AffectedIds = result.AffectedIds,
            Changes = result.FieldChanges,
        });

        // 用新快照与新结果写回撤销栈：时间戳更新成这次重做的时刻，
        // 这样撤销栈里的记录始终准确描述"撤销它会回到什么状态"。
        Context.History.PushUndo(entry with
        {
            Memento = memento,
            Result = result,
            Timestamp = timestamp,
        });

        Record(entry.Command, AuditKind.Redone, ChangeSource.Redo, timestamp, sessionId, $"redo of {entry.Command.CommandId}", null);
        Broadcast(ChangeSource.Redo, result.AffectedIds, timestamp);

        return result;
    }

    /// <summary>
    /// 确定这次操作归属哪个会话：命令自己声明了就用它，没声明则用当前会话兜底。
    /// </summary>
    /// <remarks>
    /// 两边都给了值却不一样时记一条警告。这种情况通常意味着调用方在做"代别人提交"这类操作，
    /// 不一定是错误，但如果频繁出现就说明某处的会话传递链路有问题。
    /// 仍然采用命令声明的值——它是这次操作更具体的描述，会话提供者给的只是环境默认值。
    /// </remarks>
    private string? ResolveSessionId(string? contextSessionId)
    {
        var current = Context.Session.CurrentSessionId;

        if (string.IsNullOrEmpty(contextSessionId))
        {
            return current;
        }

        if (!string.IsNullOrEmpty(current) && !string.Equals(current, contextSessionId, StringComparison.Ordinal))
        {
            Context.Diagnostics.Warn(
                $"SessionId mismatch: command declared '{contextSessionId}', session provider reports '{current}'. " +
                "Using the declared value.");
        }

        return contextSessionId;
    }

    /// <summary>
    /// 按变更类型重算哈希。结构变了两个都要重算，只改外观则只重算视觉哈希。
    /// </summary>
    private static void UpdateHashes(DiagramDocument document, CommandResult result)
    {
        if (result.StructuralChanged)
        {
            document.StructuralHash = DiagramHashing.ComputeStructuralHash(document);
        }

        if (result.StructuralChanged || result.VisualChanged)
        {
            document.VisualHash = DiagramHashing.ComputeVisualHash(document);
        }
    }

    /// <summary>写一条审计记录。来源单独传参，因为撤销重做要用自己的来源而不是原命令的来源。</summary>
    private void Record(
        IDiagramCommand command,
        AuditKind kind,
        ChangeSource source,
        DateTimeOffset timestamp,
        string? sessionId,
        string? reason,
        CommandError[]? errors)
    {
        Context.AuditLog.Record(new AuditEntry
        {
            CommandId = command.CommandId,
            Source = source,
            ActorId = command.Context.ActorId,
            SessionId = sessionId,
            Timestamp = timestamp,
            Reason = reason,
            Kind = kind,

            // 空数组与"没有错误"在语义上是一回事，统一成空值省得下游多一次判断。
            Errors = errors is { Length: > 0 } ? errors : null,
        });
    }

    /// <summary>
    /// 广播变更通知。
    /// </summary>
    /// <remarks>
    /// 版本号从文档现取而不是从参数传入，这样通知里的版本一定与文档当前状态一致。
    /// 调用方只需提供来源与受影响元素。
    /// </remarks>
    private void Broadcast(ChangeSource source, string[] affectedIds, DateTimeOffset timestamp)
    {
        Context.Broadcaster.Enqueue(new ChangeNotification
        {
            DocumentId = Context.Document.Id,
            Version = Context.Document.Version,
            AffectedIds = affectedIds,
            Source = source,
            Timestamp = timestamp,
        });
    }

    public void Dispose() => _gate.Dispose();
}
