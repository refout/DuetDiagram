using System.Reflection;
using System.Text.RegularExpressions;
using DuetDiagram.Core.Commands;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 命令清单与实际注册的命令必须逐条对得上。
/// </summary>
/// <remarks>
/// <para>
/// 那张表是**外部代理照着自己那份文档写代码时唯一的依据**。表里多一条实际不存在的命令，
/// 代理就会去调一个永远返回"命令不存在"的东西；表里少一条，那条命令对代理来说根本不存在，
/// 而它本来能做那件事。两种错都不会让任何测试变红——除非有人专门比一遍。
/// </para>
/// <para>
/// 比的是**标识与类名两列**，不是只比标识。类名那一列同样会过期：改了类名而没改文档，
/// 读文档的人按旧名字去搜代码，搜不到。
/// </para>
/// <para>
/// 清单里另外几张表（计划中的那些）不参与比对——它们是目标不是现状，写法本来就不同，
/// 而且已经明确标注了不准。
/// </para>
/// </remarks>
public sealed class CommandListTests
{
    /// <summary>表格单元格里被反引号包起来的标识，形态是小写字母开头、允许连字符与数字。</summary>
    private static readonly Regex BacktickedId =
        new(@"^`([a-z][a-z0-9-]*)`$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>表格单元格里被反引号包起来的类型名。</summary>
    private static readonly Regex BacktickedType =
        new(@"^`([A-Za-z_][A-Za-z0-9_]*)`$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>已实现那一节的标题行。只比这一节，后面几节是目标不是现状。</summary>
    private const string ImplementedHeading = "## 已实现";

    [Fact]
    [Trait("Category", "CommandGroups")]
    public void Every_registered_command_is_listed_in_the_command_table()
    {
        var rows = ReadImplementedTable();
        var registered = RegisteredCommands();

        var listedIds = rows.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        var unlisted = registered.Keys
            .Where(id => !listedIds.Contains(id))
            .Order(StringComparer.Ordinal)
            .ToArray();

        unlisted.Should().BeEmpty(
            "命令清单的「已实现」表要列出每一条真的存在的命令，否则外部代理不知道它能用");

        var phantom = listedIds
            .Where(id => !registered.ContainsKey(id))
            .Order(StringComparer.Ordinal)
            .ToArray();

        phantom.Should().BeEmpty(
            "清单里不能有实际不存在的命令，否则外部代理会去调一个永远失败的东西");
    }

    [Fact]
    [Trait("Category", "CommandGroups")]
    public void The_class_column_names_the_type_that_owns_that_command_id()
    {
        var rows = ReadImplementedTable();
        var registered = RegisteredCommands();

        foreach (var row in rows)
        {
            if (!registered.TryGetValue(row.Id, out var actual))
            {
                // 标识本身对不对由上面那条管，这里只管类名那一列。
                continue;
            }

            row.ClassName.Should().Be(
                actual.Name,
                $"清单里 `{row.Id}` 那一行写的类名要与真正实现它的类型一致");
        }
    }

    [Fact]
    [Trait("Category", "CommandGroups")]
    public void Command_ids_are_lowercase_hyphenated_and_unique()
    {
        var registered = RegisteredCommands();

        registered.Keys.Should().OnlyContain(
            id => id.Length > 0 && id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'));

        registered.Keys.Should().NotContain(id => id.StartsWith('-') || id.EndsWith('-'));

        // 标识重复的话，审计日志与版本日志里两条不同的命令看起来是同一条。
        var duplicates = typeof(DiagramCommandBase).Assembly.GetTypes()
            .Where(IsConcreteCommand)
            .Select(t => (Type: t, Id: ReadIdConstant(t)))
            .Where(pair => pair.Id is not null)
            .GroupBy(pair => pair.Id!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} 由 {string.Join("、", g.Select(p => p.Type.Name))} 共用")
            .ToArray();

        duplicates.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "CommandGroups")]
    public void Every_command_declares_a_readable_id_constant()
    {
        var missing = typeof(DiagramCommandBase).Assembly.GetTypes()
            .Where(IsConcreteCommand)
            .Where(t => ReadIdConstant(t) is null)
            .Select(t => t.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // 标识读不出来就没人能按标识调它。反射拿不到只可能是那个常量被改名或改成了别的形状。
        missing.Should().BeEmpty("每条命令都要有一个公开的字符串常量作为它的标识");
    }

    #region 反射

    /// <summary>枚举程序集里所有具体的命令类型，读出它们各自的标识。</summary>
    private static Dictionary<string, Type> RegisteredCommands()
    {
        var result = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var type in typeof(DiagramCommandBase).Assembly.GetTypes().Where(IsConcreteCommand))
        {
            var id = ReadIdConstant(type);

            if (id is not null)
            {
                result[id] = type;
            }
        }

        return result;
    }

    private static bool IsConcreteCommand(Type type) =>
        type.IsClass && !type.IsAbstract && typeof(DiagramCommandBase).IsAssignableFrom(type);

    /// <summary>
    /// 读一个命令类型上那个公开的字符串常量。
    /// </summary>
    /// <remarks>
    /// 用反射读而不是把标识硬编码进这份测试：硬编码的话，新加一条命令时这份测试不会红，
    /// 而它红不红正是这张表能不能被信任的全部依据。
    /// </remarks>
    private static string? ReadIdConstant(Type type)
    {
        var field = type.GetField(
            "Id",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        return field is { IsLiteral: true } && field.FieldType == typeof(string)
            ? field.GetRawConstantValue() as string
            : null;
    }

    #endregion

    #region 读表

    /// <summary>已实现那一节里的一行：命令标识与实现它的类名。</summary>
    private sealed record CommandRow(string Id, string ClassName);

    /// <summary>
    /// 只扫「已实现」那一节，逐行取出前两列。
    /// </summary>
    /// <remarks>
    /// 表头与分隔行取不出反引号包着的标识，自然被跳过。后面那几节是计划中的目标，
    /// 它们的写法（分组名放第一列、一条命令可能列在括号里）与这里要的形态不同，
    /// 所以到下一个二级标题就停。
    /// </remarks>
    private static List<CommandRow> ReadImplementedTable()
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "Command-List.md");
        File.Exists(path).Should().BeTrue($"命令清单应当在 {path}");

        var lines = File.ReadAllLines(path);
        var rows = new List<CommandRow>();
        var inside = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                inside = trimmed.StartsWith(ImplementedHeading, StringComparison.Ordinal);
                continue;
            }

            if (!inside || !trimmed.StartsWith('|'))
            {
                continue;
            }

            var cells = trimmed.Split('|');
            if (cells.Length < 3)
            {
                continue;
            }

            var id = BacktickedId.Match(cells[1].Trim());
            var className = BacktickedType.Match(cells[2].Trim());

            if (id.Success && className.Success)
            {
                rows.Add(new CommandRow(id.Groups[1].Value, className.Groups[1].Value));
            }
        }

        rows.Should().NotBeEmpty("「已实现」那一节至少要有一行，否则这张表已经不成形了");

        return rows;
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

    #endregion
}
