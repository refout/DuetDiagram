using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Sidecar;

/// <summary>
/// 人工产物。
/// </summary>
/// <remarks>
/// <para>
/// 与布局结果相反，这份内容**不可丢**：它是用户手工调整的结果，
/// 重算不出来。因此它的处置策略与布局缓存完全不同——
/// 解析失败时不能"丢掉重来"，只能走备份恢复那条路。
/// </para>
/// <para>
/// 三样东西的共同点是"人工覆盖了自动结果"：固定位置覆盖布局算出的坐标，
/// 固定折线覆盖路由算出的路径，自定义端口覆盖按形状均分的端口。
/// 分开存是为了让"哪些是机器算的、哪些是人定的"一目了然，
/// 而不是混在一份文件里靠字段名去猜。
/// </para>
/// <para>
/// 键一律区分大小写，与 IR 中的标识比较规则一致。不区分的话，
/// <c>Node1</c> 与 <c>node1</c> 会指向同一个条目，而 IR 里它们是两个不同的节点。
/// </para>
/// </remarks>
public sealed record UserSidecar
{
    public string? DocumentId { get; init; }

    /// <summary>节点标识到固定位置的映射。</summary>
    public IReadOnlyDictionary<string, Anchor> PinnedNodes { get; init; } =
        new Dictionary<string, Anchor>(StringComparer.Ordinal);

    /// <summary>边标识到固定折线的映射。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Anchor>> PinnedEdges { get; init; } =
        new Dictionary<string, IReadOnlyList<Anchor>>(StringComparer.Ordinal);

    /// <summary>节点标识到自定义端口的映射。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PortDef>> CustomPorts { get; init; } =
        new Dictionary<string, IReadOnlyList<PortDef>>(StringComparer.Ordinal);

    /// <summary>
    /// 结构化相等。
    /// </summary>
    /// <remarks>
    /// 必须重写：三个成员都是集合。其中两个的值还是列表，
    /// 普通字典比较会把列表值按引用比，于是内容相同的两份被判为不等。
    /// </remarks>
    public bool Equals(UserSidecar? other) =>
        other is not null
        && string.Equals(DocumentId, other.DocumentId, StringComparison.Ordinal)
        && Model.CollectionEquality.Map(PinnedNodes, other.PinnedNodes)
        && Model.CollectionEquality.MapOfLists(PinnedEdges, other.PinnedEdges)
        && Model.CollectionEquality.MapOfLists(CustomPorts, other.CustomPorts);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(DocumentId, StringComparer.Ordinal);
        hash.Add(Model.CollectionEquality.MapHash(PinnedNodes));
        hash.Add(Model.CollectionEquality.MapOfListsHash(PinnedEdges));
        hash.Add(Model.CollectionEquality.MapOfListsHash(CustomPorts));

        return hash.ToHashCode();
    }
}

/// <summary>
/// 加载状态。
/// </summary>
/// <remarks>
/// 状态与内容分开，是因为"没有内容"有四种原因，而它们该触发完全不同的后续动作。
/// 合成一个空值让调用方自己去猜，是最容易把"缓存过期"当成"文件损坏"处理的做法。
/// </remarks>
public enum SidecarStatus
{
    /// <summary>读到了并且有效。</summary>
    Loaded,

    /// <summary>文件不存在。首次打开文档就是这样，属于正常情况。</summary>
    Missing,

    /// <summary>
    /// 内容过期。
    /// </summary>
    /// <remarks>
    /// 只对布局缓存有意义：文档改过之后哈希对不上，缓存自然失效。
    /// 处置是静默重算，不是报错——用户没有做错任何事。
    /// </remarks>
    Stale,

    /// <summary>
    /// 内容损坏或与本份文档不符，无法使用。
    /// </summary>
    /// <remarks>
    /// 对布局缓存来说丢掉即可；对人工产物来说这意味着要提示用户，
    /// 因为那份内容重算不出来。两者的处置差异由调用方按文件类型决定。
    /// </remarks>
    Unusable,
}

/// <summary>
/// 一次加载的结果。
/// </summary>
/// <param name="Value">读到的内容。状态不是 <see cref="SidecarStatus.Loaded"/> 时为空。</param>
/// <param name="Status">加载状态。</param>
/// <param name="Orphans">
/// 指向了不存在元素的条目。
/// </param>
/// <param name="Detail">给人看的说明，便于排查。不参与任何判断。</param>
/// <remarks>
/// <para>
/// <see cref="Orphans"/> 单独列出来而不是直接丢弃：条目指向的节点可能只是被临时删掉了，
/// 撤销回来之后这条固定位置仍然有意义。直接丢掉等于让用户的一次误删顺手毁掉手工调整。
/// </para>
/// <para>
/// 是否真的丢弃由调用方决定。加载器只负责如实报告。
/// </para>
/// </remarks>
public sealed record SidecarLoad<T>(
    T? Value,
    SidecarStatus Status,
    IReadOnlyList<string> Orphans,
    string? Detail = null)
{
    public bool IsUsable => Status == SidecarStatus.Loaded;

    public static SidecarLoad<T> Missing() => new(default, SidecarStatus.Missing, []);

    public static SidecarLoad<T> Unusable(string detail) => new(default, SidecarStatus.Unusable, [], detail);

    public static SidecarLoad<T> Loaded(T value, IReadOnlyList<string> orphans) =>
        new(value, SidecarStatus.Loaded, orphans);

    public static SidecarLoad<T> Stale(string detail) =>
        new(default, SidecarStatus.Stale, [], detail);
}
