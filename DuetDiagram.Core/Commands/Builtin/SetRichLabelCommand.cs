using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 改一个节点的富文本内容。
/// </summary>
/// <remarks>
/// <para>
/// **与"改节点字段"那条命令共用同一份写入口径。** 这一条收的是结构化的内容对象，
/// 而字段那条路收的是一段 JSON 文本——两条路的差别只在值的载体，
/// 落到节点上的那一步都走 <see cref="RichLabelRules"/>。各写一遍的话，
/// 总有一条会忘了同步纯文本标签，而那种文档只有整体校验器会报。
/// </para>
/// <para>
/// **编辑界面提交的就是这一条。** 整次编辑算一条命令，退出时才发：每敲一个字发一条的话，
/// 撤销栈会被一次编辑灌满，而用户按一次撤销只想退回编辑之前。
/// </para>
/// <para>
/// 传空内容表示把分段样式清掉，标签与富文本开关都不动。改成与原来一样的内容是空操作。
/// </para>
/// </remarks>
public sealed class SetRichLabelCommand : DiagramCommandBase
{
    public const string Id = "set-rich-label";

    private readonly string _nodeId;
    private readonly RichTextContent? _content;

    public SetRichLabelCommand(string nodeId, RichTextContent? content)
        : base(Id)
    {
        _nodeId = nodeId;
        _content = content;
    }

    /// <summary>要改哪一个节点。</summary>
    public string NodeId => _nodeId;

    /// <summary>要写进去的内容。空表示清掉分段样式。</summary>
    public RichTextContent? Content => _content;

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(_nodeId))
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.InvalidId, "节点标识是空的"));
        }

        // 内容本身没有可校验的地方：它是强类型的记录，认不出的行内样式名在解析那一步
        // 就被挡住了，走不到这里。所以这一条只查节点在不在。
        return document.FindNode(_nodeId) is null
            ? ValidationResult.Invalid(CommandError.Of(ErrorCodes.NodeMissing, _nodeId))
            : ValidationResult.Valid;
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
        var after = RichLabelRules.WithContent(before, _content);

        if (Equals(before, after))
        {
            return CommandResult.NoOp("内容没有变");
        }

        document.MutableNodes[index] = after;

        return CommandResult.Ok(
            affected: [_nodeId],
            changes:
            [
                new FieldChange
                {
                    ElementId = _nodeId,
                    Field = FieldNames.RichLabel,
                    OldValue = NodeFieldValue.Read(before, FieldNames.RichLabel),
                    NewValue = NodeFieldValue.Read(after, FieldNames.RichLabel),
                    Kind = ChangeKind.Modified,
                },
            ],

            // 富文本只影响画出来的样子：坐标不受它影响，但像素会变。
            structural: false,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) =>
        new SetRichLabelCommand(_nodeId, _content);

    protected override CommandMemento CaptureCore(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var node = document.FindNode(_nodeId)
            ?? throw new InvalidOperationException($"节点 {_nodeId} 不存在，无法记录逆变更");

        // 逆变更的两端都按同一份口径算出来，而不是把传进来的内容原样写进去：
        // 传进来的那份可能带着空片段或全空样式，落盘时已经被折过一遍，
        // 日志里照原样写会让"改回去"这一步看起来改的东西与真正改的东西不一样。
        var after = RichLabelRules.WithContent(node, _content);

        return new SetRichLabelMemento
        {
            NodeId = _nodeId,
            Previous = node,
            AffectedIds = [_nodeId],
            InverseChanges =
            [
                new FieldChange
                {
                    ElementId = _nodeId,
                    Field = FieldNames.RichLabel,
                    OldValue = NodeFieldValue.Read(after, FieldNames.RichLabel),
                    NewValue = NodeFieldValue.Read(node, FieldNames.RichLabel),
                    Kind = ChangeKind.Modified,
                },
            ],
        };
    }

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(memento);

        var typed = (SetRichLabelMemento)memento;
        var index = document.IndexOfNode(typed.NodeId);

        // 节点不在了就什么都不做。这里**不**把它插回去：还原的逆操作是"改回去"，
        // 不是"复活一个被删掉的节点"，插回去会让撤销把别的命令删掉的东西带回来。
        if (index >= 0)
        {
            document.MutableNodes[index] = typed.Previous;
        }
    }
}
