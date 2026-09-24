using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 改一个文本样式预设的一个成员。
/// </summary>
/// <remarks>
/// <para>
/// **一次只改一个字段**，与改节点、改边、改调色板条目那几条同一个形状。
/// 界面上一个成员一个控件，冲突判定要能认出"一边改字号、一边改字重"可以共存——
/// 整份样式一起写的话，两次互不相干的修改会被判成冲突。
/// </para>
/// <para>
/// **字段名走白名单，认不出就拒绝，并把可用字段列在错误里。** 放行未知字段的话，
/// 渲染层拿到一个不认识的名字只会静默按默认画——调用方以为改成了，
/// 图上一个字节都没变。预设的样式记录成员比白名单多：白名单只收
/// 节点属性面板上已经有编辑控件的那八个成员。
/// </para>
/// <para>
/// **预设不存在时报错，不顺手新建一个。** 与改调色板条目同一句理由：
/// 顺手建的话，一次拼错标识的调用会造出一个只有一半成员的预设。
/// 要新建走定义那一条命令。
/// </para>
/// </remarks>
public sealed class UpdateTextPresetCommand : DiagramCommandBase
{
    public const string Id = "update-text-preset";

    private readonly string _presetId;
    private readonly string _field;
    private readonly string? _value;

    public UpdateTextPresetCommand(string presetId, string field, string? value)
        : base(Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        _presetId = presetId;
        _field = field;
        _value = value;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!TextPresetFieldValue.IsWritable(_field))
        {
            // 字段名认不出与值不合法分成两个码：前者说明调用方拿的是另一套字段表
            // （版本对不上或拼错了），后者只说明这一次输入有问题，处置完全不同。
            // 载荷里把可用字段列出来：不然调用方只能逐个试，而清单就长在白名单里。
            return ValidationResult.Invalid(CommandError.Of(
                ErrorCodes.FieldUnknown,
                $"{_field}（可用的有 {string.Join("、", TextPresetFieldValue.Writable)}）"));
        }

        if (Find(document, _presetId) is null)
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.TextPresetMissing, _presetId));
        }

        return TextPresetFieldValue.TryWrite(Find(document, _presetId)!, _field, _value, out _, out var error)
            ? ValidationResult.Valid
            : ValidationResult.Invalid(CommandError.Of(ErrorCodes.FieldValueInvalid, error));
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var index = IndexOf(document, _presetId);

        if (index < 0)
        {
            return CommandResult.Fail([CommandError.Of(ErrorCodes.TextPresetMissing, _presetId)]);
        }

        var before = document.TextPresets[index];

        if (!TextPresetFieldValue.TryWrite(before, _field, _value, out var after, out var error))
        {
            return CommandResult.Fail([CommandError.Of(ErrorCodes.FieldValueInvalid, error)]);
        }

        if (Equals(before, after))
        {
            return CommandResult.NoOp("这个成员没变");
        }

        document.MutableTextPresets[index] = after;

        return CommandResult.Ok(
            affected: [_presetId],
            changes:
            [
                new FieldChange
                {
                    ElementId = _presetId,
                    Field = _field,
                    OldValue = TextPresetFieldValue.Read(before, _field),
                    NewValue = TextPresetFieldValue.Read(after, _field),
                    Kind = ChangeKind.Modified,
                },
            ],
            structural: false,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) =>
        new UpdateTextPresetCommand(_presetId, _field, _value);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new TextPresetMemento
    {
        // 记整份预设集合而不是只记这一个预设：还原时整份换回去，不必按成员再拼一次。
        // 两处拼接一旦分叉，撤销出来的预设与原来那份会有细微差别，而差异只体现在哈希上。
        PreviousPresets = [.. document.TextPresets],
        AffectedIds = [_presetId],
        InverseChanges = [],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        var typed = (TextPresetMemento)memento;

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
