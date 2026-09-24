namespace DuetDiagram.Core.Model;

/// <summary>
/// 一类没能写进导出产物的东西。
/// </summary>
/// <remarks>
/// <para>
/// **它放在 Core 是因为两个导出器不在同一个程序集里。** Mermaid 导出只引用 Core，
/// 渲染层（SVG 那一档）也只引用 Core，两边要报的是同一件事：这次导出丢了什么。
/// 各自定义一份的话，"丢失清单"就有了两种形状，而调用方要按来源分两路去读它。
/// </para>
/// <para>
/// **按类收拢而不是逐个元素一条。** 一次导出里同一类丢失往往涉及几十个元素，
/// 逐条列出来之后报告会长到没人看，而"丢了端口，涉及 A、B、C"一眼就够。
/// </para>
/// </remarks>
/// <param name="Feature">是什么，例如"端口""布局约束"。</param>
/// <param name="Ids">涉及哪些元素。整份文档级的属性为空。</param>
/// <param name="Reason">为什么写不出来。</param>
public sealed record DroppedFeature(string Feature, IReadOnlyList<string> Ids, string Reason);
