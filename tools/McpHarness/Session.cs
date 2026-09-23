using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Mcp.Server;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DuetDiagram.Tools.McpHarness;

/// <summary>
/// 服务端写在标准错误上的行。
/// </summary>
/// <remarks>
/// 这是看服务端到底收到了什么**唯一**的窗口：它跑在另一个进程里，父进程拿不到它的内存；
/// 而标准输出被协议占用，不能借。所以拉传输之前就要把收集器建好。
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

    public string All()
    {
        lock (_gate)
        {
            return string.Join('\n', _lines);
        }
    }
}

/// <summary>
/// 起一个网络服务端要给的几样东西。
/// </summary>
/// <param name="Workspace">工作区根。</param>
/// <param name="Document">要编辑的文档，相对工作区根。为空表示从一张空图开始。</param>
/// <param name="Tokens">认得的凭据，形态是 <c>名字:权限档:凭据</c>。</param>
internal sealed record HarnessOptions(string Workspace, string? Document, IReadOnlyList<string> Tokens);

/// <summary>
/// 一个连上服务端的会话。
/// </summary>
/// <remarks>
/// <para>
/// **拉的是真的可执行文件，不是进程内建一个服务端。** 这一层要验的正是「拿出去能不能用」，
/// 而进程内建一个绕过了命令行那条通路——那条通路上有参数解析、有端口、有日志改道，
/// 三样都可能出错，且都只有真的起一次才发现。
/// </para>
/// <para>
/// 标准输入输出那条由客户端作为子进程拉起来；网络那条也是子进程，但要显式给端口，
/// 因为命令行里写零表示由系统挑，而挑中的那个只印在标准错误的一句话里——
/// 去解析那句话等于拿自己的输出当接口。
/// </para>
/// </remarks>
internal sealed class HarnessSession : IAsyncDisposable
{
    private readonly Process? _process;

    private HarnessSession(McpClient client, string transport, Uri? address, ErrorLog log, Process? process)
    {
        Client = client;
        Transport = transport;
        Address = address;
        Log = log;
        _process = process;
    }

    public McpClient Client { get; }

    /// <summary>这条会话走的是哪条传输。进报告。</summary>
    public string Transport { get; }

    /// <summary>网络那条才有。用它发裸请求看传输层的拒绝长什么样。</summary>
    public Uri? Address { get; }

    /// <summary>服务端的标准错误。</summary>
    public ErrorLog Log { get; }

    /// <summary>拉一个标准输入输出的服务端子进程，再连上去。</summary>
    public static async Task<HarnessSession> StdioAsync(
        string? document = null,
        SessionState? declaration = null,
        CancellationToken cancellationToken = default)
    {
        var log = new ErrorLog();

        var arguments = new List<string>();

        if (document is not null)
        {
            arguments.Add(DuetDiagram.Mcp.Program.DocumentSwitch);
            arguments.Add(document);
        }

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "duetdiagram-harness",
            Command = ServerPath(),
            Arguments = arguments,
            StandardErrorLines = log.Add,
        });

        var client = await McpClient.CreateAsync(
            transport,
            ClientOptions(declaration),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new HarnessSession(client, "标准输入输出", null, log, null);
    }

    /// <summary>起一个网络服务端子进程，等它开始监听，再连上去。</summary>
    public static async Task<HarnessSession> HttpAsync(
        HarnessOptions options,
        string token,
        SessionState? declaration = null,
        CancellationToken cancellationToken = default)
    {
        var log = new ErrorLog();
        var port = FreePort();
        var address = new Uri($"http://127.0.0.1:{port}");

        var arguments = new List<string>
        {
            DuetDiagram.Mcp.Program.HttpSwitch,
            address.ToString().TrimEnd('/'),
            DuetDiagram.Mcp.Program.WorkspaceSwitch,
            options.Workspace,
        };

        if (options.Document is not null)
        {
            arguments.Add(DuetDiagram.Mcp.Program.DocumentSwitch);
            arguments.Add(options.Document);
        }

        foreach (var declared in options.Tokens)
        {
            arguments.Add(DuetDiagram.Mcp.Program.TokenSwitch);
            arguments.Add(declared);
        }

        var process = Launch(arguments, log);

        await WaitForPortAsync(port, cancellationToken).ConfigureAwait(false);

        var client = await ConnectAsync(address, token, declaration, cancellationToken).ConfigureAwait(false);

        return new HarnessSession(client, "网络", address, log, process);
    }

    /// <summary>
    /// 再连一条客户端到已经起好的服务端上。
    /// </summary>
    /// <remarks>
    /// 并发那一档要的是**十个 agent 打在同一个服务端上**，不是起十个服务端。
    /// 每个 agent 各起一个服务端的话，它们各改各的文档，竞态根本不存在——
    /// 而那正是那一档要验的东西。
    /// </remarks>
    public static async Task<McpClient> ConnectAsync(
        Uri address,
        string token,
        SessionState? declaration,
        CancellationToken cancellationToken)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Name = "duetdiagram-harness",
            Endpoint = new Uri(address, HttpHost.McpPath),
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

    /// <summary>每个会话自己一个临时目录。</summary>
    public static string NewWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), $"duetdiagram-harness-{Guid.NewGuid():N}");

        Directory.CreateDirectory(path);

        return path;
    }

    /// <summary>
    /// 造一份有内容的文档写进某个目录，返回文件名。
    /// </summary>
    /// <remarks>
    /// 空文档的结构哈希是空的——那是刻意的，而它让"上报了结构哈希"这条判据变成一句空话。
    /// 有内容的文档才看得出那一位到底有没有被填上。
    /// </remarks>
    public static string WriteDocument(string directory, string id)
    {
        var name = $"{id}.json";

        var document = DiagramDocument.CreateFromContent(
            id,
            nodes: [new NodeDef { Id = "start", Label = "开始" }, new NodeDef { Id = "end", Label = "结束" }],
            edges: [new EdgeDef { Id = "e1", From = "start", To = "end" }]);

        File.WriteAllText(
            Path.Combine(directory, name),
            DiagramSerializer.SerializeFull(document));

        return name;
    }

    /// <summary>一份声明了某个版本的会话状态。</summary>
    public static SessionState At(int version, string documentId = DocumentId) =>
        new(documentId, version, StructuralHash: null);

    /// <summary>从一张空图开始时那份文档的标识。</summary>
    public const string DocumentId = "mcp-document";

    /// <summary>服务端的可执行文件在哪。</summary>
    /// <remarks>
    /// 先在本目录找、再按当前配置与目标框架去服务端自己的输出目录找。两处都没有就直接报错，
    /// 不要静默跳过——静默跳过的话这一轮会变成「零项检查」，而那是查不出来的。
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

    /// <summary>从本目录逐级上溯，找到含解决方案文件的目录。</summary>
    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DuetDiagram.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("从当前目录找不到仓库根。");
    }

    /// <summary>一个带凭据的裸客户端，用来看传输层的拒绝长什么样。</summary>
    public HttpClient RawClient(string? token)
    {
        var address = Address ?? throw new InvalidOperationException("这条会话不是网络那一档，没有裸客户端可用。");

        var client = new HttpClient { BaseAddress = address };

        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    /// <summary>
    /// 直接发一条工具调用请求。
    /// </summary>
    /// <remarks>
    /// 用它而不是走客户端库，是为了看**传输层**的拒绝长什么样：客户端库会把非 2xx
    /// 当成一次调用失败抛出来，而这一层要验的正是那个状态码与正文。
    /// 两个接受类型都要带上，少一个会被协议端点以 406 挡回来，而那个 406
    /// 与本装置想看的拒绝长得很像。
    /// </remarks>
    public static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, HttpHost.McpPath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");

        return await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>一条最小可用的工具调用请求。声明写在参数元数据里，与客户端库的做法一致。</summary>
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

    /// <summary>调一个工具，把返回的那段 JSON 读出来。</summary>
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

    /// <summary>读一份资源，把文本那一支取出来。</summary>
    public static async Task<string> ReadAsync(McpClient client, string uri, CancellationToken cancellationToken)
    {
        var result = await client.ReadResourceAsync(uri, cancellationToken: cancellationToken);

        return result.Contents.OfType<TextResourceContents>().FirstOrDefault()?.Text
            ?? throw new InvalidOperationException($"资源 {uri} 没有返回文本内容。");
    }

    /// <summary>
    /// 一条裸调用的答复。
    /// </summary>
    /// <param name="Status">HTTP 状态码。冲突、限流、拒绝都只有在这一层才看得出来。</param>
    /// <param name="Payload">正文。拒绝那一档是错误正文，成功那一档是 JSON-RPC 信封。</param>
    /// <param name="Text">原始正文。要把它打进说明里的时候用。</param>
    internal sealed record RawReply(HttpStatusCode Status, JsonElement Payload, string Text);

    /// <summary>
    /// 直接发一条工具调用，把状态码与正文原样拿回来。
    /// </summary>
    /// <remarks>
    /// 并发那一档用它，**不用客户端库**：客户端库会把非 2xx 当成一次调用失败抛出来，
    /// 而那一档要数的正是拿到了几次 409。走库的话，冲突变成一条异常消息，
    /// 数出来的是"抛了几次"，而它和"回了几次冲突"不是同一件事。
    /// </remarks>
    public static async Task<RawReply> RawCallAsync(
        HttpClient client,
        string tool,
        string arguments,
        CancellationToken cancellationToken,
        SessionState? declaration = null)
    {
        using var response = await PostAsync(client, CallBody(tool, arguments, declaration), cancellationToken)
            .ConfigureAwait(false);

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return new RawReply(response.StatusCode, Parse(text), text);
    }

    /// <summary>
    /// 从一条裸答复里取出工具结果。
    /// </summary>
    /// <remarks>
    /// 工具结果在协议正文里是**一段装在文本块里的 JSON**，不是嵌套的对象：
    /// 服务端把它序列化成字符串再放进内容块，所以这里要多解析一层。
    /// 少解析这一层的话，读到的是一段字符串，而它看起来也像成功。
    /// </remarks>
    public static JsonElement ToolResult(RawReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        var block = reply.Payload.GetProperty("result").GetProperty("content")[0];

        return JsonDocument.Parse(block.GetProperty("text").GetString()!).RootElement.Clone();
    }

    /// <summary>
    /// 把协议正文解析成 JSON。
    /// </summary>
    /// <remarks>
    /// 协议端点回的是**事件流**，不是一段 JSON：正文长成 <c>event: message</c> 加
    /// <c>data: {...}</c>。当成 JSON 直接解析会失败，而失败发生在每一个调用上，
    /// 看起来像"服务端坏了"。两个接受类型都要带（少一个会被 406 挡回来），
    /// 于是两条格式都要认：事件流那一支与直接一段 JSON 那一支。
    /// </remarks>
    private static JsonElement Parse(string text)
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

        var json = data.Length > 0 ? data : text;

        return JsonDocument.Parse(json).RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync().ConfigureAwait(false);

        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }

        _process?.Dispose();
    }

    /// <summary>客户端的身份与可选的会话声明。</summary>
    private static McpClientOptions ClientOptions(SessionState? declaration)
    {
        var options = new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "duetdiagram-harness", Version = "0.1.0" },
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

    private static Process Launch(IReadOnlyList<string> arguments, ErrorLog log)
    {
        var info = new ProcessStartInfo(ServerPath())
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = info };

        process.ErrorDataReceived += (_, line) =>
        {
            if (line.Data is not null)
            {
                log.Add(line.Data);
            }
        };

        process.Start();
        process.BeginErrorReadLine();

        return process;
    }

    /// <summary>
    /// 先绑一次零号端口拿一个空闲的，放掉，再把那个端口给服务端。
    /// </summary>
    /// <remarks>
    /// 中间那一点竞态在装置上可以接受：抢这个端口的只会是同一台机器上的别的东西，
    /// 而它失败了会在下面那个等待里以超时的方式报出来，不会静默。
    /// </remarks>
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        listener.Stop();

        return port;
    }

    private static async Task WaitForPortAsync(int port, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var probe = new TcpClient();

                await probe.ConnectAsync(IPAddress.Loopback, port, cancellationToken).ConfigureAwait(false);

                return;
            }
            catch (SocketException)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException($"等不到端口 {port} 上有服务端在听。");
    }
}
