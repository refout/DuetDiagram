using System.Net;
using System.Text.Json;
using DuetDiagram.Core.Commands;
using DuetDiagram.Llm.Tools;
using DuetDiagram.Mcp.Server;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DuetDiagram.Tools.McpHarness;

/// <summary>一项检查的结果。</summary>
/// <param name="Name">这一项验的是什么。</param>
/// <param name="Passed">过没过。</param>
/// <param name="Detail">过或不过的一句话说明，带观察到的数字。</param>
internal sealed record Check(string Name, bool Passed, string Detail);

/// <summary>
/// §15.2 那张 MCP 清单上的各项，逐条在协议层跑一遍。
/// </summary>
/// <remarks>
/// <para>
/// **跑的是真的可执行文件。** 这一层要回答的是「拿出去能不能用」，所以它拉的是
/// `DuetDiagram.Mcp` 那个子进程，走的是命令行那条通路——参数解析、端口、日志改道
/// 三样都在那条通路上，而它们都只有真的起一次才发现。
/// </para>
/// <para>
/// 每一项各起自己的服务端实例，互相不干扰：限流那一项会把一份凭据的额度用光，
/// 与别的项共用一个实例的话，后面的项会因为"上一项把额度花完了"而失败。
/// </para>
/// </remarks>
internal static class Scenarios
{
    /// <summary>能做任何事的凭据。</summary>
    public const string FullToken = "harness-full-5d21";

    /// <summary>只被允许读的凭据。</summary>
    public const string ReadToken = "harness-read-9a04";

    /// <summary>凭据表：两份，形态是 <c>名字:权限档:凭据</c>。</summary>
    public static IReadOnlyList<string> Tokens =>
        [$"writer:Full:{FullToken}", $"reader:Read:{ReadToken}"];

    /// <summary>把八项跑一遍。</summary>
    public static async Task<IReadOnlyList<Check>> RunAsync(CancellationToken cancellationToken)
    {
        var checks = new List<Check>
        {
            await GuardAsync("协议一致", () => AgreementAsync(cancellationToken)),
            await GuardAsync("8 工具参数校验", () => ArgumentValidationAsync(cancellationToken)),
            await Agents.RunAsync(cancellationToken),
            await GuardAsync("409 + diff", () => ConflictAsync(cancellationToken)),
            await GuardAsync("只读无法写", () => ReadOnlyAsync(cancellationToken)),
            await GuardAsync("速率限制", () => RateLimitAsync(cancellationToken)),
            await GuardAsync("审计日志", () => AuditAsync(cancellationToken)),
            await GuardAsync("clientState 握手", () => HandshakeAsync(cancellationToken)),
        };

        return checks;
    }

    /// <summary>两条传输挂上去的工具是不是同一份，以及是不是那八个。</summary>
    private static async Task<string> AgreementAsync(CancellationToken cancellationToken)
    {
        string[] expected =
        [
            DiagramToolset.Read,
            DiagramToolset.Edit,
            DiagramToolset.Style,
            DiagramToolset.Layout,
            DiagramToolset.Composite,
            DiagramToolset.Export,
            DiagramToolset.Validate,
            DiagramToolset.UndoRedo,
        ];

        await using var stdio = await HarnessSession.StdioAsync(cancellationToken: cancellationToken);

        await using var http = await HarnessSession.HttpAsync(
            new HarnessOptions(HarnessSession.NewWorkspace(), null, Tokens),
            FullToken,
            cancellationToken: cancellationToken);

        var viaStdio = await stdio.Client.ListToolsAsync(cancellationToken: cancellationToken);
        var viaHttp = await http.Client.ListToolsAsync(cancellationToken: cancellationToken);

        // 按名字排序之后比：清单的次序是协议实现那边决定的，与挂上去的次序无关，
        // 而这一项要验的是"挂上去的是哪几个"，不是"它们按什么次序回来"。
        foreach (var (transport, tools) in new[] { (stdio.Transport, viaStdio), (http.Transport, viaHttp) })
        {
            var names = tools.Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray();

            Expect(
                names.SequenceEqual(expected.Order(StringComparer.Ordinal)),
                $"{transport} 上挂的不是那八个工具：{string.Join('、', names)}");

            foreach (var tool in tools)
            {
                Expect(!string.IsNullOrWhiteSpace(tool.Description), $"{transport} 上 {tool.Name} 没有描述");
                Expect(
                    tool.JsonSchema.TryGetProperty("properties", out _),
                    $"{transport} 上 {tool.Name} 没有参数表");
            }
        }

        // 两条传输说的是同一份。各挂一份的话，同一条调用换一条传输的 schema 就不一样，
        // 而模型侧照着哪一份写都可能对不上。
        Expect(
            viaStdio.Select(Shape).Order(StringComparer.Ordinal)
                .SequenceEqual(viaHttp.Select(Shape).Order(StringComparer.Ordinal)),
            "两条传输上的名称、描述与参数 schema 不一致");

        return $"两条传输各 {viaStdio.Count} 个工具，名称、描述与参数 schema 逐字相同";

        static string Shape(McpClientTool tool) =>
            $"{tool.Name}\u0000{tool.Description}\u0000{tool.JsonSchema.GetRawText()}";
    }

    /// <summary>八个工具各自把坏参数挡成一条带错误码的结构化错误。</summary>
    private static async Task<string> ArgumentValidationAsync(CancellationToken cancellationToken)
    {
        await using var session = await HarnessSession.StdioAsync(cancellationToken: cancellationToken);

        (string Tool, string Arguments)[] bad =
        [
            (DiagramToolset.Read, """{"pageId":"不是标识"}"""),
            (DiagramToolset.Edit, """{"action":"不是动作"}"""),
            (DiagramToolset.Style, """{"action":"不是动作"}"""),
            (DiagramToolset.Layout, """{"action":"不是动作"}"""),
            (DiagramToolset.Composite, """{"action":"不是动作"}"""),
            (DiagramToolset.Export, """{"format":"不是格式"}"""),
            (DiagramToolset.Validate, """{"scope":"整份"}"""),
            (DiagramToolset.UndoRedo, """{"action":"不是动作"}"""),
        ];

        foreach (var (tool, arguments) in bad)
        {
            var payload = await HarnessSession.CallAsync(session.Client, tool, arguments, cancellationToken);

            Expect(
                !payload.GetProperty("isSuccess").GetBoolean(),
                $"{tool} 收到坏参数却报了成功");

            Expect(
                payload.TryGetProperty("errors", out var errors)
                    && errors.GetArrayLength() > 0
                    && errors[0].TryGetProperty("code", out var code)
                    && code.GetString() is { Length: > 0 },
                $"{tool} 的失败里没有错误码");
        }

        // 好参数要照常通过。只挡坏的不放好的，等于把八个工具全废掉，而这一项照样绿。
        var read = await HarnessSession.CallAsync(session.Client, DiagramToolset.Read, "{}", cancellationToken);

        Expect(read.GetProperty("isSuccess").GetBoolean(), "diagram_read 带空参数应当成功");

        return $"八个工具各回一条带错误码的结构化错误；好参数照常通过";
    }

    /// <summary>手上那份旧了的时候回的是 409 加一份内容，不是一句「冲突」。</summary>
    private static async Task<string> ConflictAsync(CancellationToken cancellationToken)
    {
        await using var session = await HarnessSession.HttpAsync(
            new HarnessOptions(HarnessSession.NewWorkspace(), null, Tokens),
            FullToken,
            cancellationToken: cancellationToken);

        await HarnessSession.CallAsync(
            session.Client,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"a","label":"甲"}""",
            cancellationToken,
            HarnessSession.At(0));

        using var raw = session.RawClient(FullToken);

        using var response = await HarnessSession.PostAsync(
            raw,
            HarnessSession.CallBody(
                DiagramToolset.Edit,
                """{"action":"add-node","id":"b","label":"乙"}""",
                HarnessSession.At(0)),
            cancellationToken);

        Expect(
            response.StatusCode == HttpStatusCode.Conflict,
            $"旧声明应当回 409，拿到的是 {(int)response.StatusCode}");

        var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken)).RootElement;

        Expect(
            body.GetProperty("code").GetString() == ConflictResponder.Code,
            $"409 的错误码不是 {ConflictResponder.Code}");

        // 只回一句「冲突」的话，代理只能整份重读再重试，而重试大概率还是冲突。
        Expect(
            body.TryGetProperty("diff", out var diff) && diff.ValueKind == JsonValueKind.Object,
            "409 的正文里没有那份内容");

        return $"409 带 {ConflictResponder.Code} 与一份 {diff.ValueKind} 的内容";
    }

    /// <summary>只读那一档连改都不许，且拒的是权限码而不是别的东西。</summary>
    private static async Task<string> ReadOnlyAsync(CancellationToken cancellationToken)
    {
        await using var session = await HarnessSession.HttpAsync(
            new HarnessOptions(HarnessSession.NewWorkspace(), null, Tokens),
            ReadToken,
            cancellationToken: cancellationToken);

        using var raw = session.RawClient(ReadToken);

        using var response = await HarnessSession.PostAsync(
            raw,
            HarnessSession.CallBody(
                DiagramToolset.Edit,
                """{"action":"add-node","id":"b"}""",
                HarnessSession.At(0)),
            cancellationToken);

        Expect(
            response.StatusCode == HttpStatusCode.Forbidden,
            $"只读凭据改文档应当回 403，拿到的是 {(int)response.StatusCode}");

        var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken)).RootElement;

        Expect(
            body.GetProperty("code").GetString() == BearerAuth.ForbiddenCode,
            $"403 的错误码不是 {BearerAuth.ForbiddenCode}");

        // 读那一档照常能用：把只读实现成"什么都干不了"也是一种过，而它不是本意。
        var read = await HarnessSession.CallAsync(
            session.Client, DiagramToolset.Read, "{}", cancellationToken);

        Expect(read.GetProperty("isSuccess").GetBoolean(), "只读凭据应当读得到");

        return $"写回 403 加 {BearerAuth.ForbiddenCode}，读照常";
    }

    /// <summary>一分钟里发得太密时回 429，且带上一个可以照着退避的间隔。</summary>
    private static async Task<string> RateLimitAsync(CancellationToken cancellationToken)
    {
        await using var session = await HarnessSession.HttpAsync(
            new HarnessOptions(HarnessSession.NewWorkspace(), null, Tokens),
            FullToken,
            cancellationToken: cancellationToken);

        using var raw = session.RawClient(FullToken);

        // 上限是每分钟六十次。撞到之前一直发，撞到之后把那一份留下来看。
        HttpResponseMessage? limited = null;
        string? body = null;
        var attempts = 0;

        for (; attempts < 90 && limited is null; attempts++)
        {
            var response = await HarnessSession.PostAsync(
                raw,
                HarnessSession.CallBody(DiagramToolset.Read, "{}"),
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                limited = response;
                body = await response.Content.ReadAsStringAsync(cancellationToken);

                break;
            }

            response.Dispose();
        }

        Expect(limited is not null, "发了九十次都没有被限流");
        Expect(limited!.Headers.RetryAfter is not null, "429 的响应头里没有重试间隔");

        var payload = JsonDocument.Parse(body!).RootElement;

        Expect(
            payload.GetProperty("code").GetString() == RateLimiter.RateLimitedCode,
            $"429 的错误码不是 {RateLimiter.RateLimitedCode}");

        // 间隔两个地方都要有：响应头给通用客户端，正文给只解析正文的那一类。
        Expect(
            payload.TryGetProperty("retryAfterSeconds", out var seconds) && seconds.GetInt32() >= 1,
            "429 的正文里没有可以照着退避的间隔");

        // 数的是这一份凭据上的第几次请求：会话建立那一次也算在里面，
        // 因为限流就是按凭据算的。报"第几次"而不是"发了几次"，读的人才能与上限对上。
        return $"这一份凭据上第 {attempts + 1} 次请求撞上限流，429 带 {seconds.GetInt32()} 秒的重试间隔";
    }

    /// <summary>每一条请求留一条审计，且凭据本身与完整路径都不进去。</summary>
    private static async Task<string> AuditAsync(CancellationToken cancellationToken)
    {
        var workspace = HarnessSession.NewWorkspace();

        await using var session = await HarnessSession.HttpAsync(
            new HarnessOptions(workspace, null, Tokens),
            FullToken,
            cancellationToken: cancellationToken);

        await HarnessSession.CallAsync(session.Client, DiagramToolset.Read, "{}", cancellationToken);

        // 被挡下的那一条也要留一条。只记成功的调用等于没有审计，
        // 而真正要看的是「谁在反复撞门」——那一条恰恰从不成功。
        using (var anonymous = session.RawClient(null))
        {
            using var rejected = await HarnessSession.PostAsync(
                anonymous,
                HarnessSession.CallBody(DiagramToolset.Read, "{}"),
                cancellationToken);

            Expect(
                rejected.StatusCode == HttpStatusCode.Unauthorized,
                $"不带凭据应当回 401，拿到的是 {(int)rejected.StatusCode}");
        }

        var log = await WaitForAsync(
            session.Log,
            text => text.Contains("[audit]", StringComparison.Ordinal)
                && text.Contains("<anonymous>", StringComparison.Ordinal),
            cancellationToken);

        Expect(
            !log.Contains(FullToken, StringComparison.Ordinal),
            "日志里出现了凭据本身——任何能看到日志的人都能拿它去冒充");
        Expect(
            !log.Contains(workspace, StringComparison.Ordinal),
            "日志里出现了完整的工作区路径——它把这台机器上的目录结构画了出来");

        var lines = log.Split('\n').Count(line => line.Contains("[audit]", StringComparison.Ordinal));

        return $"留了 {lines} 条审计，含一条认不出是谁的；凭据与完整路径都不在里面";
    }

    /// <summary>会话应答挂在能力扩展位上，两条传输都有；而它是一份快照，不是活值。</summary>
    private static async Task<string> HandshakeAsync(CancellationToken cancellationToken)
    {
        // 拿一份有内容的文档起两条会话：空文档的结构哈希是空的，
        // 那样"上报了结构哈希"这条判据就成了一句空话。
        var workspace = HarnessSession.NewWorkspace();
        var name = HarnessSession.WriteDocument(workspace, "harness-document");

        await using var stdio = await HarnessSession.StdioAsync(
            Path.Combine(workspace, name),
            cancellationToken: cancellationToken);

        await using var http = await HarnessSession.HttpAsync(
            new HarnessOptions(workspace, name, Tokens),
            FullToken,
            cancellationToken: cancellationToken);

        string? hash = null;

        foreach (var session in new[] { stdio, http })
        {
            var ack = Ack(session.Client);

            Expect(
                ack.DocumentId == "harness-document",
                $"{session.Transport} 上报的文档标识是 {ack.DocumentId}");
            Expect(ack.Version == 0, $"{session.Transport} 上报的版本是 {ack.Version}");
            Expect(
                ack.StructuralHash.Length > 0,
                $"{session.Transport} 没有上报结构哈希——它读不出文档标识也读不出结构哈希，"
                + "而调用方靠它判断要不要重读整份");

            hash = ack.StructuralHash;
        }

        // 应答是会话建立那一刻的快照。改一次之后它还是零——所以追平要靠**每条请求的声明**，
        // 而不是靠它。拿它当"当前版本"用的话，第二次写入会被判成冲突。
        await HarnessSession.CallAsync(
            http.Client,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"a","label":"甲"}""",
            cancellationToken,
            HarnessSession.At(0));

        using var raw = http.RawClient(FullToken);

        using var stale = await HarnessSession.PostAsync(
            raw,
            HarnessSession.CallBody(
                DiagramToolset.Edit,
                """{"action":"add-node","id":"b"}""",
                HarnessSession.At(Ack(http.Client).Version)),
            cancellationToken);

        Expect(
            stale.StatusCode == HttpStatusCode.Conflict,
            $"拿应答里那个版本去改应当被判成冲突，拿到的是 {(int)stale.StatusCode}");

        return $"两条传输都上报了文档标识、版本与结构哈希（{hash![..8]}…）；"
            + "应答是快照，追平靠每条请求的声明";
    }

    /// <summary>把一条检查包起来：抛出来的异常算这一项没过，不打断后面的项。</summary>
    private static async Task<Check> GuardAsync(string name, Func<Task<string>> body)
    {
        try
        {
            return new Check(name, true, await body().ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return new Check(name, false, $"{ex.GetType().Name}：{ex.Message}");
        }
    }

    /// <summary>断言。不过就抛，由上面那一层收成"这一项没过"。</summary>
    internal static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>从能力扩展位里把会话应答读回来。</summary>
    private static SessionAck Ack(McpClient client)
    {
        var extensions = client.ServerCapabilities?.Extensions;

        Expect(
            extensions is not null && extensions.ContainsKey(SessionState.CapabilityKey),
            "能力声明里没有会话应答那一位");

        var ack = SessionAck.FromExtensionValue(extensions![SessionState.CapabilityKey]);

        Expect(ack is not null, "会话应答那一位读不出来");

        return ack!;
    }

    /// <summary>等日志里出现满足条件的行。</summary>
    private static async Task<string> WaitForAsync(
        ErrorLog log,
        Func<string, bool> match,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            var text = log.All();

            if (match(text))
            {
                return text;
            }

            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException($"等不到符合条件的日志。已经收到的：{log.All()}");
    }
}
