namespace DuetDiagram.Core.Commands;

/// <summary>
/// 结构化错误码。
/// </summary>
/// <remarks>
/// 用字符串常量而不是枚举，目的是让错误码能直接出现在 JSON 协议里，
/// 外部代理写代码判断时不需要知道枚举顺序，也不怕将来插入新值导致序号漂移。
/// 命名统一用大写下划线。
/// </remarks>
public static class ErrorCodes
{
    /// <summary>要创建的节点或边的标识已经存在。</summary>
    public const string DuplicateId = "DUPLICATE_ID";

    /// <summary>边的终点节点不存在。</summary>
    public const string EdgeTargetMissing = "EDGE_TARGET_MISSING";

    /// <summary>边的起点节点不存在。</summary>
    public const string EdgeSourceMissing = "EDGE_SOURCE_MISSING";

    /// <summary>组合的成员不存在。</summary>
    public const string GroupMemberMissing = "GROUP_MEMBER_MISSING";

    /// <summary>组合的父子关系成环。</summary>
    public const string GroupCycle = "GROUP_CYCLE";

    /// <summary>调用方持有的版本落后于当前版本，需要先同步。</summary>
    public const string VersionConflict = "VERSION_CONFLICT";

    /// <summary>调用方声称的版本号比当前版本还新，属于参数错误而非并发冲突。</summary>
    public const string InvalidExpectedVersion = "INVALID_EXPECTED_VERSION";

    /// <summary>这条通路要求携带版本信息，但调用方没带。</summary>
    public const string ExpectedVersionRequired = "EXPECTED_VERSION_REQUIRED";

    /// <summary>布局的各级降级全部超时。</summary>
    public const string LayoutAllLevelsTimeout = "LAYOUT_ALL_LEVELS_TIMEOUT";

    /// <summary>调用方未通过认证。</summary>
    public const string McpUnauthorized = "MCP_UNAUTHORIZED";

    /// <summary>调用方触发了速率限制。</summary>
    public const string McpRateLimited = "MCP_RATE_LIMITED";

    /// <summary>命令实现内部出错。对外只暴露这个码，不带任何内部细节。</summary>
    public const string InternalError = "INTERNAL_ERROR";

    /// <summary>要操作的节点不存在。</summary>
    public const string NodeMissing = "NODE_MISSING";

    /// <summary>要操作的边不存在。</summary>
    public const string EdgeMissing = "EDGE_MISSING";

    /// <summary>标识为空或全是空白字符。</summary>
    public const string InvalidId = "INVALID_ID";
}
