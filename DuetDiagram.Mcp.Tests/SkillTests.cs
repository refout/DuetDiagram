using System.Text.RegularExpressions;
using DuetDiagram.Mcp.Server;
using DuetDiagram.Mcp.Skills;
using FluentAssertions;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace DuetDiagram.Mcp.Tests;

/// <summary>
/// Skill 那一层：有哪几份、按 <c>skill://</c> 取得到取不到、以及 URI 能不能爬出这一层。
/// </summary>
/// <remarks>
/// <para>
/// 这一层要验的是**装置**：目录里有那两份、两条传输都能按 URI 取到、取回来的是同一份正文。
/// 正文写得好不好用例判不了，但「正文在不在」「取不取得到」判得了——
/// 而这两样一坏，表现是模型从头到尾没看过 Skill，却照样能答话。
/// </para>
/// <para>
/// 逃逸那几条按解析之后的段判，不按字符串前缀判：前缀比较挡不住 <c>..</c>，
/// 也挡不住编码过的 <c>..</c>。用例里两种都摆上。
/// </para>
/// </remarks>
public sealed class SkillTests
{
    /// <summary>版本号的形状。带版本这条判据要有一个能判的写法。</summary>
    private static readonly Regex VersionShape = new(@"^\d+(\.\d+)?$", RegexOptions.Compiled);

    #region 目录

    [Fact]
    [Trait("Category", "Skill")]
    public void Both_skills_are_reachable_and_carry_a_version()
    {
        var catalog = SkillCatalog.Default;

        catalog.Skills.Select(skill => skill.Name)
            .Should().Equal("diagram-workflow", "diagram-syntax");

        foreach (var skill in catalog.Skills)
        {
            skill.Uri.Should().Be($"skill://{skill.Name}");
            skill.Title.Should().NotBeNullOrWhiteSpace();

            // 「什么时候该取它」是清单里唯一能据以挑的东西。缺了它，调用方只有全取或者瞎猜。
            skill.Summary.Should().NotBeNullOrWhiteSpace();

            skill.Body.Should().NotBeNullOrWhiteSpace();
            skill.Version.Should().MatchRegex(VersionShape);

            catalog.Read(skill.Uri).Should().Be(skill.Body);
        }
    }

    [Fact]
    [Trait("Category", "Skill")]
    public void The_syntax_skill_carries_the_documented_grammar_as_a_resource_file()
    {
        var catalog = SkillCatalog.Default;
        var syntax = catalog.Find("diagram-syntax")!;

        syntax.Should().NotBeNull();

        var asset = syntax.Assets.Should().ContainSingle().Which;

        asset.Path.Should().Be("dsl");
        asset.Title.Should().NotBeNullOrWhiteSpace();
        asset.Summary.Should().NotBeNullOrWhiteSpace();
        asset.Text.Should().NotBeNullOrWhiteSpace();

        catalog.Read("skill://diagram-syntax/dsl").Should().Be(asset.Text);

        // 工作流那一份没有资源文件。第三层是按需要才有的，不是每份 Skill 都配一份。
        catalog.Find("diagram-workflow")!.Assets.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Skill")]
    public void The_listing_says_what_each_one_is_for()
    {
        var catalog = SkillCatalog.Default;
        var resources = catalog.ToMcpResources();

        // 两份正文加一份资源文件。
        resources.Should().HaveCount(catalog.Skills.Count + catalog.Skills.Sum(skill => skill.Assets.Count));

        foreach (var resource in resources)
        {
            resource.ProtocolResource!.Name.Should().NotBeNullOrWhiteSpace();
            resource.ProtocolResource.Title.Should().NotBeNullOrWhiteSpace();
            resource.ProtocolResource.Description.Should().NotBeNullOrWhiteSpace();
            resource.ProtocolResource.MimeType.Should().Be(SkillCatalog.Markdown);
        }

        resources.Select(resource => resource.ProtocolResource!.Uri)
            .Should().Contain(["skill://diagram-workflow", "skill://diagram-syntax", "skill://diagram-syntax/dsl"]);
    }

    #endregion

    #region 逃逸

    [Theory]
    [Trait("Category", "Skill")]
    [InlineData("skill://diagram-syntax/../diagram-workflow")]
    [InlineData("skill://diagram-syntax/../../diagram-syntax")]
    [InlineData("skill://../diagram-workflow")]
    [InlineData("skill://diagram-syntax/%2E%2E/diagram-workflow")]
    [InlineData("skill://diagram-syntax/..%2Fdiagram-workflow")]
    [InlineData("skill://diagram-syntax\\..\\diagram-workflow")]
    [InlineData("skill://diagram-syntax/./../../diagram-workflow")]
    [InlineData("skill://diagram-syntax/dsl/../../diagram-workflow")]
    [InlineData("skill://")]
    [InlineData("skill://nope")]
    [InlineData("skill://diagram-syntax/nope")]
    [InlineData("skill://Diagram-Workflow")]
    [InlineData("http://diagram-workflow")]
    [InlineData("skill:diagram-workflow")]
    [InlineData("")]
    public void A_uri_that_does_not_land_on_a_skill_is_refused(string uri)
    {
        SkillCatalog.Default.Read(uri).Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Skill")]
    public async Task The_protocol_answers_an_unknown_uri_with_an_error()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        // 目录那一层返回空，协议这一层要把它变成一条错误。回一份空正文的话，
        // 调用方会以为自己拿到了一份内容为空的 Skill，而不会想到是自己把 URI 写错了。
        var act = async () => await session.Client.ReadResourceAsync(
            "skill://nope",
            cancellationToken: cancellationToken);

        await act.Should().ThrowAsync<McpException>();
    }

    #endregion

    #region 两条传输

    [Fact]
    [Trait("Category", "Skill")]
    public async Task Both_skills_come_back_over_the_standard_input_output_transport()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        await AssertServedAsync(session.Client, cancellationToken);
    }

    [Fact]
    [Trait("Category", "Skill")]
    public async Task Both_skills_come_back_over_the_network_transport()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.Workspace()), cancellationToken);
        await using var client = await Harness.ConnectAsync(host, cancellationToken: cancellationToken);

        await AssertServedAsync(client, cancellationToken);
    }

    /// <summary>两条传输上验的是同一件事：清单里有，取回来与目录里那份逐字相同。</summary>
    private static async Task AssertServedAsync(McpClient client, CancellationToken cancellationToken)
    {
        var listed = await client.ListResourcesAsync(cancellationToken: cancellationToken);

        var uris = listed.Select(resource => resource.Uri).ToArray();

        uris.Should().Contain(["skill://diagram-workflow", "skill://diagram-syntax", "skill://diagram-syntax/dsl"]);

        // 版本随清单走。正文改了不会以别的方式暴露出来——一份过期的正文读起来与新的没有区别。
        var workflow = listed.Single(resource => resource.Uri == "skill://diagram-workflow");

        workflow.ProtocolResource.Meta?["version"]?.GetValue<string>()
            .Should().Be(SkillCatalog.Default.Find("diagram-workflow")!.Version);

        foreach (var skill in SkillCatalog.Default.Skills)
        {
            var text = await ReadAsync(client, skill.Uri, cancellationToken);

            text.Should().Be(skill.Body);

            foreach (var asset in skill.Assets)
            {
                var assetText = await ReadAsync(client, $"{skill.Uri}/{asset.Path}", cancellationToken);

                assetText.Should().Be(asset.Text);
            }
        }
    }

    /// <summary>读一份资源，把文本那一支取出来。</summary>
    private static async Task<string> ReadAsync(McpClient client, string uri, CancellationToken cancellationToken)
    {
        var result = await client.ReadResourceAsync(uri, cancellationToken: cancellationToken);

        var contents = result.Contents.Should().ContainSingle().Which;

        contents.Uri.Should().Be(uri);

        return contents.Should().BeOfType<TextResourceContents>().Which.Text;
    }

    #endregion
}
