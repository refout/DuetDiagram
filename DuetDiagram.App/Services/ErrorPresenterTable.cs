using DuetDiagram.Core.Commands;

namespace DuetDiagram.App.Services;

/// <summary>
/// 一个错误码在界面上怎么呈现。
/// </summary>
/// <remarks>
/// 呈现方式只有这几种，因为用户能做的事只有这几种：看到哪几个元素被指出来、被问一句要不要补上、
/// 看一份差异、在状态栏读一句话、还是面对一个要选一条路的对话框。多出一种呈现方式，
/// 就意味着多一种用户没见过、也不知道该怎么回应的东西。
/// </remarks>
public enum ErrorPresentationKind
{
    /// <summary>把相关元素标出来，不弹窗。</summary>
    HighlightTargets,

    /// <summary>问一句"要不要创建"，由用户决定。</summary>
    PromptCreate,

    /// <summary>弹差异对话框。版本冲突走这一档。</summary>
    VersionDiffDialog,

    /// <summary>状态栏给一句话。</summary>
    StatusBar,

    /// <summary>状态栏灰显一句话。无操作的失败走这一档，不弹窗。</summary>
    StatusBarMuted,

    /// <summary>提示认证失败。</summary>
    AuthFailed,

    /// <summary>弹布局失败对话框，给重试 / 手动布局 / 简化图三个选项。</summary>
    LayoutFailureDialog,
}

/// <summary>
/// 一次错误呈现：怎么呈现、给用户看什么。
/// </summary>
/// <param name="Kind">呈现方式。</param>
/// <param name="Message">给用户看的一句话。内部错误只给一句笼统的话，细节进日志。</param>
/// <param name="IsRetryable">换个版本或过一会儿重试可能成功。</param>
/// <param name="Detail">补充细节，例如冲突的标识。可为空。</param>
public sealed record ErrorPresentation(
    ErrorPresentationKind Kind,
    string Message,
    bool IsRetryable = false,
    string? Detail = null);

/// <summary>
/// 错误码到呈现方式的对照表。
/// </summary>
/// <remarks>
/// <para>
/// **呈现方式由这张表决定，不在各处就地判断。** 就地判断的结果是同一个错误码在两个入口
/// 给出两种呈现，而用户以为遇到的是两个问题。
/// </para>
/// <para>
/// 表**必须覆盖全部错误码**：漏一个就是一次没有反馈的失败——用户点了一下，什么都没发生，
/// 也没有一句话。漏项由一个测试挡住：拿错误码逐个查表，查不到就红。
/// </para>
/// </remarks>
public static class ErrorPresenterTable
{
    private static readonly IReadOnlyDictionary<string, ErrorPresentation> Table =
        new Dictionary<string, ErrorPresentation>(StringComparer.Ordinal)
        {
            [ErrorCodes.DuplicateId] = new(ErrorPresentationKind.HighlightTargets, "标识已存在"),
            [ErrorCodes.EdgeTargetMissing] = new(ErrorPresentationKind.PromptCreate, "终点不存在，是否创建？"),
            [ErrorCodes.EdgeSourceMissing] = new(ErrorPresentationKind.PromptCreate, "起点不存在，是否创建？"),
            [ErrorCodes.GroupMemberMissing] = new(ErrorPresentationKind.PromptCreate, "成员不存在，是否创建？"),
            [ErrorCodes.GroupCycle] = new(ErrorPresentationKind.HighlightTargets, "组合关系成环"),
            [ErrorCodes.VersionConflict] = new(ErrorPresentationKind.VersionDiffDialog, "版本冲突，先同步再重试", IsRetryable: true),
            [ErrorCodes.InvalidExpectedVersion] = new(ErrorPresentationKind.StatusBar, "内部错误，已记录日志"),
            [ErrorCodes.ExpectedVersionRequired] = new(ErrorPresentationKind.StatusBar, "缺少版本信息"),
            [ErrorCodes.LayoutAllLevelsTimeout] = new(ErrorPresentationKind.LayoutFailureDialog, "布局全部降级失败，画面保留上一次成功的结果"),
            [ErrorCodes.McpUnauthorized] = new(ErrorPresentationKind.AuthFailed, "认证失败"),
            [ErrorCodes.McpRateLimited] = new(ErrorPresentationKind.StatusBar, "请求过于频繁", IsRetryable: true),
            [ErrorCodes.InternalError] = new(ErrorPresentationKind.StatusBar, "内部错误，已记录日志"),

            // 命令级前置条件。这些没有专门的界面动作，一句话说清就够。
            [ErrorCodes.NodeMissing] = new(ErrorPresentationKind.StatusBar, "节点不存在"),
            [ErrorCodes.EdgeMissing] = new(ErrorPresentationKind.StatusBar, "边不存在"),
            [ErrorCodes.InvalidId] = new(ErrorPresentationKind.StatusBar, "标识为空"),
            [ErrorCodes.FieldUnknown] = new(ErrorPresentationKind.StatusBar, "字段名认不出"),
            [ErrorCodes.FieldValueInvalid] = new(ErrorPresentationKind.StatusBar, "字段值不合法"),

            // 整体校验产生的码。处置是"把出问题的元素指出来"。
            [ErrorCodes.EdgePortMissing] = new(ErrorPresentationKind.HighlightTargets, "端口不存在"),
            [ErrorCodes.EdgePortOnComposite] = new(ErrorPresentationKind.HighlightTargets, "组合没有端口"),
            [ErrorCodes.LayoutNodeMissing] = new(ErrorPresentationKind.HighlightTargets, "约束引用的节点不存在"),
            [ErrorCodes.LayoutOrderEdgeMissing] = new(ErrorPresentationKind.HighlightTargets, "次序引用的边不存在"),
            [ErrorCodes.MembershipMismatch] = new(ErrorPresentationKind.HighlightTargets, "成员与父级互相矛盾"),
            [ErrorCodes.ParentMissing] = new(ErrorPresentationKind.PromptCreate, "父级不存在，是否创建？"),
            [ErrorCodes.TagMemberMissing] = new(ErrorPresentationKind.HighlightTargets, "标签成员不存在"),
            [ErrorCodes.ActionTargetMissing] = new(ErrorPresentationKind.HighlightTargets, "动作目标不存在"),
            [ErrorCodes.LayoutConstraintInvalid] = new(ErrorPresentationKind.StatusBar, "这条布局约束不成立"),
            [ErrorCodes.LayoutConstraintMissing] = new(ErrorPresentationKind.StatusBar, "要删的布局约束已经不在了"),
            [ErrorCodes.PaletteEntryMissing] = new(ErrorPresentationKind.StatusBar, "调色板条目不存在"),

            // 删一个还被引用的调色板条目：要把引用它的元素指出来，
            // 用户才知道该先改哪些元素。只给一句话的话，他无从知道是谁在用这个令牌。
            [ErrorCodes.PaletteEntryInUse] = new(ErrorPresentationKind.HighlightTargets, "还有元素在用这个样式令牌"),

            [ErrorCodes.CompositeMissing] = new(ErrorPresentationKind.StatusBar, "这个组合已经不在了"),
            [ErrorCodes.CompositeTooDeep] = new(ErrorPresentationKind.StatusBar, "组合嵌套得太深了"),
            [ErrorCodes.LayerMissing] = new(ErrorPresentationKind.StatusBar, "这个图层已经不在了"),

            // 只读那一份。它不是失败，是"这件事在这份文档上做不了"，
            // 所以走灰显那一档：不弹窗，也不报成红的。
            [ErrorCodes.DocumentReadOnly] = new(ErrorPresentationKind.StatusBarMuted, "这份文档是只读的"),
        };

    /// <summary>全部登记过的呈现方式，供门禁逐个核对。</summary>
    public static IReadOnlyDictionary<string, ErrorPresentation> All => Table;

    /// <summary>查一个错误码的呈现方式。认不出的码按"内部错误"处理。</summary>
    /// <remarks>
    /// 认不出时不抛异常也不静默：退回一句笼统的话，至少让用户知道"出事了，已记录"。
    /// 真正的兜底是那条覆盖全表的门禁——到了线上还能走到这里，说明码是别处新加的。
    /// </remarks>
    public static ErrorPresentation For(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        return Table.TryGetValue(code, out var presentation)
            ? presentation
            : new ErrorPresentation(ErrorPresentationKind.StatusBar, "内部错误，已记录日志");
    }
}
