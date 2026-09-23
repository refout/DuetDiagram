using DuetDiagram.Mcp.Server;
using Microsoft.Extensions.Hosting;

namespace DuetDiagram.Mcp;

/// <summary>
/// 命令行入口：起一个通过标准输入输出对话的服务端。
/// </summary>
/// <remarks>
/// 这个进程只服务一个客户端：它由客户端作为子进程拉起来，客户端的标准输入输出
/// 就是它的传输。所以没有任何"监听端口""多个客户端"之类的选项。
/// </remarks>
public static class Program
{
    /// <summary>指定要编辑的文档文件的开关。</summary>
    public const string DocumentSwitch = "--document";

    private static async Task<int> Main(string[] args)
    {
        // 标准错误会被另一个进程读走，所以它的编码要是固定的，不能跟着这台机器的代码页走。
        // 不固定的话，日志里的中文在对端那边变成乱码，而协议本身照常工作——
        // 于是"日志读不出来"会被当成"日志没写出来"，往错的方向查。
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        try
        {
            await using var server = DiagramMcpServer.Create(ReadOption(args, DocumentSwitch));

            await server.Host.RunAsync().ConfigureAwait(false);

            return 0;
        }
        catch (Exception ex)
        {
            // 走标准错误：标准输出被协议占用，写进去会让对端在解析时失败，
            // 而那时对端看到的是一个格式错误，与这里真正的原因对不上。
            Console.Error.WriteLine($"服务端起不来：{ex.GetType().Name} - {ex.Message}");

            return 1;
        }
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
