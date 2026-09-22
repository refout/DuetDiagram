using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Time;
using DuetDiagram.Llm.Context;

namespace DuetDiagram.Llm.Tools;

/// <summary>
/// 一次会话里工具要用的东西。
/// </summary>
/// <remarks>
/// <para>
/// 文档之外的三样都不是 IR 里的内容，只有宿主拿得到：固定位置在人工产物里，
/// 层投影在布局结果里，变更记录在命令总线的版本日志里。
/// </para>
/// <para>
/// **不用静态字段兜。** 静态字段会让同一个进程里的两个窗口互相看到对方的文档，
/// 而表现是「摘要里的图不是我这一张」——只在多窗口下出现，且看起来像是随机串了。
/// 传进来就不会有这个问题：每个窗口自己一份上下文。
/// </para>
/// </remarks>
public sealed record DiagramToolContext
{
    public required DiagramDocument Document { get; init; }

    /// <summary>版本日志。为空表示没有变更记录，摘要里的「最近修改」就是空的。</summary>
    public VersionLog? VersionLog { get; init; }

    /// <summary>被固定的节点标识。来自人工产物。</summary>
    public IReadOnlyList<string> PinnedNodes { get; init; } = [];

    /// <summary>最近一次布局里每个节点落在第几层。为空表示还没有排过。</summary>
    public IReadOnlyList<NodeRank> Placement { get; init; } = [];

    /// <summary>
    /// 读时刻的地方。
    /// </summary>
    /// <remarks>
    /// 摘要里的「最近修改」写的是相对时间，而相对时间需要一个参照时刻。
    /// 默认读系统时间；测试传一个固定的，这样同一份摘要两次渲染得到同一段文本。
    /// </remarks>
    public ITimeProvider Clock { get; init; } = SystemTimeProvider.Instance;

    /// <summary>把上下文里那几样非 IR 的内容收成摘要输入。</summary>
    internal SummaryInput ToSummaryInput() => new()
    {
        Document = Document,
        Placement = Placement,
        PinnedNodes = PinnedNodes,
        RecentChanges = VersionLog?.Snapshot() ?? [],
    };
}
