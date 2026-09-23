using System.Net;
using System.Text.Json;
using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Llm.Tools;
using DuetDiagram.Mcp.Server;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Mcp.Tests;

/// <summary>
/// 冲突时回什么内容：四种情形各一种，以及它在网络那一档上的样子。
/// </summary>
/// <remarks>
/// <para>
/// 四种情形的分界是**能不能只回差异**。合并任意两种都会让某一类调用方拿到它不需要的全量，
/// 或者拿不到它需要的差异——而两种错法都不会报错，只会在对端那边表现成"同步之后还是对不上"。
/// </para>
/// <para>
/// 后一半用例走真端口，因为「409 + 差异」这件事只有在有状态码的那条通路上才谈得上：
/// 工具调用的失败是协议内的结果，状态码那一层看不见它。
/// </para>
/// </remarks>
public sealed class ConflictResponseTests
{
    #region 四种情形

    [Fact]
    [Trait("Category", "Conflict")]
    public void An_undeclared_caller_gets_a_full_snapshot()
    {
        // 它没说自己停在哪一版，也就无从算出它缺什么。只回一句"缺少版本声明"的话，
        // 它连"该声明哪一版"都问不出来。
        var document = DocumentAt(0);

        var answer = Decide(null, document, new VersionLog());

        answer.Should().BeOfType<FullSnapshotDiff>()
            .Which.FullJson.Should().Contain("conflict-doc");
    }

    [Fact]
    [Trait("Category", "Conflict")]
    public void A_caller_at_the_current_version_gets_nothing()
    {
        var answer = Decide(At(3), DocumentAt(3), new VersionLog());

        answer.Should().BeOfType<EmptyDiff>();
    }

    [Fact]
    [Trait("Category", "Conflict")]
    public void A_caller_inside_the_log_with_a_matching_hash_gets_a_reference()
    {
        // 结构没变过，对端本地的拓扑仍然有效，只需要按清单重取这些元素的属性。
        // 给它全量的话，一次改标签的冲突会换来整张图。
        var answer = Decide(
            new VersionCheckRequest { ClientVersion = 3, ClientStructuralHash = Hash },
            DocumentAt(5),
            Log(5));

        answer.Should().BeOfType<ReferenceDiff>()
            .Which.AffectedIds.Should().Equal("n4", "n5");
    }

    [Fact]
    [Trait("Category", "Conflict")]
    public void A_caller_outside_the_log_gets_a_full_snapshot()
    {
        // 日志是环形的，最早那几版会被挤出去。落在区间外的对端拿到的增量会缺开头，
        // 而缺开头的增量按上去只会得到一份错的图。
        var answer = Decide(At(1), DocumentAt(VersionLogLimits.MaxEntries + 5), Log(VersionLogLimits.MaxEntries + 5));

        answer.Should().BeOfType<FullSnapshotDiff>();
    }

    [Fact]
    [Trait("Category", "Conflict")]
    public void A_caller_inside_the_log_with_a_mismatched_hash_gets_a_full_snapshot()
    {
        // 刻意退成全量，尽管逐条增量算得出来：走到这一步说明对端声明的结构哈希与服务端对不上，
        // 而它的本地副本是否还与它自己声明的那个版本对得上，从这边无从验证。
        // 把字段级的增量按在一份来路不明的副本上，正是产生"静默改错"的方式。
        var answer = Decide(
            new VersionCheckRequest { ClientVersion = 3, ClientStructuralHash = "对不上的哈希" },
            DocumentAt(5),
            Log(5));

        answer.Should().BeOfType<FullSnapshotDiff>();
    }

    [Fact]
    [Trait("Category", "Conflict")]
    public void A_version_ahead_of_the_document_is_not_a_conflict()
    {
        // 报的版本比服务端还新是参数写错了，处置是改参数，不是同步之后重试。
        // 当成冲突的话，它会一遍遍去同步，而同步多少次都改不掉那个写错的数。
        var answer = Decide(At(99), DocumentAt(5), Log(5));

        answer.Should().BeOfType<InvalidDiff>();
    }

    #endregion

    #region 网络那一档

    /// <summary>报了一个旧版本号就拿到 409，而正文里带着能拿去追平的那份内容。</summary>
    [Fact]
    [Trait("Category", "Conflict")]
    public async Task A_stale_declaration_is_refused_with_a_diff()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);

        // 先改一次，让文档走到第 1 版。
        await using var client = await Harness.ConnectAsync(host, cancellationToken: cancellationToken);

        await Harness.CallSucceedsAsync(
            client,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"a","label":"甲"}""",
            cancellationToken,
            Harness.At(0));

        // 再拿一个还停在第 0 版的调用方来改。它应当在协议那一层之前就被挡下。
        using var stale = Harness.RawClient(host, Harness.FullToken);

        using var response = await Harness.PostAsync(
            stale,
            Harness.CallBody(
                DiagramToolset.Edit,
                """{"action":"add-node","id":"b","label":"乙"}""",
                Harness.At(0)),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await Harness.RejectionAsync(response, cancellationToken);

        body.GetProperty("code").GetString().Should().Be(ErrorCodes.VersionConflict);

        // 差异必须带出来：只回一句"版本冲突"的话，代理只能整份重读再重试，
        // 而重试大概率还是冲突。
        body.TryGetProperty("diff", out var diff).Should().BeTrue("409 必须带上差异");
        diff.GetProperty("$diff").GetString().Should().NotBeNullOrEmpty();

        host.Session.Document.Nodes.Should().ContainSingle("版本对不上时那条命令不该落下去");
    }

    /// <summary>没声明版本的写入拿到 409，而正文里是一份全量快照。</summary>
    /// <remarks>
    /// 这是"未声明就回全量"那一条在网络那一档上的样子。原来它回的是一个不带任何内容的
    /// 参数错误，调用方拿它只能去猜自己该声明哪一版。
    /// </remarks>
    [Fact]
    [Trait("Category", "Conflict")]
    public async Task A_write_without_a_declaration_gets_a_full_snapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        using var client = Harness.RawClient(host, Harness.FullToken);

        using var response = await Harness.PostAsync(
            client,
            Harness.CallBody(DiagramToolset.Edit, """{"action":"add-node","id":"a","label":"甲"}"""),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await Harness.RejectionAsync(response, cancellationToken);

        body.GetProperty("diff").GetProperty("$diff").GetString().Should().Be("full-snapshot");
        body.GetProperty("diff").GetProperty("fullJson").GetString().Should().Contain(Harness.DocumentId);

        host.Session.Document.Nodes.Should().BeEmpty("没声明的那次写入不该留下任何痕迹");
    }

    /// <summary>读那一档不带声明也照常放行：它不改文档，也就没有版本可言。</summary>
    [Fact]
    [Trait("Category", "Conflict")]
    public async Task A_read_without_a_declaration_still_goes_through()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        await using var client = await Harness.ConnectAsync(host, cancellationToken: cancellationToken);

        var summary = await Harness.CallSucceedsAsync(client, DiagramToolset.Read, "{}", cancellationToken);

        summary.GetProperty("data").GetProperty("summary").GetProperty("version").GetInt32().Should().Be(0);
    }

    /// <summary>声明与当前版本一致时照常放行，不该被那道预判误伤。</summary>
    [Fact]
    [Trait("Category", "Conflict")]
    public async Task A_current_declaration_goes_through()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        using var client = Harness.RawClient(host, Harness.FullToken);

        using var response = await Harness.PostAsync(
            client,
            Harness.CallBody(
                DiagramToolset.Edit,
                """{"action":"add-node","id":"a","label":"甲"}""",
                Harness.At(0)),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Session.Document.Nodes.Should().ContainSingle();
    }

    /// <summary>撤销重做不接受版本声明，那道预判不该挡它。</summary>
    /// <remarks>
    /// 它走的是历史栈，不检查声明。对着一个旧版本撤销是调用方自己的判断，
    /// 替它挡下来只会让它无从知道该怎么撤。
    /// </remarks>
    [Fact]
    [Trait("Category", "Conflict")]
    public async Task Undo_is_not_version_checked()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(Harness.Options(Harness.NewWorkspace()), cancellationToken);
        using var client = Harness.RawClient(host, Harness.FullToken);

        using var first = await Harness.PostAsync(
            client,
            Harness.CallBody(
                DiagramToolset.Edit,
                """{"action":"add-node","id":"a","label":"甲"}""",
                Harness.At(0)),
            cancellationToken);

        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // 报的是第 0 版，而文档已经是第 1 版了。撤销照样进得去。
        using var undo = await Harness.PostAsync(
            client,
            Harness.CallBody(DiagramToolset.UndoRedo, """{"action":"undo"}""", Harness.At(0)),
            cancellationToken);

        undo.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Session.Document.Nodes.Should().BeEmpty();
    }

    #endregion

    #region 取数

    /// <summary>造一份停在第 <paramref name="version"/> 版的文档，结构哈希写成 <see cref="Hash"/>。</summary>
    /// <remarks>
    /// 走反序列化用的那个构造器而不是命令总线：这一组验的是"分支对不对"，
    /// 而分支只看版本号与两个哈希的相等关系，不看它们是怎么来的。
    /// </remarks>
    private static DiagramDocument DocumentAt(int version) => new(
        "conflict-doc",
        DiagramKind.Flowchart,
        Direction.LR,
        version,
        Hash,
        visualHash: string.Empty,
        pages: null,
        layers: null,
        nodes: null,
        edges: null,
        composites: null,
        tags: null,
        actions: null,
        fonts: null,
        textPresets: null,
        palette: null,
        layout: null,
        canvas: null);

    /// <summary>文档这一刻的结构哈希。要非空，否则"只回清单"那条捷径根本不会被考虑。</summary>
    private const string Hash = "hash-at-current";

    private static VersionCheckRequest At(int version) => new() { ClientVersion = version };

    /// <summary>造一份有 <paramref name="count"/> 条记录的版本日志，每条波及一个节点。</summary>
    private static VersionLog Log(int count)
    {
        var log = new VersionLog();

        for (var version = 1; version <= count; version++)
        {
            log.Record(new VersionEntry
            {
                Version = version,
                CommandId = "add-node",
                Source = ChangeSource.Mcp,
                Timestamp = DateTimeOffset.UnixEpoch,
                AffectedIds = [$"n{version}"],
            });
        }

        return log;
    }

    /// <summary>直接问判定器该回什么。</summary>
    /// <param name="declared">调用方声明的版本。为空表示没声明。</param>
    /// <param name="document">服务端这一刻的文档。</param>
    /// <param name="log">版本日志。</param>
    private static DiffResult Decide(VersionCheckRequest? declared, DiagramDocument document, VersionLog log) =>
        ConflictResponder.Decide(declared, document, log, () => DiagramSerializer.SerializeFull(document));

    #endregion
}
