using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 新增一个节点。
/// </summary>
/// <remarks>
/// <para>
/// 插入位置可以指定，也可以留空追加到末尾。集合顺序决定层内次序，
/// 所以"插到第几个"是有语义的操作，不是纯粹的排列问题。
/// </para>
/// <para>
/// 关键实现细节：<see cref="Apply"/> 与 <see cref="CaptureCore"/> 必须算出**同一个**插入索引。
/// 两边各自调用 <see cref="ResolveIndex"/> 而不是各写一段逻辑，避免将来修改其中一处时忘记另一处，
/// 导致撤销时把节点还原到错误的位置。
/// </para>
/// </remarks>
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

        // 同一个标识出现两次会让后续所有按标识定位的操作产生歧义，必须在入口挡住。
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

        // 逆变更与命令本身方向相反：新增的逆操作是移除。
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

        // 先查索引再删：重做会复用这条路径，而重做前节点可能已经因为别的原因不在了。
        var index = document.IndexOfNode(typed.Node.Id);
        if (index >= 0)
        {
            document.MutableNodes.RemoveAt(index);
        }
    }

    /// <summary>
    /// 把请求的索引夹紧到合法区间，越界不报错。
    /// </summary>
    /// <remarks>
    /// 索引可能来自布局结果或外部代理，这些来源拿到的索引很可能在请求发出之后就已经过期了
    /// （例如期间有别的元素被删掉）。夹紧到一个仍然合理的位置，比让整条命令失败更符合预期。
    /// 这里不做任何"修正提示"——位置本身没有语义，插到末尾和插到倒数第二位在视觉上都能接受。
    /// </remarks>
    private int ResolveIndex(DiagramDocument document) =>
        Math.Clamp(_index ?? document.Nodes.Count, 0, document.Nodes.Count);
}
