using DuetDiagram.Core.Model;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 核心程序集的依赖边界。
/// </summary>
/// <remarks>
/// <para>
/// 这个边界一旦被打破，重新拆分的成本极高，所以在两个层面各钉一道：
/// 项目文件层（有没有声明引用）和程序集元数据层（运行时实际引用了哪些程序集）。
/// 只看项目文件是不够的——传递依赖不会出现在项目文件里。
/// </para>
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
            "核心程序集只允许引用基础类库，多出一个第三方程序集就意味着它不再能独立复用");
    }

    [Fact]
    [Trait("Category", "CorePurity")]
    public void Core_does_not_reference_any_other_duetdiagram_assembly()
    {
        var referenced = typeof(DiagramDocument).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        // 反向依赖意味着分层被打破，而且会形成循环，将来无法单独编译或复用核心层。
        referenced.Should().NotContain(name => name.StartsWith("DuetDiagram.", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "CorePurity")]
    public void Core_project_file_declares_no_references()
    {
        var projectFile = FindCoreProjectFile();

        projectFile.Should().NotBeNull("测试必须能从仓库根目录找到核心项目文件");

        var content = File.ReadAllText(projectFile!);

        content.Should().NotContain("<PackageReference");
        content.Should().NotContain("<ProjectReference");
    }

    /// <summary>从测试程序集所在目录逐级上溯，找到核心项目文件。</summary>
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
