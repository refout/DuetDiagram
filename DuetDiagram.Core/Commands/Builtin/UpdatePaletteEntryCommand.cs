using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 改一个调色板条目的一个成员。
/// </summary>
/// <remarks>
/// <para>
/// **一次只改一个字段**，与改节点、改边那两条命令同一个形状。界面上一人一个控件，
/// 冲突判定要能认出"一边改填充、一边改描边"可以共存——整份条目一起写的话，
/// 两次互不相干的修改会被判成冲突。
/// </para>
/// <para>
/// **条目不存在时报错，不顺手新建一个。** 顺手建的话，一次拼错令牌名的调用会造出一个
/// 只有一半成员的条目，而引用它的节点会照着那个半成品上色。要新建走定义那一条命令。
/// </para>
/// <para>
/// 令牌名不在可改字段里。它是条目的标识而不是它的属性，改它等于换一个条目，
/// 由删除加新增表达。
/// </para>
/// </remarks>
public sealed class UpdatePaletteEntryCommand : DiagramCommandBase
{
    public const string Id = "update-palette-entry";

    private readonly string _name;
    private readonly string _field;
    private readonly string? _value;

    public UpdatePaletteEntryCommand(string name, string field, string? value)
        : base(Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        _field = field;
        _value = value;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!PaletteFieldValue.IsWritable(_field))
        {
            // 字段名认不出与值不合法分成两个码：前者说明调用方拿的是另一套字段表
            // （版本对不上或拼错了），后者只说明这一次输入有问题，处置完全不同。
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.FieldUnknown, _field));
        }

        if (document.Palette.Find(_name) is not { } entry)
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.PaletteEntryMissing, _name));
        }

        return PaletteFieldValue.TryWrite(entry, _field, _value, out _, out var error)
            ? ValidationResult.Valid
            : ValidationResult.Invalid(CommandError.Of(ErrorCodes.FieldValueInvalid, error));
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Palette.Find(_name) is not { } before)
        {
            return CommandResult.Fail([CommandError.Of(ErrorCodes.PaletteEntryMissing, _name)]);
        }

        if (!PaletteFieldValue.TryWrite(before, _field, _value, out var after, out var error))
        {
            return CommandResult.Fail([CommandError.Of(ErrorCodes.FieldValueInvalid, error)]);
        }

        if (after == before)
        {
            return CommandResult.NoOp("这个成员没变");
        }

        document.Palette = document.Palette.WithEntry(after);

        return CommandResult.Ok(
            affected: [_name],
            changes:
            [
                new FieldChange
                {
                    ElementId = _name,
                    Field = _field,
                    OldValue = PaletteFieldValue.Read(before, _field),
                    NewValue = PaletteFieldValue.Read(after, _field),
                    Kind = ChangeKind.Modified,
                },
            ],
            structural: false,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) =>
        new UpdatePaletteEntryCommand(_name, _field, _value);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new PaletteMemento
    {
        // 记整份调色板而不是只记这个条目：还原时整份换回去，不必按成员再拼一次。
        // 两处拼接一旦分叉，撤销出来的条目与原来那份会有细微差别，而差异只体现在哈希上。
        Previous = document.Palette,
        AffectedIds = [_name],
        InverseChanges = [],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento) =>
        document.Palette = ((PaletteMemento)memento).Previous;
}
