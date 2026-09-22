using System.Reflection;
using System.Text.Json.Nodes;
using DuetDiagram.Core.Commands;
using DuetDiagram.Llm.Loop;
using DuetDiagram.Llm.Tools;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Llm.Tests;

/// <summary>
/// 错误码到修复建议的映射表。
/// </summary>
/// <remarks>
/// <para>
/// **映射表放在一处。** 散在各处的话，同一个错误码会在两个入口给模型两种说法，
/// 而调用方按哪一条都可能是错的。这一层盯的是表本身完不完整、以及它有没有与文档对上。
/// </para>
/// <para>
/// 表要覆盖**两套**错误码：命令层的前置检查与工具层的参数校验。回灌给模型的失败
/// 两条来源都会出现，漏一套就是半张表——模型在另一半失败上只能靠猜。
/// </para>
/// <para>
/// 文档里的那张表也要对得上。外部代理是按文档写代码的，两张表分头维护的话，
/// 它照着文档处理，实际行为却是另一套。
/// </para>
/// </remarks>
public sealed class RepairHintTests
{
    #region 表本身

    /// <summary>两套错误码都查得到修复建议。</summary>
    [Fact]
    [Trait("Category", "RepairHint")]
    public void Every_error_code_has_a_repair_hint()
    {
        var missing = Codes()
            .Where(code => !RepairHints.All.ContainsKey(code))
            .ToList();

        missing.Should().BeEmpty(
            "查不到修复建议，模型就只能把同一个调用原样再发一遍，而每一次重试都是一轮往返");
    }

    /// <summary>表里没有多余的行。</summary>
    /// <remarks>
    /// 多出来的一行多半是拼错了码。拼错的码永远不会被查到，而它看起来"已经处理过了"，
    /// 于是真正那个码仍然漏着，却没人再去看。
    /// </remarks>
    [Fact]
    [Trait("Category", "RepairHint")]
    public void The_table_carries_no_code_that_is_not_an_error_code()
    {
        var known = Codes().ToHashSet(StringComparer.Ordinal);
        var extra = RepairHints.All.Keys.Where(code => !known.Contains(code)).ToList();

        extra.Should().BeEmpty("表里的键必须都是错误码常量，多出来的多半是拼错了");
    }

    /// <summary>每条建议都是一句能照着做的事，不是一句「参数非法」。</summary>
    /// <remarks>
    /// 判据取的是长度与结尾：一句话要说得清下一步，总得有几个字；而以句号收尾
    /// 是这一层写建议的体例，混进一句没写完的话在这里就会露出来。
    /// 真正拦住"不可执行的建议"的是写表的人，这条只挡明显没写完的。
    /// </remarks>
    [Fact]
    [Trait("Category", "RepairHint")]
    public void Every_suggestion_says_what_to_do_next()
    {
        var weak = RepairHints.All.Values
            .Where(hint => hint.Suggestion.Length < 12 || !hint.Suggestion.EndsWith('。'))
            .Select(hint => $"{hint.Code}：{hint.Suggestion}")
            .ToList();

        weak.Should().BeEmpty("「参数非法」不是建议，「换一个没被占用的标识」才是");
    }

    /// <summary>建议里点名的参数必须真的是某个工具的参数。</summary>
    /// <remarks>
    /// 点错参数名的后果是模型去改一个根本不相干的字段，而它改完之后拿到的还是同一个错误——
    /// 这一条把那种错挡在表里，而不是等模型撞了两轮之后才发现。
    /// </remarks>
    [Fact]
    [Trait("Category", "RepairHint")]
    public void Every_named_parameter_is_a_real_tool_parameter()
    {
        var declared = DeclaredParameters();

        var unknown = RepairHints.All.Values
            .Where(hint => hint.Parameter is not null)
            .Where(hint => !declared.Contains(hint.Parameter!))
            .Select(hint => $"{hint.Code} 指向 {hint.Parameter}")
            .ToList();

        unknown.Should().BeEmpty("建议里点名的参数必须真的是某个工具的参数");
    }

    /// <summary>认不出的码退回一句通用的，既不抛也不给空字符串。</summary>
    [Fact]
    [Trait("Category", "RepairHint")]
    public void An_unknown_code_falls_back_instead_of_returning_nothing()
    {
        var hint = RepairHints.For("SOMETHING_NEW");

        hint.Code.Should().Be("SOMETHING_NEW");
        hint.Suggestion.Should().NotBeEmpty("给一句空建议，模型多半会把同一个调用原样再发一遍");
    }

    #endregion

    #region 与文档对上

    /// <summary>文档里的那张表与代码里的表逐行一致。</summary>
    [Fact]
    [Trait("Category", "RepairHint")]
    public void The_documented_hints_match_the_table()
    {
        var documented = Documented();

        documented.Keys.Should().BeEquivalentTo(
            RepairHints.All.Keys,
            "文档与代码两张表必须覆盖同一批错误码");

        foreach (var (code, (parameter, suggestion)) in documented)
        {
            var hint = RepairHints.All[code];

            hint.Parameter.Should().Be(parameter, $"{code} 的出错参数在文档与代码里必须是同一个");
            hint.Suggestion.Should().Be(suggestion, $"{code} 的修复建议在文档与代码里必须是同一句");
        }
    }

    #endregion

    #region 取数

    /// <summary>两套错误码清单。反射一遍，加一个常量就自动进这一批。</summary>
    private static IEnumerable<string> Codes() =>
    [
        .. Constants(typeof(ErrorCodes)),
        .. Constants(typeof(ToolErrorCodes)),
    ];

    private static IEnumerable<string> Constants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

    /// <summary>八个工具的参数名合起来那一份。</summary>
    private static HashSet<string> DeclaredParameters()
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tool in Harness.Registry().Tools)
        {
            if (JsonNode.Parse(tool.Parameters.GetRawText()) is not JsonObject schema
                || schema["properties"] is not JsonObject properties)
            {
                continue;
            }

            foreach (var (name, _) in properties)
            {
                declared.Add(name);
            }
        }

        return declared;
    }

    /// <summary>
    /// 从错误码文档里读回那张修复建议表。
    /// </summary>
    /// <remarks>
    /// 按小节读而不是全文扫：这张表有一列是自由文本，靠"两格都是反引号标识"那种判据
    /// 分不出它和别的表——别处那几张表也有三列，会把它们的正文读成建议。
    /// </remarks>
    private static Dictionary<string, (string? Parameter, string Suggestion)> Documented()
    {
        var path = Path.Combine(Harness.RepositoryRoot(), "docs", "Error-Codes.md");
        var known = Codes().ToHashSet(StringComparer.Ordinal);
        var found = new Dictionary<string, (string?, string)>(StringComparer.Ordinal);
        var inSection = false;

        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inSection = line.Contains("修复建议", StringComparison.Ordinal);
                continue;
            }

            if (!inSection)
            {
                continue;
            }

            var cells = line.Split('|', StringSplitOptions.TrimEntries);

            if (cells.Length != 5 || !Unwrap(cells[1], out var code) || !known.Contains(code))
            {
                continue;
            }

            found[code] = (Unwrap(cells[2], out var parameter) ? parameter : null, cells[3]);
        }

        return found;
    }

    /// <summary>反引号包起来的短标识。那一格写的是短横线时不认，表示这一列没有值。</summary>
    private static bool Unwrap(string cell, out string value)
    {
        value = string.Empty;

        if (cell.Length < 3 || cell[0] != '`' || cell[^1] != '`')
        {
            return false;
        }

        var inner = cell[1..^1];

        if (inner.Length == 0 || !inner.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            return false;
        }

        value = inner;
        return true;
    }

    #endregion
}
