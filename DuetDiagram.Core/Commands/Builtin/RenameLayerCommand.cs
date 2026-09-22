using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 给一个图层改名。
/// </summary>
/// <remarks>
/// <para>
/// **只改名字，不改次序。** 图层的标识与次序都不在这里动：标识是引用它的那个字段
/// （节点上的图层归属）写的东西，改名连带改标识会让所有引用一起失效；
/// 次序有它自己的那条命令。
/// </para>
/// <para>
/// 改成同一个名字是 NoOp。面板上的输入框失焦就会提交一次，
/// 每次失焦都推进版本、每次都广播一遍，是白付的代价。
/// </para>
/// </remarks>
public sealed class RenameLayerCommand : DiagramCommandBase
{
    public const string Id = "rename-layer";

    private readonly string _layerId;
    private readonly string _name;

    public RenameLayerCommand(string layerId, string name)
        : base(Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);
        ArgumentNullException.ThrowIfNull(name);

        _layerId = layerId;
        _name = name;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return LayerAccess.Find(document, _layerId) is null
            ? ValidationResult.Invalid(CommandError.Of(ErrorCodes.LayerMissing, _layerId))
            : ValidationResult.Valid;
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var layer = LayerAccess.Find(document, _layerId)
            ?? throw new InvalidOperationException($"Cannot rename layer: '{_layerId}' does not exist.");

        if (string.Equals(layer.Name, _name, StringComparison.Ordinal))
        {
            return CommandResult.NoOp("名字没变");
        }

        LayerAccess.Replace(document, layer with { Name = _name });

        return CommandResult.Ok(
            affected: [_layerId],
            changes:
            [
                new FieldChange
                {
                    ElementId = _layerId,
                    Field = FieldNames.LayerElement,
                    OldValue = layer.Name,
                    NewValue = _name,
                    Kind = ChangeKind.Modified,
                },
            ],
            structural: false,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new RenameLayerCommand(_layerId, _name);

    protected override CommandMemento CaptureCore(DiagramDocument document)
    {
        var layer = LayerAccess.Find(document, _layerId)
            ?? throw new InvalidOperationException($"Cannot capture memento: layer '{_layerId}' does not exist.");

        return new LayerMemento
        {
            PreviousLayers = [.. document.Layers],
            AffectedIds = [_layerId],
            InverseChanges =
            [
                new FieldChange
                {
                    ElementId = _layerId,
                    Field = FieldNames.LayerElement,
                    OldValue = _name,
                    NewValue = layer.Name,
                    Kind = ChangeKind.Modified,
                },
            ],
        };
    }

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento) =>
        LayerAccess.RestoreAll(document, ((LayerMemento)memento).PreviousLayers);
}
