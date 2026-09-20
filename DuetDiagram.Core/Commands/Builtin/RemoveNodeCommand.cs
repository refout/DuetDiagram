using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 删除节点，并连带删除其所有关联边（一次命令 = 一个原子事务）。
/// </summary>
public sealed class RemoveNodeCommand : DiagramCommandBase
{
    public const string Id = "remove-node";

    private readonly string _nodeId;

    public RemoveNodeCommand(string nodeId)
        : base(Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        _nodeId = nodeId;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.HasNode(_nodeId)
            ? ValidationResult.Valid
            : ValidationResult.Invalid(CommandError.Of(ErrorCodes.NodeMissing, _nodeId));
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var nodeIndex = document.IndexOfNode(_nodeId);
        document.MutableNodes.RemoveAt(nodeIndex);

        var removedEdges = new List<string>();
        for (var i = document.MutableEdges.Count - 1; i >= 0; i--)
        {
            var edge = document.MutableEdges[i];
            if (string.Equals(edge.From, _nodeId, StringComparison.Ordinal) ||
                string.Equals(edge.To, _nodeId, StringComparison.Ordinal))
            {
                document.MutableEdges.RemoveAt(i);
                removedEdges.Add(edge.Id);
            }
        }

        removedEdges.Reverse();

        var changes = new List<FieldChange>(removedEdges.Count + 1)
        {
            new()
            {
                ElementId = _nodeId,
                Field = "node",
                OldValue = null,
                NewValue = null,
                Kind = ChangeKind.Removed,
            },
        };

        changes.AddRange(removedEdges.Select(id => new FieldChange
        {
            ElementId = id,
            Field = "edge",
            OldValue = null,
            NewValue = null,
            Kind = ChangeKind.Removed,
        }));

        return CommandResult.Ok(
            affected: [_nodeId, .. removedEdges],
            changes: [.. changes],
            structural: true,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new RemoveNodeCommand(_nodeId);

    protected override CommandMemento CaptureCore(DiagramDocument document)
    {
        var node = document.FindNode(_nodeId)
            ?? throw new InvalidOperationException($"Cannot capture memento: node '{_nodeId}' does not exist.");

        var placements = new List<EdgePlacement>();
        for (var i = 0; i < document.Edges.Count; i++)
        {
            var edge = document.Edges[i];
            if (string.Equals(edge.From, _nodeId, StringComparison.Ordinal) ||
                string.Equals(edge.To, _nodeId, StringComparison.Ordinal))
            {
                placements.Add(new EdgePlacement(i, edge));
            }
        }

        var affected = new List<string>(placements.Count + 1) { _nodeId };
        affected.AddRange(placements.Select(p => p.Edge.Id));

        var inverse = new List<FieldChange>(placements.Count + 1)
        {
            new()
            {
                ElementId = _nodeId,
                Field = "node",
                OldValue = null,
                NewValue = node.Label,
                Kind = ChangeKind.Added,
            },
        };

        inverse.AddRange(placements.Select(p => new FieldChange
        {
            ElementId = p.Edge.Id,
            Field = "edge",
            OldValue = null,
            NewValue = p.Edge.Label,
            Kind = ChangeKind.Added,
        }));

        return new RemoveNodeMemento
        {
            Node = node,
            Index = document.IndexOfNode(_nodeId),
            RemovedEdges = [.. placements],
            AffectedIds = [.. affected],
            InverseChanges = [.. inverse],
        };
    }

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        var typed = (RemoveNodeMemento)memento;

        if (!document.HasNode(typed.Node.Id))
        {
            document.MutableNodes.Insert(
                Math.Clamp(typed.Index, 0, document.Nodes.Count),
                typed.Node);
        }

        // 按原索引升序插回，保证还原后的边顺序与删除前逐字节一致。
        foreach (var placement in typed.RemovedEdges.OrderBy(p => p.Index))
        {
            if (document.HasEdge(placement.Edge.Id))
            {
                continue;
            }

            document.MutableEdges.Insert(
                Math.Clamp(placement.Index, 0, document.MutableEdges.Count),
                placement.Edge);
        }
    }
}
