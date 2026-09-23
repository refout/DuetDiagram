using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using DuetDiagram.Llm.Tools;
using DuetDiagram.Mcp.Server;

namespace DuetDiagram.Tools.McpHarness;

/// <summary>
/// 十个 agent 打在同一个服务端上并发写入，看文档会不会坏。
/// </summary>
/// <remarks>
/// <para>
/// **真并发，不是顺序跑十次。** 顺序跑验不到版本检查的竞态，而竞态正是这一档要挡的东西：
/// 十个调用方各自读到一个版本、各自以为自己手上的副本是新的，然后一起写。
/// 挡住它的那条判据是"版本对不上就拒绝"，而那条判据只有在真的同时到达时才被考验。
/// </para>
/// <para>
/// **判据是文档仍然校验通过、版本号单调递增**，不是"没有抛异常"。抛异常那条路早就被
/// 挡住了（传输层那一道预判），漏网的是"两个都成功，后一个把前一个盖掉"——
/// 那件事两个调用方都不报错，版本号还会往前走，只有在"成功的次数与最终版本对不上"
/// 或者"有两个成功报了同一个版本"时才看得出来。
/// </para>
/// <para>
/// 十个 agent 各带自己的一份凭据：限流是按凭据算的，共用一份的话十个人挤在一个额度里，
/// 这一档会以限流的形式失败，而失败看起来像并发出了问题。
/// </para>
/// </remarks>
internal static class Agents
{
    /// <summary>同时写入的 agent 个数。</summary>
    private const int AgentCount = 10;

    /// <summary>每个 agent 要写成的次数。</summary>
    private const int WritesPerAgent = 2;

    /// <summary>一次写入最多试几回。撞上冲突就重读重试，所以它比成功次数大得多。</summary>
    private const int AttemptsPerWrite = 40;

    /// <summary>限流之后最多等多久再来。</summary>
    private static readonly TimeSpan MaxThrottleWait = TimeSpan.FromSeconds(30);

    /// <summary>跑这一档。</summary>
    public static async Task<Check> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            return new Check("10 agent 并发", true, await BodyAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return new Check("10 agent 并发", false, $"{ex.GetType().Name}：{ex.Message}");
        }
    }

    private static async Task<string> BodyAsync(CancellationToken cancellationToken)
    {
        var tokens = Enumerable.Range(0, AgentCount).Select(Declare).ToArray();

        await using var session = await HarnessSession
            .HttpAsync(
                new HarnessOptions(HarnessSession.NewWorkspace(), null, tokens),
                Value(0),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var succeeded = new ConcurrentBag<int>();
        var conflicts = 0;
        var busConflicts = 0;
        var throttled = 0;

        // 第一轮齐步走：十个 agent 各自读完版本、都到齐了再一起写。
        // 不齐步走的话，"读到同一个版本"这件事由调度决定，可能十次写入根本不在同一瞬间，
        // 而这一档要挡的正是"同一个版本上同时有多个写入方"。
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;

        await Task.WhenAll(Enumerable
            .Range(0, AgentCount)
            .Select(index => AgentAsync(index)))
            .ConfigureAwait(false);

        // 写完之后另起一条连接看结果：十个 agent 各自看到的都是它们自己的那几次，
        // 只有从一个没参与过的视角读，读到的才是这份文档最终的样子。
        using var verifier = session.RawClient(Value(0));

        var summary = await ReadSummaryAsync(verifier, cancellationToken).ConfigureAwait(false);

        var version = summary.GetProperty("version").GetInt32();
        var nodes = summary.GetProperty("nodes").GetArrayLength();

        var versions = succeeded.Order().ToArray();
        var expected = AgentCount * WritesPerAgent;

        Scenarios.Expect(
            versions.Length == expected,
            $"十个 agent 各要写成 {WritesPerAgent} 次，一共只写成了 {versions.Length} 次");

        Scenarios.Expect(
            versions.SequenceEqual(Enumerable.Range(1, expected)),
            $"成功的版本号不是 1..{expected} 的连续整数：{string.Join('、', versions)}");

        // 齐步走保证了十个调用方第一轮报的是同一个版本，所以那一轮里只能有一个写成功——
        // 其余九个必须被拒。**这条数是确定的，不随调度变**：冲突落在传输层那一道预判上
        // 还是落在命令总线那道锁上由时序决定，但两者加起来至少是九个。
        Scenarios.Expect(
            conflicts + busConflicts >= AgentCount - 1,
            $"十个调用方报同一个版本时只能有一个写成功，其余九个都要被拒"
                + $"（实际传输层 {conflicts} 次、命令层 {busConflicts} 次）");

        Scenarios.Expect(version == expected, $"最终版本是 {version}，而成功写入了 {expected} 次");
        Scenarios.Expect(nodes == expected, $"最终有 {nodes} 个节点，而成功写入了 {expected} 次");

        var issues = await ValidateAsync(verifier, cancellationToken).ConfigureAwait(false);

        Scenarios.Expect(issues == 0, $"并发写完之后文档校验出 {issues} 个问题");

        return $"十个 agent 各写 {WritesPerAgent} 次全部写成；撞上 {conflicts} 次 409、"
            + $"{busConflicts} 次总线级冲突、{throttled} 次限流；"
            + $"版本 1..{version} 逐个不重复，{nodes} 个节点，校验 {issues} 个问题";

        async Task AgentAsync(int index)
        {
            using var client = session.RawClient(Value(index));

            var written = 0;

            for (var attempt = 0; attempt < WritesPerAgent * AttemptsPerWrite && written < WritesPerAgent; attempt++)
            {
                var declared = await ReadVersionAsync(client, cancellationToken).ConfigureAwait(false);

                // 只有第一次写入之前等这一道：到齐之后再一起写，十个调用方报的就是同一个版本。
                if (attempt == 0 && written == 0)
                {
                    if (Interlocked.Increment(ref arrived) == AgentCount)
                    {
                        gate.SetResult();
                    }

                    // 等齐的时限给得宽：正常路径上是毫秒级，机器忙的时候十个任务排上来会慢一截。
                    await gate.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
                }

                var id = $"a{index}-{written}";

                var reply = await HarnessSession
                    .RawCallAsync(
                        client,
                        DiagramToolset.Edit,
                        $$"""{"action":"add-node","id":"{{id}}","label":"{{id}}"}""",
                        cancellationToken,
                        HarnessSession.At(declared))
                    .ConfigureAwait(false);

                if (reply.Status == HttpStatusCode.TooManyRequests)
                {
                    Interlocked.Increment(ref throttled);

                    await ThrottleAsync(reply, cancellationToken).ConfigureAwait(false);

                    continue;
                }

                // 手上的副本旧了。传输层在进协议之前就把这一条挡下来了，
                // 而且带上了一份能拿去追平的内容——重读一次再来。
                if (reply.Status == HttpStatusCode.Conflict)
                {
                    Interlocked.Increment(ref conflicts);

                    Scenarios.Expect(
                        reply.Payload.TryGetProperty("diff", out var diff) && diff.ValueKind == JsonValueKind.Object,
                        "409 里没有那份内容，调用方只能整份重读再猜");

                    continue;
                }

                Scenarios.Expect(
                    reply.Status == HttpStatusCode.OK,
                    $"写入拿到 {(int)reply.Status}：{reply.Text}");

                var result = HarnessSession.ToolResult(reply);

                if (!result.GetProperty("isSuccess").GetBoolean())
                {
                    // 预判与真正执行之间还有一个窗口，那个窗口里的冲突由命令总线在门锁内挡下，
                    // 形状是工具结果里的一条错误。它也是冲突，不是别的问题。
                    var code = result.GetProperty("errors")[0].GetProperty("code").GetString();

                    Scenarios.Expect(
                        code == ConflictResponder.Code,
                        $"写入失败，但错误码不是冲突：{result.GetRawText()}");

                    Interlocked.Increment(ref busConflicts);

                    continue;
                }

                succeeded.Add(result.GetProperty("data").GetProperty("version").GetInt32());

                written++;
            }

            Scenarios.Expect(
                written == WritesPerAgent,
                $"agent-{index} 只写成了 {written} 次，试了 {WritesPerAgent * AttemptsPerWrite} 回");
        }
    }

    /// <summary>读一次摘要里的版本号。</summary>
    private static async Task<int> ReadVersionAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var reply = await HarnessSession
            .RawCallAsync(client, DiagramToolset.Read, "{}", cancellationToken)
            .ConfigureAwait(false);

        Scenarios.Expect(
            reply.Status == HttpStatusCode.OK,
            $"读摘要拿到 {(int)reply.Status}：{reply.Text}");

        return Summary(reply).GetProperty("version").GetInt32();
    }

    /// <summary>从一个没参与过写入的视角读一次摘要。</summary>
    private static async Task<JsonElement> ReadSummaryAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var reply = await HarnessSession
            .RawCallAsync(client, DiagramToolset.Read, "{}", cancellationToken)
            .ConfigureAwait(false);

        Scenarios.Expect(reply.Status == HttpStatusCode.OK, $"读摘要拿到 {(int)reply.Status}：{reply.Text}");

        return Summary(reply);
    }

    /// <summary>校验一次，把问题条数取回来。</summary>
    private static async Task<int> ValidateAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var reply = await HarnessSession
            .RawCallAsync(client, DiagramToolset.Validate, "{}", cancellationToken)
            .ConfigureAwait(false);

        Scenarios.Expect(reply.Status == HttpStatusCode.OK, $"校验拿到 {(int)reply.Status}：{reply.Text}");

        return HarnessSession
            .ToolResult(reply)
            .GetProperty("data")
            .GetProperty("issueCount")
            .GetInt32();
    }

    /// <summary>从一条读的结果里取出结构化摘要。</summary>
    private static JsonElement Summary(HarnessSession.RawReply reply) =>
        HarnessSession.ToolResult(reply).GetProperty("data").GetProperty("summary");

    /// <summary>按服务端给的间隔退避一次。</summary>
    /// <remarks>
    /// 等多久按服务端报的那个数来，不自己定一个：自己定的话，报的间隔比实际需要的短时，
    /// 调用方会在同一秒里再撞一次，而那一轮又会被限流。
    /// </remarks>
    private static async Task ThrottleAsync(HarnessSession.RawReply reply, CancellationToken cancellationToken)
    {
        var seconds = reply.Payload.TryGetProperty("retryAfterSeconds", out var value) ? value.GetInt32() : 1;

        var wait = TimeSpan.FromSeconds(seconds);

        await Task.Delay(wait > MaxThrottleWait ? MaxThrottleWait : wait, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>给第几个 agent 的那份凭据。</summary>
    /// <remarks>
    /// 每个 agent 一份，形态是 <c>名字:权限档:凭据</c>。权限档给最高档——
    /// 这一档要验的是并发，不是权限；被权限挡下来会让失败看起来像并发出了问题。
    /// </remarks>
    private static string Declare(int index) => $"agent-{index}:Full:{Value(index)}";

    private static string Value(int index) => $"harness-agent-{index}-7c41";
}
