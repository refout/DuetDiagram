using System.Net;
using System.Text.Json;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Diagnostics;
using DuetDiagram.Llm.Tools;
using DuetDiagram.Mcp.Server;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Mcp.Tests;

/// <summary>
/// 网络那一档前面的那道关：认凭据、算频次、看权限档、限住路径、卡住时间。
/// </summary>
/// <remarks>
/// <para>
/// **四种拒绝各自一个状态码、各自一个错误码。** 合成一个「拒绝」的话，代理侧分不清
/// 「凭据不认得」「凭据权限不够」「你调得太快」「这份文件不许碰」——
/// 而这四件事的处置完全不同。
/// </para>
/// <para>
/// 每一条拒绝都要连**文档没被动过**一起断言。只断言状态码的话，一个先改了文档
/// 再回拒绝的实现也能通过，而那种实现比放行还糟：调用方以为没改成。
/// </para>
/// </remarks>
public sealed class SecurityTests
{
    #region 认证

    [Fact]
    [Trait("Category", "McpSecurity")]
    public async Task A_request_without_a_token_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        using var client = Harness.RawClient(host, token: null);

        using var response = await Harness.ChangesAsync(client, 0, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Harness.RejectionCodeAsync(response, cancellationToken))
            .Should().Be(ErrorCodes.McpUnauthorized);
    }

    [Fact]
    [Trait("Category", "McpSecurity")]
    public async Task A_token_that_is_not_recognised_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        using var client = Harness.RawClient(host, "tok-nobody-0b17");

        using var response = await Harness.ChangesAsync(client, 0, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Harness.RejectionCodeAsync(response, cancellationToken))
            .Should().Be(ErrorCodes.McpUnauthorized);
    }

    #endregion

    #region 权限档

    /// <summary>只读凭据调得动读的那几个工具。</summary>
    [Fact]
    [Trait("Category", "McpSecurity")]
    public async Task A_read_token_can_still_read()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        await using var client = await Harness.ConnectAsync(
            host,
            Harness.ReadToken,
            cancellationToken: cancellationToken);

        await Harness.CallSucceedsAsync(client, DiagramToolset.Read, "{}", cancellationToken);
    }

    /// <summary>
    /// 只读凭据调不动改文档的工具。
    /// </summary>
    /// <remarks>
    /// 判据是 403 而不是某个工具错误：这一条拒绝发生在协议那一层之前，
    /// 所以连"这个工具的参数长什么样"都不必告诉对方。
    /// </remarks>
    [Theory]
    [InlineData(DiagramToolset.Edit, """{"action":"add-node","id":"a","label":"甲"}""")]
    [InlineData(DiagramToolset.Style, """{"action":"set-shape","id":"a","value":"rect"}""")]
    [InlineData(DiagramToolset.Layout, """{"action":"set-direction","value":"LR"}""")]
    [InlineData(DiagramToolset.Composite, """{"action":"create","id":"g1","memberIds":["a"]}""")]
    [InlineData(DiagramToolset.UndoRedo, """{"action":"undo"}""")]
    [Trait("Category", "McpSecurity")]
    public async Task A_read_token_cannot_call_a_write_tool(string tool, string arguments)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        using var client = Harness.RawClient(host, Harness.ReadToken);

        using var response = await Harness.PostAsync(client, Call(tool, arguments), cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Harness.RejectionCodeAsync(response, cancellationToken))
            .Should().Be(ErrorCodes.McpForbidden);

        host.Session.Document.Version.Should().Be(0, "被挡住的那条调用一个字节都不该改到文档");
    }

    #endregion

    #region 限流

    /// <summary>
    /// 一分钟里超过上限就被挡下，并且告诉调用方过多久再来。
    /// </summary>
    /// <remarks>
    /// 报出的间隔必须真的能用：报一个比实际等待时间还短的值，调用方会在同一秒里再撞一次，
    /// 而它以为自己已经照做了。
    /// </remarks>
    [Fact]
    [Trait("Category", "McpSecurity")]
    public async Task Too_many_requests_in_a_minute_are_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(
            Harness.Options(Harness.NewWorkspace(), requestsPerMinute: 2),
            cancellationToken);

        using var client = Harness.RawClient(host, Harness.FullToken);

        using var first = await Harness.ChangesAsync(client, 0, cancellationToken);
        using var second = await Harness.ChangesAsync(client, 0, cancellationToken);

        first.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        second.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);

        using var third = await Harness.ChangesAsync(client, 0, cancellationToken);

        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        third.Headers.RetryAfter!.Delta.Should().BePositive("不报间隔的话调用方只能立刻重试，而立刻重试只会再撞一次");

        var body = await third.Content.ReadAsStringAsync(cancellationToken);

        JsonDocument.Parse(body).RootElement.GetProperty("retryAfterSeconds").GetInt32()
            .Should().BePositive("响应头与正文两处都要有：只解析正文的调用方也得拿到它");
    }

    /// <summary>另一份凭据有自己的额度。</summary>
    /// <remarks>
    /// 按来源地址算的话，同一个出口后面的两个代理会互相掩护，而其中一个是无辜的。
    /// </remarks>
    [Fact]
    [Trait("Category", "McpSecurity")]
    public async Task The_budget_is_counted_per_token()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(
            Harness.Options(Harness.NewWorkspace(), requestsPerMinute: 1),
            cancellationToken);

        using var writer = Harness.RawClient(host, Harness.FullToken);
        using var reader = Harness.RawClient(host, Harness.ReadToken);

        using var mine = await Harness.ChangesAsync(writer, 0, cancellationToken);
        using var theirs = await Harness.ChangesAsync(reader, 0, cancellationToken);

        mine.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        theirs.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests, "另一份凭据的额度不该被我用掉");
    }

    #endregion

    #region 工作区

    /// <summary>
    /// 解析之后落在工作区外面的路径被挡住。
    /// </summary>
    /// <remarks>
    /// 判据是**解析之后**的路径。只在字符串层做前缀比较的话，`..` 与符号链接这两种
    /// 最普通的写法都能绕过去，而它们看起来完全正常。
    /// </remarks>
    [Theory]
    [InlineData("../outside.json")]
    [InlineData("sub/../../outside.json")]
    [InlineData("..")]
    [Trait("Category", "McpSecurity")]
    public void A_path_that_escapes_the_workspace_is_refused(string candidate)
    {
        var workspace = Harness.NewWorkspace();
        var guard = new WorkspaceGuard(workspace);

        var act = () => guard.Resolve(candidate);

        act.Should().Throw<WorkspaceEscapeException>()
            .Which.Candidate.Should().Be(candidate);

        WorkspaceEscapeException.Code.Should().Be(ErrorCodes.McpPathEscaped);
    }

    /// <summary>工作区里面的路径照常放行。</summary>
    [Theory]
    [InlineData("doc.json")]
    [InlineData("sub/doc.json")]
    [InlineData("./doc.json")]
    [Trait("Category", "McpSecurity")]
    public void A_path_inside_the_workspace_is_accepted(string candidate)
    {
        var workspace = Harness.NewWorkspace();
        var guard = new WorkspaceGuard(workspace);

        guard.Resolve(candidate).Should().StartWith(guard.Root);
    }

    /// <summary>
    /// 一条指向工作区外面的目录链接也要被挡住。
    /// </summary>
    /// <remarks>
    /// 这是字符串前缀比较挡不住的那一种：候选路径里一个 `..` 都没有，
    /// 看着完全正常，而解析之后落在外面。
    /// </remarks>
    [Fact]
    [Trait("Category", "McpSecurity")]
    public void A_link_that_points_outside_the_workspace_is_refused()
    {
        var workspace = Harness.NewWorkspace();
        var outside = Harness.NewWorkspace();
        var link = Path.Combine(workspace, "link");

        Directory.CreateSymbolicLink(link, outside);

        var guard = new WorkspaceGuard(workspace);

        var act = () => guard.Resolve(Path.Combine("link", "doc.json"));

        act.Should().Throw<WorkspaceEscapeException>();
    }

    /// <summary>起服务端时就把越权的文档路径挡住，而不是等到读的时候。</summary>
    [Fact]
    [Trait("Category", "McpSecurity")]
    public void A_document_outside_the_workspace_keeps_the_server_from_starting()
    {
        var workspace = Harness.NewWorkspace();

        var act = () => HttpHost.Create(Harness.Options(workspace, document: "../outside.json"));

        act.Should().Throw<WorkspaceEscapeException>();
    }

    #endregion

    #region 时间预算

    /// <summary>
    /// 没有时间预算的调用一条命令都不发。
    /// </summary>
    /// <remarks>
    /// 这是这一层能给的保证：命令是同步的，中途取消观察不到，能保证的是
    /// 「不会回一个超时响应、而那条命令还在后台把它改完」。
    /// </remarks>
    [Fact]
    [Trait("Category", "McpSecurity")]
    public async Task A_call_with_no_budget_changes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(
            Harness.Options(Harness.NewWorkspace(), callTimeout: TimeSpan.Zero),
            cancellationToken);

        await using var client = await Harness.ConnectAsync(host, cancellationToken: cancellationToken);

        var refused = await Harness.CallAsync(
            client,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"a","label":"甲"}""",
            cancellationToken,
            Harness.At(0));

        refused.GetProperty("isSuccess").GetBoolean().Should().BeFalse();
        refused.GetProperty("errors")[0].GetProperty("code").GetString()
            .Should().Be(ErrorCodes.McpTimeout);

        host.Session.Document.Version.Should().Be(0, "超时的调用不该留下任何改动");
        host.Session.Document.Nodes.Should().BeEmpty();
    }

    /// <summary>读也走同一条预算：超了照样不发。</summary>
    [Fact]
    [Trait("Category", "McpSecurity")]
    public async Task The_budget_covers_reads_too()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(
            Harness.Options(Harness.NewWorkspace(), callTimeout: TimeSpan.Zero),
            cancellationToken);

        await using var client = await Harness.ConnectAsync(host, cancellationToken: cancellationToken);

        var refused = await Harness.CallAsync(client, DiagramToolset.Read, "{}", cancellationToken);

        refused.GetProperty("errors")[0].GetProperty("code").GetString()
            .Should().Be(ErrorCodes.McpTimeout);
    }

    #endregion

    #region 审计

    /// <summary>
    /// 审计记了谁做了什么，但不记凭据、也不记完整路径。
    /// </summary>
    /// <remarks>
    /// 日志是最容易被复制走的东西。凭据进了日志，任何能看到日志的人都能拿它去冒充；
    /// 完整路径进了日志，就把这台机器上的目录结构画了出来。两样都不必写。
    /// </remarks>
    [Fact]
    [Trait("Category", "McpSecurity")]
    public async Task The_audit_log_carries_neither_the_token_nor_the_workspace()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workspace = Harness.NewWorkspace();
        var audit = new CollectingDiagnosticsSink();

        await using var host = await Harness.StartAsync(
            Harness.Options(workspace, diagnostics: audit),
            cancellationToken);

        await using var client = await Harness.ConnectAsync(host, cancellationToken: cancellationToken);

        await Harness.CallSucceedsAsync(client, DiagramToolset.Read, "{}", cancellationToken);

        var lines = audit.Warnings.Where(line => line.StartsWith("[audit]", StringComparison.Ordinal)).ToList();

        lines.Should().NotBeEmpty("每一条请求都要留一条审计");
        lines.Should().Contain(line => line.Contains("writer", StringComparison.Ordinal));

        lines.Should().OnlyContain(line => !line.Contains(Harness.FullToken, StringComparison.Ordinal));
        lines.Should().OnlyContain(line => !line.Contains(workspace, StringComparison.Ordinal));
    }

    /// <summary>被挡下的请求也留一条审计。</summary>
    /// <remarks>
    /// 只记成功的调用等于没有审计：真正要看的是"谁在反复撞门"。
    /// </remarks>
    [Fact]
    [Trait("Category", "McpSecurity")]
    public async Task A_refused_request_is_still_recorded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var audit = new CollectingDiagnosticsSink();

        await using var host = await Harness.StartAsync(
            Harness.Options(Harness.NewWorkspace(), diagnostics: audit),
            cancellationToken);

        using var client = Harness.RawClient(host, Harness.ReadToken);

        using var response = await Harness.PostAsync(
            client,
            Call(DiagramToolset.Edit, """{"action":"add-node","id":"a","label":"甲"}"""),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        audit.Warnings.Should().Contain(
            line => line.StartsWith("[audit]", StringComparison.Ordinal)
                && line.Contains("403", StringComparison.Ordinal));
    }

    #endregion

    #region 取数

    /// <summary>一条最小可用的工具调用请求。带不带版本声明由用例自己决定。</summary>
    private static string Call(string tool, string arguments) =>
        "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\""
            + tool
            + "\",\"arguments\":"
            + arguments
            + "}}";

    #endregion
}
