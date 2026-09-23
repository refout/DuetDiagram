using System.Net;
using System.Text.Json;
using DuetDiagram.Core.Commands;
using DuetDiagram.Llm.Tools;
using DuetDiagram.Mcp.Server;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Mcp.Tests;

/// <summary>
/// 变化源：已连接的代理靠它知道文档什么时候变了，断线之后靠它补差。
/// </summary>
/// <remarks>
/// <para>
/// 这一条不是协议那条推送。协议那条路在当前版本下要么不支持、要么得踩过时与实验性的接口，
/// 所以这里是一条普通的只读端点，形状是长轮询。
/// </para>
/// <para>
/// **只保存最新的一份状态。** 判据是"断了一段的调用方一次请求就拿到当前版本"，
/// 而不是把它错过的每一条通知逐条收一遍——它真正需要的只是"现在是第几版"。
/// </para>
/// </remarks>
public sealed class ChangeFeedTests
{
    #region 立刻拿得到

    /// <summary>已经有更新的版本时立刻回，不让调用方干等一轮。</summary>
    [Fact]
    [Trait("Category", "ChangeFeed")]
    public async Task A_change_is_visible_without_waiting()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        await using var agent = await Harness.ConnectAsync(host, cancellationToken: cancellationToken);

        await Harness.CallSucceedsAsync(
            agent,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"a","label":"甲"}""",
            cancellationToken,
            Harness.At(0));

        using var watcher = Harness.RawClient(host, Harness.FullToken);

        // 等待时长给零：这一条要验的是"该立刻回的时候真的立刻回"。
        using var response = await Harness.ChangesAsync(watcher, 0, cancellationToken, waitSeconds: 0);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var notice = await ReadAsync(response, cancellationToken);

        notice.GetProperty("version").GetInt32().Should().Be(1);
        notice.GetProperty("affectedIds").EnumerateArray().Select(id => id.GetString())
            .Should().Contain("a");
    }

    /// <summary>变化源报的结构哈希与文档当下那一份是同一个。</summary>
    /// <remarks>
    /// 两份值分开读的话，它们可能来自不同的版本，而调用方拿它们是要去比对的。
    /// </remarks>
    [Fact]
    [Trait("Category", "ChangeFeed")]
    public async Task The_notice_reports_the_current_hash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        await using var agent = await Harness.ConnectAsync(host, cancellationToken: cancellationToken);

        await Harness.CallSucceedsAsync(
            agent,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"a","label":"甲"}""",
            cancellationToken,
            Harness.At(0));

        using var watcher = Harness.RawClient(host, Harness.FullToken);
        using var response = await Harness.ChangesAsync(watcher, 0, cancellationToken, waitSeconds: 0);

        var notice = await ReadAsync(response, cancellationToken);

        notice.GetProperty("structuralHash").GetString()
            .Should().Be(host.Session.Document.StructuralHash);
    }

    #endregion

    #region 等

    /// <summary>没有更新的版本时等到超时，回一个空响应而不是一个假的版本号。</summary>
    [Fact]
    [Trait("Category", "ChangeFeed")]
    public async Task The_feed_returns_empty_when_nothing_changed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        using var watcher = Harness.RawClient(host, Harness.FullToken);

        using var response = await Harness.ChangesAsync(watcher, 0, cancellationToken, waitSeconds: 0);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// 挂在等待里的调用方会被另一条连接上的改动叫醒。
    /// </summary>
    /// <remarks>
    /// 这是"推送"这两个字在这一层里的实际含义：调用方挂在那儿，改的人不必知道它在等。
    /// 只做轮询的话，改动与调用方发现它之间的间隔就是轮询周期，而那个周期得由调用方猜。
    /// </remarks>
    [Fact]
    [Trait("Category", "ChangeFeed")]
    public async Task A_waiter_is_woken_by_a_change_on_another_connection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        await using var agent = await Harness.ConnectAsync(host, cancellationToken: cancellationToken);

        using var watcher = Harness.RawClient(host, Harness.FullToken);

        // 挂上去，等十秒。改动发生在另一条连接上，而且是在它挂好之后。
        var waiting = Harness.ChangesAsync(watcher, 0, cancellationToken, waitSeconds: 10);

        await Task.Delay(200, cancellationToken);

        await Harness.CallSucceedsAsync(
            agent,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"a","label":"甲"}""",
            cancellationToken,
            Harness.At(0));

        using var response = await waiting;

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var notice = await ReadAsync(response, cancellationToken);

        notice.GetProperty("version").GetInt32().Should().Be(1);
    }

    #endregion

    #region 断线之后补差

    /// <summary>
    /// 断了一段的调用方一次请求就拿到当前版本。
    /// </summary>
    /// <remarks>
    /// 判据是**一次请求**。存通知历史的话，一个断了一小时的调用方会把这一小时里的
    /// 每一条都收一遍，而它真正需要的只是"现在是第几版、结构哈希是什么"。
    /// </remarks>
    [Fact]
    [Trait("Category", "ChangeFeed")]
    public async Task A_reconnecting_caller_gets_the_current_version_in_one_request()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        await using var agent = await Harness.ConnectAsync(host, cancellationToken: cancellationToken);

        await Harness.CallSucceedsAsync(
            agent,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"a","label":"甲"}""",
            cancellationToken,
            Harness.At(0));

        // 到这里为止，那个调用方看到的是第 1 版，然后它断了。
        using var watcher = Harness.RawClient(host, Harness.FullToken);

        for (var index = 0; index < 2; index++)
        {
            var id = ((char)('b' + index)).ToString();

            await Harness.CallSucceedsAsync(
                agent,
                DiagramToolset.Edit,
                $$"""{"action":"add-node","id":"{{id}}","label":"{{id}}"}""",
                cancellationToken,
                Harness.At(host.Session.Document.Version));
        }

        // 它带着"我见过第 1 版"回来了，一次请求就补齐。
        using var response = await Harness.ChangesAsync(watcher, 1, cancellationToken, waitSeconds: 0);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var notice = await ReadAsync(response, cancellationToken);

        notice.GetProperty("version").GetInt32().Should().Be(3);
    }

    /// <summary>报的版本已经是最新的，就等——而不是把同一版再回一遍。</summary>
    /// <remarks>
    /// 把同一版再回一遍的话，调用方会以为又变了，于是又读一次摘要，如此循环。
    /// </remarks>
    [Fact]
    [Trait("Category", "ChangeFeed")]
    public async Task A_caller_that_is_already_current_waits()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        using var watcher = Harness.RawClient(host, Harness.FullToken);

        host.Feed.Current().Version.Should().Be(0);

        using var response = await Harness.ChangesAsync(watcher, 0, cancellationToken, waitSeconds: 0);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion

    #region 拒绝

    /// <summary>变化源也要认凭据：文档改了什么，本身就是一条不该白给的信息。</summary>
    [Fact]
    [Trait("Category", "ChangeFeed")]
    public async Task The_feed_is_behind_the_same_guard()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        using var watcher = Harness.RawClient(host, token: null);

        using var response = await Harness.ChangesAsync(watcher, 0, cancellationToken, waitSeconds: 0);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Harness.RejectionCodeAsync(response, cancellationToken))
            .Should().Be(ErrorCodes.McpUnauthorized);
    }

    #endregion

    #region 取数

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return JsonDocument.Parse(text).RootElement.Clone();
    }

    #endregion
}
