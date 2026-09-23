using System.Text.RegularExpressions;
using DuetDiagram.Dsl.Parsing;
using DuetDiagram.Llm.Chat;
using DuetDiagram.Llm.Tools;
using DuetDiagram.Mcp.Skills;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DuetDiagram.Mcp.Tests;

/// <summary>
/// 渐进式披露的三层：哪一层放什么、以及「始终在上下文里」的那一层到底有多重。
/// </summary>
/// <remarks>
/// <para>
/// 三层各有各的代价。工具描述**始终在上下文里**，每一条都在为八成的场景付费，
/// 所以它只能是一句话；Skill 正文是任务匹配上才取的；精确语法是写到那一步才取的。
/// 边界一破，代价最大的是第一层——它会被撑到几百字，而且看不出来，因为它是"内容更全了"。
/// </para>
/// <para>
/// 这里判的是**形状**，不是文字好坏：单行、长度上限、没有语法记号、正文段落没被抄进
/// 始终在上下文的那一份。文字好坏用例判不了。
/// </para>
/// </remarks>
public sealed class ProgressiveDisclosureTests
{
    /// <summary>一条工具描述的字数上限。</summary>
    /// <remarks>
    /// 卡在这里是因为它的代价与别的两层不同：别的两层取一次付一次，
    /// 而它每一轮都在，八条一起算。撑到几百字的时候，付出去的是每一轮的预算。
    /// </remarks>
    private const int DescriptionLimit = 200;

    /// <summary>DSL 里那些只属于"精确语法"的记号。</summary>
    private static readonly string[] GrammarMarkers = ["->", "-->", "shape=", "same-rank", "ports=", "align ", "pin "];

    /// <summary>正文里够长、够特别的行。</summary>
    private const int ParagraphLength = 24;

    /// <summary>列表项开头那个记号。去掉之后剩下的才是那句话本身。</summary>
    private static readonly Regex ListMarker = new(@"^(\d+[.、]|[-*])\s+", RegexOptions.Compiled);

    private static readonly Regex DslBlock = new(@"```dsl\r?\n(?<body>.*?)```", RegexOptions.Singleline | RegexOptions.Compiled);

    #region 第一层：工具描述

    [Fact]
    [Trait("Category", "SkillDisclosure")]
    public async Task The_tool_descriptions_stay_one_line_and_say_only_what_it_is()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        var tools = await session.Client.ListToolsAsync(cancellationToken: cancellationToken);

        tools.Should().HaveCount(8);

        foreach (var tool in tools)
        {
            var description = tool.Description;

            description.Should().NotBeNullOrWhiteSpace();

            // 一行是这一层的形状：开始编号或者换行分段，它就从「何时用」变成了「一步步怎么做」，
            // 而后者是 Skill 正文那一层的事。
            description.Should().NotContain("\n", $"{tool.Name} 的描述不该分段");
            description.Should().NotContain("```", $"{tool.Name} 的描述里不该有代码块");
            description!.Length.Should().BeLessThan(DescriptionLimit, $"{tool.Name} 的描述每一轮都在上下文里");

            foreach (var marker in GrammarMarkers)
            {
                description.Should().NotContain(marker, $"{tool.Name} 的描述属于第一层，精确语法在第三层");
            }
        }
    }

    #endregion

    #region 第二层：Skill 正文

    [Fact]
    [Trait("Category", "SkillDisclosure")]
    public void The_skill_bodies_do_not_carry_the_grammar()
    {
        foreach (var skill in SkillCatalog.Default.Skills)
        {
            skill.Body.Should().NotContain("```", $"{skill.Name} 的正文里不该有代码块");
            skill.Body.Should().NotContain("```dsl", $"{skill.Name} 的正文里不该有语法示例");

            foreach (var marker in GrammarMarkers)
            {
                skill.Body.Should().NotContain(marker, $"{skill.Name} 的正文讲的是怎么做，精确写法在资源文件里");
            }
        }
    }

    #endregion

    #region 第三层：资源文件

    [Fact]
    [Trait("Category", "SkillDisclosure")]
    public void The_resource_file_is_where_the_grammar_lives()
    {
        var syntax = SkillCatalog.Default.Find("diagram-syntax")!;
        var grammar = syntax.Assets.Should().ContainSingle().Which.Text;

        grammar.Should().Contain("```dsl");

        foreach (var marker in new[] { "shape=", "same-rank", "ports=" })
        {
            grammar.Should().Contain(marker);
        }

        // 正文里明说了去哪看，两处对不上的话，模型会去一个取不到的地方。
        syntax.Body.Should().Contain($"skill://{syntax.Name}/{syntax.Assets[0].Path}");
    }

    /// <summary>
    /// 资源文件里的每一段例子都拿真的解析器跑一遍。
    /// </summary>
    /// <remarks>
    /// 文档里的例子过期了不会报错，只会让模型照着写错的东西——本仓在错误码名上已经踩过一次
    /// 同样的坑。所以判据不是"人看过"，而是"解析器跑过"。
    /// </remarks>
    [Fact]
    [Trait("Category", "SkillDisclosure")]
    [Trait("Category", "Skill")]
    public void Every_dsl_example_in_the_resource_file_parses_clean()
    {
        var grammar = SkillCatalog.Default.Find("diagram-syntax")!.Assets.Single().Text;
        var blocks = DslBlock.Matches(grammar);

        // 一段都没有的话下面那个循环一次都不走，而这条用例会绿——那是最坏的一种绿。
        blocks.Should().NotBeEmpty("资源文件里要有能跑的例子");

        var index = 0;

        foreach (Match block in blocks)
        {
            index++;

            var parsed = DslParser.Parse(block.Groups["body"].Value);

            parsed.Diagnostics.Should().BeEmpty($"资源文件里第 {index} 段例子解析不干净");
        }
    }

    #endregion

    #region 三层的边界

    [Fact]
    [Trait("Category", "SkillDisclosure")]
    public async Task Nothing_that_is_always_in_context_carries_a_skill_body()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        var alwaysOn = session.Client.ServerInstructions + "\n"
            + string.Join('\n', (await session.Client.ListToolsAsync(cancellationToken: cancellationToken))
                .Select(tool => tool.Description));

        foreach (var skill in SkillCatalog.Default.Skills)
        {
            foreach (var line in LongLines(skill.Body))
            {
                alwaysOn.Should().NotContain(
                    line,
                    $"{skill.Name} 的正文只在任务匹配上时取；抄进始终在上下文的那一份等于把三层压成一层");
            }
        }
    }

    #endregion

    #region 内部那条通路

    [Fact]
    [Trait("Category", "SkillDisclosure")]
    public void The_internal_path_adds_nothing_when_no_skill_matched()
    {
        var options = ChatOptionsFactory.Create(new ToolRegistry(), instructions: "基础提示");

        options.Instructions.Should().Be("基础提示");
    }

    [Fact]
    [Trait("Category", "SkillDisclosure")]
    public void The_internal_path_appends_the_matched_skill_after_the_system_prompt()
    {
        var body = SkillCatalog.Default.Find("diagram-workflow")!.Body;

        var options = ChatOptionsFactory.Create(new ToolRegistry(), instructions: "基础提示", skill: body);

        // 接在后面而不是替掉：系统提示说的是这份图是什么，Skill 说的是这一类任务该怎么做。
        options.Instructions.Should().StartWith("基础提示");
        options.Instructions.Should().EndWith(body);
    }

    #endregion

    /// <summary>
    /// 正文里那些够长、够特别的行。
    /// </summary>
    /// <remarks>
    /// 按行取而不是按段取：段落里带换行，而始终在上下文的那一份是另一种换行方式，
    /// 拿整段去比永远比不上，那条断言于是恒真——恒真的断言比没有断言更糟，
    /// 它看上去验过了。短行与标题跳过，它们出现在别处可能只是巧合。
    /// </remarks>
    private static IEnumerable<string> LongLines(string body) =>
        body.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => ListMarker.Replace(line.Trim(), string.Empty))
            .Where(line => line.Length >= ParagraphLength && !line.StartsWith('#'));
}
