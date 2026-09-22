using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 删除一条边，两端节点都留着。
/// </summary>
/// <remarks>
/// <para>
/// 与删节点看着像，其实是两种操作。删节点会**连带**删掉挂在它身上的边——那些边
/// 离开了端点就不再是一条边；而删边不牵连任何别的东西：边不持有子对象，
/// 端点节点也不会因为少了一条边而改变。所以两条命令各写一遍，而不是共用一条。
/// </para>
/// <para>
/// **引用了这条边的东西（例如层内次序约束）不在这里清理。** 删完之后约束会指向一个
/// 不存在的边标识，那是**有意留着**的：整体校验器把它报成"次序引用的边不存在"，
/// 用户因此知道有一条约束悬空了。命令里顺手把约束删掉的话，用户失去的是一条他
/// 自己设过的约束，而且没有任何提示——比一条能被报出来的悬空引用糟得多。
/// 布局侧也是同一口径：求解器把认不出的标识当陈旧数据跳过而**不过滤**，
/// 就是为了让这种悬空还能被看出来。
/// </para>
/// <para>
/// 撤销要按**原索引**把边插回去，不能追加到末尾。边的集合顺序是有语义的——
/// 层内次序就是按出边的先后排列的，删掉再追加的话，撤销之后连线的交叉数量
/// 与删除前不一样，而结构哈希与外观哈希都按标识排序后再遍历，两次算出来一模一样，
/// 所以这个错只能靠专门的顺序用例发现。
/// </para>
/// </remarks>
public sealed class DisconnectEdgeCommand : DiagramCommandBase
{
    public const string Id = "disconnect-edge";

    private readonly string _edgeId;

    public DisconnectEdgeCommand(string edgeId)
        : base(Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(edgeId);
        _edgeId = edgeId;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.HasEdge(_edgeId)
            ? ValidationResult.Valid
            : ValidationResult.Invalid(CommandError.Of(ErrorCodes.EdgeMissing, _edgeId));
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var index = document.IndexOfEdge(_edgeId);

        // 走到这里说明校验已经通过。取不到索引说明调用方绕过了校验，
        // 或者文档在校验与执行之间被换掉了。明确失败，不要靠下标越界把异常抛出去。
        if (index < 0)
        {
            return CommandResult.Fail([CommandError.Of(ErrorCodes.EdgeMissing, _edgeId)]);
        }

        var edge = document.MutableEdges[index];
        document.MutableEdges.RemoveAt(index);

        return CommandResult.Ok(
            affected: [_edgeId],
            changes:
            [
                new FieldChange
                {
                    ElementId = _edgeId,
                    Field = FieldNames.EdgeElement,
                    OldValue = $"{edge.From}->{edge.To}",
                    NewValue = null,
                    Kind = ChangeKind.Removed,
                },
            ],
            structural: true,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new DisconnectEdgeCommand(_edgeId);

    protected override CommandMemento CaptureCore(DiagramDocument document)
    {
        // 此刻文档还没被改动，所以索引就是这条边当前的位置，也是撤销时该插回去的位置。
        var index = document.IndexOfEdge(_edgeId);

        // 走到这里说明校验已经通过，边必然存在。真找不到就是调用方绕过了校验，
        // 明确抛出来比悄悄生成一份指向空边的快照要好。
        var edge = document.FindEdge(_edgeId)
            ?? throw new InvalidOperationException($"Cannot capture memento: edge '{_edgeId}' does not exist.");

        return new DisconnectEdgeMemento
        {
            Edge = edge,
            Index = index,
            AffectedIds = [_edgeId],

            // 逆变更的方向是"新增"：撤销删除等于把这条边加回来。
            InverseChanges =
            [
                new FieldChange
                {
                    ElementId = _edgeId,
                    Field = FieldNames.EdgeElement,
                    OldValue = null,
                    NewValue = $"{edge.From}->{edge.To}",
                    Kind = ChangeKind.Added,
                },
            ],
        };
    }

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        var typed = (DisconnectEdgeMemento)memento;

        // 这一段会被撤销与重做两条路径共用，所以先判断目标是否已经存在，
        // 让整段逻辑可重复执行而不会插出两条一模一样的边。
        if (document.HasEdge(typed.Edge.Id))
        {
            return;
        }

        document.MutableEdges.Insert(
            Math.Clamp(typed.Index, 0, document.MutableEdges.Count),
            typed.Edge);
    }
}
