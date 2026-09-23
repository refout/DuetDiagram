using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 把一批节点归到同一个图层上。
/// </summary>
/// <remarks>
/// <para>
/// **为什么要单开一条命令。** 单元素的归属走 <c>set-node-field</c> 的 <c>layer</c> 字段就够，
/// 但界面上一次移入是多选之后的一次操作：逐个发命令的话，撤销要按很多次，
/// 而用户眼里那是同一次操作。合成一条之后，「一条操作进一次历史」这件事由命令层保证，
/// 界面不必自己攒。
/// </para>
/// <para>
/// **原子性靠两遍走。** 先把要写的每一个都算出来，一个都不写；算不出来的立刻整条失败。
/// 边算边写是不行的：第三个发现值不合法时，前两个已经写进去了，
/// 而失败的命令按约定不该改动文档。
/// </para>
/// <para>
/// **只报外观变更。** 归属不改坐标——被移走的元素仍然占着它原来的位置，
/// 连线仍然按原路走。它改的是画在哪一档，所以画面要重画、布局不必重算。
/// </para>
/// </remarks>
public sealed class AssignLayerCommand : DiagramCommandBase
{
    public const string Id = "assign-layer";

    private readonly string _layerId;
    private readonly IReadOnlyList<string> _nodeIds;

    public AssignLayerCommand(string layerId, IReadOnlyList<string> nodeIds)
        : base(Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);
        ArgumentNullException.ThrowIfNull(nodeIds);

        _layerId = layerId;

        // 调用方送重复标识过来是无害的，但必须在这里去掉：留着的话结果里会出现两条
        // 一模一样的变更，受影响集合里也会多一个，而两处都只是"看起来多了一项"。
        // 去掉之后这一条对"写一次还是写两次"没有歧义。
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var accepted = new List<string>(nodeIds.Count);

        foreach (var id in nodeIds)
        {
            if (seen.Add(id))
            {
                accepted.Add(id);
            }
        }

        _nodeIds = accepted;
    }

    /// <summary>这一批要归到哪一层。</summary>
    public string LayerId => _layerId;

    /// <summary>被点名的节点，去重之后的。</summary>
    public IReadOnlyList<string> NodeIds => _nodeIds;

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_nodeIds.Count == 0)
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.InvalidId, "没有点名任何节点"));
        }

        if (LayerAccess.Find(document, _layerId) is null)
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.LayerMissing, _layerId));
        }

        foreach (var id in _nodeIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return ValidationResult.Invalid(CommandError.Of(ErrorCodes.InvalidId, "节点标识是空的"));
            }

            if (document.FindNode(id) is null)
            {
                return ValidationResult.Invalid(CommandError.Of(ErrorCodes.NodeMissing, id));
            }
        }

        return ValidationResult.Valid;
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var planned = new List<(int Index, NodeDef Before, NodeDef After)>(_nodeIds.Count);

        // 第一遍：全部算出来，一个都不写。
        foreach (var id in _nodeIds)
        {
            // 前置检查已经确认过它在，这里再判一次是为了让这条路径自己站得住：
            // Apply 的调用方不只命令总线，测试与将来的批量路径也会直接调它。
            var index = document.IndexOfNode(id);

            if (index < 0)
            {
                return CommandResult.Fail([CommandError.Of(ErrorCodes.NodeMissing, id)]);
            }

            var before = document.MutableNodes[index];

            if (!NodeFieldValue.TryWrite(before, FieldNames.Layer, _layerId, out var after, out var error))
            {
                return CommandResult.Fail([CommandError.Of(ErrorCodes.FieldValueInvalid, error)]);
            }

            planned.Add((index, before, after));
        }

        // 第二遍：写。跳掉本来就对的那些——一次"全部移入这一层"里通常有几条已经在里面了。
        var affected = new List<string>(planned.Count);
        var changes = new List<FieldChange>(planned.Count);

        foreach (var (index, before, after) in planned)
        {
            if (Equals(before, after))
            {
                continue;
            }

            document.MutableNodes[index] = after;
            affected.Add(after.Id);

            changes.Add(new FieldChange
            {
                ElementId = after.Id,
                Field = FieldNames.Layer,
                OldValue = before.Layer,
                NewValue = after.Layer,
                Kind = ChangeKind.Modified,
            });
        }

        if (affected.Count == 0)
        {
            return CommandResult.NoOp("这些元素本来就都在这一层");
        }

        return CommandResult.Ok(
            affected: [.. affected],
            changes: [.. changes],
            structural: false,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) =>
        new AssignLayerCommand(_layerId, _nodeIds);

    protected override CommandMemento CaptureCore(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var previous = new List<NodeLayerPlacement>(_nodeIds.Count);
        var inverse = new List<FieldChange>(_nodeIds.Count);

        foreach (var id in _nodeIds)
        {
            var node = document.FindNode(id)
                ?? throw new InvalidOperationException($"节点 {id} 不存在，无法记录逆变更");

            previous.Add(new NodeLayerPlacement(id, node.Layer));

            inverse.Add(new FieldChange
            {
                ElementId = id,
                Field = FieldNames.Layer,
                OldValue = _layerId,
                NewValue = node.Layer,
                Kind = ChangeKind.Modified,
            });
        }

        return new AssignLayerMemento
        {
            Previous = [.. previous],
            AffectedIds = [.. _nodeIds],
            InverseChanges = [.. inverse],
        };
    }

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(memento);

        var typed = (AssignLayerMemento)memento;

        foreach (var placement in typed.Previous)
        {
            var index = document.IndexOfNode(placement.NodeId);

            // 节点不在了就跳过。这里**不**把它插回去：字段还原的逆操作是"改回去"，
            // 不是"复活一个被删掉的节点"，插回去会让撤销把别的命令删掉的东西带回来。
            if (index < 0)
            {
                continue;
            }

            if (NodeFieldValue.TryWrite(
                document.MutableNodes[index],
                FieldNames.Layer,
                placement.Layer,
                out var restored,
                out _))
            {
                document.MutableNodes[index] = restored;
            }
        }
    }
}
