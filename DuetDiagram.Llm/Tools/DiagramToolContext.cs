using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Concurrency;
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

    /// <summary>
    /// 这一次调用所属主体能改哪些图层。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 由传输层填，随**每一条请求**变。缺省是不受限：界面那条通路没有"凭据"这回事，
    /// 用它的宿主也就不该被图层挡住，而它拿不到这一项时得到的是不受限的那一份。
    /// </para>
    /// <para>
    /// **用委托而不是一个值**，理由与 <see cref="ExpectedVersion"/> 相同：工具是按会话
    /// 建一次、之后一直复用的，而权限随请求的主体变。存一个值的话，第一条请求的图层范围
    /// 会一直管着后面的每一条，而表现是"换个凭据还是被同一份范围挡着"。
    /// </para>
    /// <para>
    /// 判定放在动作分发那一侧，不放在这里：一次写入有没有点名图层、点名的是哪个，
    /// 只有解析过动作参数的那一层知道。放在这里的话，上下文得先替所有动作把参数读一遍，
    /// 而那正是各工具自己的事。
    /// </para>
    /// </remarks>
    public Func<PermissionSet> Permissions { get; init; } = static () => PermissionSet.Full;

    /// <summary>
    /// 把一份文档导成 SVG。由宿主喂进来。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **用委托而不是直接引渲染层。** SVG 不是文档的函数——它要先把文档排成布局、
    /// 再按字体量出标签尺寸，而那两步一个在布局引擎里、一个在绘图后端的字体度量里，
    /// 两者都是需要平台适配的第三方组件。这一层只做"把文档翻译成工具调用"，
    /// 引进来之后它连同它的每个宿主都要带上原生绘图库。
    /// </para>
    /// <para>
    /// 与 <see cref="Placement"/>、<see cref="PinnedNodes"/> 同一口径：那两样也不是文档的
    /// 函数（一个在布局结果里、一个在人工产物里），所以只能由宿主喂进来。
    /// 对照 DSL 导出——那是文档的纯函数、只依赖 Core，所以它直接调，不绕这一道。
    /// </para>
    /// <para>
    /// 参数是文档与页面标识：页面过滤那一套口径在 Core 里只有一份，
    /// 而它同时被布局与绘制列表构建用到，所以按页过滤不能在这一层先做一遍。
    /// </para>
    /// <para>
    /// 为空表示这个宿主没接上渲染层；返回空表示渲染层拿到了文档却排不出结果
    /// （例如布局彻底失败）。两种都不许悄悄给一份空文件——调用方会把一份空图
    /// 当成"这张图就是空的"。失败的原因归宿主：这一层看不见布局引擎的异常类型，
    /// 也就无从分辨"排不出来"与"程序坏了"。
    /// </para>
    /// </remarks>
    public Func<DiagramDocument, string?, SvgExport?>? SvgExporter { get; init; }

    /// <summary>
    /// 把一份文档导成 PNG。由宿主喂进来。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与 <see cref="SvgExporter"/> 同一口径、同一理由：位图也不是文档的函数
    /// （它要先把文档排成布局、再按字体量出标签尺寸，还要一块离屏画布去光栅化），
    /// 所以只能由宿主喂进来。这一层不引渲染层，引了的话它连同它的每个宿主
    /// 都要带上原生绘图库。
    /// </para>
    /// <para>
    /// **导出必须无头**，理由正在于喂它的是谁：服务端与模型那条通路都没有窗口平台，
    /// 依赖窗口的话，命令行导出必失败而界面上一切正常。
    /// </para>
    /// <para>
    /// 为空表示这个宿主没接上渲染层；返回空表示渲染层拿到了文档却排不出结果。
    /// 两种都不许悄悄给一张空图——调用方会把空图当成"这张图就是空的"。
    /// </para>
    /// </remarks>
    public Func<DiagramDocument, string?, BitmapExport?>? BitmapExporter { get; init; }

    /// <summary>
    /// 把一份文档导成 PDF。由宿主喂进来。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与 <see cref="SvgExporter"/>、<see cref="BitmapExporter"/> 同一口径、同一理由：
    /// 也不是文档的函数，所以只能由宿主喂进来。
    /// </para>
    /// <para>
    /// **它与那两个有一处不同：一份文档可以出好几页。** 页面标识为空时，
    /// 文档自己有哪几页就出哪几页；点了名就只出那一页。所以喂进来的这个函数
    /// 要替调用方决定"整份文档是几页"，而那不是这一层能算的——
    /// 这一层看不见页面归属那一套口径。
    /// </para>
    /// <para>
    /// 为空表示这个宿主没接上渲染层；返回空表示渲染层拿到了文档却排不出结果。
    /// 两种都不许悄悄给一份空文件——调用方会把空文件当成"这张图就是空的"。
    /// </para>
    /// </remarks>
    public Func<DiagramDocument, string?, PdfExport?>? PdfExporter { get; init; }

    /// <summary>当前文档。与总线管着的是同一个对象。</summary>
    public DiagramDocument Document => Bus.Context.Document;

    /// <summary>把上下文里那几样非 IR 的内容收成摘要输入。</summary>
    /// <param name="pageId">只看这一页。传空表示整份文档。</param>
    internal SummaryInput ToSummaryInput(string? pageId = null) => new()
    {
        Document = Document,
        PageId = pageId,
        Placement = Placement,
        PinnedNodes = PinnedNodes,
        RecentChanges = Bus.Context.VersionLog.Snapshot(),
    };
}
