namespace DuetDiagram.Core.Diagnostics;

/// <summary>
/// 诊断出口。Core 不引入 Microsoft.Extensions.Logging（P1 判据 #1：仅依赖 BCL），
/// 但方案 §4.9 明确要求「SessionId 不一致时记录警告日志」，因此保留一个最小接口。
/// </summary>
public interface IDiagnosticsSink
{
    void Warn(string message);

    void Error(string message);
}

/// <summary>默认实现：丢弃。</summary>
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

/// <summary>收集实现。测试用，也用于 GUI 的状态栏提示。</summary>
public sealed class CollectingDiagnosticsSink : IDiagnosticsSink
{
    private readonly List<string> _warnings = [];
    private readonly List<string> _errors = [];

    public IReadOnlyList<string> Warnings => _warnings;

    public IReadOnlyList<string> Errors => _errors;

    public void Warn(string message) => _warnings.Add(message);

    public void Error(string message) => _errors.Add(message);
}
