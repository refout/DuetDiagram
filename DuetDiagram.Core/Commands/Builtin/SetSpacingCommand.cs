using System.Globalization;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 改布局的两个间距。
/// </summary>
/// <remarks>
/// <para>
/// 间距落在文档的布局提示上，不在任何元素上，所以进不了元素字段表，要单立一条命令。
/// </para>
/// <para>
/// **两个参数都是可选的，传空表示"这一项不动"。** 界面上是两个数字框共用一个应用按钮，
/// 要求调用方两个都填的话，改一项就得先把另一项读出来再写回去——那次读回来的值可能已经过期。
/// </para>
/// <para>
/// **每改一项就发一条变更明细，而不是两条一起发。** 冲突判定是按"哪个元素的哪个字段"算的，
/// 所以粒度由变更明细决定、不由命令的参数个数决定。两条一起发的话，
/// 一边调层间距、一边调同层间距会被判成冲突，而那是两次互不相干的修改。
/// </para>
/// <para>
/// **间距进的是结构哈希。** 它决定坐标，改了必须重排；报成纯外观的话宿主只会重绘，
/// 而画面上的间距根本没变。
/// </para>
/// </remarks>
public sealed class SetSpacingCommand : DiagramCommandBase
{
    public const string Id = "set-spacing";

    private readonly double? _nodeSpacing;
    private readonly double? _layerSpacing;

    /// <param name="nodeSpacing">同层节点之间的间距。传空表示不动这一项。</param>
    /// <param name="layerSpacing">层与层之间的间距。传空表示不动这一项。</param>
    public SetSpacingCommand(double? nodeSpacing = null, double? layerSpacing = null)
        : base(Id)
    {
        _nodeSpacing = nodeSpacing;
        _layerSpacing = layerSpacing;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_nodeSpacing is null && _layerSpacing is null)
        {
            // 两个都不给就是一次什么都不做的调用。报成成功会让调用方以为自己改了东西。
            return ValidationResult.Invalid(CommandError.Of(
                ErrorCodes.FieldValueInvalid,
                "两个间距都没给，这一次调用没有要改的东西"));
        }

        if (Invalid(_nodeSpacing) is { } badNode)
        {
            return ValidationResult.Invalid(CommandError.Of(
                ErrorCodes.FieldValueInvalid,
                $"同层间距 {badNode}：必须是大于零的有限数"));
        }

        if (Invalid(_layerSpacing) is { } badLayer)
        {
            return ValidationResult.Invalid(CommandError.Of(
                ErrorCodes.FieldValueInvalid,
                $"层间距 {badLayer}：必须是大于零的有限数"));
        }

        return ValidationResult.Valid;
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var previous = document.Layout;

        var updated = previous with
        {
            NodeSpacing = _nodeSpacing ?? previous.NodeSpacing,
            LayerSpacing = _layerSpacing ?? previous.LayerSpacing,
        };

        if (updated == previous)
        {
            return CommandResult.NoOp("间距没变");
        }

        document.Layout = updated;

        // 只给真的变了的那些发变更明细。传了值但值与原来相同的那一项不算改过。
        var changes = new List<FieldChange>(2);

        if (updated.NodeSpacing != previous.NodeSpacing)
        {
            changes.Add(SpacingChange(
                document.Id, FieldNames.NodeSpacing, previous.NodeSpacing, updated.NodeSpacing));
        }

        if (updated.LayerSpacing != previous.LayerSpacing)
        {
            changes.Add(SpacingChange(
                document.Id, FieldNames.LayerSpacing, previous.LayerSpacing, updated.LayerSpacing));
        }

        return CommandResult.Ok(
            affected: [],
            changes: [.. changes],
            structural: true,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) =>
        new SetSpacingCommand(_nodeSpacing, _layerSpacing);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new LayoutConstraintMemento
    {
        // 记整份布局提示而不是只记这两个数：还原时直接换回去，不必按字段再拼一次。
        // 两处拼接一旦分叉，撤销出来的间距与原来那份会有细微差别，而差异只体现在哈希上。
        Previous = document.Layout,
        AffectedIds = [],
        InverseChanges = [],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento) =>
        document.Layout = ((LayoutConstraintMemento)memento).Previous;

    /// <summary>
    /// 一条间距变更明细。
    /// </summary>
    /// <remarks>
    /// 归属元素写的是**文档自己的标识**：这一次改动没有落在任何元素上。
    /// 数字用不变文化格式化，与其它命令一致——跟着当前区域走的话，
    /// 同一次改动在不同机器上会写出不同的小数分隔符，而变更明细要能逐字节比较。
    /// </remarks>
    private static FieldChange SpacingChange(string documentId, string field, double oldValue, double newValue) => new()
    {
        ElementId = documentId,
        Field = field,
        OldValue = oldValue.ToString("0.###", CultureInfo.InvariantCulture),
        NewValue = newValue.ToString("0.###", CultureInfo.InvariantCulture),
        Kind = ChangeKind.Modified,
    };

    /// <summary>给了一个值但它不合法时返回它，合法或没给都返回空。</summary>
    private static double? Invalid(double? value) =>
        value is { } v && (!double.IsFinite(v) || v <= 0) ? v : null;
}
