using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Concurrency;
using DuetDiagram.Core.Diagnostics;
using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Llm.Tools;
using DuetDiagram.Mcp.Skills;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DuetDiagram.Mcp.Server;

/// <summary>
/// 网络服务端怎么起、起在哪、认哪几份凭据。
/// </summary>
/// <param name="Workspace">工作区根。文档路径不许解析到它外面去。</param>
/// <param name="Document">要编辑的文档文件，相对工作区根。为空表示从一张空图开始。</param>
/// <param name="Tokens">认得的凭据。一份都不给等于谁都进不来。</param>
/// <param name="RequestsPerMinute">一份凭据每分钟最多发几次。</param>
/// <param name="CallTimeout">单次调用的时间预算。</param>
/// <param name="ChangeWait">变化源最长等多久。</param>
/// <param name="Url">监听地址。端口写零表示由系统挑一个空闲的。</param>
/// <param name="Diagnostics">应用日志出口。审计记录也走这里。</param>
public sealed record HttpHostOptions
{
    public required string Workspace { get; init; }

    public string? Document { get; init; }

    public required IReadOnlyList<AgentToken> Tokens { get; init; }

    public int RequestsPerMinute { get; init; } = 60;

    public TimeSpan CallTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan ChangeWait { get; init; } = TimeSpan.FromSeconds(30);

    public string Url { get; init; } = "http://127.0.0.1:0";

    public IDiagnosticsSink Diagnostics { get; init; } = NullDiagnosticsSink.Instance;
}

/// <summary>
/// 通过 HTTP 对话的服务端：协议端点加一条变化源，前面挡着认证、限流与权限档。
/// </summary>
/// <remarks>
/// <para>
/// **无状态。** 协议端点不在请求之间记任何东西，所以同一个请求发给哪个实例都成立，
/// 前面的负载均衡也不必做会话粘滞。这是当前协议版本的缺省行为，这里把它显式写出来，
/// 免得哪天缺省变了而这一层跟着悄悄变成有状态的。
/// </para>
/// <para>
/// 调用方要改文档时，版本声明随**每一条请求**带上来（会话建立那一次说的只够改一次）。
/// 那一条读取与标准输入输出那条传输共用一份实现，不在这里重写。
/// </para>
/// </remarks>
public sealed class HttpHost : IAsyncDisposable
{
    /// <summary>协议端点挂在哪。</summary>
    public const string McpPath = "/mcp";

    /// <summary>变化源挂在哪。</summary>
    public const string ChangesPath = "/changes";

    /// <summary>单次调用超时用的错误码。</summary>
    public const string TimeoutCode = ErrorCodes.McpTimeout;

    /// <summary>身份在请求项里放的键。</summary>
    internal const string IdentityKey = "duetdiagram/identity";

    /// <summary>变化源最长等多久，超过这个数的等待请求按这个数算。</summary>
    private static readonly TimeSpan MaxChangeWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 改得动文档的那几个工具。
    /// </summary>
    /// <remarks>
    /// 取的是工具表里的常量而不是手写的字面量：工具改名时这里会编译不过，
    /// 而写死一串字符串的话，改名之后这份清单里会剩一个谁也不认识的旧名字，
    /// 于是那个工具对只读凭据悄悄放行了。
    /// </remarks>
    private static readonly HashSet<string> WriteTools = new(StringComparer.Ordinal)
    {
        DiagramToolset.Edit,
        DiagramToolset.Style,
        DiagramToolset.Layout,
        DiagramToolset.Composite,
        DiagramToolset.UndoRedo,
    };

    /// <summary>
    /// 发命令时真的会报上版本声明的那几个工具。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 比上面那一份少一个撤销重做：它走的是历史栈，**不接受也不检查声明**。
    /// 对着一个旧版本撤销是调用方自己的判断，这里替它挡下来只会让它无从知道该怎么撤。
    /// </para>
    /// <para>
    /// 取的是动作表里那一份，不另抄一张：那张表说的正是"哪些工具把动作落到带版本检查的命令上"，
    /// 抄一份的话，某个工具改走别的通路之后，这里会继续按老样子预判。
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> VersionedTools =
        new(ActionTable.Wired, StringComparer.Ordinal);

    private readonly WebApplication _app;
    private readonly SessionCore _session;
    private readonly ChangeFeed _feed;
    private readonly HttpHostOptions _options;

    private Uri? _address;

    private HttpHost(WebApplication app, SessionCore session, ChangeFeed feed, HttpHostOptions options)
    {
        _app = app;
        _session = session;
        _feed = feed;
        _options = options;
    }

    /// <summary>实际监听的地址。起之前读不到。</summary>
    public Uri Address =>
        _address ?? throw new InvalidOperationException("服务端还没起来，读不到监听地址。");

    /// <summary>这一份会话。用例与宿主靠它拿文档。</summary>
    public SessionCore Session => _session;

    /// <summary>变化源。用例靠它看当前停在第几版。</summary>
    public ChangeFeed Feed => _feed;

    /// <summary>
    /// 起一个网络服务端。
    /// </summary>
    /// <remarks>
    /// 文档路径在**这里**过工作区那道关，而这一步发生在读文件之前：
    /// 放到读之后的话，一份越权的文件已经被读进内存了，而那道关本来是要拦住这件事的。
    /// </remarks>
    public static HttpHost Create(HttpHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var guard = new WorkspaceGuard(options.Workspace);
        var document = options.Document is null ? null : guard.Resolve(options.Document);

        var session = SessionCore.Create("http", document, options.Diagnostics);
        var feed = new ChangeFeed(session.Document, session.Broadcaster);
        var auth = new BearerAuth(options.Tokens);
        var limiter = new RateLimiter(options.RequestsPerMinute);

        // 主体名到权限的绑定。绑的是主体而不是连接：绑连接的话，同一个令牌换一条连接
        // 就能绕开图层限制，而那条限制本来要说的是"这份凭据只许碰这几个图层"。
        var acl = new LayerAcl(options.Tokens.Select(token => (token.Name, token.Permissions)));

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(options.Url);

        var tools = ToolRegistry
            .CreateDefault(DiagramMcpServer.ToolContext(session))
            .Tools
            .Select(tool => McpServerTool.Create(new BudgetedFunction(tool.Function, options.CallTimeout)))
            .ToArray();

        builder.Services
            .AddMcpServer(o => DiagramMcpServer.ConfigureOptions(o, session.Ack.ToJson()))
            .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools(tools)
            .WithResources(SkillCatalog.Default.ToMcpResources())
            .WithMessageFilters(filters => filters.AddIncomingFilter(next => async (messageContext, cancellationToken) =>
            {
                session.Observe(messageContext.JsonRpcMessage);
                await next(messageContext, cancellationToken).ConfigureAwait(false);
            }));

        var app = builder.Build();

        // 挡在协议端点前面。顺序是有意的：先认凭据（不认得就不必再算别的），
        // 再算频次（认得出才谈得上"这份凭据调得太快"），再看权限档，
        // 最后看版本声明（前几关都过了才谈得上"这次写入注定冲突"）。
        //
        // 审计写在最外层：被挡下的请求也要留一条。只记成功的调用等于没有审计，
        // 而真正要看的是"谁在反复撞门"——那一条恰恰从不成功。
        app.Use(async (context, next) =>
        {
            var started = TimeProvider.System.GetTimestamp();
            AgentToken? identity = null;

            try
            {
                identity = auth.Authenticate(context.Request.Headers.Authorization);

                if (identity is null)
                {
                    await TransportRejection.WriteAsync(
                        context,
                        StatusCodes.Status401Unauthorized,
                        BearerAuth.UnauthorizedCode,
                        "这份凭据不认得，或者请求里根本没带。");

                    return;
                }

                context.Items[IdentityKey] = identity;

                // 图层范围随请求放进会话：工具表是所有凭据共用的一份，权限只能这样传下去。
                session.Scope(acl.For(identity.Name));

                if (!limiter.TryAcquire(identity.Name, out var retryAfterSeconds))
                {
                    await TransportRejection.WriteAsync(
                        context,
                        StatusCodes.Status429TooManyRequests,
                        RateLimiter.RateLimitedCode,
                        "这一分钟里发得太密了。等一会儿再来。",
                        retryAfterSeconds);

                    return;
                }

                var shape = await InspectAsync(context).ConfigureAwait(false);

                if (identity.Scope == AgentScope.Read && shape.NamesWriteTool)
                {
                    await TransportRejection.WriteAsync(
                        context,
                        StatusCodes.Status403Forbidden,
                        BearerAuth.ForbiddenCode,
                        "这份凭据只被允许读。要改这份图就换一份能改的凭据。");

                    return;
                }

                if (shape.VersionChecked
                    && await RefuseConflictAsync(context, session, shape).ConfigureAwait(false))
                {
                    return;
                }

                await next(context).ConfigureAwait(false);
            }
            finally
            {
                Audit(options.Diagnostics, identity, context, started);
            }
        });

        app.MapGet(ChangesPath, async context =>
        {
            var since = ReadInt(context.Request.Query["since"]) ?? 0;
            var wait = Clamp(ReadInt(context.Request.Query["wait"]), options.ChangeWait);

            var notice = await feed.WaitAsync(since, wait, context.RequestAborted).ConfigureAwait(false);

            if (notice is null)
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;

                return;
            }

            context.Response.ContentType = "application/json";

            await context.Response.WriteAsync(notice.ToJson().ToJsonString(), context.RequestAborted).ConfigureAwait(false);
        });

        app.MapMcp(McpPath);

        return new HttpHost(app, session, feed, options);
    }

    /// <summary>开始监听。</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _app.StartAsync(cancellationToken).ConfigureAwait(false);

        var addresses = _app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()?.Addresses;

        _address = addresses is { Count: > 0 }
            ? new Uri(addresses.First())
            : throw new InvalidOperationException("服务端起来了，但读不到监听地址。");
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);

        _feed.Dispose();

        await _session.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 这一条请求要看出来的东西：它调的是哪个工具、那个工具改不改文档、带没带版本声明。
    /// </summary>
    /// <param name="NamesWriteTool">调的是改得动文档的那几个工具之一。</param>
    /// <param name="VersionChecked">调的是会把动作落到带版本检查的命令上的那几个工具之一。</param>
    /// <param name="Declared">请求里带的版本声明。没带时为空。</param>
    private sealed record RequestShape(bool NamesWriteTool, bool VersionChecked, VersionCheckRequest? Declared);

    /// <summary>
    /// 这一次写入会不会注定冲突，会的话把内容回给调用方。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **判据只是"能不能只回差异"，不是"要不要让它进去"。** 版本一样就放它进去，
    /// 报的版本更靠前也放它进去——那是参数错误，总线会带着明确的错误码回它，
    /// 而在这里替它下结论会把一个参数问题说成一次冲突。
    /// </para>
    /// <para>
    /// 命令总线那一道检查仍然是说了算的那一道。这里判完之后到命令真正执行之间还有个窗口，
    /// 那个窗口里的冲突由总线在门锁内挡下，形状是工具结果里的错误码。
    /// </para>
    /// </remarks>
    private static async Task<bool> RefuseConflictAsync(
        HttpContext context,
        SessionCore session,
        RequestShape shape)
    {
        var answer = ConflictResponder.Decide(
            shape.Declared,
            session.Document,
            session.Bus.Context.VersionLog,
            () => DiagramSerializer.SerializeFull(session.Document));

        // 空差异与"参数错误"都不算冲突，放它进去。
        if (answer is not (FullSnapshotDiff or ReferenceDiff))
        {
            return false;
        }

        await TransportRejection.WriteAsync(
            context,
            StatusCodes.Status409Conflict,
            ConflictResponder.Code,
            shape.Declared is null
                ? "要改这份图就得说明你看到的是哪一版。先读一次图，把版本号带上来再改。"
                : "手上的副本旧了。按这份差异追平之后再改。",
            diff: answer).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// 这一条请求调的是哪个工具、带没带版本声明。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 判据得从请求体里读，因为工具名只在里面。读之前先开缓冲、读完复位：
    /// 不复位的话协议那一层拿到的是一个已经读空的体，表现是"调用进去了、参数全丢了"。
    /// </para>
    /// <para>
    /// 读不出来（不是 JSON、不是 POST、没有工具名）一律当"不是写调用、也没带声明"。
    /// 当"是"的话，一个畸形请求会拿到 403 或 409，而不是它真正该拿的那个错误，
    /// 而调用方会去换凭据或者去同步。
    /// </para>
    /// <para>
    /// 声明交给会话那一层的读取器解析，不在这里另写一份：它有四种形态要认，
    /// 而协议一动，另写的那一份就会静默失效。
    /// </para>
    /// </remarks>
    private static async Task<RequestShape> InspectAsync(HttpContext context)
    {
        var nothing = new RequestShape(false, false, null);

        if (!HttpMethods.IsPost(context.Request.Method) || !context.Request.HasJsonContentType())
        {
            return nothing;
        }

        context.Request.EnableBuffering();

        try
        {
            using var body = await JsonDocument
                .ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted)
                .ConfigureAwait(false);

            if (body.RootElement.ValueKind != JsonValueKind.Object
                || !body.RootElement.TryGetProperty("params", out var parameters)
                || parameters.ValueKind != JsonValueKind.Object
                || !parameters.TryGetProperty("name", out var name)
                || name.ValueKind != JsonValueKind.String)
            {
                return nothing;
            }

            var tool = name.GetString()!;
            var declared = SessionState.FromRequest(JsonNode.Parse(parameters.GetRawText()))?.ToVersionCheck();

            return new RequestShape(WriteTools.Contains(tool), VersionedTools.Contains(tool), declared);
        }
        catch (JsonException)
        {
            return nothing;
        }
        finally
        {
            context.Request.Body.Position = 0;
        }
    }

    /// <summary>
    /// 记一条审计。
    /// </summary>
    /// <remarks>
    /// **凭据本身与完整文件路径都不进来。** 日志是最容易被复制走的东西，
    /// 而这两样一样能拿去冒充、一样能画出这台机器上的目录结构。
    /// 记的是谁、什么方法、打到哪条路径、结果如何、花了多久。认不出是谁时也照记一条——
    /// 那一条恰恰是最该看的。
    /// </remarks>
    private static void Audit(IDiagnosticsSink diagnostics, AgentToken? identity, HttpContext context, long started)
    {
        var elapsed = TimeProvider.System.GetElapsedTime(started);
        var who = identity is null ? "<anonymous>" : $"{identity.Name} {identity.Scope}";

        diagnostics.Warn(
            $"[audit] {who} {context.Request.Method} {context.Request.Path} " +
            $"-> {context.Response.StatusCode} ({elapsed.TotalMilliseconds.ToString("0.#", CultureInfo.InvariantCulture)}ms)");
    }

    private static int? ReadInt(string? text) =>
        int.TryParse(text, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>
    /// 等待时长卡在零与上限之间。
    /// </summary>
    /// <remarks>
    /// 不给就用这一档自己的缺省值，而不是那个上限：上限是"最多能等多久"，
    /// 拿它当缺省等于把每一档的配置都变成同一个数，而调用方看不出这件事。
    /// </remarks>
    private static TimeSpan Clamp(int? seconds, TimeSpan fallback)
    {
        var wanted = seconds is { } value ? TimeSpan.FromSeconds(value) : fallback;

        return wanted < TimeSpan.Zero ? TimeSpan.Zero
            : wanted > MaxChangeWait ? MaxChangeWait
            : wanted;
    }
}

/// <summary>
/// 给一个工具套上时间预算。
/// </summary>
/// <remarks>
/// <para>
/// 预算在**发命令之前**检查：超了就直接回，一条命令都不发。这是这一层能给的保证——
/// 命令是同步的，中途取消观察不到，能保证的是"不会回一个超时响应、而那条命令还在后台把它改完"。
/// </para>
/// <para>
/// 参数与结果的形状都不动，只是多包一层，所以两侧的声明逐字相同这件事不受影响。
/// </para>
/// </remarks>
internal sealed class BudgetedFunction(AIFunction inner, TimeSpan budget) : AIFunction
{
    public override string Name => inner.Name;

    public override string Description => inner.Description;

    public override JsonElement JsonSchema => inner.JsonSchema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        if (budget <= TimeSpan.Zero)
        {
            return Expired();
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(budget);

        return await inner.InvokeAsync(arguments, deadline.Token).ConfigureAwait(false);
    }

    private JsonElement Expired() => ToolResult.Fail(ToolError.Of(
        HttpHost.TimeoutCode,
        "这一次调用没有时间预算了，一条命令都没发出去，文档一个字节都没改。",
        null,
        $"单次调用不超过 {budget.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} 秒")).ToJson();
}
