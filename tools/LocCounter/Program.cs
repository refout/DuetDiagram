using System.Text;

namespace DuetDiagram.Tools.LocCounter;

/// <summary>
/// 统计仓库内 C# 代码行数并生成 reports/loc.md。
/// 方案 §12 要求 LOC 由脚本生成、CI 强制一致 —— 因此输出必须是字节稳定的
/// （不含时间戳、绝对路径、随机顺序）。
/// </summary>
internal static class Program
{
    private static readonly string[] ExcludedDirectories = ["bin", "obj", ".git", "artifacts", "node_modules"];

    private static int Main(string[] args)
    {
        var root = ResolveRepositoryRoot(args);
        var reportPath = Path.Combine(root, "reports", "loc.md");

        var files = EnumerateSourceFiles(root).ToArray();

        if (files.Length == 0)
        {
            Console.Error.WriteLine($"No C# files found under {root}");
            return 1;
        }

        var projects = files
            .GroupBy(f => ClassifyProject(root, f))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ProjectStats(g.Key, g.Select(CountFile).ToArray()))
            .ToArray();

        // 统一成 LF：报告必须跨平台字节一致，否则 --check 在 Windows/Linux 之间会互相打脸。
        var report = Render(projects).ReplaceLineEndings("\n");
        var check = args.Contains("--check", StringComparer.Ordinal);
        var previous = File.Exists(reportPath)
            ? File.ReadAllText(reportPath).ReplaceLineEndings("\n")
            : null;

        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Console.WriteLine(report);

        if (check && !string.Equals(previous, report, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("reports/loc.md is out of date. Run `dotnet run --project tools/LocCounter`.");
            return 1;
        }

        return 0;
    }

    private static string ResolveRepositoryRoot(string[] args)
    {
        var index = Array.IndexOf(args, "--root");

        if (index >= 0 && index + 1 < args.Length)
        {
            return Path.GetFullPath(args[index + 1]);
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DuetDiagram.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? Directory.GetCurrentDirectory();
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsExcluded(root, path))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal);

    private static bool IsExcluded(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Any(seg => ExcludedDirectories.Contains(seg, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 归属到顶层目录（DuetDiagram.Core.AotSmokeTest 这种多级项目名会被还原成项目目录名）。
    /// </summary>
    private static string ClassifyProject(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Length <= 1 ? "(root)" : segments[0];
    }

    private static FileStats CountFile(string path)
    {
        var total = 0;
        var code = 0;
        var comment = 0;
        var blank = 0;
        var inBlockComment = false;

        foreach (var raw in File.ReadLines(path))
        {
            total++;
            var line = raw.Trim();

            if (inBlockComment)
            {
                comment++;

                if (line.Contains("*/", StringComparison.Ordinal))
                {
                    inBlockComment = false;
                }

                continue;
            }

            if (line.Length == 0)
            {
                blank++;
            }
            else if (line.StartsWith("//", StringComparison.Ordinal))
            {
                comment++;
            }
            else if (line.StartsWith("/*", StringComparison.Ordinal))
            {
                comment++;

                if (!line.Contains("*/", StringComparison.Ordinal))
                {
                    inBlockComment = true;
                }
            }
            else
            {
                code++;
            }
        }

        return new FileStats(path, total, code, comment, blank);
    }

    private static string Render(IReadOnlyList<ProjectStats> projects)
    {
        var builder = new StringBuilder();

        builder.AppendLine("# LOC 统计");
        builder.AppendLine();
        builder.AppendLine("> 本文件由 `tools/LocCounter` 生成，请勿手工编辑。");
        builder.AppendLine("> CI 用 `dotnet run --project tools/LocCounter -- --check` 强制本文件与代码一致。");
        builder.AppendLine();
        builder.AppendLine("| 项目 | 文件 | 总行 | 代码 | 注释 | 空行 |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|");

        foreach (var project in projects)
        {
            builder.AppendLine(
                $"| {project.Name} | {project.Files.Length} | {project.Total} | {project.Code} | {project.Comment} | {project.Blank} |");
        }

        builder.AppendLine(
            $"| **合计** | **{projects.Sum(p => p.Files.Length)}** | **{projects.Sum(p => p.Total)}** | " +
            $"**{projects.Sum(p => p.Code)}** | **{projects.Sum(p => p.Comment)}** | **{projects.Sum(p => p.Blank)}** |");

        builder.AppendLine();
        builder.AppendLine("## 明细");
        builder.AppendLine();

        foreach (var project in projects)
        {
            builder.AppendLine($"### {project.Name}");
            builder.AppendLine();
            builder.AppendLine("| 文件 | 代码 |");
            builder.AppendLine("|---|---:|");

            foreach (var file in project.Files.OrderBy(f => f.Path, StringComparer.Ordinal))
            {
                builder.AppendLine($"| {Path.GetFileName(file.Path)} | {file.Code} |");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private sealed record FileStats(string Path, int Total, int Code, int Comment, int Blank);

    private sealed record ProjectStats(string Name, FileStats[] Files)
    {
        public int Total => Files.Sum(f => f.Total);

        public int Code => Files.Sum(f => f.Code);

        public int Comment => Files.Sum(f => f.Comment);

        public int Blank => Files.Sum(f => f.Blank);
    }
}
