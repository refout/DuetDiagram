using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 把一个文本样式预设应用到一批节点上。
/// </summary>
/// <remarks>
/// <para>
/// **叠加，不是替换。** 预设里声明了的成员抄到节点的文本样式上，
/// 没声明的保持原样（<see cref="TextStyle"/> 本来就是逐层继承的）。
/// 整份替换的话，一次"只想要一套字体"的应用会把用户单独调过的字号抹掉，
/// 而他在界面上看不出任何征兆。
/// </para>
/// <para>
/// **批量是一次操作。** 多选之后应用是一条命令，不是每个元素各发一次：
/// 「一条操作进一次历史」由命令层保证，撤销按一次就把这一批全部还原，
/// 界面不必自己攒。原子性靠两遍走，与归层那一条同一套做法——
/// 先把每一个要写的都算出来，一个都不写；有一个节点不在，整条失败。
/// </para>
/// <para>
/// **只报外观变更。** 预设进视觉哈希；但字体变化会改变标签的实际宽度、
/// 进而改变节点尺寸——这一点由布局层对文本样式变化的既有处置负责，
/// 命令层照实报"改了文本"，不在这里替布局做决定。
/// </para>
/// </remarks>
public sealed class ApplyTextPresetCommand : DiagramCommandBase
{
    public const string Id = "apply-text-preset";

    private readonly string _presetId;
    private readonly IReadOnlyList<string> _nodeIds;

    public ApplyTextPresetCommand(string presetId, IReadOnlyList<string> nodeIds)
        : base(Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetId);
        ArgumentNullException.ThrowIfNull(nodeIds);

        _presetId = presetId;

        // 调用方送重复标识过来是无害的，但必须去掉：留着的话结果里会出现两条
        // 一模一样的变更，受影响集合里也会多一个，而两处都只是"看起来多了一项"。
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var accepted = new List<string>(nodeIds.Count);

        foreach (var id in nodeIds)
        {
            if (seen.Add(id))
            {
                accepted.Add(id);
            }
        }

        _nodeIds = accepted;
    }

    /// <summary>要应用哪一个预设。</summary>
    public string PresetId => _presetId;

    /// <summary>被点名的节点，去重之后的。</summary>
    public IReadOnlyList<string> NodeIds => _nodeIds;

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_nodeIds.Count == 0)
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.InvalidId, "没有点名任何节点"));
        }

        if (FindPreset(document) is null)
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.TextPresetMissing, _presetId));
        }

        foreach (var id in _nodeIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return ValidationResult.Invalid(CommandError.Of(ErrorCodes.InvalidId, "节点标识是空的"));
            }

            if (document.FindNode(id) is null)
            {
                return ValidationResult.Invalid(CommandError.Of(ErrorCodes.NodeMissing, id));
            }
        }

        return ValidationResult.Valid;
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var preset = FindPreset(document);

        if (preset is null)
        {
            return CommandResult.Fail([CommandError.Of(ErrorCodes.TextPresetMissing, _presetId)]);
        }

        var planned = new List<(int Index, NodeDef Before, NodeDef After)>(_nodeIds.Count);

        // 第一遍：全部算出来，一个都不写。
        foreach (var id in _nodeIds)
        {
            // 前置检查已经确认过它在，这里再判一次是为了让这条路径自己站得住：
            // Apply 的调用方不只命令总线，测试与将来的批量路径也会直接调它。
            var index = document.IndexOfNode(id);

            if (index < 0)
            {
                return CommandResult.Fail([CommandError.Of(ErrorCodes.NodeMissing, id)]);
            }

            var before = document.MutableNodes[index];
            var after = before with { Text = NodeFieldValue.Trim(Overlay(before.Text, preset.Style)) };

            planned.Add((index, before, after));
        }

        // 第二遍：写。跳掉本来就是这个样式的那些。
        var affected = new List<string>(planned.Count);
        var changes = new List<FieldChange>(planned.Count);

        foreach (var (index, before, after) in planned)
        {
            if (Equals(before, after))
            {
                continue;
            }

            document.MutableNodes[index] = after;
            affected.Add(after.Id);

            changes.Add(new FieldChange
            {
                ElementId = after.Id,
                Field = FieldNames.Text,
                OldValue = NodeFieldValue.Read(before, FieldNames.Text),
                NewValue = NodeFieldValue.Read(after, FieldNames.Text),
                Kind = ChangeKind.Modified,
            });
        }

        if (affected.Count == 0)
        {
            return CommandResult.NoOp("这些元素本来就是这个样式");
        }

        return CommandResult.Ok(
            affected: [.. affected],
            changes: [.. changes],
            structural: false,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) =>
        new ApplyTextPresetCommand(_presetId, _nodeIds);

    protected override CommandMemento CaptureCore(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var previous = new List<PresetTextPlacement>(_nodeIds.Count);
        var inverse = new List<FieldChange>(_nodeIds.Count);

        foreach (var id in _nodeIds)
        {
            var node = document.FindNode(id)
                ?? throw new InvalidOperationException($"节点 {id} 不存在，无法记录逆变更");

            previous.Add(new PresetTextPlacement(id, node.Text));

            inverse.Add(new FieldChange
            {
                ElementId = id,
                Field = FieldNames.Text,
                OldValue = null,
                NewValue = NodeFieldValue.Read(node, FieldNames.Text),
                Kind = ChangeKind.Modified,
            });
        }

        return new TextPresetApplyMemento
        {
            Previous = [.. previous],
            AffectedIds = [.. _nodeIds],
            InverseChanges = [.. inverse],
        };
    }

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(memento);

        var typed = (TextPresetApplyMemento)memento;

        foreach (var placement in typed.Previous)
        {
            var index = document.IndexOfNode(placement.NodeId);

            // 节点不在了就跳过。这里**不**把它插回去：文本样式的还原是"改回去"，
            // 不是"复活一个被删掉的节点"，插回去会让撤销把别的命令删掉的东西带回来。
            if (index < 0)
            {
                continue;
            }

            document.MutableNodes[index] = document.MutableNodes[index] with { Text = placement.Text };
        }
    }

    private TextStylePreset? FindPreset(DiagramDocument document)
    {
        foreach (var preset in document.TextPresets)
        {
            if (string.Equals(preset.Id, _presetId, StringComparison.Ordinal))
            {
                return preset;
            }
        }

        return null;
    }

    /// <summary>
    /// 把预设声明了的成员抄到节点样式上，没声明的保持原样。
    /// </summary>
    /// <remarks>
    /// 十六个成员逐个写一遍看着笨，但它是"叠加"这条语义最直白的写法：
    /// 一个 <c>??</c> 一行，声明的盖上去、空着的放过去。
    /// 抽一个按成员名的反射循环反而要把成员名再登记一遍，而登记漏一个就是静默丢样式。
    /// </remarks>
    private static TextStyle Overlay(TextStyle? baseStyle, TextStyle overlay)
    {
        var baseText = baseStyle ?? new TextStyle();

        return new TextStyle
        {
            FontFamily = overlay.FontFamily ?? baseText.FontFamily,
            FontSize = overlay.FontSize ?? baseText.FontSize,
            FontWeight = overlay.FontWeight ?? baseText.FontWeight,
            Italic = overlay.Italic ?? baseText.Italic,
            Underline = overlay.Underline ?? baseText.Underline,
            Strikethrough = overlay.Strikethrough ?? baseText.Strikethrough,
            FontColor = overlay.FontColor ?? baseText.FontColor,
            TextBackgroundColor = overlay.TextBackgroundColor ?? baseText.TextBackgroundColor,
            Align = overlay.Align ?? baseText.Align,
            VerticalAlign = overlay.VerticalAlign ?? baseText.VerticalAlign,
            WordWrap = overlay.WordWrap ?? baseText.WordWrap,
            LineHeight = overlay.LineHeight ?? baseText.LineHeight,
            TextIndent = overlay.TextIndent ?? baseText.TextIndent,
            Padding = overlay.Padding ?? baseText.Padding,
            LabelPosition = overlay.LabelPosition ?? baseText.LabelPosition,
            WritingDirection = overlay.WritingDirection ?? baseText.WritingDirection,
        };
    }
}
