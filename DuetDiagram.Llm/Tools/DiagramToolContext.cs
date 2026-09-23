using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Time;
using DuetDiagram.Llm.Context;

namespace DuetDiagram.Llm.Tools;

/// <summary>
/// 一次会话里工具要用的东西。
/// </summary>
/// <remarks>
/// <para>
/// 工具作用在某一份文档上，而改那份文档的唯一入口是命令总线。总线自己就拿着文档与
/// 版本日志，所以这里只存总线，另外两样是派生出来的。
/// </para>
/// <para>
/// **不各存一份。** 各存一份的话，迟早会有人传进来一份「总线管着甲的文档、上下文里的文档
/// 是乙」——那种不一致没有任何东西会报错，表现是摘要里的图与改动结果对不上。
/// </para>
/// <para>
/// **也不用静态字段兜。** 静态字段会让同一个进程里的两个窗口互相看到对方的文档，
/// 而表现是「摘要里的图不是我这一张」——只在多窗口下出现，且看起来像是随机串了。
/// 传进来就不会有这个问题：每个窗口自己一份上下文。
/// </para>
/// </remarks>
public sealed record DiagramToolContext
{
    public required DiagramCommandBus Bus { get; init; }

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

    /// <summary>
    /// 每条命令执行前要报给总线的版本声明。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 由传输层填：外部代理把「我这边看到的是第几版」随每条请求带上来，总线据此挡住
    /// 照着旧副本改的那一类写入。为空表示这条通路不做版本检查——界面那条通路就是，
    /// 它的写入方只有一个，没有别人可能改过这份文档。
    /// </para>
    /// <para>
    /// **用委托而不是一个值。** 它随每条请求变，而工具是按会话建一次、之后一直复用的：
    /// 存一个值的话，第二次调用会拿着第一次声明的版本去比对，而那个版本已经旧了，
    /// 表现是「第一次改得动、第二次改不动」，且两边都不报错。
    /// </para>
    /// </remarks>
    public Func<VersionCheckRequest?>? ExpectedVersion { get; init; }

    /// <summary>当前文档。与总线管着的是同一个对象。</summary>
    public DiagramDocument Document => Bus.Context.Document;

    /// <summary>把上下文里那几样非 IR 的内容收成摘要输入。</summary>
    internal SummaryInput ToSummaryInput() => new()
    {
        Document = Document,
        Placement = Placement,
        PinnedNodes = PinnedNodes,
        RecentChanges = Bus.Context.VersionLog.Snapshot(),
    };
}
