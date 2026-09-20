using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DuetDiagram.Poc.McpTransport;

/// <summary>
/// 网络传输回环。
/// </summary>
/// <remarks>
/// <para>
/// 这条路径与前两条的差别不只是"换个管道"：前两条是双向长连接，服务端可以主动推消息；
/// 网络传输下客户端与服务的生命周期是解耦的，会话要靠会话号来维持。
/// 外部代理跨机器接入走的就是这条路，所以它必须单独验证。
/// </para>
/// <para>
/// 用无状态模式：外部代理每次调用都是独立请求，不依赖服务端保留会话上下文。
/// 这对多实例部署是必要条件——有状态的话请求必须落到同一个实例上，运维成本高得多。
/// </para>
/// </remarks>
internal static class HttpRoundtrip
{
    public static async Task<RoundtripReport> RunAsync(CancellationToken cancellationToken)
    {
        var log = new MessageLog();

        var builder = WebApplication.CreateBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddMcpServer(ServerHost.ConfigureServerOptions)
            .WithTools<DiagramTools>()
            .WithHttpTransport(options => options.Stateless = true)
            .WithObservability(log);

        await using var app = builder.Build();

        // 端口用零让系统分配，避免与机器上已有的服务撞车。
        // 固定端口在别人的机器上跑不起来，而这种失败看起来像"协议不通"，很容易被误判。
        app.Urls.Add("http://127.0.0.1:0");

        app.MapMcp();

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        var address = app.Urls.FirstOrDefault()
            ?? throw new InvalidOperationException("服务端没有报告监听地址。");

        try
        {
            using var httpClient = new HttpClient();

            await using var client = await McpClient.CreateAsync(
                new HttpClientTransport(
                    new HttpClientTransportOptions { Endpoint = new Uri($"{address}/") },
                    httpClient),
                Roundtrips.ClientOptions(),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var observations = await Roundtrips.ExerciseAsync(client, cancellationToken).ConfigureAwait(false);

            return new RoundtripReport(
                $"网络传输（{address}）",
                Roundtrips.BuildChecks(observations, log, capturedWire: null));
        }
        finally
        {
            await app.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
