using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 把图层挪到第几位。
/// </summary>
/// <remarks>
/// <para>
/// **改的是次序字段，不是集合里的位置。** 图层集合在视觉哈希里是按标识排序后遍历的，
/// 所以集合里换个位置进不了任何哈希、也不改变任何坐标——那会是一条**效果不可观测**的命令。
/// 真正决定叠放次序的是 <see cref="LayerDef.Order"/> 字段。
/// </para>
/// <para>
/// **次序值整体重排成连续的 0、1、2……** 只把两个图层的次序对调也可以，但那样一来，
/// 从文件里读进来的次序里如果有重复值，对调之后仍然重复，而重复次序下"谁在上面"
/// 变成由集合位置决定的偶然结果。整体重排顺带把这种文档治好。
/// </para>
/// <para>
/// 排序时用标识断掉并列：次序相同的两个图层之间本来就没有确定的前后，
/// 取一个确定的顺序才能让同一条命令在同一份文档上永远得到同一个结果。
/// </para>
/// </remarks>
public sealed class ReorderLayerCommand : DiagramCommandBase
{
    public const string Id = "reorder-layer";

    private readonly string _layerId;
    private readonly int _index;

    /// <param name="layerId">要挪的图层。</param>
    /// <param name="index">挪到第几位（从零开始）。越界一律夹紧，不报错。</param>
    public ReorderLayerCommand(string layerId, int index)
        : base(Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);

        _layerId = layerId;
        _index = index;
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

        if (LayerAccess.Find(document, _layerId) is null)
        {
            return CommandResult.Fail([CommandError.Of(ErrorCodes.LayerMissing, _layerId)]);
        }

        var ordered = InOrder(document);
        var from = ordered.FindIndex(layer => string.Equals(layer.Id, _layerId, StringComparison.Ordinal));
        var to = Math.Clamp(_index, 0, ordered.Count - 1);

        if (from == to)
        {
            // 次序已经对了。位置本身没变时还要把次序值重排一遍的话，
            // 一次"什么都没做"的调用也会推进版本。
            return CommandResult.NoOp("次序没变");
        }

        var moved = ordered[from];
        ordered.RemoveAt(from);
        ordered.Insert(to, moved);

        var changes = new List<FieldChange>(ordered.Count);

        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Order == i)
            {
                continue;
            }

            changes.Add(new FieldChange
            {
                ElementId = ordered[i].Id,
                Field = FieldNames.LayerElement,
                OldValue = ordered[i].Order.ToString(System.Globalization.CultureInfo.InvariantCulture),
                NewValue = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Kind = ChangeKind.Modified,
            });
        }

        document.MutableLayers.Clear();
        document.MutableLayers.AddRange(ordered.Select((layer, i) => layer with { Order = i }));

        return CommandResult.Ok(
            affected: [_layerId],
            changes: [.. changes],
            structural: false,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) =>
        new ReorderLayerCommand(_layerId, _index);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new LayerMemento
    {
        // 重排一次会改到每一个图层的次序，所以记整份。
        PreviousLayers = [.. document.Layers],
        AffectedIds = [_layerId],
        InverseChanges = [],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento) =>
        LayerAccess.RestoreAll(document, ((LayerMemento)memento).PreviousLayers);

    /// <summary>按次序字段排好的图层。次序相同的用标识断掉并列。</summary>
    private static List<LayerDef> InOrder(DiagramDocument document) =>
        [.. document.Layers
            .OrderBy(layer => layer.Order)
            .ThenBy(layer => layer.Id, StringComparer.Ordinal)];
}
