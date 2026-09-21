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

    #region 整体校验器使用的码
    //
    // 上面那些由命令的前置检查产生，下面这些由加载文件、接收同步结果时的整体校验产生。
    // 分成两段是因为两者的使用场合不同：前者在写入前挡，后者在外部内容进来时挡。
    // 命名风格一致，外部代理不需要区分来源就能按同一张表处理。

    /// <summary>边指定的端口在该节点上不存在。</summary>
    public const string EdgePortMissing = "EDGE_PORT_MISSING";

    /// <summary>边的一端是组合，却指定了端口。组合没有端口。</summary>
    public const string EdgePortOnComposite = "EDGE_PORT_ON_COMPOSITE";

    /// <summary>布局约束引用的节点不存在。</summary>
    public const string LayoutNodeMissing = "LAYOUT_NODE_MISSING";

    /// <summary>层内次序引用的边不存在，或者不是主语节点的出边。</summary>
    public const string LayoutOrderEdgeMissing = "LAYOUT_ORDER_EDGE_MISSING";

    /// <summary>组合的成员列表与成员的父级字段互相矛盾。</summary>
    public const string MembershipMismatch = "MEMBERSHIP_MISMATCH";

    /// <summary>节点或组合的父级指向了一个不存在的组合。</summary>
    public const string ParentMissing = "PARENT_MISSING";

    /// <summary>标签的成员不存在。</summary>
    public const string TagMemberMissing = "TAG_MEMBER_MISSING";

    /// <summary>动作的目标不存在。</summary>
    public const string ActionTargetMissing = "ACTION_TARGET_MISSING";

    #endregion
}
