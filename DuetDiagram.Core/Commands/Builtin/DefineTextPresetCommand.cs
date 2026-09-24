using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 定义一个新的文本样式预设。
/// </summary>
/// <remarks>
/// <para>
/// 预设是文本样式的那份命名清单：调色板管填充、描边与文字颜色，
/// 字号、字体、粗细这些它管不着——它们在 <see cref="TextStyle"/> 里。
/// 两者合起来才叫「样式可复用」。
/// </para>
/// <para>
/// **重名报错的是标识，不是显示名。** 预设的身份是标识，九个集合共用一个命名空间，
/// 撞上任何一个都让按标识定位的操作变得有歧义。显示名允许重复：两个预设叫同一个名字
/// 只是清单上难分，不产生任何指向上的歧义。名为空仍然拒绝——预设靠名字被挑中，
/// 空名字的行在清单上分不出彼此。
/// </para>
/// <para>
/// 样式成员在这里**不做白名单校验**：定义走的是强类型的 <see cref="TextStyle"/> 记录，
/// 不存在"不认识的字段名"这回事。白名单挡的是另一条路——按字段名逐个改的那条
/// （<see cref="UpdateTextPresetCommand"/>），那里才有人能把字段名拼错。
/// </para>
/// <para>
/// 只计外观，不计结构：预设进视觉哈希，定义一个预设不改变任何坐标。
/// </para>
/// </remarks>
public sealed class DefineTextPresetCommand : DiagramCommandBase
{
    public const string Id = "define-text-preset";

    private readonly TextStylePreset _preset;

    public DefineTextPresetCommand(TextStylePreset preset)
        : base(Id)
    {
        ArgumentNullException.ThrowIfNull(preset);
        _preset = preset;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(_preset.Id))
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.InvalidId, "预设标识为空"));
        }

        if (string.IsNullOrWhiteSpace(_preset.Name))
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.InvalidId, "预设名为空"));
        }

        return document.IsIdTaken(_preset.Id)
            ? ValidationResult.Invalid(CommandError.Of(ErrorCodes.DuplicateId, _preset.Id))
            : ValidationResult.Valid;
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.IsIdTaken(_preset.Id))
        {
            return CommandResult.Fail([CommandError.Of(ErrorCodes.DuplicateId, _preset.Id)]);
        }

        document.MutableTextPresets.Add(_preset);

        return CommandResult.Ok(
            affected: [_preset.Id],
            changes:
            [
                new FieldChange
                {
                    ElementId = _preset.Id,
                    Field = FieldNames.TextPresetElement,
                    OldValue = null,
                    NewValue = Describe(_preset),
                    Kind = ChangeKind.Added,
                },
            ],
            structural: false,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) =>
        new DefineTextPresetCommand(_preset);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new TextPresetMemento
    {
        PreviousPresets = [.. document.TextPresets],
        AffectedIds = [_preset.Id],

        // 逆变更与命令本身方向相反：新增的逆操作是移除。
        InverseChanges =
        [
            new FieldChange
            {
                ElementId = _preset.Id,
                Field = FieldNames.TextPresetElement,
                OldValue = Describe(_preset),
                NewValue = null,
                Kind = ChangeKind.Removed,
            },
        ],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        var typed = (TextPresetMemento)memento;

        // 整份换回去。预设与元素的关系是按值的（应用是把成员抄过去），
        // 元素身上没有回指预设的字段，所以没有别处要摘。
        document.MutableTextPresets.Clear();
        document.MutableTextPresets.AddRange(typed.PreviousPresets);
    }

    /// <summary>给变更明细写的一句人读描述：列出预设声明了哪几个成员。</summary>
    internal static string Describe(TextStylePreset preset)
    {
        var parts = new List<string>(4);

        if (preset.Style.FontFamily is { } family)
        {
            parts.Add($"字体 {family}");
        }

        if (preset.Style.FontSize is { } size)
        {
            parts.Add($"字号 {size.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
        }

        if (preset.Style.FontWeight is { } weight)
        {
            parts.Add($"字重 {weight}");
        }

        if (preset.Style.FontColor is { } color)
        {
            parts.Add($"文字颜色 {color}");
        }

        return parts.Count == 0 ? "（空样式）" : string.Join("、", parts);
    }
}
