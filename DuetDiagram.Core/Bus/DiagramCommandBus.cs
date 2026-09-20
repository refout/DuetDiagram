using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.History;
using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;

namespace DuetDiagram.Core.Bus;

/// <summary>
/// 命令总线。GUI、内部 LLM、外部 MCP Agent、导入器在此汇合，之后走完全相同的
/// 校验 → 应用 → 布局 → 渲染 → 历史管线（方案 §三）。
/// </summary>
/// <remarks>
/// <para>AGENTS.md 约定 2：<c>Apply</c> 失败必须整体回滚，<c>Execute</c> 与 <c>Redo</c> 一视同仁。</para>
/// <para>AGENTS.md 约定 7：命令内部不得异步调用 <c>Execute</c>；AsyncLocal 在 Task.Run 中会传播。</para>
/// <para>AGENTS.md 约定 10：MCP 模式下广播器不得为 <c>NullChangeBroadcaster</c>。</para>
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

        // AGENTS.md 约定 10：判据必须是 is NullChangeBroadcaster，不能只判 null。
        if (context.Options.RequiresVersionCheck && context.Broadcaster is NullChangeBroadcaster)
        {
            throw new ArgumentException(
                "MCP mode (RequiresVersionCheck=true) requires a real broadcaster; " +
                "NullChangeBroadcaster cannot carry cross-process change notifications.",
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

    /// <summary>撤销一次操作。人与 LLM 共用同一条历史栈。</summary>
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

    /// <summary>重做一次操作。</summary>
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

    private void EnsureNotNested()
    {
        if (_depth.Value > 0)
        {
            throw new InvalidOperationException(NestedExecuteMessage);
        }
    }

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
            normalized.RestoreMemento(document, memento);
            Record(normalized, AuditKind.Cancelled, normalized.Context.Source, timestamp, sessionId, "apply cancelled", null);
            throw;
        }
        catch (Exception)
        {
            normalized.RestoreMemento(document, memento);
            // AGENTS.md 约定 9：Payload 不携带异常类型名，只记结构化的 INTERNAL_ERROR。
            Record(normalized, AuditKind.Failed, normalized.Context.Source, timestamp, sessionId, "apply threw", [CommandError.Of(ErrorCodes.InternalError)]);
            throw;
        }

        if (!result.IsSuccess)
        {
            normalized.RestoreMemento(document, memento);
            Record(normalized, AuditKind.Failed, normalized.Context.Source, timestamp, sessionId, result.Message, result.Errors);
            return result;
        }

        if (result.IsNoOp)
        {
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

        Context.History.ClearRedo();

        Record(normalized, AuditKind.Executed, normalized.Context.Source, timestamp, sessionId, result.Message, null);
        Broadcast(normalized.Context.Source, result.AffectedIds, timestamp);

        return result;
    }

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
            AffectedIds = entry.Memento.AffectedIds,
            Changes = entry.Memento.InverseChanges,
        });

        Context.History.PushRedo(entry);

        Record(entry.Command, AuditKind.Undone, ChangeSource.Undo, timestamp, sessionId, $"undo of {entry.Command.CommandId}", null);
        Broadcast(ChangeSource.Undo, entry.Memento.AffectedIds, timestamp);

        return CommandResult.Ok(
            affected: entry.Memento.AffectedIds,
            changes: entry.Memento.InverseChanges,
            structural: entry.Result.StructuralChanged,
            visual: entry.Result.VisualChanged);
    }

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

        // 重新捕获 memento：重做是在**当前**文档状态上重放，不能沿用旧的逆变更。
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
            // AGENTS.md 约定 2：Redo 失败必须同时恢复文档状态与重做栈，返回 result 而不是抛异常。
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
    /// 解析 SessionId：优先 <c>ctx.SessionId</c>，为空时回退到会话提供者。
    /// 两者不一致时告警但仍以命令声明为准（方案 §4.9 步骤 4）。
    /// </summary>
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
            Errors = errors is { Length: > 0 } ? errors : null,
        });
    }

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
