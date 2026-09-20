using DuetDiagram.Core.Model;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// P1 判据 #1：DuetDiagram.Core 仅依赖 BCL。
/// </summary>
/// <remarks>
/// 这条约束是 DAG 的地基 —— 它是 <c>DuetDiagram.AotSmokeTest</c> 能只引用 Core、
/// 以及 Core 能被任何宿主（GUI / CLI / 测试）无副作用引用的前提。
/// 一旦被打破，重新拆包的成本极高，所以用测试钉住。
/// </remarks>
public sealed class CorePurityTests
{
    [Fact]
    [Trait("Category", "CorePurity")]
    public void Core_references_only_the_base_class_library()
    {
        var referenced = typeof(DiagramDocument).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToArray();

        referenced.Should().NotBeEmpty();

        referenced.Should().OnlyContain(
            name => name.StartsWith("System", StringComparison.Ordinal)
                || name == "netstandard"
                || name == "mscorlib",
            "Core 只允许引用 BCL");
    }

    [Fact]
    [Trait("Category", "CorePurity")]
    public void Core_does_not_reference_any_other_duetdiagram_assembly()
    {
        var referenced = typeof(DiagramDocument).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        referenced.Should().NotContain(name => name.StartsWith("DuetDiagram.", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "CorePurity")]
    public void Core_project_file_declares_no_references()
    {
        var projectFile = FindCoreProjectFile();

        projectFile.Should().NotBeNull("测试必须能从仓库根目录找到 DuetDiagram.Core.csproj");

        var content = File.ReadAllText(projectFile!);

        content.Should().NotContain("<PackageReference");
        content.Should().NotContain("<ProjectReference");
    }

    private static string? FindCoreProjectFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "DuetDiagram.Core", "DuetDiagram.Core.csproj");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
