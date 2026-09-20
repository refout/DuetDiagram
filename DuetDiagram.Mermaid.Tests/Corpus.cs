using System.Text.Json;
using DuetDiagram.Mermaid.Lexing;

namespace DuetDiagram.Mermaid.Tests;

/// <summary>冻结语料里的一条 Mermaid 回答。</summary>
internal sealed record CorpusEntry(string Arm, string PromptId, string Content);

/// <summary>
/// 读取冻结的 Mermaid 语料。
/// </summary>
/// <remarks>
/// <para>
/// 语料是 Phase 0a 冻结的一百份模型响应（两个 Mermaid 组各五十条）。
/// 它比"官方示例"更适合当验收基准：官方示例是手写的，干净得不真实，
/// 而这一百份是模型**在真实提示词下**产出的，正是解析器必须能吃下的东西。
/// </para>
/// <para>
/// 语料走 LFS。没拉取时文件里是指针文本而不是内容，那种情况下**必须响亮地失败**，
/// 而不是静默跳过——静默跳过会让这个测试在别人机器上"通过"，而它什么都没验。
/// </para>
/// </remarks>
internal static class Corpus
{
    private const string PointerMarker = "version https://git-lfs";

    /// <summary>语料目录。相对测试程序集向上找到仓库根。</summary>
    public static string Root => Path.Combine(RepositoryRoot(), "reports", "raw");

    public static IReadOnlyList<CorpusEntry> Load()
    {
        var root = Root;

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"找不到语料目录 {root}。它是冻结的对比测试语料，见 reports/raw/README.md。");
        }

        var entries = new List<CorpusEntry>();

        foreach (var arm in new[] { "a-bare", "b-documented" })
        {
            var directory = Path.Combine(root, arm);

            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                var text = File.ReadAllText(file);

                if (text.StartsWith(PointerMarker, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"语料文件 {file} 是 LFS 指针而不是内容。先运行 git lfs pull。"
                        + "这个测试必须跑在真实语料上，跳过等于什么都没验。");
                }

                using var document = JsonDocument.Parse(text);

                entries.Add(new CorpusEntry(
                    arm,
                    Path.GetFileNameWithoutExtension(file),
                    document.RootElement.GetProperty("content").GetString() ?? string.Empty));
            }
        }

        return entries;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DuetDiagram.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("从测试程序集的位置找不到仓库根（含 DuetDiagram.slnx 的目录）。");
    }
}
