using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 代码注释必须自解释，不许把读者指到别处。
/// </summary>
/// <remarks>
/// <para>
/// 注释里出现文档路径、那份规格文档的简称、或任务编号，都把理解成本推给了读者；
/// 而且那些东西一旦改名或调整编号，注释就变成误导。本项目是一次真实撞上的：
/// 文档里六个错误码名与代码对不上，而没有任何东西会报错。
/// </para>
/// <para>
/// **只看注释行，不看代码行。** 工具往生成报告里写的正文、路径常量、报错文案里的引用都是正当的，
/// 它们本身就是给人看的文字。判据是"这一行的头两个字符是不是注释起始符"，
/// 所以这类字符串字面量不会被误伤。
/// </para>
/// <para>
/// 本文件的注释刻意不写出那几个被禁的片段——否则门禁先把自己判红。
/// </para>
/// </remarks>
public sealed class CommentDisciplineTests
{
    /// <summary>被禁的片段：文档目录、产物目录、契约文件名。</summary>
    private static readonly string[] Forbidden =
    [
        "docs/",
        "reports/",
        "AGENTS.md",
    ];

    /// <summary>任务编号，形态是字母加数字、连字符、数字，例如阶段一的第十七号。</summary>
    private static readonly Regex TaskId = new(@"\bP\d+-\d+\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>那份规格文档在注释里的简称。</summary>
    private const string PlanShorthand = "方案";

    /// <summary>「解决」两字。它后面接上简称就变成一个正经的技术词，不该误伤。</summary>
    private const string SolutionPrefix = "解决";

    private static readonly string[] SkippedDirectories = ["bin", "obj", ".git", "node_modules"];

    [Fact]
    [Trait("Category", "CommentDiscipline")]
    public void Comments_do_not_point_at_documents_or_task_numbers()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();

        foreach (var file in Enumerate(root))
        {
            var lines = File.ReadAllLines(file).Select(line => line.TrimStart()).ToArray();

            for (var index = 0; index < lines.Length; index++)
            {
                if (!IsComment(file, lines[index]))
                {
                    continue;
                }

                if (Forbidden.Any(fragment => lines[index].Contains(fragment, StringComparison.Ordinal))
                    || NamesThePlan(lines[index])
                    || TaskId.IsMatch(lines[index]))
                {
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{index + 1}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "代码注释要直接说清实现，不许写成指路；Markdown 与任务 YAML 不受此限");
    }

    /// <summary>
    /// 这一行有没有拿那份规格文档当指代。
    /// </summary>
    /// <remarks>
    /// 逐处判断而不是简单地找子串：「解决」加简称是描述解决方案文件的正经技术词，
    /// 在这个仓库里到处都是，一刀切会把它们全判红。
    /// </remarks>
    private static bool NamesThePlan(string line)
    {
        var index = line.IndexOf(PlanShorthand, StringComparison.Ordinal);

        while (index >= 0)
        {
            var isTechnicalTerm = index >= SolutionPrefix.Length
                && line.AsSpan(index - SolutionPrefix.Length, SolutionPrefix.Length)
                    .SequenceEqual(SolutionPrefix.AsSpan());

            if (!isTechnicalTerm)
            {
                return true;
            }

            index = line.IndexOf(PlanShorthand, index + PlanShorthand.Length, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>注释行的判据。两种文件两种注释起始符。</summary>
    private static bool IsComment(string file, string trimmed) =>
        Path.GetExtension(file) switch
        {
            ".cs" => trimmed.StartsWith("//", StringComparison.Ordinal),
            ".csproj" or ".props" => trimmed.StartsWith("<!--", StringComparison.Ordinal),
            _ => false,
        };

    private static IEnumerable<string> Enumerate(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(child), StringComparer.Ordinal))
                {
                    pending.Push(child);
                }
            }

            foreach (var extension in (string[])[".cs", ".csproj", ".props"])
            {
                foreach (var file in Directory.EnumerateFiles(directory, $"*{extension}"))
                {
                    yield return file;
                }
            }
        }
    }

    /// <summary>从测试程序集的位置逐级上溯，找到含解决方案文件的目录。</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DuetDiagram.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("从测试程序集的位置找不到仓库根。");
    }
}
