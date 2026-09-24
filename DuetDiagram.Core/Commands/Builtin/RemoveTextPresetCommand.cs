using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 删掉一个文本样式预设。
/// </summary>
/// <remarks>
/// <para>
/// **没有引用者要查，这与删调色板条目是两种处境。** 删一个被引用的调色板条目
/// 之所以要挡，判据是"这件事有没有第二个地方能看见"——渲染层遇到认不出的令牌
/// 会静默退回元素自己的样式，没有任何东西会报。预设不一样：应用是把样式成员
/// **按值抄**到节点的文本样式上，元素身上不存预设的标识，IR 里没有任何一处
/// 回指预设。删掉之后没有东西悬空、没有东西悄悄变样——已经应用过的样式
/// 原样长在各个节点上。
/// </para>
/// <para>
/// 这一条也把"标签颜色取令牌名"那一类扫描在预设上对过形：节点的样式令牌、
/// 边的样式令牌、标签的颜色，三处指的都是调色板条目，没有一处指预设。
/// 所以这里只查"预设存不存在"，不查"还有谁在用"。
/// </para>
/// <para>
/// 只计外观，不计结构：预设进视觉哈希，删除一个预设不改变任何坐标。
/// </para>
/// </remarks>
public sealed class RemoveTextPresetCommand : DiagramCommandBase
{
    public const string Id = "remove-text-preset";

    private readonly string _presetId;

    public RemoveTextPresetCommand(string presetId)
        : base(Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetId);
        _presetId = presetId;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Find(document, _presetId) is null
            ? ValidationResult.Invalid(CommandError.Of(ErrorCodes.TextPresetMissing, _presetId))
            : ValidationResult.Valid;
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var index = IndexOf(document, _presetId);

        if (index < 0)
        {
            return CommandResult.Fail([CommandError.Of(ErrorCodes.TextPresetMissing, _presetId)]);
        }

        var removed = document.TextPresets[index];
        document.MutableTextPresets.RemoveAt(index);

        return CommandResult.Ok(
            affected: [_presetId],
            changes:
            [
                new FieldChange
                {
                    ElementId = _presetId,
                    Field = FieldNames.TextPresetElement,
                    OldValue = DefineTextPresetCommand.Describe(removed),
                    NewValue = null,
                    Kind = ChangeKind.Removed,
                },
            ],
            structural: false,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) =>
        new RemoveTextPresetCommand(_presetId);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new TextPresetMemento
    {
        PreviousPresets = [.. document.TextPresets],
        AffectedIds = [_presetId],

        // 逆变更与命令本身方向相反：删除的逆操作是新增。
        InverseChanges =
        [
            new FieldChange
            {
                ElementId = _presetId,
                Field = FieldNames.TextPresetElement,
                OldValue = null,
                NewValue = Find(document, _presetId) is { } preset
                    ? DefineTextPresetCommand.Describe(preset)
                    : null,
                Kind = ChangeKind.Added,
            },
        ],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        var typed = (TextPresetMemento)memento;

        // 先清再整份放回，与标签、动作那两条同一套做法：
        // 重做前集合里可能已经有东西了，先判断再放比直接追加稳。
        document.MutableTextPresets.Clear();
        document.MutableTextPresets.AddRange(typed.PreviousPresets);
    }

    private static TextStylePreset? Find(DiagramDocument document, string presetId)
    {
        foreach (var preset in document.TextPresets)
        {
            if (string.Equals(preset.Id, presetId, StringComparison.Ordinal))
            {
                return preset;
            }
        }

        return null;
    }

    private static int IndexOf(DiagramDocument document, string presetId)
    {
        for (var index = 0; index < document.TextPresets.Count; index++)
        {
            if (string.Equals(document.TextPresets[index].Id, presetId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
