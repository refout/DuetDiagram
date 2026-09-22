using System.Globalization;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 改画布设置：网格、网格尺寸、纸张尺寸、纸张方向、背景色、是否无限画布。
/// </summary>
/// <remarks>
/// <para>
/// 画布设置是文档的一个子对象，不是任何元素的字段，所以进不了元素字段表，要单立一条命令。
/// </para>
/// <para>
/// **每一项都是可选的，传空表示"这一项不动"。** 界面上是六个控件共用一个应用按钮，
/// 要求调用方六项都填的话，改一项就得先把其余五项读出来再写回去——那份读回来的值
/// 可能已经过期，一次"只改网格"的调用会把背景色悄悄改回旧值。
/// </para>
/// <para>
/// **背景色用空串表示清除，用空引用表示不动。** 空引用在可选参数里已经占了"这一项不动"
/// 的意思，清除只能另给一个信号；空串在字段写入那条路上本来就是"这个字段没有值"的写法，
/// 两处口径一致。所以想清掉背景色就传一个空串，而不是传空引用。
/// </para>
/// <para>
/// **每改一项就发一条变更明细，而不是六项一起发。** 冲突判定是按"哪个元素的哪个字段"算的，
/// 所以粒度由变更明细决定、不由命令的参数个数决定。一起发的话，一边调网格、一边调背景色
/// 会被判成冲突，而那是两次互不相干的修改。
/// </para>
/// <para>
/// **它只计外观，不触发重排。** 画布设置进的是视觉哈希：网格、背景、纸张尺寸都不改变
/// 任何坐标。报成结构变更的话，换个网格样式就要把整张图重排一遍。
/// </para>
/// </remarks>
public sealed class SetCanvasSettingsCommand : DiagramCommandBase
{
    public const string Id = "set-canvas-settings";

    private readonly GridStyle? _grid;
    private readonly double? _gridSize;
    private readonly Size? _pageSize;
    private readonly CanvasOrientation? _orientation;
    private readonly string? _background;
    private readonly bool? _infinite;

    /// <param name="grid">网格样式。传空表示不动这一项。</param>
    /// <param name="gridSize">网格尺寸。传空表示不动这一项。</param>
    /// <param name="pageSize">纸张尺寸。传空表示不动这一项。</param>
    /// <param name="orientation">纸张方向。传空表示不动这一项。</param>
    /// <param name="background">背景色。传空引用表示不动这一项，传空串表示清除。</param>
    /// <param name="infinite">是否无限画布。传空表示不动这一项。</param>
    public SetCanvasSettingsCommand(
        GridStyle? grid = null,
        double? gridSize = null,
        Size? pageSize = null,
        CanvasOrientation? orientation = null,
        string? background = null,
        bool? infinite = null)
        : base(Id)
    {
        _grid = grid;
        _gridSize = gridSize;
        _pageSize = pageSize;
        _orientation = orientation;
        _background = background;
        _infinite = infinite;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_grid is null
            && _gridSize is null
            && _pageSize is null
            && _orientation is null
            && _background is null
            && _infinite is null)
        {
            // 一项都不给就是一次什么都不做的调用。报成成功会让调用方以为自己改了东西。
            return ValidationResult.Invalid(CommandError.Of(
                ErrorCodes.FieldValueInvalid,
                "画布设置一项都没给，这一次调用没有要改的东西"));
        }

        if (_grid is { } grid && !Enum.IsDefined(grid))
        {
            return ValidationResult.Invalid(CommandError.Of(
                ErrorCodes.FieldValueInvalid,
                $"不是已定义的网格样式：{(int)grid}"));
        }

        if (_orientation is { } orientation && !Enum.IsDefined(orientation))
        {
            return ValidationResult.Invalid(CommandError.Of(
                ErrorCodes.FieldValueInvalid,
                $"不是已定义的纸张方向：{(int)orientation}"));
        }

        if (NonPositive(_gridSize) is { } badGridSize)
        {
            return ValidationResult.Invalid(CommandError.Of(
                ErrorCodes.FieldValueInvalid,
                $"网格尺寸 {badGridSize}：必须是大于零的有限数"));
        }

        if (_pageSize is { } size && (NonPositive(size.Width) is not null || NonPositive(size.Height) is not null))
        {
            return ValidationResult.Invalid(CommandError.Of(
                ErrorCodes.FieldValueInvalid,
                $"纸张尺寸 {Describe(size)}：宽与高都必须是大于零的有限数"));
        }

        return ValidationResult.Valid;
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var previous = document.Canvas;

        var updated = previous with
        {
            Grid = _grid ?? previous.Grid,
            GridSize = _gridSize ?? previous.GridSize,
            PageSize = _pageSize ?? previous.PageSize,
            Orientation = _orientation ?? previous.Orientation,

            // 空引用表示不动，空串表示清除。两者在可选参数里必须分开，
            // 否则"清掉背景色"这件事没有写法。
            Background = _background is null
                ? previous.Background
                : Blank(_background) ? null : _background,

            Infinite = _infinite ?? previous.Infinite,
        };

        if (updated == previous)
        {
            return CommandResult.NoOp("画布设置没变");
        }

        document.Canvas = updated;

        // 只给真的变了的那些发变更明细。传了值但值与原来相同的那一项不算改过。
        var changes = new List<FieldChange>(6);

        if (updated.Grid != previous.Grid)
        {
            changes.Add(Change(document.Id, FieldNames.CanvasGrid, previous.Grid.ToString(), updated.Grid.ToString()));
        }

        if (updated.GridSize != previous.GridSize)
        {
            changes.Add(Change(
                document.Id, FieldNames.CanvasGridSize, Number(previous.GridSize), Number(updated.GridSize)));
        }

        if (updated.PageSize != previous.PageSize)
        {
            changes.Add(Change(
                document.Id, FieldNames.CanvasPageSize, Describe(previous.PageSize), Describe(updated.PageSize)));
        }

        if (updated.Orientation != previous.Orientation)
        {
            changes.Add(Change(
                document.Id,
                FieldNames.CanvasOrientation,
                previous.Orientation.ToString(),
                updated.Orientation.ToString()));
        }

        if (!string.Equals(updated.Background, previous.Background, StringComparison.Ordinal))
        {
            changes.Add(Change(
                document.Id, FieldNames.CanvasBackground, previous.Background, updated.Background));
        }

        if (updated.Infinite != previous.Infinite)
        {
            changes.Add(Change(
                document.Id, FieldNames.CanvasInfinite, Flag(previous.Infinite), Flag(updated.Infinite)));
        }

        return CommandResult.Ok(
            affected: [],
            changes: [.. changes],
            structural: false,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) =>
        new SetCanvasSettingsCommand(_grid, _gridSize, _pageSize, _orientation, _background, _infinite);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new CanvasMemento
    {
        // 记整份画布设置而不是只记改到的那几项：还原时直接换回去，不必按成员再拼一次。
        // 两处拼接一旦分叉，撤销出来的设置与原来那份会有细微差别，而差异只体现在哈希上。
        Previous = document.Canvas,
        AffectedIds = [],
        InverseChanges = [],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento) =>
        document.Canvas = ((CanvasMemento)memento).Previous;

    /// <summary>
    /// 一条画布设置的变更明细。
    /// </summary>
    /// <remarks>
    /// 归属元素写的是**文档自己的标识**：这一次改动没有落在任何元素上。
    /// </remarks>
    private static FieldChange Change(string documentId, string field, string? oldValue, string? newValue) => new()
    {
        ElementId = documentId,
        Field = field,
        OldValue = oldValue,
        NewValue = newValue,
        Kind = ChangeKind.Modified,
    };

    /// <summary>给了一个值但它不合法时返回它，合法或没给都返回空。</summary>
    private static double? NonPositive(double? value) =>
        value is { } v && (!double.IsFinite(v) || v <= 0) ? v : null;

    /// <summary>数字用不变文化格式化，与其它命令一致。</summary>
    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>纸张尺寸写成"宽x高"。两个数都按不变文化格式化。</summary>
    private static string Describe(Size size) => $"{Number(size.Width)}x{Number(size.Height)}";

    private static string Flag(bool value) => value ? "true" : "false";

    private static bool Blank(string text) => string.IsNullOrWhiteSpace(text);
}
