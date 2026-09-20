using DuetDiagram.Core.Logging;

namespace DuetDiagram.Core.Commands;

/// <summary>
/// 命令执行结果。调用方只看这个对象就能决定下一步做什么：
/// 要不要刷新界面、要不要重跑布局、要不要把错误报给用户。
/// </summary>
/// <remarks>
/// <para>
/// 两个布尔标志 <see cref="StructuralChanged"/> 与 <see cref="VisualChanged"/> 是刻意分开的，
/// 因为它们对应两种代价完全不同的后续动作：结构变了要重新求解布局，
/// 只是样式变了则沿用现有坐标重绘即可。合成一个"变了"标志会让每次改颜色都触发重布局。
/// </para>
/// <para>
/// <see cref="IsNoOp"/> 用于表达"命令合法、但没什么可做"这种情况，
/// 例如在空的撤销栈上执行撤销。它算成功（<see cref="IsSuccess"/> 为真），
/// 但不推进版本、不进历史、不广播——所以真正需要触发副作用的判断要用
/// <see cref="IsEffectiveSuccess"/>，而不是 <see cref="IsSuccess"/>。
/// </para>
/// </remarks>
public sealed record CommandResult
{
    public bool IsSuccess { get; init; }

    /// <summary>合法但没有实际变更。调用方通常只在状态栏给一句灰色提示，不弹窗。</summary>
    public bool IsNoOp { get; init; }

    public string? Message { get; init; }

    public CommandError[] Errors { get; init; } = [];

    /// <summary>本次变更涉及的元素标识。</summary>
    public string[] AffectedIds { get; init; } = [];

    /// <summary>字段级变更明细。</summary>
    public FieldChange[] FieldChanges { get; init; } = [];

    /// <summary>连接关系发生变化，需要重新求解布局。</summary>
    public bool StructuralChanged { get; init; }

    /// <summary>外观发生变化，需要重绘。结构变化时它必然也是真。</summary>
    public bool VisualChanged { get; init; }

    /// <summary>
    /// 版本冲突时算出的差异。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 冲突之外的情况一律为空。冲突时必须带上——界面的处理方式是"弹可视化差异对话框"
    /// （见方案 §9.6），拿不到差异就只能给用户一句"版本冲突"，而用户无从知道冲突在哪。
    /// </para>
    /// <para>
    /// 更重要的是：差异是按需计算的，走到这一步时可能已经把整份文档序列化过了。
    /// 算完丢掉，等于每次冲突都白付一次全量序列化的代价。
    /// </para>
    /// </remarks>
    public DiffResult? Diff { get; init; }

    /// <summary>成功且真的产生了变更。只有它为真才应触发版本推进、历史入栈、布局重算与广播。</summary>
    public bool IsEffectiveSuccess => IsSuccess && !IsNoOp;

    /// <summary>
    /// 换个时间点或换个版本重试同一个请求可能成功。
    /// 版本冲突是典型的"先同步再重试"，限流是典型的"等一会儿再重试"，
    /// 其余错误（校验失败、内部错误）重试没有意义。
    /// </summary>
    public bool IsRetryable => Errors.Any(e =>
        string.Equals(e.Code, ErrorCodes.VersionConflict, StringComparison.Ordinal) ||
        string.Equals(e.Code, ErrorCodes.McpRateLimited, StringComparison.Ordinal));

    public static CommandResult Ok(
        string[]? affected = null,
        FieldChange[]? changes = null,
        bool structural = false,
        bool visual = false,
        string? message = null) => new()
        {
            IsSuccess = true,
            AffectedIds = affected ?? [],
            FieldChanges = changes ?? [],
            StructuralChanged = structural,
            VisualChanged = visual,
            Message = message,
        };

    public static CommandResult NoOp(string? message = null) => new()
    {
        IsSuccess = true,
        IsNoOp = true,
        Message = message,
    };

    public static CommandResult Fail(params CommandError[] errors) => new() { Errors = errors };

    public static CommandResult FailWith(string message, params CommandError[] errors) => new()
    {
        Message = message,
        Errors = errors,
    };

    /// <summary>
    /// 由差异类型推导冲突错误码，并把差异一并带上。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 差异计算已经判断出"调用方声称的版本比当前版本还新"时给出参数错误，
    /// 其余情况一律按并发冲突处理。两者对调用方的含义不同：
    /// 参数错误说明请求本身有问题，重试不会好；并发冲突说明只要先同步再重试就能成功。
    /// 混淆这两者会让调用方陷入无意义的重试循环。
    /// </para>
    /// <para>
    /// 差异必须随结果返回。调用方拿到 <see cref="Diff"/> 才能知道"我落后了哪些内容"，
    /// 进而决定是自己追平还是让用户处理。
    /// </para>
    /// </remarks>
    public static CommandResult Conflict(DiffResult diff)
    {
        ArgumentNullException.ThrowIfNull(diff);

        return new CommandResult
        {
            Diff = diff,
            Errors =
            [
                diff is InvalidDiff
                    ? CommandError.Of(ErrorCodes.InvalidExpectedVersion)
                    : CommandError.Of(ErrorCodes.VersionConflict),
            ],
        };
    }
}
