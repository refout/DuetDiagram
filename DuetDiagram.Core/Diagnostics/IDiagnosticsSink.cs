namespace DuetDiagram.Core.Diagnostics;

/// <summary>
/// 诊断出口。核心程序集不引入任何日志框架，但确实需要把少数异常情况报出去。
/// </summary>
/// <remarks>
/// 只有两个级别，因为核心层要报的东西确实只有两类：
/// 一类是"能继续跑但值得注意"（警告），一类是"这里出问题了"（错误）。
/// 引入更细的分级只会增加实现方的负担，而真正的分级过滤交给宿主自己的日志框架去做。
/// </remarks>
public interface IDiagnosticsSink
{
    void Warn(string message);

    void Error(string message);
}

/// <summary>默认实现：丢弃所有消息。用于不关心诊断的宿主与单元测试。</summary>
public sealed class NullDiagnosticsSink : IDiagnosticsSink
{
    public static NullDiagnosticsSink Instance { get; } = new();

    private NullDiagnosticsSink()
    {
    }

    public void Warn(string message)
    {
    }

    public void Error(string message)
    {
    }
}

/// <summary>
/// 把消息收进内存列表。测试用它断言"该告警的时候确实告警了"，
/// 界面也可以用它把最近的告警显示在状态栏。
/// </summary>
public sealed class CollectingDiagnosticsSink : IDiagnosticsSink
{
    private readonly List<string> _warnings = [];
    private readonly List<string> _errors = [];

    public IReadOnlyList<string> Warnings => _warnings;

    public IReadOnlyList<string> Errors => _errors;

    public void Warn(string message) => _warnings.Add(message);

    public void Error(string message) => _errors.Add(message);
}
