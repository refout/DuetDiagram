using DuetDiagram.Core.Model;
using DuetDiagram.Core.Templates;
using DuetDiagram.Mermaid.Import;
using DuetDiagram.Mermaid.Parsing;

namespace DuetDiagram.App.Services;

/// <summary>
/// 一次导入的结果：要么是一份能拼进文档的片段，要么是一句拒绝的理由。
/// </summary>
/// <param name="File">文件名，不含路径。面板上摆不下一条全路径。</param>
/// <param name="Fragment">导出来的片段。导不进来时为空。</param>
/// <param name="Refusal">为什么导不进来。导得进来时为空。</param>
/// <param name="Notes">
/// 要摆到界面上给用户看的那些话：解析诊断、有意的取舍、方向不一致。
/// 它可能是空的——一份干净的内容导进来没有任何要交代的。
/// </param>
/// <remarks>
/// 片段与拒绝理由合成一条记录而不是两个返回值：两者**互斥**，
/// 分开给的话调用方要自己判断"两个都空"和"两个都有"这两种不该出现的组合该怎么办。
/// </remarks>
public sealed record ImportOutcome(
    string File,
    TemplateDocument? Fragment,
    string? Refusal,
    IReadOnlyList<string> Notes)
{
    /// <summary>导得进来吗。</summary>
    public bool Succeeded => Refusal is null;
}

/// <summary>
/// 读一份文件，把它导成一份可以拼进当前文档的片段。
/// </summary>
/// <remarks>
/// <para>
/// **格式由内容判，不按扩展名。** 后缀只用来给这次导入起个名字；
/// 认不认得出来是解析器看第一行认的。按后缀判的话，一份 <c>.txt</c> 里的 Mermaid
/// 导不进来，而用户会以为程序不认识 Mermaid。
/// </para>
/// <para>
/// **它只读文件、只做映射，不碰文档。** 拼进去是命令层的事，
/// 而"读文件"与"改文档"混在一起的话，一次读失败会留下半份内容，
/// 撤销也没法做——导入之后最常见的第一个动作就是"导错了想退回去"。
/// </para>
/// <para>
/// **认不出的内容不抛异常，记成要摆在界面上的话。** 宽松模式下整份判死会让用户
/// 手里一份九成正确的图变成零；而认不出的地方一句都不说，用户拿到的是一张
/// 少了几条边的图却不知道少了什么。
/// </para>
/// <para>
/// **方向跟着文档走，不跟着内容走。** 导进来的是「片段」，不是整份文档，
/// 片段里没有方向这个字段。两者不一致时这里留一句话说清——
/// 不说的话，用户看到的是"我明明写的是从左到右，导进来怎么是竖的"。
/// </para>
/// </remarks>
public static class ImportService
{
    /// <summary>
    /// 读一份文件，导成片段。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <param name="target">目标文档现在的方向。只用来判断要不要留一句"方向不一致"。</param>
    public static ImportOutcome Read(string path, Direction target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var file = Path.GetFileName(path);

        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ImportOutcome(file, null, $"读不出这份文件：{exception.Message}", []);
        }

        // 解析与映射两步分开调，与语料那批用例走的是同一条路：
        // 这里另起一个"一步到位"的入口的话，两处的行为迟早会分叉，
        // 而分叉之后语料全绿、真实文件照样导不进来。
        var chart = MermaidParser.Parse(text);

        var result = MermaidImporter.Import(chart, new ImportOptions
        {
            // 片段不是一份文档，这个标识落不到任何地方；给文件名是为了让日志与
            // 说明里点得出这次导的是什么。它不参与查重——重名由命令层消解。
            DocumentId = Path.GetFileNameWithoutExtension(path),
        });

        var fragment = new TemplateDocument(
            file,
            result.Document.Nodes,
            result.Document.Edges,
            result.Document.Composites);

        var notes = Notes(chart, result.Report, target, importable: fragment.ElementCount > 0);

        // 解析通了但一条元素都没有：一份只有一行 `flowchart LR` 的文件就是这样，
        // 一张时序图也是这样。拒绝而不是放一份空片段过去——
        // 放过去的表现是"点了导入什么都没发生"，而用户以为是自己点错了。
        return fragment.ElementCount == 0
            ? new ImportOutcome(file, null, "这份内容里没有能导入的元素", notes)
            : new ImportOutcome(file, fragment, null, notes);
    }

    /// <summary>
    /// 要摆在界面上给用户看的话，按"先说问题、后说取舍"排。
    /// </summary>
    /// <remarks>
    /// 三类都留着出处：诊断是"解析没看懂的地方"，被丢掉的节点与没映射的样式是
    /// **有意的取舍**——不是错误，但都会让图与原文对不上。样式丢掉尤其隐蔽：
    /// 图还是画得出来，只是少了一处颜色，而"少了一处"在图上几乎看不出来。
    /// </remarks>
    private static IReadOnlyList<string> Notes(
        MermaidFlowchart chart,
        MermaidImportReport report,
        Direction target,
        bool importable)
    {
        var notes = new List<string>();

        foreach (var diagnostic in report.Diagnostics)
        {
            notes.Add($"第 {diagnostic.Line} 行第 {diagnostic.Column} 列：{diagnostic.Message}");
        }

        foreach (var dropped in report.DroppedNodes)
        {
            notes.Add($"{dropped.Id} 没有当成节点：它同时是子图 {dropped.SubgraphId} 的名字，按子图处理了。");
        }

        foreach (var ignored in report.IgnoredStyleProperties)
        {
            notes.Add($"{ignored.Target} 上的 {ignored.Property} 没映射进来：{ignored.Reason}");
        }

        // 一条元素都没导进来时不说方向：那句话会被当成"导进来了但方向不对"，
        // 而实际上什么都没导进来。
        if (importable && chart.Direction != target)
        {
            notes.Add(
                $"这份内容写的是{Word(chart.Direction)}，导入不改文档的方向——文档现在是{Word(target)}。"
                + "要换方向用「布局」那一档。");
        }

        return notes;
    }

    /// <summary>方向在界面上叫什么。</summary>
    private static string Word(Direction direction) => direction switch
    {
        Direction.LR => "从左到右",
        Direction.TB => "从上到下",
        Direction.RL => "从右到左",
        Direction.BT => "从下到上",
        _ => direction.ToString(),
    };
}
