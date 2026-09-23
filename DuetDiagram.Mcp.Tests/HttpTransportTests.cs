using System.Text.Json;
using DuetDiagram.Llm.Tools;
using DuetDiagram.Mcp.Server;
using FluentAssertions;
using ModelContextProtocol.Client;
using Xunit;

namespace DuetDiagram.Mcp.Tests;

/// <summary>
/// 网络传输：工具在一个真端口上被调用，而服务端不在请求之间记任何东西。
/// </summary>
/// <remarks>
/// <para>
/// 这一层盯的是**无状态**那一条：同一个调用发给哪个实例都成立，前面的负载均衡不必做
/// 会话粘滞。判据不是"文档里写了无状态"，而是"换一个实例、不做任何握手，同一个请求照样成立"。
/// </para>
/// <para>
/// 每一条用例自己起一个服务端、自己一个工作区：共用一个的话，前一条用例留下的版本号
/// 会让后一条的断言时对时错，而那种失败看起来像是传输的问题。
/// </para>
/// </remarks>
public sealed class HttpTransportTests
{
    #region 连接与工具发现

    [Fact]
    [Trait("Category", "McpHttp")]
    public async Task A_tool_call_works_without_any_handshake()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workspace = Harness.NewWorkspace();

        await using var host = await Harness.StartAsync(Harness.Options(workspace), cancellationToken);
        await using var client = await Harness.ConnectAsync(host, cancellationToken: cancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

        tools.Should().HaveCount(8, "八个工具都该挂在这条传输上");

        var summary = await Harness.CallSucceedsAsync(
            client,
            DiagramToolset.Read,
            "{}",
            cancellationToken);

        summary.GetProperty("isSuccess").GetBoolean().Should().BeTrue();
    }

    /// <summary>这条传输不给会话标识，因为服务端不记会话。</summary>
    /// <remarks>
    /// 会话标识非空就说明服务端在请求之间记了东西，而那正是无状态要排除的：
    /// 记了东西之后，请求落到另一个实例上就会失败，而失败的方式是"会话找不到"。
    /// </remarks>
    [Fact]
    [Trait("Category", "McpHttp")]
    public async Task The_connection_carries_no_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        await using var client = await Harness.ConnectAsync(host, cancellationToken: cancellationToken);

        client.SessionId.Should().BeNullOrEmpty();
    }

    #endregion

    #region 换实例

    /// <summary>
    /// 同一个请求发给另一个实例仍然成立。
    /// </summary>
    /// <remarks>
    /// 这是无状态那一条真正的判据。做法是把第一个实例整个停掉，再在一个全新的实例上
    /// 发同一个请求——中间没有任何会话被搬过去，请求里带的只有它自己那份声明。
    /// </remarks>
    [Fact]
    [Trait("Category", "McpHttp")]
    public async Task The_same_request_works_against_another_instance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string Arguments = """{"action":"add-node","id":"a","label":"甲"}""";

        var first = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        await using var firstClient = await Harness.ConnectAsync(first, cancellationToken: cancellationToken);

        var written = await Harness.CallSucceedsAsync(
            firstClient,
            DiagramToolset.Edit,
            Arguments,
            cancellationToken,
            Harness.At(0));

        var version = written.GetProperty("data").GetProperty("version").GetInt32();

        version.Should().Be(1);

        await firstClient.DisposeAsync();
        await first.DisposeAsync();

        // 换一个实例：它自己的文档、自己的总线、自己的端口。
        await using var second = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        await using var secondClient = await Harness.ConnectAsync(second, cancellationToken: cancellationToken);

        var again = await Harness.CallSucceedsAsync(
            secondClient,
            DiagramToolset.Edit,
            Arguments,
            cancellationToken,
            Harness.At(0));

        again.GetProperty("data").GetProperty("version").GetInt32().Should().Be(
            version,
            "同一个请求在两个互不相干的实例上应当得到同一个结果");

        second.Session.Document.Nodes.Should().ContainSingle(node => node.Id == "a");
    }

    #endregion

    #region 版本声明

    /// <summary>改动落在文档上，并且回一个新的版本号。</summary>
    [Fact]
    [Trait("Category", "McpHttp")]
    public async Task A_write_reaches_the_document_and_reports_a_new_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        await using var client = await Harness.ConnectAsync(host, cancellationToken: cancellationToken);

        var written = await Harness.CallSucceedsAsync(
            client,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"a","label":"甲"}""",
            cancellationToken,
            Harness.At(0));

        var outcome = written.GetProperty("data");

        outcome.GetProperty("version").GetInt32().Should().Be(1);
        outcome.GetProperty("structuralChanged").GetBoolean().Should().BeTrue();

        host.Session.Document.Nodes.Should().ContainSingle(node => node.Id == "a");
    }

    /// <summary>
    /// 改两次要声明两次，第二次带的是新版本号。
    /// </summary>
    /// <remarks>
    /// 会话建立时声明一次只够改一次：每改一次版本号就往前一格，只认那一次声明的话，
    /// 第二次改会被判成冲突，而调用方明明已经把新的版本号拿在手里了。
    /// </remarks>
    [Fact]
    [Trait("Category", "McpHttp")]
    public async Task The_declaration_rides_on_every_request()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        await using var client = await Harness.ConnectAsync(
            host,
            declaration: Harness.At(0),
            cancellationToken: cancellationToken);

        await Harness.CallSucceedsAsync(
            client,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"a","label":"甲"}""",
            cancellationToken);

        var second = await Harness.CallSucceedsAsync(
            client,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"b","label":"乙"}""",
            cancellationToken,
            Harness.At(1));

        second.GetProperty("data").GetProperty("version").GetInt32().Should().Be(2);
    }

    /// <summary>报了一个旧版本号就拿到冲突，而文档一个字节没动。</summary>
    [Fact]
    [Trait("Category", "McpHttp")]
    public async Task A_stale_declaration_is_refused_and_the_document_is_untouched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        await using var client = await Harness.ConnectAsync(host, cancellationToken: cancellationToken);

        await Harness.CallSucceedsAsync(
            client,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"a","label":"甲"}""",
            cancellationToken,
            Harness.At(0));

        var refused = await Harness.CallAsync(
            client,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"b","label":"乙"}""",
            cancellationToken,
            Harness.At(0));

        refused.GetProperty("isSuccess").GetBoolean().Should().BeFalse();
        refused.GetProperty("errors")[0].GetProperty("code").GetString()
            .Should().Be("VERSION_CONFLICT");

        host.Session.Document.Nodes.Should().ContainSingle("版本对不上时那条命令不该落下去");
    }

    /// <summary>这条通路上要改就必须声明版本。</summary>
    /// <remarks>
    /// 不声明也放行的话，外部代理会养成"不带版本"的习惯，而那条路一旦有人并发改，
    /// 后写的那一份会把前一份悄悄盖掉，两边都不报错。
    /// </remarks>
    [Fact]
    [Trait("Category", "McpHttp")]
    public async Task A_write_without_a_declaration_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        await using var client = await Harness.ConnectAsync(host, cancellationToken: cancellationToken);

        var refused = await Harness.CallAsync(
            client,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"a","label":"甲"}""",
            cancellationToken);

        refused.GetProperty("isSuccess").GetBoolean().Should().BeFalse();
        refused.GetProperty("errors")[0].GetProperty("code").GetString()
            .Should().Be("EXPECTED_VERSION_REQUIRED");
    }

    /// <summary>只读的那几个工具不带版本声明也调得动。</summary>
    [Fact]
    [Trait("Category", "McpHttp")]
    public async Task A_read_works_without_a_declaration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        await using var client = await Harness.ConnectAsync(host, cancellationToken: cancellationToken);

        var exported = await Harness.CallSucceedsAsync(
            client,
            DiagramToolset.Export,
            """{"format":"mermaid"}""",
            cancellationToken);

        exported.GetProperty("data").GetProperty("format").GetString().Should().Be("mermaid");
    }

    #endregion

    #region 从文件起

    /// <summary>给了文档文件就从它读，而不是从一张空图开始。</summary>
    [Fact]
    [Trait("Category", "McpHttp")]
    public async Task A_document_file_supplies_the_document_the_server_edits()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workspace = Harness.NewWorkspace();

        Harness.WriteDocument(workspace, "from-file");

        await using var host = await Harness.StartAsync(
            Harness.Options(workspace, document: "from-file.json"),
            cancellationToken);

        host.Session.Document.Id.Should().Be("from-file");

        await using var client = await Harness.ConnectAsync(host, cancellationToken: cancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

        tools.Should().HaveCount(8);
    }

    #endregion

    #region 结果形状

    /// <summary>工具结果按它交给调用方时的那个形状出去，与另一条传输逐字一致。</summary>
    /// <remarks>
    /// 两条传输各序列化一次的话，某天一边改了命名策略，代理从这一条拿到的键名
    /// 会与从另一条拿到的不一样，而两种都能被解析，看不出差别。
    /// </remarks>
    [Fact]
    [Trait("Category", "McpHttp")]
    public async Task The_result_text_is_the_tool_result_as_json()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        await using var client = await Harness.ConnectAsync(host, cancellationToken: cancellationToken);

        var result = await client.CallToolAsync(
            DiagramToolset.Read,
            new Dictionary<string, object?>(),
            cancellationToken: cancellationToken);

        var text = result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Single().Text;

        using var parsed = JsonDocument.Parse(text);

        parsed.RootElement.GetProperty("isSuccess").GetBoolean().Should().BeTrue();
        parsed.RootElement.TryGetProperty("data", out _).Should().BeTrue();
    }

    #endregion
}
