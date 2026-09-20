namespace DuetDiagram.Core.Bus;

/// <summary>
/// 乐观并发的版本检查请求（方案 §4.9）。MCP 层把 clientState 转换为此类型。
/// </summary>
public sealed record VersionCheckRequest
{
    public required int ClientVersion { get; init; }

    public string? ClientStructuralHash { get; init; }
}
