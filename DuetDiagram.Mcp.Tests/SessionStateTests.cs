using System.Text.Json;
using System.Text.Json.Nodes;
using DuetDiagram.Llm.Tools;
using DuetDiagram.Mcp.Server;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Mcp.Tests;

/// <summary>
/// 会话状态：怎么读进来，以及它到了服务端之后真的起了作用。
/// </summary>
/// <remarks>
/// 这一组的重点不是"字段能不能解析"，而是**声明的版本真的接到了命令上**。
/// 只把它记进日志等于没做：文档会被照着旧副本改掉，而两边都不报错。
/// </remarks>
public sealed class SessionStateTests
{
    #region 读取

    [Fact]
    [Trait("Category", "McpSession")]
    public void The_declaration_is_read_from_the_request_metadata()
    {
        var parameters = JsonNode.Parse(
            """
            {"_meta":{"io.modelcontextprotocol/clientCapabilities":{"extensions":{
              "duetdiagram/session":{"documentId":"d1","hasVersion":7,"structuralHash":"h1"}}}}}
            """);

        var state = SessionState.FromRequest(parameters);

        state.Should().NotBeNull();
        state!.DocumentId.Should().Be("d1");
        state.Version.Should().Be(7);
        state.StructuralHash.Should().Be("h1");
    }

    [Fact]
    [Trait("Category", "McpSession")]
    public void The_declaration_is_also_read_from_the_parameters_top_level()
    {
        // 早期协议版本把能力声明放在参数顶层。只认当前那个位置的话，
        // 协议一动这条读取就静默失效，而表现是"调用方明明声明了、服务端却说没收到"。
        var parameters = JsonNode.Parse(
            """
            {"capabilities":{"extensions":{"duetdiagram/session":{"documentId":"d2","hasVersion":3}}}}
            """);

        var state = SessionState.FromRequest(parameters);

        state.Should().NotBeNull();
        state!.DocumentId.Should().Be("d2");
        state.Version.Should().Be(3);
    }

    [Fact]
    [Trait("Category", "McpSession")]
    public void The_call_level_declaration_overrides_the_session_one()
    {
        // 会话建立时声明一次只够改一次：每改一次版本号就往前一格。
        // 认不出调用自带的那一份的话，第二次改会被判成冲突，
        // 而调用方明明已经把新的版本号拿在手里了。
        var parameters = JsonNode.Parse(
            """
            {"_meta":{
              "duetdiagram/session":{"documentId":"d1","hasVersion":9},
              "io.modelcontextprotocol/clientCapabilities":{"extensions":{
                "duetdiagram/session":{"documentId":"d1","hasVersion":2}}}}}
            """);

        SessionState.FromRequest(parameters)!.Version.Should().Be(9);
    }

    [Fact]
    [Trait("Category", "McpSession")]
    public void The_extension_value_is_read_in_every_shape_it_arrives_in()
    {
        // 声明上它是对象；经协议往返之后它要么是装着对象的 JSON 节点，
        // 要么是**装着一段文本的 JSON 值**（对端把它当字符串处理的结果，实际线上就是这一种），
        // 也可能是别的实现给的一段原生文本。只认一种的话，会出现
        // "字段明明在、读出来却是空"——而它既不报错也不抛异常。
        const string Text = """{"documentId":"d3","hasVersion":4}""";

        SessionState.FromExtensionValue(JsonNode.Parse(Text))!.Version.Should().Be(4);
        SessionState.FromExtensionValue(JsonValue.Create(Text))!.Version.Should().Be(4);
        SessionState.FromExtensionValue(JsonDocument.Parse(Text).RootElement)!.Version.Should().Be(4);
        SessionState.FromExtensionValue(Text)!.Version.Should().Be(4);
    }

    [Fact]
    [Trait("Category", "McpSession")]
    public void An_unreadable_declaration_is_empty_rather_than_an_error()
    {
        SessionState.FromExtensionValue(null).Should().BeNull();
        SessionState.FromExtensionValue("").Should().BeNull();
        SessionState.FromExtensionValue("这不是 JSON").Should().BeNull();
        SessionState.FromRequest(JsonNode.Parse("""{"_meta":{}}""")).Should().BeNull();
        SessionState.FromRequest(null).Should().BeNull();
    }

    [Fact]
    [Trait("Category", "McpSession")]
    public void A_declaration_without_a_version_yields_no_version_check()
    {
        // 没声明版本时不编一个零出来：编出来的零会被当成"我看的是第 0 版"，
        // 而真相是"我没说"，两者的处置完全不同。
        new SessionState("d1", Version: null, StructuralHash: null).ToVersionCheck().Should().BeNull();

        var check = new SessionState("d1", 5, "h1").ToVersionCheck();

        check.Should().NotBeNull();
        check!.ClientVersion.Should().Be(5);
        check.ClientStructuralHash.Should().Be("h1");
    }

    #endregion

    #region 到了服务端之后起什么作用

    [Fact]
    [Trait("Category", "McpSession")]
    public async Task A_declared_version_lets_a_write_through()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        var written = await Harness.CallSucceedsAsync(
            session.Client,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"a"}""",
            cancellationToken,
            Harness.At(0));

        written.GetProperty("data").GetProperty("version").GetInt32().Should().Be(1);
    }

    [Fact]
    [Trait("Category", "McpSession")]
    public async Task A_stale_declaration_is_refused_and_the_document_is_untouched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        await Harness.CallSucceedsAsync(
            session.Client,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"a"}""",
            cancellationToken,
            Harness.At(0));

        // 同一个调用方还停在版本 0，而文档已经是 1。这一次写入必须被拒绝而不是静默覆盖。
        var stale = await Harness.CallAsync(
            session.Client,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"b"}""",
            cancellationToken,
            Harness.At(0));

        stale.GetProperty("isSuccess").GetBoolean().Should().BeFalse();
        stale.GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("VERSION_CONFLICT");

        var summary = await Harness.CallSucceedsAsync(session.Client, DiagramToolset.Read, "{}", cancellationToken);

        summary.GetProperty("data").GetProperty("summary").GetProperty("nodes").GetArrayLength()
            .Should().Be(1, "被拒绝的那次写入不该留下任何痕迹");
    }

    [Fact]
    [Trait("Category", "McpSession")]
    public async Task A_version_ahead_of_the_document_is_a_parameter_error_not_a_conflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        // 声明一个比当前还新的版本属于参数写错了，处置是改参数，不是同步之后重试。
        var ahead = await Harness.CallAsync(
            session.Client,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"a"}""",
            cancellationToken,
            Harness.At(9));

        ahead.GetProperty("errors")[0].GetProperty("code").GetString()
            .Should().Be("INVALID_EXPECTED_VERSION");
    }

    [Fact]
    [Trait("Category", "McpSession")]
    public async Task A_write_without_a_declaration_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        var undeclared = await Harness.CallAsync(
            session.Client,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"a"}""",
            cancellationToken);

        // 这条通路要求携带版本信息。不声明就写，等于"我不知道我看到的是哪一版"——
        // 那正是乐观并发要挡的那种写入。
        undeclared.GetProperty("errors")[0].GetProperty("code").GetString()
            .Should().Be("EXPECTED_VERSION_REQUIRED");
    }

    [Fact]
    [Trait("Category", "McpSession")]
    public async Task A_read_works_without_a_declaration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        // 读不改文档，也就没有版本可言。挡在这里的话，调用方连"我该声明哪一版"都问不出来。
        var summary = await Harness.CallSucceedsAsync(session.Client, DiagramToolset.Read, "{}", cancellationToken);

        summary.GetProperty("data").GetProperty("summary").GetProperty("version").GetInt32().Should().Be(0);
    }

    [Fact]
    [Trait("Category", "McpSession")]
    public async Task Every_request_carries_the_declaration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(
            declaration: Harness.At(0),
            cancellationToken: cancellationToken);

        await session.Client.ListToolsAsync(cancellationToken: cancellationToken);
        await Harness.CallSucceedsAsync(session.Client, DiagramToolset.Read, "{}", cancellationToken);

        var requests = session.Log.Requests();

        // 三条：会话建立、工具发现、工具调用。每一条都带着声明——
        // 这正是这一层能做成无状态的原因：服务端不需要记住任何东西。
        requests.Should().HaveCountGreaterThanOrEqualTo(3);
        requests.Should().AllSatisfy(line => line.Should().Contain(Harness.DocumentId));
    }

    #endregion
}
