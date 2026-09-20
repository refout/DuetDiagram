namespace DuetDiagram.Core.Commands;

/// <summary>
/// 会话标识的统一生成入口。
/// </summary>
/// <remarks>
/// 会话标识会写进审计日志和版本日志，如果各处自己拼字符串，同一个窗口很容易出现
/// "gui:w1" 和 "gui:W1" 两种写法，跨进程比对时就认不出是同一个会话。
/// 所以把格式收敛到这几个工厂方法里：冒号分隔，第一段是入口类型，
/// 后面的段数按入口不同而不同（MCP 需要令牌号加会话号两级）。
/// </remarks>
public static class SessionIds
{
    public static string Gui(string windowId) => $"gui:{windowId}";

    public static string Mcp(string tokenId, string sessionId) => $"mcp:{tokenId}:{sessionId}";

    public static string Llm(string conversationId) => $"llm:{conversationId}";

    public static string Import(string filename) => $"import:{filename}";
}
