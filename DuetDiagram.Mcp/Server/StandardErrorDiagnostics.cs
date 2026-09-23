using DuetDiagram.Core.Diagnostics;

namespace DuetDiagram.Mcp.Server;

/// <summary>
/// 把诊断写进标准错误的出口。
/// </summary>
/// <remarks>
/// <para>
/// **审计记录得有个去处。** 网络那条传输上，每一次请求都要留一条（谁、什么方法、
/// 打到哪条路径、结果如何），而被挡下的那几条恰恰是最该看的——它们说的是"谁在反复撞门"。
/// 不接一个出口的话，这些记录写进的是一个丢弃一切的实现，表现是"审计功能已经有了"，
/// 而实际上一条都读不到。
/// </para>
/// <para>
/// 写**标准错误**而不是标准输出：网络这条传输的标准输出没被协议占用，但容器与守护进程的
/// 惯例里它是"程序的结果"，把日志混进去之后，接它的人会拿到一份混着日志的结果。
/// 标准输入输出那条传输上的日志也走标准错误，两条通路的去处一致，
/// 同一份告警在两条通路上按同一种方式筛得出来。
/// </para>
/// </remarks>
internal sealed class StandardErrorDiagnostics : IDiagnosticsSink
{
    public static StandardErrorDiagnostics Instance { get; } = new();

    private StandardErrorDiagnostics()
    {
    }

    public void Warn(string message) => Console.Error.WriteLine($"warn: {message}");

    public void Error(string message) => Console.Error.WriteLine($"error: {message}");
}
