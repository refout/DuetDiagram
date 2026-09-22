using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 定义一个新的调色板条目。
/// </summary>
/// <remarks>
/// <para>
/// 调色板是文档的子对象，不在元素字段表里，所以它要单立命令。
/// 令牌名到具体外观的映射集中在这一处，换主题时只改这里，不必遍历全图。
/// </para>
/// <para>
/// **重名报错，不覆盖。** 覆盖的话，一次本想"加个新令牌"的调用会把一个正在被
/// 几百个节点引用的令牌悄悄换掉外观，而调用方以为自己只是加了个东西。
/// 要改已有的条目走改字段那条命令，那一条的语义是"改这个条目的某个成员"。
/// </para>
/// <para>
/// 只计外观，不计结构：换调色板不改变节点尺寸，已算出的坐标仍然有效。
/// 所以它报的是纯视觉变更，宿主只重绘不重排。
/// </para>
/// </remarks>
public sealed class DefinePaletteEntryCommand : DiagramCommandBase
{
    public const string Id = "define-palette-entry";

    private readonly PaletteEntry _entry;

    public DefinePaletteEntryCommand(PaletteEntry entry)
        : base(Id)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entry = entry;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(_entry.Name))
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.InvalidId, "调色板条目名为空"));
        }

        return document.Palette.Entries.ContainsKey(_entry.Name)
            ? ValidationResult.Invalid(CommandError.Of(ErrorCodes.DuplicateId, _entry.Name))
            : ValidationResult.Valid;
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Palette.Entries.ContainsKey(_entry.Name))
        {
            return CommandResult.Fail([CommandError.Of(ErrorCodes.DuplicateId, _entry.Name)]);
        }

        document.Palette = document.Palette.WithEntry(_entry);

        return CommandResult.Ok(
            affected: [_entry.Name],
            changes:
            [
                new FieldChange
                {
                    ElementId = _entry.Name,
                    Field = FieldNames.PaletteEntryElement,
                    OldValue = null,
                    NewValue = Describe(_entry),
                    Kind = ChangeKind.Added,
                },
            ],
            structural: false,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new DefinePaletteEntryCommand(_entry);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new PaletteMemento
    {
        Previous = document.Palette,
        AffectedIds = [_entry.Name],
        InverseChanges =
        [
            new FieldChange
            {
                ElementId = _entry.Name,
                Field = FieldNames.PaletteEntryElement,
                OldValue = Describe(_entry),
                NewValue = null,
                Kind = ChangeKind.Removed,
            },
        ],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento) =>
        document.Palette = ((PaletteMemento)memento).Previous;

    /// <summary>给变更明细写的一句人读描述。</summary>
    internal static string Describe(PaletteEntry entry)
    {
        var parts = new List<string>(4);

        if (entry.Fill is not null)
        {
            parts.Add($"填充 {entry.Fill}");
        }

        if (entry.Stroke is not null)
        {
            parts.Add($"描边 {entry.Stroke}");
        }

        if (entry.Text is not null)
        {
            parts.Add($"文字 {entry.Text}");
        }

        if (entry.Weight is { } weight)
        {
            parts.Add($"粗细 {weight.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
        }

        return parts.Count == 0 ? "（空条目）" : string.Join("、", parts);
    }
}
