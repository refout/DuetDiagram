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

    /// <summary>要操作的组合不存在。</summary>
    public const string CompositeMissing = "COMPOSITE_MISSING";

    /// <summary>组合的父子关系成环。</summary>
    public const string GroupCycle = "GROUP_CYCLE";

    /// <summary>
    /// 组合的嵌套深度超过上限。
    /// </summary>
    /// <remarks>
    /// 上限见 <c>CompositeLimits.MaxDepth</c>，命令层与整体校验器读的是同一个常量。
    /// 单独一个码而不是复用成环那一个：成环是"这条归属关系本身不成立"，
    /// 而超深是"每一条归属关系都成立，只是整体太深了"——前者的处置是断开那一环，
    /// 后者是别再往里套。
    /// </remarks>
    public const string CompositeTooDeep = "COMPOSITE_TOO_DEEP";

    /// <summary>要操作的图层不存在。</summary>
    public const string LayerMissing = "LAYER_MISSING";

    /// <summary>要操作的页面不存在。</summary>
    public const string PageMissing = "PAGE_MISSING";

    /// <summary>
    /// 文档至少要留一页，这一页是最后一页，删不得。
    /// </summary>
    /// <remarks>
    /// 单独一个码而不是复用"页面不存在"：那一种是"这个标识写错了或已经没了"，
    /// 处置是换一个标识；这一种是"这一页确实在，但删掉之后文档就没有页了"，
    /// 处置是先建一页再删。渲染层拿到空页面集合时该画什么没有定义，
    /// 所以这条限制放在命令层而不是留给渲染层去兜底。
    /// </remarks>
    public const string PageRequired = "PAGE_REQUIRED";

    /// <summary>要操作的标签不存在。</summary>
    public const string TagMissing = "TAG_MISSING";

    /// <summary>要操作的动作不存在。</summary>
    public const string ActionMissing = "ACTION_MISSING";

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

    /// <summary>
    /// 要改的字段没有登记在字段表里。
    /// </summary>
    /// <remarks>
    /// 单独一个码而不是并进 <see cref="FieldValueInvalid"/>：两者的处置完全不同——
    /// 字段名认不出说明调用方拿的是另一套字段表（版本对不上或拼错了），
    /// 而值不合法只说明这一次输入有问题，字段本身是对的。
    /// </remarks>
    public const string FieldUnknown = "FIELD_UNKNOWN";

    /// <summary>字段的值解析不出该字段要的类型。</summary>
    public const string FieldValueInvalid = "FIELD_VALUE_INVALID";

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

    /// <summary>
    /// 布局约束本身不成立：成员不够、主语缺失或多余、成员重复。
    /// </summary>
    /// <remarks>
    /// 与"引用的元素不存在"分开：那两种码说明图里少了东西，而这个码说明这条约束
    /// 从一开始就描述不出任何东西。前者的处置是补上缺的元素，后者是改这条约束的写法。
    /// </remarks>
    public const string LayoutConstraintInvalid = "LAYOUT_CONSTRAINT_INVALID";

    /// <summary>要删除的布局约束不存在。</summary>
    public const string LayoutConstraintMissing = "LAYOUT_CONSTRAINT_MISSING";

    /// <summary>要改或要删的调色板条目不存在。</summary>
    public const string PaletteEntryMissing = "PALETTE_ENTRY_MISSING";

    /// <summary>
    /// 这条调色板条目还被样式令牌引用着，删掉会让那些元素悄悄变样。
    /// </summary>
    /// <remarks>
    /// 单独一个码而不是复用"条目不存在"：前者是"换个名字再来"，
    /// 后者是"先把引用它的元素改掉"。两者的处置完全不同。
    /// 与删边那一处留下的悬空引用也刻意不同：那边有整体校验器会报出来，
    /// 而删掉一个被引用的调色板条目，渲染层只是静默退回元素自己的样式，没有任何东西会报。
    /// </remarks>
    public const string PaletteEntryInUse = "PALETTE_ENTRY_IN_USE";

    /// <summary>
    /// 这份文档这一份是只读的：另一个进程正拿着它。
    /// </summary>
    /// <remarks>
    /// 单独一个码而不是复用"版本冲突"：版本冲突是"你手上的副本旧了，同步一下再来"，
    /// 而这个码是"这一次操作在这份文档上根本不允许"。前者的处置是同步，后者是去改那一份。
    /// 报成版本冲突的话，用户会一遍遍重试，而重试永远不会成功。
    /// </remarks>
    public const string DocumentReadOnly = "DOCUMENT_READ_ONLY";

    #endregion
}
