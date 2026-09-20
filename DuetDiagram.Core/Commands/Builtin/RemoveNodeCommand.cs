using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 删除一个节点，并连带删除所有挂在它身上的边。
/// </summary>
/// <remarks>
/// <para>
/// 为什么把"删节点"和"删关联边"合成一条命令，而不是让调用方先删边再删节点：
/// 分成两步就存在中间态——边没了、节点还在，这个状态既不合法也没有意义，
/// 而且一旦第二步失败，文档就停在了那个半成品状态。合成一条命令之后，
/// 整个操作是一个事务，要么都发生要么都不发生。
/// </para>
/// <para>
/// 撤销是本命令最复杂的部分。被连带删除的边不但要还原，还要回到原来的位置，
/// 否则边的集合顺序会变，而顺序是有语义的。所以快照里记录的是
/// "边 + 它原来的索引"，还原时按索引升序插回。
/// </para>
/// </remarks>
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

        document.MutableNodes.RemoveAt(document.IndexOfNode(_nodeId));

        // 倒着遍历边集合，这样每次移除都不会影响尚未检查的下标。
        // 收集到的标识是按倒序来的，最后反转一次，让结果与集合的原始顺序一致。
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
                Field = FieldNames.NodeElement,
                OldValue = null,
                NewValue = null,
                Kind = ChangeKind.Removed,
            },
        };

        changes.AddRange(removedEdges.Select(id => new FieldChange
        {
            ElementId = id,
            Field = FieldNames.EdgeElement,
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
        // 走到这里说明校验已经通过，节点必然存在。真找不到就是调用方绕过了校验，
        // 明确抛出来比悄悄生成一份指向空节点的快照要好。
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

        // 逆变更的方向是"新增"：撤销删除等于把节点和边加回来。
        var inverse = new List<FieldChange>(placements.Count + 1)
        {
            new()
            {
                ElementId = _nodeId,
                Field = FieldNames.NodeElement,
                OldValue = null,
                NewValue = node.Label,
                Kind = ChangeKind.Added,
            },
        };

        inverse.AddRange(placements.Select(p => new FieldChange
        {
            ElementId = p.Edge.Id,
            Field = FieldNames.EdgeElement,
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

        // 这一段会被撤销和重做两条路径共用，所以每一步都先判断目标是否已经存在，
        // 让整段逻辑可重复执行而不会重复插入。
        if (!document.HasNode(typed.Node.Id))
        {
            document.MutableNodes.Insert(
                Math.Clamp(typed.Index, 0, document.Nodes.Count),
                typed.Node);
        }

        // 按原索引升序插回：先放回位置靠前的边，后面每条边的插入点才会落回正确的位置。
        // 如果顺序反了，索引会随着插入不断右移，最终的排列就跟删除前不一样了。
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
