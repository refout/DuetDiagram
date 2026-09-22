using DuetDiagram.Core.Commands;
using DuetDiagram.Llm.Tools;

namespace DuetDiagram.Llm.Loop;

/// <summary>
/// 回灌给模型的一条结构化错误。
/// </summary>
/// <remarks>
/// <para>
/// 它是 <see cref="ToolError"/> 加上修复建议与回环状态之后的形状。分开两个类型是因为
/// 两者出现在不同的时刻：<see cref="ToolError"/> 是执行体在失败那一刻造出来的，
/// 只有它自己知道出错参数与可用取值；信封是回环准备把这一次失败交给模型时造的，
/// 它才知道这是第几次、上一步是不是同一个错。
/// </para>
/// <para>
/// **回灌的不是异常栈。** 异常类型名只写应用日志：它对模型没有信息量，
/// 还会把内部实现暴露给外部代理。
/// </para>
/// <para>
/// <see cref="Expected"/> 取自错误自己填的那一份，取不到才回落到修复线索里的。
/// 顺序不能反过来：可用取值随文档与工具表变，调用点填的才是当下真的可用的那些。
/// </para>
/// </remarks>
public sealed record ErrorEnvelope
{
    /// <summary>错误码。取自命令层或工具层那两套常量。</summary>
    public required string Code { get; init; }

    /// <summary>出错的参数名。失败不落在某个参数上时为空。</summary>
    public string? Parameter { get; init; }

    /// <summary>期望的形式或可用取值。写不出固定的形式时为空。</summary>
    public string? Expected { get; init; }

    /// <summary>说明哪里错了，带上出错的值。</summary>
    public required string Message { get; init; }

    /// <summary>一句可执行的修复建议。</summary>
    public required string Suggestion { get; init; }

    /// <summary>这是这一轮里的第几次失败，从 1 开始。</summary>
    public int Attempt { get; init; }

    /// <summary>上一次失败里出现过同一个错误码。</summary>
    public bool Repeated { get; init; }

    /// <summary>
    /// 上一次也撞在同一个错误上时给的一句提醒，否则为空。
    /// </summary>
    /// <remarks>
    /// 不提醒的话，模型会原样重试——这一条是回环里最容易缺的，而缺了之后的表现是
    /// 「模型卡在一个错误上反复撞」，从日志上看只是同一条错误码重复了很多次。
    /// </remarks>
    public string? Note { get; init; }

    /// <summary>
    /// 把一条工具层的错误翻成信封。
    /// </summary>
    /// <remarks>
    /// 出错参数与期望形式都优先取错误自己填的那一份：执行体在失败现场，
    /// 它知道这一次具体是哪个参数、可用取值是哪些。修复线索只补它没填的那一半。
    /// </remarks>
    /// <param name="error">执行体报出来的那条错误。</param>
    /// <param name="attempt">这是这一轮里的第几次失败。</param>
    /// <param name="repeated">上一次失败里有没有同一个错误码。</param>
    public static ErrorEnvelope From(ToolError error, int attempt, bool repeated)
    {
        ArgumentNullException.ThrowIfNull(error);

        var hint = RepairHints.For(error.Code);

        return new ErrorEnvelope
        {
            Code = error.Code,
            Parameter = error.Parameter ?? hint.Parameter,
            Expected = error.Expected,
            Message = error.Message,
            Suggestion = hint.Suggestion,
            Attempt = attempt,
            Repeated = repeated,
            Note = repeated ? "上一次这么改也不行，别再原样重试。" : null,
        };
    }

    /// <summary>
    /// 一次失败里的全部错误各自翻成信封。
    /// </summary>
    /// <remarks>
    /// 一次调用可能同时报出好几个错误（参数校验就是一次报全的）。摊平成一个信封的话，
    /// 模型只改得动其中一条，下一轮再收到另一条，来回好几轮。
    /// </remarks>
    /// <param name="failure">失败的那一次调用结果。</param>
    /// <param name="attempt">这是这一轮里的第几次失败。</param>
    /// <param name="previousCodes">上一次失败里出现过的错误码。</param>
    public static IReadOnlyList<ErrorEnvelope> AllOf(
        ToolResult failure,
        int attempt,
        IReadOnlySet<string> previousCodes)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(previousCodes);

        // 一条错误都没有的失败等于没告诉模型任何可行动的东西。命令层拒了却给不出码时
        // 按内部错误算——与执行体那一路翻译被拒命令时的口径一致。
        var errors = failure.Errors.Length > 0
            ? failure.Errors
            : [ToolError.Of(ErrorCodes.InternalError, failure.Message ?? "调用失败，但没有给出错误码")];

        return [.. errors.Select(error => From(error, attempt, previousCodes.Contains(error.Code)))];
    }
}
