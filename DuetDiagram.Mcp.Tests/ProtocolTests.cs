using DuetDiagram.Mcp.Server;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using Xunit;

namespace DuetDiagram.Mcp.Tests;

/// <summary>
/// 协议层：会话怎么建起来、自定义字段挂在哪、以及为什么断言不按方法名写。
/// </summary>
/// <remarks>
/// 会话建立那一次请求的名字会随协议版本变化——早期版本叫初始化请求，当前版本换成了
/// 服务发现请求。任何按名字匹配的代码在改名之后都会**静默失效**：服务端不报错，
/// 只是那条分支永远不走。所以这里的断言一律按「第一条请求」取样本，不按名字匹配。
/// </remarks>
public sealed class ProtocolTests
{
    [Fact]
    [Trait("Category", "McpProtocol")]
    public async Task The_client_learns_the_server_info_and_instructions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        session.Client.ServerInfo.Should().NotBeNull();
        session.Client.ServerInfo!.Name.Should().Be("duetdiagram");
        session.Client.ServerInfo.Version.Should().Be(DiagramMcpServer.Version);
        session.Client.ServerInstructions.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "McpProtocol")]
    public async Task The_session_ack_rides_in_the_capability_extension()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        // 自定义字段走能力声明的扩展位：两端都能按类型直接读到，
        // 客户端不需要挂任何拦截器去看原始消息。
        var capabilities = session.Client.ServerCapabilities;

        capabilities.Should().NotBeNull();
        capabilities!.Extensions.Should().ContainKey(SessionState.CapabilityKey);

        var ack = SessionAck.FromExtensionValue(capabilities.Extensions[SessionState.CapabilityKey]);

        ack.Should().NotBeNull();
        ack!.DocumentId.Should().Be(Harness.DocumentId);
        ack.Version.Should().Be(0);

        // 结构哈希对一张新建的空图是空的：它在第一次成功变更之后才算得出来。
        // 空值不会让调用方误判成"结构没变过"——拿空哈希去比的那条判据会直接跳过，
        // 落到"逐条下发"那条路上，而那一条是安全的。
        ack.StructuralHash.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "McpProtocol")]
    public async Task The_first_request_is_sampled_by_position_not_by_name()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(
            declaration: Harness.At(0),
            cancellationToken: cancellationToken);

        await session.Client.ListToolsAsync(cancellationToken: cancellationToken);

        var requests = session.Log.Requests();

        // 第一条就是会话建立那一次。这里不断言它的方法名，只断言它到了、并且带着声明——
        // 协议把那个方法改个名字，这条断言照样成立。
        requests.Should().NotBeEmpty();
        requests[0].Should().Contain(Harness.DocumentId);

        // 会话真的建起来了：服务端信息是从那一次请求的应答里来的。
        session.Client.ServerInfo.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "McpProtocol")]
    public async Task An_undeclared_session_still_gets_a_reply()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // 什么都没声明的调用方照样能连上、能列工具、能读——它只是不能写。
        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        var tools = await session.Client.ListToolsAsync(cancellationToken: cancellationToken);

        tools.Should().HaveCount(8);

        var line = await session.Log.WaitAsync(
            text => text.Contains("未声明会话状态", StringComparison.Ordinal),
            cancellationToken);

        line.Should().Contain("收到 ");
    }

    [Fact]
    [Trait("Category", "McpProtocol")]
    public async Task The_tool_declarations_match_the_model_side()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        var tools = await session.Client.ListToolsAsync(cancellationToken: cancellationToken);

        // 代理侧那份是由模型侧的函数派生出来的，所以参数 schema 是同一份。
        // 这里挑一条断言它带着自己那几个参数，说明挂上去的确实是那八个工具、
        // 而不是一个空壳。
        var edit = tools.Should().ContainSingle(tool => tool.Name == "diagram_edit").Which;

        edit.JsonSchema.GetProperty("properties").TryGetProperty("action", out _).Should().BeTrue();
        edit.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "McpProtocol")]
    public void The_two_directions_use_their_own_field_names()
    {
        // 字段名是两端共用的契约，写错了不会报错，只会让对方读不到。
        // 两个方向刻意不同名：调用方说的是"我看到的是第几版"，
        // 服务端说的是"我这边是第几版"。同名的话，一份应答被当成声明读回来时不会被发现。
        var declaration = new SessionState("doc-7f3a", 12, "hash-9c02").ToJson();

        declaration["documentId"]!.GetValue<string>().Should().Be("doc-7f3a");
        declaration["hasVersion"]!.GetValue<int>().Should().Be(12);
        declaration["structuralHash"]!.GetValue<string>().Should().Be("hash-9c02");

        var ack = new SessionAck("doc-7f3a", 12, "hash-9c02").ToJson();

        ack["documentId"]!.GetValue<string>().Should().Be("doc-7f3a");
        ack["serverVersion"]!.GetValue<int>().Should().Be(12);
        ack["serverStructuralHash"]!.GetValue<string>().Should().Be("hash-9c02");
    }

    [Fact]
    [Trait("Category", "McpProtocol")]
    public void The_capability_key_is_the_one_both_sides_use()
    {
        SessionState.CapabilityKey.Should().Be("duetdiagram/session");
    }
}
