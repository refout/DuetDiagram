using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 把一个图层藏起来或者放出来。
/// </summary>
/// <remarks>
/// <para>
/// **与锁定分开两条命令。** 两者都是"改一个布尔"，形状相同，本来可以合成一条
/// 由参数给出字段名的命令。但字段名在别处是要去 <c>FieldRegistry</c> 查的
/// （界面按那张表选编辑器），而 <see cref="LayerDef"/> 的三个字段都不在那张表里，
/// 没有名字可查——硬造一个进来，等于在字段表之外再开一套字段命名。
/// </para>
/// <para>
/// **藏起来只是不画，不改坐标。** 被藏起来的元素仍然参与布局：它占的位置还在，
/// 连线仍然绕着它走。让它退出布局的话，藏一个元素会让整张图重排，
/// 而用户以为自己只是把它藏起来了。
/// </para>
/// <para>
/// **值没变就是 NoOp。** 面板上的开关被点到已经是那个值时会提交一次，
/// 每次提交都推进版本、每次都广播一遍，是白付的代价。
/// </para>
/// </remarks>
public sealed class SetLayerVisibleCommand : DiagramCommandBase
{
    public const string Id = "set-layer-visible";

    private readonly string _layerId;
    private readonly bool _visible;

    public SetLayerVisibleCommand(string layerId, bool visible)
        : base(Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);

        _layerId = layerId;
        _visible = visible;
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

        if (layer.Visible == _visible)
        {
            return CommandResult.NoOp(_visible ? "这一层本来就画着" : "这一层本来就没画");
        }

        LayerAccess.Replace(document, layer with { Visible = _visible });

        return LayerCommands.Result(_layerId, layer.Visible, _visible);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) =>
        new SetLayerVisibleCommand(_layerId, _visible);

    protected override CommandMemento CaptureCore(DiagramDocument document) =>
        LayerCommands.Capture(document, _layerId, undoFrom: _visible, undoTo: LayerAccess.Require(document, _layerId).Visible);

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento) =>
        LayerAccess.RestoreAll(document, ((LayerMemento)memento).PreviousLayers);
}
