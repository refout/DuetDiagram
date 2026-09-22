using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 改一条边的一个字段（当前为标签与样式）。
/// </summary>
/// <remarks>
/// <para>
/// 与节点同构：一条命令覆盖边的全部可写字段，值的载体是字符串，编解码在
/// <see cref="EdgeFieldValue"/>。解析放在 <see cref="Validate"/> 里做，
/// 所以 <see cref="Apply"/> 没有任何失败路径——凡是能失败的都在进门时被挡掉，
/// 这是命令原子性的前提。
/// </para>
/// <para>
/// 新旧值相同时报空操作，不推进版本、不进历史、不广播。
/// </para>
/// </remarks>
public sealed class SetEdgeFieldCommand : DiagramCommandBase
{
    public const string Id = "set-edge-field";

    private readonly string _edgeId;
    private readonly string _field;
    private readonly string? _value;

    public SetEdgeFieldCommand(string edgeId, string field, string? value)
        : base(Id)
    {
        ArgumentNullException.ThrowIfNull(edgeId);
        ArgumentNullException.ThrowIfNull(field);

        _edgeId = edgeId;
        _field = field;
        _value = value;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(_edgeId))
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.InvalidId, "edge id is empty"));
        }

        if (!EdgeFieldValue.IsWritable(_field))
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.FieldUnknown, _field));
        }

        if (document.FindEdge(_edgeId) is not { } edge)
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.EdgeMissing, _edgeId));
        }

        return EdgeFieldValue.TryWrite(edge, _field, _value, out _, out var error)
            ? ValidationResult.Valid
            : ValidationResult.Invalid(CommandError.Of(ErrorCodes.FieldValueInvalid, error));
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var index = document.IndexOfEdge(_edgeId);

        if (index < 0)
        {
            return CommandResult.Fail([CommandError.Of(ErrorCodes.EdgeMissing, _edgeId)]);
        }

        var before = document.MutableEdges[index];

        if (!EdgeFieldValue.TryWrite(before, _field, _value, out var after, out var error))
        {
            return CommandResult.Fail([CommandError.Of(ErrorCodes.FieldValueInvalid, error)]);
        }

        if (Equals(before, after))
        {
            return CommandResult.NoOp();
        }

        document.MutableEdges[index] = after;

        var oldValue = EdgeFieldValue.Read(before, _field);
        var newValue = EdgeFieldValue.Read(after, _field);
        var scope = FieldRegistry.Descriptor(_field)?.Scope ?? FieldScope.Visual;

        return CommandResult.Ok(
            affected: [_edgeId],
            changes:
            [
                new FieldChange
                {
                    ElementId = _edgeId,
                    Field = _field,
                    OldValue = oldValue,
                    NewValue = newValue,
                    Kind = ChangeKind.Modified,
                },
            ],
            structural: scope == FieldScope.Structural,
            visual: scope != FieldScope.Neither);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) =>
        new SetEdgeFieldCommand(_edgeId, _field, _value);

    protected override CommandMemento CaptureCore(DiagramDocument document)
    {
        var edge = document.FindEdge(_edgeId)
            ?? throw new InvalidOperationException($"边 {_edgeId} 不存在，无法记录逆变更");

        return new SetEdgeFieldMemento
        {
            EdgeId = _edgeId,
            Previous = edge,
            Field = _field,
            OldValue = EdgeFieldValue.Read(edge, _field),
            NewValue = _value,
            AffectedIds = [_edgeId],
            InverseChanges =
            [
                new FieldChange
                {
                    ElementId = _edgeId,
                    Field = _field,
                    OldValue = _value,
                    NewValue = EdgeFieldValue.Read(edge, _field),
                    Kind = ChangeKind.Modified,
                },
            ],
        };
    }

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        var typed = (SetEdgeFieldMemento)memento;
        var index = document.IndexOfEdge(typed.EdgeId);

        if (index >= 0)
        {
            document.MutableEdges[index] = typed.Previous;
        }
    }
}
