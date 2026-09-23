using DuetDiagram.Mcp.Server;
using Microsoft.Extensions.Hosting;

namespace DuetDiagram.Mcp;

/// <summary>
/// 命令行入口：起一个通过标准输入输出对话的服务端，或者一个监听端口、带认证的服务端。
/// </summary>
/// <remarks>
/// <para>
/// 标准输入输出那一档下这个进程只服务一个客户端：它由客户端作为子进程拉起来，
/// 客户端的标准输入输出就是它的传输。所以那一档没有任何"监听端口""多个客户端"之类的选项。
/// </para>
/// <para>
/// 网络那一档是给远程代理用的，所以它必须带凭据表：**一份都不给就不起**。
/// 不给也能起的话，一个监听在端口上的服务端会对任何连上来的人开放，
/// 而它的八个工具能改文档——这不需要攻击者，一个扫端口的脚本就够了。
/// </para>
/// </remarks>
public static class Program
{
    /// <summary>指定要编辑的文档文件的开关。</summary>
    public const string DocumentSwitch = "--document";

    /// <summary>监听地址。给了它就起网络服务端，不给就起标准输入输出。</summary>
    public const string HttpSwitch = "--http";

    /// <summary>工作区根。网络那一档必填。</summary>
    public const string WorkspaceSwitch = "--workspace";

    /// <summary>一份凭据，形态是 <c>名字:权限档:凭据</c>。可重复。</summary>
    public const string TokenSwitch = "--token";

    private static async Task<int> Main(string[] args)
    {
        // 标准错误会被另一个进程读走，所以它的编码要是固定的，不能跟着这台机器的代码页走。
        // 不固定的话，日志里的中文在对端那边变成乱码，而协议本身照常工作——
        // 于是"日志读不出来"会被当成"日志没写出来"，往错的方向查。
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        try
        {
            return ReadOption(args, HttpSwitch) is { } url
                ? await RunHttpAsync(args, url).ConfigureAwait(false)
                : await RunStdioAsync(args).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 走标准错误：标准输出可能被协议占用，写进去会让对端在解析时失败，
            // 而那时对端看到的是一个格式错误，与这里真正的原因对不上。
            Console.Error.WriteLine($"服务端起不来：{ex.GetType().Name} - {ex.Message}");

            return 1;
        }
    }

    private static async Task<int> RunStdioAsync(string[] args)
    {
        await using var server = DiagramMcpServer.Create(ReadOption(args, DocumentSwitch));

        await server.Host.RunAsync().ConfigureAwait(false);

        return 0;
    }

    private static async Task<int> RunHttpAsync(string[] args, string url)
    {
        var tokens = ReadTokens(args);

        if (tokens.Count == 0)
        {
            throw new InvalidOperationException(
                $"网络那一档至少要给一份凭据（{TokenSwitch} 名字:权限档:凭据），一份都不给等于对任何连上来的人开放。");
        }

        var workspace = ReadOption(args, WorkspaceSwitch)
            ?? throw new InvalidOperationException($"网络那一档要指定工作区（{WorkspaceSwitch} 目录）。");

        await using var host = HttpHost.Create(new HttpHostOptions
        {
            Workspace = workspace,
            Document = ReadOption(args, DocumentSwitch),
            Tokens = tokens,
            Url = url,

            // 审计与命令层的告警都走这个出口。不给的话它们写进的是一个丢弃一切的实现，
            // 而表现是"审计功能已经有了"——一条都读不到，且没有任何地方会报出来。
            Diagnostics = StandardErrorDiagnostics.Instance,
        });

        await host.StartAsync().ConfigureAwait(false);

        // 地址自带一个结尾的斜杠，路径自带一个开头的斜杠，直接拼会多出来一个。
        Console.Error.WriteLine($"服务端在 {new Uri(host.Address, HttpHost.McpPath)} 上等着，变化源在 {HttpHost.ChangesPath}。");

        // 网络那一档没有"跑到输入结束"这回事，所以等到进程被叫停。
        await Task.Delay(Timeout.Infinite).ConfigureAwait(false);

        return 0;
    }

    /// <summary>
    /// 读出全部凭据。
    /// </summary>
    /// <remarks>
    /// 权限档写错时直接拒绝启动，不退回一个缺省档：退回的话，一份本来只想给只读的凭据
    /// 会因为一个拼写错误而拿到最高档，而这件事没有任何地方会报出来。
    /// </remarks>
    private static List<AgentToken> ReadTokens(string[] args)
    {
        var tokens = new List<AgentToken>();

        for (var index = 0; index < args.Length - 1; index++)
        {
            if (!string.Equals(args[index], TokenSwitch, StringComparison.Ordinal))
            {
                continue;
            }

            var parts = args[index + 1].Split(':', 3);

            if (parts.Length != 3 || !Enum.TryParse<AgentScope>(parts[1], ignoreCase: true, out var scope))
            {
                throw new InvalidOperationException(
                    $"{TokenSwitch} 的形态是 名字:权限档:凭据，权限档取 {string.Join('、', Enum.GetNames<AgentScope>())}。");
            }

            tokens.Add(new AgentToken(parts[0], scope, parts[2]));
        }

        return tokens;
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
