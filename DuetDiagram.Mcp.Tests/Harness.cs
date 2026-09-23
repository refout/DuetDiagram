using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using DuetDiagram.Core.Diagnostics;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Mcp.Server;
using FluentAssertions;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DuetDiagram.Mcp.Tests;

/// <summary>
/// 服务端写在标准错误上的行。
/// </summary>
/// <remarks>
/// 这是看服务端到底收到了什么**唯一**的窗口：它跑在另一个进程里，父进程拿不到它的内存；
/// 而标准输出被协议占用，不能借。回调可能在会话建好之前就被触发，所以这个收集器
/// 要在拉传输之前就建好，不能挂在会话对象上。
/// </remarks>
internal sealed class ErrorLog
{
    private readonly List<string> _lines = [];
    private readonly Lock _gate = new();

    public void Add(string line)
    {
        lock (_gate)
        {
            _lines.Add(line);
        }
    }

    public string[] Lines
    {
        get
        {
            lock (_gate)
            {
                return [.. _lines];
            }
        }
    }

    /// <summary>服务端记下的请求行。会话建立那一次也在里面。</summary>
    public string[] Requests() => [.. Lines.Where(line => line.Contains("收到 ", StringComparison.Ordinal))];

    /// <summary>等到第一行满足条件的记录出现。</summary>
    public async Task<string> WaitAsync(Func<string, bool> match, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            var found = Lines.FirstOrDefault(match);

            if (found is not null)
            {
                return found;
            }

            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException($"等不到符合条件的日志行。已经收到的：{string.Join(" / ", Lines)}");
    }
}

/// <summary>
/// 一个代理会话：拉起来的服务端子进程，以及连上它的客户端。
/// </summary>
internal sealed class AgentSession : IAsyncDisposable
{
    private AgentSession(McpClient client, ErrorLog log)
    {
        Client = client;
        Log = log;
    }

    public McpClient Client { get; }

    /// <summary>服务端的标准错误。</summary>
    public ErrorLog Log { get; }

    public static async Task<AgentSession> ConnectAsync(
        string? documentPath = null,
        SessionState? declaration = null,
        CancellationToken cancellationToken = default)
    {
        var log = new ErrorLog();

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "duetdiagram-agent",
            Command = Harness.ServerPath(),
            Arguments = documentPath is null ? [] : [Program.DocumentSwitch, documentPath],
            StandardErrorLines = log.Add,
        });

        var client = await McpClient.CreateAsync(
            transport,
            Harness.ClientOptions(declaration),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new AgentSession(client, log);
    }

    public async ValueTask DisposeAsync() => await Client.DisposeAsync().ConfigureAwait(false);
}

/// <summary>
/// 一条裸调用的答复。
/// </summary>
/// <param name="Status">HTTP 状态码。冲突、限流、拒绝都只有在这一层才看得出来。</param>
/// <param name="Payload">正文。拒绝那一档是错误正文，成功那一档是协议信封。</param>
/// <param name="Text">原始正文。要把它打进失败说明里的时候用。</param>
internal sealed record RawReply(HttpStatusCode Status, JsonElement Payload, string Text);

/// <summary>
/// 用例要用的固定环境：服务端可执行文件在哪、客户端怎么声明会话状态、结果怎么读。
/// </summary>
internal static class Harness
{
    /// <summary>服务端从空图开始那一份文档的标识。</summary>
    public const string DocumentId = "mcp-document";

    #region 标准输入输出

    /// <summary>
    /// 找到服务端的可执行文件。
    /// </summary>
    /// <remarks>
    /// 工程引用会把服务端编译出来，但它的可执行文件未必落在测试的输出目录里，
    /// 所以先在本目录找、再按当前配置与目标框架去服务端自己的输出目录找。
    /// 两处都没有就直接报错，不要静默跳过——静默跳过的话这一组用例会变成
    /// 「零个用例通过」，而那是查不出来的。
    /// </remarks>
    public static string ServerPath()
    {
        var name = OperatingSystem.IsWindows() ? "DuetDiagram.Mcp.exe" : "DuetDiagram.Mcp";

        var beside = Path.Combine(AppContext.BaseDirectory, name);

        if (File.Exists(beside))
        {
            return beside;
        }

        var output = new DirectoryInfo(AppContext.BaseDirectory);
        var framework = output.Name;
        var configuration = output.Parent?.Name ?? "Release";

        var built = Path.Combine(RepositoryRoot(), "DuetDiagram.Mcp", "bin", configuration, framework, name);

        return File.Exists(built)
            ? built
            : throw new FileNotFoundException($"找不到服务端的可执行文件，找过 {beside} 与 {built}。");
    }

    /// <summary>客户端选项。声明了会话状态就带上，不带就是"这个调用方什么都没说"。</summary>
    public static McpClientOptions ClientOptions(SessionState? declaration)
    {
        var options = new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "duetdiagram-test-agent", Version = "0.1.0" },
        };

        if (declaration is not null)
        {
            options.Capabilities = new ClientCapabilities
            {
                Extensions = new Dictionary<string, object>
                {
                    [SessionState.CapabilityKey] = declaration.ToJson().ToJsonString(),
                },
            };
        }

        return options;
    }

    /// <summary>调一个工具，把返回的那段 JSON 读出来。</summary>
    /// <param name="declaration">
    /// 这一次调用自带的会话声明。会话建立时声明一次只够改一次：每改一次版本号就往前一格，
    /// 所以后续调用要把新版本号随调用带上来。
    /// </param>
    public static async Task<JsonElement> CallAsync(
        McpClient client,
        string tool,
        string arguments,
        CancellationToken cancellationToken,
        SessionState? declaration = null)
    {
        var result = await client.CallToolAsync(
            tool,
            JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments),
            options: declaration is null
                ? null
                : new RequestOptions { Meta = new JsonObject { [SessionState.CapabilityKey] = declaration.ToJson() } },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
            ?? throw new InvalidOperationException($"工具 {tool} 没有返回文本内容。");

        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>调一个工具并断言它成功了。</summary>
    public static async Task<JsonElement> CallSucceedsAsync(
        McpClient client,
        string tool,
        string arguments,
        CancellationToken cancellationToken,
        SessionState? declaration = null)
    {
        var payload = await CallAsync(client, tool, arguments, cancellationToken, declaration).ConfigureAwait(false);

        payload.GetProperty("isSuccess").GetBoolean()
            .Should().BeTrue($"这次调用本意是成功的，而它回的是 {payload.GetRawText()}");

        return payload;
    }

    /// <summary>一份声明了当前版本的会话状态。</summary>
    public static SessionState At(int version, string documentId = DocumentId) =>
        new(documentId, version, StructuralHash: null);

    /// <summary>把一份空文档写进临时文件，返回路径。</summary>
    public static string WriteDocument(string directory, string id)
    {
        var path = Path.Combine(directory, $"{id}.json");

        File.WriteAllText(path, DiagramSerializer.SerializeFull(new DiagramDocument(id)));

        return path;
    }

    #endregion

    #region 网络那一档

    /// <summary>一份能做任何事的凭据。用例按它调工具。</summary>
    public const string FullToken = "tok-full-2f9a";

    /// <summary>一份只被允许读的凭据。</summary>
    public const string ReadToken = "tok-read-7c31";

    /// <summary>一份能改、但只被允许改 <see cref="ScopedLayer"/> 那一层的凭据。</summary>
    public const string ScopedToken = "tok-scoped-4b8e";

    /// <summary>上面那份凭据能碰的图层。</summary>
    public const string ScopedLayer = "public";

    /// <summary>一份能改、但够不着的图层。</summary>
    public const string ForbiddenLayer = "secret";

    /// <summary>这个用例自己的工作区。</summary>
    public static string Workspace() => NewWorkspace();

    /// <summary>
    /// 起一个网络服务端。
    /// </summary>
    /// <remarks>
    /// 频次上限默认给得很宽：用例关心的是别的事，撞上限流会让失败看起来像另一个问题。
    /// 只有限流那一条自己把上限压下来。
    /// </remarks>
    public static HttpHostOptions Options(
        string workspace,
        string? document = null,
        int requestsPerMinute = 1000,
        TimeSpan? callTimeout = null,
        TimeSpan? changeWait = null,
        IDiagnosticsSink? diagnostics = null) => new()
        {
            Workspace = workspace,
            Document = document,
            Tokens =
            [
                new AgentToken("writer", AgentScope.Full, FullToken),
                new AgentToken("reader", AgentScope.Read, ReadToken),

                // 能改、但只许碰一层。它验的是图层级那一道判定：
                // 粗粒度那一档放它过，细的那一档要在动作参数上才看得出来。
                new AgentToken("scoped", AgentScope.Edit, ScopedToken)
                {
                    Layers = new HashSet<string>([ScopedLayer], StringComparer.Ordinal),
                },
            ],
            RequestsPerMinute = requestsPerMinute,
            CallTimeout = callTimeout ?? TimeSpan.FromSeconds(30),
            ChangeWait = changeWait ?? TimeSpan.FromMilliseconds(300),
            Diagnostics = diagnostics ?? NullDiagnosticsSink.Instance,
        };

    /// <summary>起一个网络服务端并等它开始监听。</summary>
    public static async Task<HttpHost> StartAsync(HttpHostOptions options, CancellationToken cancellationToken)
    {
        var host = HttpHost.Create(options);

        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        return host;
    }

    /// <summary>连上网络服务端的协议端点。</summary>
    public static async Task<McpClient> ConnectAsync(
        HttpHost host,
        string token = FullToken,
        SessionState? declaration = null,
        CancellationToken cancellationToken = default)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Name = "duetdiagram-http-agent",
            Endpoint = new Uri(host.Address, HttpHost.McpPath),
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {token}",
            },
        });

        return await McpClient.CreateAsync(
            transport,
            ClientOptions(declaration),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>一个带凭据的裸 HTTP 客户端，用来打协议端点之外的请求、以及看拒绝长什么样。</summary>
    public static HttpClient RawClient(HttpHost host, string? token)
    {
        var client = new HttpClient { BaseAddress = host.Address };

        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    /// <summary>
    /// 直接发一条协议请求。
    /// </summary>
    /// <remarks>
    /// 两个接受类型都要带上：少一个会被协议端点以 406 挡回来，而那个 406 与本用例
    /// 想看的拒绝长得很像，很容易被当成"没通过认证"。
    /// </remarks>
    public static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, HttpHost.McpPath)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");

        return await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>读一条拒绝的正文里的错误码。</summary>
    public static async Task<string> RejectionCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return JsonNode.Parse(text)?["code"]?.GetValue<string>()
            ?? throw new InvalidOperationException($"这条拒绝里没有错误码：{text}");
    }

    /// <summary>读一条拒绝的完整正文。要看差异那一份内容时用它。</summary>
    public static async Task<JsonElement> RejectionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>
    /// 一条最小可用的工具调用请求，直接发到协议端点上。
    /// </summary>
    /// <remarks>
    /// 用它而不是走客户端库，是为了看**传输层**的拒绝长什么样：客户端库会把非 2xx
    /// 当成一次调用失败抛出来，而这一层要验的正是那个状态码与正文。
    /// 声明写在参数元数据里，与客户端库的做法一致。
    /// </remarks>
    public static string CallBody(string tool, string arguments, SessionState? declaration = null)
    {
        var meta = declaration is null
            ? string.Empty
            : ",\"_meta\":{\"" + SessionState.CapabilityKey + "\":" + declaration.ToJson().ToJsonString() + "}";

        return "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\""
            + tool
            + "\",\"arguments\":"
            + arguments
            + meta
            + "}}";
    }

    /// <summary>
    /// 直接发一条工具调用，把状态码与正文原样拿回来。
    /// </summary>
    /// <remarks>
    /// 并发那一组用它，**不走客户端库**：客户端库会把非 2xx 当成一次调用失败抛出来，
    /// 而那一组要数的正是"拿到了几次 409、每次带没带内容"。走库的话，
    /// 冲突变成一条异常消息，数出来的是"抛了几次"，而那不是同一件事。
    /// </remarks>
    public static async Task<RawReply> RawAsync(
        HttpClient client,
        string tool,
        string arguments,
        CancellationToken cancellationToken,
        SessionState? declaration = null)
    {
        using var response = await PostAsync(client, CallBody(tool, arguments, declaration), cancellationToken)
            .ConfigureAwait(false);

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return new RawReply(response.StatusCode, ParseBody(text), text);
    }

    /// <summary>
    /// 从一条裸答复里取出工具结果。
    /// </summary>
    /// <remarks>
    /// 结果在协议正文里是**一段装在文本块里的 JSON**，不是嵌套的对象：
    /// 服务端把它序列化成字符串再放进内容块，所以这里要多解析一层。
    /// 少解析这一层的话，读到的是一段字符串，而它看起来也像成功。
    /// </remarks>
    public static JsonElement ToolResultOf(RawReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        var block = reply.Payload.GetProperty("result").GetProperty("content")[0];

        return JsonDocument.Parse(block.GetProperty("text").GetString()!).RootElement.Clone();
    }

    /// <summary>
    /// 把协议正文解析成 JSON。
    /// </summary>
    /// <remarks>
    /// 协议端点回的是**事件流**（<c>data: {...}</c>），而传输层的拒绝回的是一段 JSON。
    /// 只认后一种的话，每一个成功的调用都会在解析上失败，而症状看起来像服务端坏了。
    /// </remarks>
    private static JsonElement ParseBody(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return default;
        }

        var data = string.Join(
            '\n',
            text.Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
                .Select(line => line["data:".Length..].TrimStart()));

        return JsonDocument.Parse(data.Length > 0 ? data : text).RootElement.Clone();
    }

    /// <summary>问一次变化源。</summary>
    public static async Task<HttpResponseMessage> ChangesAsync(
        HttpClient client,
        int since,
        CancellationToken cancellationToken,
        int? waitSeconds = null)
    {
        var query = waitSeconds is { } wait
            ? $"{HttpHost.ChangesPath}?since={since}&wait={wait}"
            : $"{HttpHost.ChangesPath}?since={since}";

        return await client.GetAsync(query, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region 取数

    /// <summary>每个用例自己一个临时目录。</summary>
    public static string NewWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), $"duetdiagram-mcp-{Guid.NewGuid():N}");

        Directory.CreateDirectory(path);

        return path;
    }

    /// <summary>从测试程序集的位置逐级上溯，找到含解决方案文件的目录。</summary>
    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DuetDiagram.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("从测试程序集的位置找不到仓库根。");
    }

    #endregion
}
