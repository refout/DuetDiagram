namespace DuetDiagram.Core.Commands;

/// <summary>
/// 当前操作主体。GUI 单窗口、MCP 连接、内部 LLM 会话各自实现。
/// </summary>
public interface ISessionProvider
{
    string? CurrentActorId { get; }

    string? CurrentSessionId { get; }
}

/// <summary>默认实现。测试与简单宿主使用。</summary>
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
