namespace DuetDiagram.Core.Commands;

/// <summary>
/// 会话标识的唯一格式来源。手写字符串是 SessionId 不一致的主要来源，因此集中在此。
/// </summary>
public static class SessionIds
{
    public static string Gui(string windowId) => $"gui:{windowId}";

    public static string Mcp(string tokenId, string sessionId) => $"mcp:{tokenId}:{sessionId}";

    public static string Llm(string conversationId) => $"llm:{conversationId}";

    public static string Import(string filename) => $"import:{filename}";
}
