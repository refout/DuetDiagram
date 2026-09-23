using Microsoft.Extensions.Logging;

namespace DuetDiagram.Mcp.Server;

/// <summary>
/// 标准输入输出这条传输下的日志去处。
/// </summary>
/// <remarks>
/// <para>
/// **标准输出被协议占用。** 默认的控制台日志提供程序写标准输出，一条日志进去就会让对端
/// 解析失败，而症状是解析错误而不是超时——很容易被误判成消息格式有问题，
/// 于是往"消息格式"那个方向查，而真正的原因在日志配置上。
/// </para>
/// <para>
/// 所以这里先把默认的提供程序全部清掉，再挂一个阈值设成最低的控制台提供程序：
/// 那个阈值的作用就是"凡是进到这个提供程序的消息都写标准错误"。
/// 只加不清的话，默认那个仍然在，日志会两边都写。
/// </para>
/// </remarks>
internal static class StdioLogging
{
    public static void RouteToStandardError(ILoggingBuilder logging)
    {
        ArgumentNullException.ThrowIfNull(logging);

        logging.ClearProviders();
        logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    }
}
