namespace DuetDiagram.Core.Bus;

/// <summary>
/// 调用方对自己所见版本的声明：我停在哪个版本，以及我那边的结构是什么样。
/// </summary>
/// <remarks>
/// 两个字段分工不同。<see cref="ClientVersion"/> 用来判断"有没有落后"，
/// 落后多少决定回什么差异。<see cref="ClientStructuralHash"/> 是可选优化：
/// 调用方声明结构没变过时，服务端可以只回一份受影响元素清单让它自己重取，
/// 而不必搬运整份文档。留空表示调用方不打算用这条捷径。
/// </remarks>
public sealed record VersionCheckRequest
{
    public required int ClientVersion { get; init; }

    public string? ClientStructuralHash { get; init; }
}
