using System.Text.Json;
using System.Text.Json.Nodes;
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
/// 用例要用的固定环境：服务端可执行文件在哪、客户端怎么声明会话状态、结果怎么读。
/// </summary>
internal static class Harness
{
    /// <summary>服务端从空图开始那一份文档的标识。</summary>
    public const string DocumentId = "mcp-document";

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
}
