using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>连接两个节点。</summary>
public sealed class ConnectEdgeCommand : DiagramCommandBase
{
    public const string Id = "connect-edge";

    private readonly EdgeDef _edge;

    public ConnectEdgeCommand(EdgeDef edge)
        : base(Id)
    {
        ArgumentNullException.ThrowIfNull(edge);
        _edge = edge;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var errors = new List<CommandError>(3);

        if (string.IsNullOrWhiteSpace(_edge.Id))
        {
            errors.Add(CommandError.Of(ErrorCodes.InvalidId, "edge id is empty"));
        }
        else if (document.HasEdge(_edge.Id))
        {
            errors.Add(CommandError.Of(ErrorCodes.DuplicateId, _edge.Id));
        }

        if (!document.HasNode(_edge.From))
        {
            errors.Add(CommandError.Of(ErrorCodes.EdgeSourceMissing, _edge.From));
        }

        if (!document.HasNode(_edge.To))
        {
            errors.Add(CommandError.Of(ErrorCodes.EdgeTargetMissing, _edge.To));
        }

        return errors.Count == 0 ? ValidationResult.Valid : ValidationResult.Invalid([.. errors]);
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.MutableEdges.Add(_edge);

        return CommandResult.Ok(
            affected: [_edge.Id],
            changes:
            [
                new FieldChange
                {
                    ElementId = _edge.Id,
                    Field = "edge",
                    OldValue = null,
                    NewValue = $"{_edge.From}->{_edge.To}",
                    Kind = ChangeKind.Added,
                },
            ],
            structural: true,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new ConnectEdgeCommand(_edge);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new ConnectEdgeMemento
    {
        Edge = _edge,
        Index = document.Edges.Count,
        AffectedIds = [_edge.Id],
        InverseChanges =
        [
            new FieldChange
            {
                ElementId = _edge.Id,
                Field = "edge",
                OldValue = $"{_edge.From}->{_edge.To}",
                NewValue = null,
                Kind = ChangeKind.Removed,
            },
        ],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        var typed = (ConnectEdgeMemento)memento;
        var index = document.IndexOfEdge(typed.Edge.Id);

        if (index >= 0)
        {
            document.MutableEdges.RemoveAt(index);
        }
    }
}
