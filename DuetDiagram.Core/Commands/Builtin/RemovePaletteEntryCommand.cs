using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 删掉一个调色板条目。
/// </summary>
/// <remarks>
/// <para>
/// **还有元素在引用它的时候挡住，并把这些元素列出来。** 渲染层遇到认不出的令牌会
/// 静默退回元素自己的样式，所以删掉一个被引用的令牌不会报错、不会崩，只会让一批节点
/// 悄悄换了颜色——而用户看到的是一次"什么都没发生"的删除，配上一张颜色不对的图。
/// </para>
/// <para>
/// 这与删边那一处的处置**刻意相反**。删边之后约束指向一条不存在的边，那件事由整体校验器
/// 报出来，所以命令层留着不管；而删一个被引用的令牌**没有任何东西会报**。
/// 判据是「这件事有没有第二个地方能看见」：有就留着让那个地方报，没有就在这里挡住。
/// </para>
/// <para>
/// 引用者只看样式令牌这一条路：节点上有令牌字段，边上有，标签的颜色也取令牌名，
/// 三者都算。组合的样式记录里没有令牌字段，所以它不算引用者。
/// </para>
/// </remarks>
public sealed class RemovePaletteEntryCommand : DiagramCommandBase
{
    public const string Id = "remove-palette-entry";

    private readonly string _name;

    public RemovePaletteEntryCommand(string name)
        : base(Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Palette.Find(_name) is null)
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.PaletteEntryMissing, _name));
        }

        var users = Referrers(document);

        // 载荷里带上引用者，界面据此把它们标出来——只说一句"还有人在用"，
        // 用户无从知道该先改哪些元素。
        return users.Count == 0
            ? ValidationResult.Valid
            : ValidationResult.Invalid(CommandError.Of(
                ErrorCodes.PaletteEntryInUse,
                string.Join("、", users)));
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Palette.Find(_name) is not { } entry)
        {
            return CommandResult.Fail([CommandError.Of(ErrorCodes.PaletteEntryMissing, _name)]);
        }

        var users = Referrers(document);

        if (users.Count > 0)
        {
            return CommandResult.Fail([CommandError.Of(
                ErrorCodes.PaletteEntryInUse,
                string.Join("、", users))]);
        }

        document.Palette = document.Palette.WithoutEntry(_name);

        return CommandResult.Ok(
            affected: [_name],
            changes:
            [
                new FieldChange
                {
                    ElementId = _name,
                    Field = FieldNames.PaletteEntryElement,
                    OldValue = DefinePaletteEntryCommand.Describe(entry),
                    NewValue = null,
                    Kind = ChangeKind.Removed,
                },
            ],
            structural: false,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new RemovePaletteEntryCommand(_name);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new PaletteMemento
    {
        Previous = document.Palette,
        AffectedIds = [_name],
        InverseChanges =
        [
            new FieldChange
            {
                ElementId = _name,
                Field = FieldNames.PaletteEntryElement,
                OldValue = null,
                NewValue = document.Palette.Find(_name) is { } entry
                    ? DefinePaletteEntryCommand.Describe(entry)
                    : null,
                Kind = ChangeKind.Added,
            },
        ],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento) =>
        document.Palette = ((PaletteMemento)memento).Previous;

    /// <summary>
    /// 还在用这个令牌的元素。
    /// </summary>
    /// <remarks>
    /// 节点、边、标签各扫一遍。写成三段而不是一段通用代码，是因为三处读令牌的方式不同：
    /// 节点上是一个直接的字段，边上藏在样式记录里，标签上是颜色字段——硬凑成一个接口
    /// 反而要多加一层抽象。
    /// </remarks>
    private IReadOnlyList<string> Referrers(DiagramDocument document) => Referrers(document, _name);

    /// <summary>
    /// 还在用某个令牌的元素，按文档次序。
    /// </summary>
    /// <remarks>
    /// 面板在按下删除**之前**也要这一份名单：等命令挡下来再展示的话，
    /// 错误消息里那串名字挤在一句话里，而「谁在用」值得单独一块地方摆出来。
    /// 与命令校验读的是同一个方法，两处各写一遍迟早会对出两份不一样的名单。
    /// </remarks>
    public static IReadOnlyList<string> Referrers(DiagramDocument document, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var users = new List<string>();

        foreach (var node in document.Nodes)
        {
            if (string.Equals(node.StyleToken, name, StringComparison.Ordinal))
            {
                users.Add(node.Id);
            }
        }

        foreach (var edge in document.Edges)
        {
            if (string.Equals(edge.StyleToken, name, StringComparison.Ordinal))
            {
                users.Add(edge.Id);
            }
        }

        foreach (var tag in document.Tags)
        {
            if (string.Equals(tag.Color, name, StringComparison.Ordinal))
            {
                users.Add(tag.Id);
            }
        }

        return users;
    }
}
