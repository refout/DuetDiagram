namespace DuetDiagram.Core.Commands;

/// <summary>
/// 结构化错误码。方案 §18 错误码表的唯一来源。
/// </summary>
public static class ErrorCodes
{
    // —— 方案 §18 错误码表 ——
    public const string DuplicateId = "DUPLICATE_ID";
    public const string EdgeTargetMissing = "EDGE_TARGET_MISSING";
    public const string EdgeSourceMissing = "EDGE_SOURCE_MISSING";
    public const string GroupMemberMissing = "GROUP_MEMBER_MISSING";
    public const string GroupCycle = "GROUP_CYCLE";
    public const string VersionConflict = "VERSION_CONFLICT";
    public const string InvalidExpectedVersion = "INVALID_EXPECTED_VERSION";
    public const string ExpectedVersionRequired = "EXPECTED_VERSION_REQUIRED";
    public const string LayoutAllLevelsTimeout = "LAYOUT_ALL_LEVELS_TIMEOUT";
    public const string McpUnauthorized = "MCP_UNAUTHORIZED";
    public const string McpRateLimited = "MCP_RATE_LIMITED";
    public const string InternalError = "INTERNAL_ERROR";

    // —— 本轮扩展 ——
    // 方案 §18 表以行为例，未穷举命令级前置条件。以下两条按同风格补充，
    // 登记在 docs/Error-Codes.md，供后续任务复用，不得另起名字。
    public const string NodeMissing = "NODE_MISSING";
    public const string EdgeMissing = "EDGE_MISSING";
    public const string InvalidId = "INVALID_ID";
}
