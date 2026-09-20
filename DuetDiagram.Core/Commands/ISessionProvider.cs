namespace DuetDiagram.Core.Commands;

/// <summary>
/// 当前操作主体的来源。界面窗口、外部代理连接、内置 AI 会话各实现一份。
/// </summary>
/// <remarks>
/// 总线在需要回填会话信息时只读这个接口，不关心宿主是什么形态。
/// 两个属性都允许为空：导入这类批处理场景没有明确的操作主体，
/// 强行编造一个假的标识反而会让审计日志失真。
/// </remarks>
public interface ISessionProvider
{
    string? CurrentActorId { get; }

    string? CurrentSessionId { get; }
}

/// <summary>可写的默认实现，供测试与简单宿主直接改字段。</summary>
public sealed class SimpleSessionProvider : ISessionProvider
{
    public SimpleSessionProvider(string? actorId = null, string? sessionId = null)
    {
        CurrentActorId = actorId;
        CurrentSessionId = sessionId;
    }

    public string? CurrentActorId { get; set; }

    public string? CurrentSessionId { get; set; }
}
