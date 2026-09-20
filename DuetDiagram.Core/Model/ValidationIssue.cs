using DuetDiagram.Core.Commands;

namespace DuetDiagram.Core.Model;

/// <summary>
/// 一条校验问题。
/// </summary>
/// <remarks>
/// <para>
/// 比 <see cref="CommandError"/> 多了两样东西：<see cref="RelatedId"/> 指出该去图上找谁，
/// <see cref="Suggestion"/> 给出可以怎么修。
/// </para>
/// <para>
/// 之所以不直接复用 <see cref="CommandError"/>：那是传给外部代理的线上形状，
/// 只需要码与载荷；而校验结果先要给人看、给界面用来高亮，
/// 光有一个码不足以让人知道该动哪里。两者分工不同，因此保留两个类型，
/// 并提供 <see cref="ToCommandError"/> 做单向转换——线上形状是窄的，内部形状是宽的。
/// </para>
/// <para>
/// <see cref="Suggestion"/> 目前是一句给人看的话，不是可直接执行的修补指令。
/// 做成机器可执行的形态要等校验工具对外暴露时再定，现在猜一个结构，
/// 很可能与那时真正需要的形状对不上。
/// </para>
/// </remarks>
public sealed record ValidationIssue
{
    /// <summary>错误码。取自 <see cref="ErrorCodes"/>，不要在这里写裸字符串。</summary>
    public required string Code { get; init; }

    /// <summary>给人看的说明。</summary>
    public required string Message { get; init; }

    /// <summary>相关的定义标识。界面用它定位到具体元素。</summary>
    public string? RelatedId { get; init; }

    /// <summary>可以怎么修。每条问题都应当给出，只说错不说怎么改等于把问题丢回给用户。</summary>
    public string? Suggestion { get; init; }

    /// <summary>转成线上形状。载荷取相关的标识，便于外部按标识定位。</summary>
    public CommandError ToCommandError() => CommandError.Of(Code, RelatedId);
}
