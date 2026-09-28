using DuetDiagram.Core.Model;
using DuetDiagram.Core.Sidecar;
using DuetDiagram.Dsl.Export;
using DuetDiagram.Dsl.Mapping;
using DuetDiagram.Dsl.Parsing;

namespace DuetDiagram.App.Services;

/// <summary>
/// 一份 DSL 绘图文件：读进来、写回去，以及把读的那一步做了什么说成人话。
/// </summary>
/// <remarks>
/// <para>
/// **DSL 文本也是一种绘图文件格式。** 它与 IR JSON 是同一种东西的两种文本形态：
/// 有路径、进跨进程所有权、保存写回打开的那一个文件。区别只在装得下的内容多寡——
/// 页、图层、标签、动作、字体、文本预设、画布设置这些 DSL 都表达不了，
/// 所以写出去时要把丢掉的逐类报出来。
/// </para>
/// <para>
/// **读进来之后一律整体校验。** 与 <see cref="DocumentFile"/> 同一口径：文件是外部输入，
/// 不经过命令层，而上一个进程还可能是写到一半被强杀的。校验只报告不修改，处置交给调用方。
/// </para>
/// <para>
/// **写盘先写临时文件再替换。** 与 <see cref="DocumentFile"/> 同一套原子替换：
/// 直接往目标文件上写的话，写到一半断电留下的是一份截断的内容，而用户手上没有第三份可以退回去。
/// </para>
/// </remarks>
internal static class DslFile
{
    /// <summary>写临时文件时用的后缀。它与目标文件同目录，替换才是同一卷内的改名。</summary>
    private const string TempSuffix = ".tmp";

    /// <summary>
    /// 读一份 DSL 绘图文件。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <param name="issues">整体校验发现的问题，按发现次序追加。</param>
    /// <returns>文档、文本里写的固定位置，以及这次映射做了什么。</returns>
    /// <exception cref="InvalidDataException">文件读不出来，或者内容不是一份 DSL。</exception>
    /// <remarks>
    /// <para>
    /// **布局意图的归属方是人。** 打开一份文件是人在界面上做的动作，所以
    /// <see cref="ConstraintOwner.Human"/>。给成模型的话，用户手工固定与手工写的约束
    /// 会被下一次自动重排冲掉——用户每次微调都会白做。
    /// </para>
    /// <para>
    /// **解析器对坏输入是宽松的，所以"这不是 DSL"要自己判。** 认不出的行记成诊断而不是抛异常，
    /// 于是任意文本都能解析出一棵（多半是空的）树。判据是「一条元素都没认出来，
    /// 而且解析器还报着看不懂」：只写了头部与方向的空文档是合法的，照常打开——
    /// 坏文件与空文档不是一回事。
    /// </para>
    /// </remarks>
    public static (DiagramDocument Document, IReadOnlyDictionary<string, Anchor> Pins, MappingReport Report) Read(
        string path,
        IList<ValidationIssue> issues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(issues);

        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"读不出这份文件：{exception.Message}", exception);
        }

        var parsed = DslParser.Parse(text);

        var mapped = DslMapper.Map(parsed, new MappingOptions
        {
            // 文本里没有文档标识（它描述的是图，不是文档），所以拿文件名当。
            DocumentId = Path.GetFileNameWithoutExtension(path),
            Owner = ConstraintOwner.Human,
        });

        var document = mapped.Document;

        if (document.Nodes.Count == 0
            && document.Edges.Count == 0
            && document.Composites.Count == 0
            && (string.IsNullOrWhiteSpace(text) || parsed.Diagnostics.Count > 0))
        {
            throw new InvalidDataException($"这份文件不是 DSL：{Why(parsed, text)}");
        }

        foreach (var issue in DiagramValidator.Validate(document))
        {
            issues.Add(issue);
        }

        return (document, mapped.Sidecar.PinnedNodes, mapped.Report);
    }

    /// <summary>
    /// 把一份文档写回 DSL 文本。
    /// </summary>
    /// <param name="path">目标文件路径。</param>
    /// <param name="document">要写的内容。</param>
    /// <param name="pins">固定位置。DSL 里没有绝对坐标，它随 <c>pin</c> 意图写进文本。</param>
    /// <returns>写出去的那份文本与它丢了什么。丢掉的东西调用方要如实说给用户。</returns>
    public static DslExportResult Write(
        string path,
        DiagramDocument document,
        IReadOnlyDictionary<string, Anchor> pins)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pins);

        var result = DslExporter.Export(document, new UserSidecar
        {
            DocumentId = document.Id,
            PinnedNodes = pins,
        });

        var temp = path + TempSuffix;

        File.WriteAllText(temp, result.Text);

        // 覆盖式改名。写成"先删目标再改名"的话，删掉之后、改名之前的那一瞬间，
        // 磁盘上两份都不在——而那一刻正好断电就是彻底的丢失。
        File.Move(temp, path, overwrite: true);

        return result;
    }

    /// <summary>
    /// 把一次映射做了什么说成界面上要摆的那些话。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 四份记录各说一句。诊断是"解析没看懂的地方"；改名、补出来的节点与没落地的布局意图
    /// 都是**正常结果**——不是错误，但都会让图与文本对不上。
    /// </para>
    /// <para>
    /// 改名尤其隐蔽：图上那个标识与文本里写的不是同一个，而"为什么对不上"
    /// 不查是看不出来的。没落地的布局意图同理——约束少了一项，图还是画得出来，
    /// 只是层内次序不是你写的那个。
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Notes(MappingReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var notes = new List<string>();

        foreach (var diagnostic in report.Diagnostics)
        {
            notes.Add($"第 {diagnostic.Line} 行第 {diagnostic.Column} 列：{diagnostic.Message}");
        }

        foreach (var rename in report.Renames)
        {
            notes.Add($"{rename.OriginalId} 改成了 {rename.NewId}：{rename.Reason}");
        }

        foreach (var created in report.CreatedNodes)
        {
            notes.Add($"{created.Id} 是补出来的节点：{created.Reason}");
        }

        foreach (var unresolved in report.UnresolvedIntents)
        {
            notes.Add($"{unresolved.Intent} 里的 {unresolved.Reference} 没能落地：{unresolved.Reason}");
        }

        return notes;
    }

    /// <summary>为什么这份内容不算一份 DSL。</summary>
    private static string Why(DslDocument parsed, string text)
    {
        if (parsed.Diagnostics.Count > 0)
        {
            var first = parsed.Diagnostics[0];

            return $"第 {first.Line} 行第 {first.Column} 列：{first.Message}";
        }

        return string.IsNullOrWhiteSpace(text) ? "文件是空的" : "没有认出一条元素";
    }
}
