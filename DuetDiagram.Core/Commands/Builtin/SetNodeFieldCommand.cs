using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 改一个节点的一个字段。
/// </summary>
/// <remarks>
/// <para>
/// **一条命令覆盖节点的全部可写字段**，而不是一个字段一条命令。字段表已经把这些字段
/// 的归属、作用域与整体性登记在一处，再按字段各写一条命令，等于把同一张表抄成几十份，
/// 而抄漏一份的后果是那个字段改不动、界面上却看不出任何异常。
/// </para>
/// <para>
/// 值的载体是字符串，编解码在 <see cref="NodeFieldValue"/>。解析放在
/// <see cref="Validate"/> 里做，所以 <see cref="Apply"/> 没有任何失败路径——
/// 凡是能失败的都在进门时被挡掉了。这一点对原子性很关键：
/// <see cref="Apply"/> 里不会出现"改了一半才发现值不合法"的情形。
/// </para>
/// <para>
/// 新旧值相同时报空操作。空操作不推进版本、不进历史、不广播——
/// 界面上一次失焦或一次回车都可能送来一个没变的旧值，
/// 把它当成一次真变更会让版本号里塞满空转的区间。
/// </para>
/// </remarks>
public sealed class SetNodeFieldCommand : DiagramCommandBase
{
    public const string Id = "set-node-field";

    private readonly string _nodeId;
    private readonly string _field;
    private readonly string? _value;

    public SetNodeFieldCommand(string nodeId, string field, string? value)
        : base(Id)
    {
        _nodeId = nodeId;
        _field = field;
        _value = value;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(_nodeId))
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.InvalidId, "node id is empty"));
        }

        if (!NodeFieldValue.IsWritable(_field))
        {
            // 字段名认不出与值不合法分成两个码：前者说明调用方拿的是另一套字段表
            // （版本对不上或拼错了），后者只说明这一次输入有问题，处置完全不同。
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.FieldUnknown, _field));
        }

        if (document.FindNode(_nodeId) is not { } node)
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.NodeMissing, _nodeId));
        }

        return NodeFieldValue.TryWrite(node, _field, _value, out _, out var error)
            ? ValidationResult.Valid
            : ValidationResult.Invalid(CommandError.Of(ErrorCodes.FieldValueInvalid, error));
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var index = document.IndexOfNode(_nodeId);

        // 前置检查已经确认过节点在，这里再判一次是为了让这条路径自己站得住：
        // Apply 的调用方不只命令总线，测试与将来的批量路径也会直接调它。
        if (index < 0)
        {
            return CommandResult.Fail([CommandError.Of(ErrorCodes.NodeMissing, _nodeId)]);
        }

        var before = document.MutableNodes[index];

        if (!NodeFieldValue.TryWrite(before, _field, _value, out var after, out var error))
        {
            return CommandResult.Fail([CommandError.Of(ErrorCodes.FieldValueInvalid, error)]);
        }

        if (Equals(before, after))
        {
            return CommandResult.NoOp();
        }

        document.MutableNodes[index] = after;

        var oldValue = NodeFieldValue.Read(before, _field);
        var newValue = NodeFieldValue.Read(after, _field);
        var scope = FieldRegistry.Descriptor(_field)?.Scope ?? FieldScope.Visual;

        return CommandResult.Ok(
            affected: [_nodeId],
            changes:
            [
                new FieldChange
                {
                    ElementId = _nodeId,
                    Field = _field,
                    OldValue = oldValue,
                    NewValue = newValue,
                    Kind = ChangeKind.Modified,
                },
            ],

            // 结构类字段改了要让已算出的坐标失效，所以它同时也是一次视觉变更；
            // 纯外观类只重绘；第三类两者都不影响，只有版本号会动。
            structural: scope == FieldScope.Structural,
            visual: scope != FieldScope.Neither);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) =>
        new SetNodeFieldCommand(_nodeId, _field, _value);

    protected override CommandMemento CaptureCore(DiagramDocument document)
    {
        var node = document.FindNode(_nodeId)
            ?? throw new InvalidOperationException($"节点 {_nodeId} 不存在，无法记录逆变更");

        return new SetNodeFieldMemento
        {
            NodeId = _nodeId,
            Previous = node,
            Field = _field,
            OldValue = NodeFieldValue.Read(node, _field),
            NewValue = _value,
            AffectedIds = [_nodeId],
            InverseChanges =
            [
                new FieldChange
                {
                    ElementId = _nodeId,
                    Field = _field,
                    OldValue = _value,
                    NewValue = NodeFieldValue.Read(node, _field),
                    Kind = ChangeKind.Modified,
                },
            ],
        };
    }

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        var typed = (SetNodeFieldMemento)memento;
        var index = document.IndexOfNode(typed.NodeId);

        // 节点不在了就什么都不做。这里**不**把它插回去：字段还原的逆操作是"改回去"，
        // 不是"复活一个被删掉的节点"，插回去会让撤销把别的命令删掉的东西带回来。
        if (index >= 0)
        {
            document.MutableNodes[index] = typed.Previous;
        }
    }
}
