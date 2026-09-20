using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 连接两个节点。
/// </summary>
/// <remarks>
/// 校验阶段会把所有问题一次性收集齐再返回。这一点对本命令尤其重要：
/// 一条边同时缺起点和缺终点是很常见的情况（比如两端节点都刚被删掉），
/// 只报第一个会让调用方改一次、失败一次，来回好几轮。
/// </remarks>
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

        // 端点缺失要分别报告，因为界面对两种情况的处置不同：
        // 缺终点提示"是否创建目标节点"，缺起点则提示补上来源。
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

        // 新边一律追加到末尾。边的绘制次序不影响语义，放到末尾最省事也最可预测。
        document.MutableEdges.Add(_edge);

        return CommandResult.Ok(
            affected: [_edge.Id],
            changes:
            [
                new FieldChange
                {
                    ElementId = _edge.Id,
                    Field = FieldNames.EdgeElement,
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

        // 此刻文档还没被改动，所以"将要被追加到的位置"就是当前边数。
        Index = document.Edges.Count,
        AffectedIds = [_edge.Id],
        InverseChanges =
        [
            new FieldChange
            {
                ElementId = _edge.Id,
                Field = FieldNames.EdgeElement,
                OldValue = $"{_edge.From}->{_edge.To}",
                NewValue = null,
                Kind = ChangeKind.Removed,
            },
        ],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        var typed = (ConnectEdgeMemento)memento;

        // 按标识定位而不是用快照里的索引：重做时文档可能已经有其它变化，
        // 索引不再可靠，但标识一定唯一。
        var index = document.IndexOfEdge(typed.Edge.Id);

        if (index >= 0)
        {
            document.MutableEdges.RemoveAt(index);
        }
    }
}
