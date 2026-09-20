using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>新增节点。</summary>
public sealed class AddNodeCommand : DiagramCommandBase
{
    public const string Id = "add-node";

    private readonly NodeDef _node;
    private readonly int? _index;

    public AddNodeCommand(NodeDef node, int? index = null)
        : base(Id)
    {
        ArgumentNullException.ThrowIfNull(node);
        _node = node;
        _index = index;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(_node.Id))
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.InvalidId, "node id is empty"));
        }

        if (document.HasNode(_node.Id))
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.DuplicateId, _node.Id));
        }

        return ValidationResult.Valid;
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.MutableNodes.Insert(ResolveIndex(document), _node);

        return CommandResult.Ok(
            affected: [_node.Id],
            changes:
            [
                new FieldChange
                {
                    ElementId = _node.Id,
                    Field = "node",
                    OldValue = null,
                    NewValue = _node.Label,
                    Kind = ChangeKind.Added,
                },
            ],
            structural: true,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new AddNodeCommand(_node, _index);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new AddNodeMemento
    {
        Node = _node,
        Index = ResolveIndex(document),
        AffectedIds = [_node.Id],
        InverseChanges =
        [
            new FieldChange
            {
                ElementId = _node.Id,
                Field = "node",
                OldValue = _node.Label,
                NewValue = null,
                Kind = ChangeKind.Removed,
            },
        ],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        var typed = (AddNodeMemento)memento;
        var index = document.IndexOfNode(typed.Node.Id);
        if (index >= 0)
        {
            document.MutableNodes.RemoveAt(index);
        }
    }

    /// <summary>
    /// 越界的 <c>index</c> 一律夹紧而不是抛异常：布局引擎与 LLM 都可能给出过期索引，
    /// 夹紧比失败更符合「确定性增强措施」的要求。Apply 与 CaptureMemento 必须得到同一结果。
    /// </summary>
    private int ResolveIndex(DiagramDocument document) =>
        Math.Clamp(_index ?? document.Nodes.Count, 0, document.Nodes.Count);
}
