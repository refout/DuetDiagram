using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 锁上一个图层或者解锁。
/// </summary>
/// <remarks>
/// <para>
/// **锁定是"别动"，不是"别看"。** 锁上的元素照常画出来，只是点不中、改不了。
/// 与藏起来合成一个开关的话，"我想看着它但别动它"这个最常见的诉求就表达不出来。
/// </para>
/// <para>
/// **与可见性分开两条命令**，理由见 <see cref="SetLayerVisibleCommand"/>：
/// 字段名在别处要去字段注册表里查，而图层这三个字段都不在那张表里。
/// </para>
/// </remarks>
public sealed class SetLayerLockedCommand : DiagramCommandBase
{
    public const string Id = "set-layer-locked";

    private readonly string _layerId;
    private readonly bool _locked;

    public SetLayerLockedCommand(string layerId, bool locked)
        : base(Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);

        _layerId = layerId;
        _locked = locked;
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

        var layer = LayerAccess.Require(document, _layerId);

        if (layer.Locked == _locked)
        {
            return CommandResult.NoOp(_locked ? "这一层本来就锁着" : "这一层本来就没锁");
        }

        LayerAccess.Replace(document, layer with { Locked = _locked });

        return LayerCommands.Result(_layerId, layer.Locked, _locked);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) =>
        new SetLayerLockedCommand(_layerId, _locked);

    protected override CommandMemento CaptureCore(DiagramDocument document) =>
        LayerCommands.Capture(document, _layerId, undoFrom: _locked, undoTo: LayerAccess.Require(document, _layerId).Locked);

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento) =>
        LayerAccess.RestoreAll(document, ((LayerMemento)memento).PreviousLayers);
}
