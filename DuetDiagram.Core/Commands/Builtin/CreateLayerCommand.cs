using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 新建一个图层。
/// </summary>
/// <remarks>
/// <para>
/// **次序由命令算出来，不由调用方给。** 调用方手上那份图层列表可能已经过期，
/// 它算出来的"下一个次序"很可能与现有某个图层撞上；而两个图层次序相同之后，
/// 叠放次序就变成由集合位置决定的偶然结果。这里每次读当前最大次序再加一，
/// 结果与文档的当前状态一致。
/// </para>
/// <para>
/// **标识占用要查全部九个集合。** 九个集合共用一个命名空间，
/// 只查图层那一张的话，一个图层会拿到与某个节点相同的标识。
/// </para>
/// <para>
/// 新图层加在集合末尾，而集合位置不表达叠放次序——次序看的是
/// <see cref="LayerDef.Order"/> 字段，集合在哈希里是按标识排序后遍历的。
/// </para>
/// </remarks>
public sealed class CreateLayerCommand : DiagramCommandBase
{
    public const string Id = "create-layer";

    private readonly string _layerId;
    private readonly string _name;

    public CreateLayerCommand(string layerId, string name = "")
        : base(Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);

        _layerId = layerId;
        _name = name;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.IsIdTaken(_layerId)
            ? ValidationResult.Invalid(CommandError.Of(ErrorCodes.DuplicateId, _layerId))
            : ValidationResult.Valid;
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.MutableLayers.Add(new LayerDef
        {
            Id = _layerId,
            Name = _name,
            Order = NextOrder(document),
        });

        return CommandResult.Ok(
            affected: [_layerId],
            changes:
            [
                new FieldChange
                {
                    ElementId = _layerId,
                    Field = FieldNames.LayerElement,
                    OldValue = null,
                    NewValue = _name,
                    Kind = ChangeKind.Added,
                },
            ],

            // 图层现在只进视觉哈希：渲染层还没有读它，所以没有坐标要重算。
            structural: false,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new CreateLayerCommand(_layerId, _name);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new LayerMemento
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
                NewValue = null,
                Kind = ChangeKind.Removed,
            },
        ],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento) =>
        LayerAccess.RestoreAll(document, ((LayerMemento)memento).PreviousLayers);

    /// <summary>当前最大的图层次序加一。没有图层时从零开始。</summary>
    private static int NextOrder(DiagramDocument document) =>
        document.Layers.Count == 0 ? 0 : document.Layers.Max(layer => layer.Order) + 1;
}
