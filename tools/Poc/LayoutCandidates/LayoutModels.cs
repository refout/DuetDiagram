namespace DuetDiagram.Poc.LayoutCandidates;

/// <summary>归一化的节点：只保留布局真正需要的信息。</summary>
internal sealed record CandidateNode(string Id, double Width, double Height);

/// <summary>归一化的边。只保留拓扑，不含样式。</summary>
internal sealed record CandidateEdge(string From, string To);

/// <summary>归一化的分组。支持嵌套，用来验证复合图。</summary>
internal sealed record CandidateGroup(string Id, string Label, string[] Members, CandidateGroup[] Children);

/// <summary>传给引擎的完整输入。这份结构不含任何引擎特性，各适配器各自翻译。</summary>
internal sealed record CandidateGraph(
    string Name,
    CandidateNode[] Nodes,
    CandidateEdge[] Edges,
    CandidateGroup[] Groups);

/// <summary>可调的布局参数。三个参数分别对应"方向""同层间距""层间间距"。</summary>
internal sealed record LayoutKnobs(string Direction, double NodeSpacing, double LayerSpacing)
{
    public static LayoutKnobs Default { get; } = new("TD", 36, 72);
}

/// <summary>布局结果的矩形。</summary>
internal sealed record CandidateBox(string Id, double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;
}

/// <summary>布局结果。节点与分组分别给出，附带整体画布尺寸。</summary>
internal sealed record CandidateResult(
    CandidateBox[] Nodes,
    CandidateBox[] Groups,
    double Width,
    double Height)
{
    public CandidateBox? Find(string id) => Nodes.FirstOrDefault(n => n.Id == id);
}

/// <summary>
/// 引擎输入模型的事实。
/// </summary>
/// <remarks>
/// 这些结论来自对公开成员的反射枚举，而不是阅读文档。
/// 判断"有没有固定位置的输入通道"必须看输入类型本身：
/// 如果连坐标字段都不存在，调用方就无从表达"这个节点固定在这里"，
/// 引擎内部再怎么处理也帮不上忙。
/// </remarks>
internal sealed record LayoutInputFacts(
    bool HasPinnedField,
    bool HasOrderOrAlignOrPlaceField,
    string NodeMembers,
    string LayoutMembers);

/// <summary>固定位置的行为验证结果。</summary>
internal sealed record PinProbeResult(bool Attempted, bool Honored, string Evidence);

/// <summary>同层约束的行为验证结果。</summary>
internal sealed record SameRankProbeResult(bool Attempted, bool Honored, string Evidence);

/// <summary>一个候选布局引擎。</summary>
internal interface ILayoutCandidate
{
    string Name { get; }

    /// <summary>输入模型的事实，来自反射。</summary>
    LayoutInputFacts InputFacts { get; }

    CandidateResult Compute(CandidateGraph graph, LayoutKnobs knobs);

    /// <summary>
    /// 尝试把若干节点固定在指定坐标，看引擎是否尊重。
    /// </summary>
    /// <remarks>
    /// 仅在输入模型里确实存在坐标字段时才有意义。没有任何表达方式时返回"未尝试"，
    /// 这与"尝试了但被忽略"是两种不同的结论，不能混为一谈。
    /// </remarks>
    PinProbeResult ProbePinned(CandidateGraph graph, LayoutKnobs knobs);

    /// <summary>
    /// 尝试把两个本来不同层的节点拉到同一层，看引擎是否尊重。
    /// </summary>
    /// <remarks>
    /// "同层"对应的视觉诉求是"这两个是并列的"。各引擎表达它的手段不同，
    /// 所以由适配器各自用自己支持的方式尝试，再由统一判据判断纵坐标是否真的对齐了。
    /// </remarks>
    SameRankProbeResult ProbeSameRank(CandidateGraph graph, LayoutKnobs knobs);
}
