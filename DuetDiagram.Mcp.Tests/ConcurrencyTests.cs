using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Llm.Tools;
using DuetDiagram.Mcp.Server;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Mcp.Tests;

/// <summary>
/// 十个 agent 同时写同一份文档。
/// </summary>
/// <remarks>
/// <para>
/// **要的是真并发，不是顺序跑十次。** 顺序跑的时候前一次写入在第二次读之前就已经落定，
/// 版本检查那条路上从头到尾只有一个写入方——而它要挡的恰恰是同时到达的那几个。
/// 所以这里十个客户端各自循环，一起打在同一个服务端实例上。
/// </para>
/// <para>
/// **判据是文档没坏，不是"没有抛异常"。** 抛异常那条路早就被传输层那一道预判挡住了；
/// 漏网的是「两个都成功，后一个把前一个盖掉」——那件事两个调用方都不报错，
/// 版本号还照样往前走，只有在"成功的次数与最终版本对不上"或者
/// "两条成功报了同一个版本"时才看得出来。所以最后要回到文档本身去核：
/// 节点数、版本号、以及整体校验。
/// </para>
/// <para>
/// 走裸请求而不是客户端库：客户端库会把非 2xx 当成一次调用失败抛出来，
/// 而这一组要数的正是拿到了几次 409。
/// </para>
/// </remarks>
public sealed class ConcurrencyTests
{
    /// <summary>同时写入的 agent 个数。</summary>
    private const int AgentCount = 10;

    /// <summary>每个 agent 要写成的次数。</summary>
    private const int WritesPerAgent = 2;

    /// <summary>一次写入最多试几回。撞上冲突就重读重试，所以它比成功次数大得多。</summary>
    private const int AttemptsPerWrite = 40;

    [Fact]
    [Trait("Category", "MultiAgent")]
    public async Task Ten_agents_writing_at_once_do_not_corrupt_the_document()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var host = await Harness.StartAsync(
            // 频次上限抬到这一组够用的高度：限流不是这一组要验的东西，而十个人共用一份凭据，
            // 撞上限流之后失败看起来像并发出了问题——排查方向会被带偏。
            Harness.Options(Harness.NewWorkspace(), requestsPerMinute: 100_000),
            cancellationToken);

        var succeeded = new ConcurrentBag<int>();
        var conflicts = 0;
        var busConflicts = 0;

        // 第一轮齐步走：十个 agent 各自读完版本、都到齐了再一起写。
        // 不齐步走的话，"读到同一个版本"这件事由调度决定，可能十次写入根本不在同一瞬间，
        // 而这一组要挡的正是"同一个版本上同时有多个写入方"。
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;

        await Task.WhenAll(Enumerable.Range(0, AgentCount).Select(AgentAsync));

        var expected = AgentCount * WritesPerAgent;
        var versions = succeeded.Order().ToArray();

        // 无损坏之一：每一次成功的写入都在文档上留下了痕迹。
        versions.Should().HaveCount(expected, $"十个 agent 各要写成 {WritesPerAgent} 次");

        // 版本单调递增：成功的版本号正好是 1 到成功次数。重号说明两条写入落在了同一个版本上
        // （后一个把前一个盖掉了），跳号说明有一版是没人认领的。
        versions.Should().Equal(
            Enumerable.Range(1, expected),
            "成功的版本号要正好是 1 到成功次数，既不重号也不跳号");

        // 齐步走保证了十个调用方第一轮报的是同一个版本，所以那一轮里只能有一个写成功——
        // 其余九个必须被拒。**这条数是确定的，不随调度变**：冲突落在传输层那一道预判上
        // 还是落在命令总线那道锁上由时序决定，但两者加起来至少是九个。
        // 一条冲突都没有的话，说明十次写入是排着队完成的，那样这一组验的东西与顺序跑没有分别。
        var refused = conflicts + busConflicts;

        refused.Should().BeGreaterThanOrEqualTo(
            AgentCount - 1,
            $"十个调用方报同一个版本时只能有一个写成功，其余九个都要被拒（这一轮传输层 {conflicts} 次、命令层 {busConflicts} 次）");

        // 无损坏之二：回到文档本身。前面几条核的是"调用方各自看到了什么"，
        // 这一条核的是"文档最后是什么样"——两条路都要走，因为版本号对得上、
        // 而文档里少了几个节点这种事是可能的。
        host.Session.Document.Version.Should().Be(expected, "每一次成功的写入各推进一版");
        host.Session.Document.Nodes.Should().HaveCount(expected, "每一次成功的写入各加一个节点");
        DiagramValidator.Validate(host.Session.Document).Should().BeEmpty("并发写完之后文档要仍然校验通过");

        async Task AgentAsync(int index)
        {
            using var client = Harness.RawClient(host, Harness.FullToken);

            var written = 0;

            for (var attempt = 0; attempt < WritesPerAgent * AttemptsPerWrite && written < WritesPerAgent; attempt++)
            {
                var declared = await ReadVersionAsync(client, cancellationToken);

                if (attempt == 0 && written == 0)
                {
                    if (Interlocked.Increment(ref arrived) == AgentCount)
                    {
                        gate.SetResult();
                    }

                    // 等齐的时限给得宽：正常路径上这一步是毫秒级，而机器忙的时候
                    // 十个任务排上来可能慢一截。给窄了会变成"偶发失败"，而偶发失败最容易被
                    // 当成"并发本来就不稳"，于是这一条会被绕过去。
                    await gate.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
                }

                var id = $"a{index}-{written}";

                var reply = await Harness.RawAsync(
                    client,
                    DiagramToolset.Edit,
                    $$"""{"action":"add-node","id":"{{id}}","label":"{{id}}"}""",
                    cancellationToken,
                    Harness.At(declared));

                // 手上的副本旧了。传输层在进协议之前就把它挡下来了，而且带了一份能追平的内容。
                if (reply.Status == HttpStatusCode.Conflict)
                {
                    Interlocked.Increment(ref conflicts);

                    reply.Payload.TryGetProperty("diff", out var diff).Should().BeTrue("409 要带上能追平的那份内容");
                    diff.ValueKind.Should().Be(
                        JsonValueKind.Object,
                        "只回一句「冲突」的话，调用方只能整份重读再重试，而重试大概率还是冲突");

                    continue;
                }

                reply.Status.Should().Be(HttpStatusCode.OK, $"写入拿到 {(int)reply.Status}：{reply.Text}");

                var result = Harness.ToolResultOf(reply);

                if (!result.GetProperty("isSuccess").GetBoolean())
                {
                    // 预判与真正执行之间还有一个窗口，落在那个窗口里的冲突由命令总线在门锁内挡下，
                    // 形状是工具结果里的一条错误。它也是冲突，不是别的问题。
                    result.GetProperty("errors")[0].GetProperty("code").GetString()
                        .Should().Be(ErrorCodes.VersionConflict, "写入失败时应当是版本冲突");

                    Interlocked.Increment(ref busConflicts);

                    continue;
                }

                succeeded.Add(result.GetProperty("data").GetProperty("version").GetInt32());

                written++;
            }

            written.Should().Be(WritesPerAgent, $"agent-{index} 只写成了 {written} 次");
        }

        async Task<int> ReadVersionAsync(HttpClient client, CancellationToken token)
        {
            var reply = await Harness.RawAsync(client, DiagramToolset.Read, "{}", token);

            reply.Status.Should().Be(HttpStatusCode.OK, $"读摘要拿到 {(int)reply.Status}：{reply.Text}");

            return Harness
                .ToolResultOf(reply)
                .GetProperty("data")
                .GetProperty("summary")
                .GetProperty("version")
                .GetInt32();
        }
    }
}
