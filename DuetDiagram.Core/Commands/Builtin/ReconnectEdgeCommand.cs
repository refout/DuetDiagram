using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 把一条边的两个端点（含端口）改接到别的元素。
/// </summary>
/// <remarks>
/// <para>
/// 一条命令覆盖四个端点字段。端点与端口是同一件事的两面：端口名只在"端点是节点"时有意义，
/// 端点是组合时端口必须为空——这条约束在 <see cref="EdgeDef"/> 里定了，这里只负责在校验里挡住。
/// 四个字段各写一条命令的话，等于把"这一拖把边从 A 连到了 B"拆成四步，
/// 而用户眼里那是一下子的事，撤销也要按四下。
/// </para>
/// <remarks>
/// 校验把问题一次性收齐再返回。改端点时"起点缺失"和"端口在组合上"是两种完全不同的处置，
/// 只报第一个会让调用方改一次、失败一次。
/// </remarks>
public sealed class ReconnectEdgeCommand : DiagramCommandBase
{
    public const string Id = "reconnect-edge";

    private readonly string _edgeId;
    private readonly string _from;
    private readonly string? _fromPort;
    private readonly string _to;
    private readonly string? _toPort;

    public ReconnectEdgeCommand(string edgeId, string from, string? fromPort, string to, string? toPort)
        : base(Id)
    {
        ArgumentNullException.ThrowIfNull(edgeId);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        _edgeId = edgeId;
        _from = from;
        _fromPort = fromPort;
        _to = to;
        _toPort = toPort;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var errors = new List<CommandError>(4);

        var edge = document.FindEdge(_edgeId);

        if (edge is null)
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.EdgeMissing, _edgeId));
        }

        if (!document.HasEndpoint(_from))
        {
            errors.Add(CommandError.Of(ErrorCodes.EdgeSourceMissing, _from));
        }
        else if (_fromPort is not null && document.HasComposite(_from))
        {
            // 端口只属于节点。端点是组合时带端口名，布局不知道往组合的哪个边界点连。
            errors.Add(CommandError.Of(ErrorCodes.EdgePortOnComposite, _from));
        }

        if (!document.HasEndpoint(_to))
        {
            errors.Add(CommandError.Of(ErrorCodes.EdgeTargetMissing, _to));
        }
        else if (_toPort is not null && document.HasComposite(_to))
        {
            errors.Add(CommandError.Of(ErrorCodes.EdgePortOnComposite, _to));
        }

        return errors.Count == 0 ? ValidationResult.Valid : ValidationResult.Invalid([.. errors]);
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var index = document.IndexOfEdge(_edgeId);

        if (index < 0)
        {
            return CommandResult.Fail([CommandError.Of(ErrorCodes.EdgeMissing, _edgeId)]);
        }

        var previous = document.MutableEdges[index];
        var updated = previous with { From = _from, FromPort = _fromPort, To = _to, ToPort = _toPort };

        // 新旧端点一致时当成空操作：一次失焦或一次松手可能送来没变的旧值。
        if (Equals(previous, updated))
        {
            return CommandResult.NoOp();
        }

        document.MutableEdges[index] = updated;

        return CommandResult.Ok(
            affected: [_edgeId],
            changes:
            [
                new FieldChange
                {
                    ElementId = _edgeId,
                    Field = FieldNames.From,
                    OldValue = previous.From,
                    NewValue = _from,
                    Kind = ChangeKind.Modified,
                },
                new FieldChange
                {
                    ElementId = _edgeId,
                    Field = FieldNames.To,
                    OldValue = previous.To,
                    NewValue = _to,
                    Kind = ChangeKind.Modified,
                },
            ],
            structural: true,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) =>
        new ReconnectEdgeCommand(_edgeId, _from, _fromPort, _to, _toPort);

    protected override CommandMemento CaptureCore(DiagramDocument document)
    {
        var edge = document.FindEdge(_edgeId)
            ?? throw new InvalidOperationException($"边 {_edgeId} 不存在，无法记录逆变更");

        return new ReconnectEdgeMemento
        {
            EdgeId = _edgeId,
            Previous = edge,
            AffectedIds = [_edgeId],
            InverseChanges =
            [
                new FieldChange
                {
                    ElementId = _edgeId,
                    Field = FieldNames.From,
                    OldValue = _from,
                    NewValue = edge.From,
                    Kind = ChangeKind.Modified,
                },
                new FieldChange
                {
                    ElementId = _edgeId,
                    Field = FieldNames.To,
                    OldValue = _to,
                    NewValue = edge.To,
                    Kind = ChangeKind.Modified,
                },
            ],
        };
    }

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        var typed = (ReconnectEdgeMemento)memento;
        var index = document.IndexOfEdge(typed.EdgeId);

        // 边不在了就什么都不做：端点还原的逆操作是"改回去"，不是"复活一条被删的边"。
        if (index >= 0)
        {
            document.MutableEdges[index] = typed.Previous;
        }
    }
}
