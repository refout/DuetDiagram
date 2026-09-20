namespace DuetDiagram.Core.Sidecar;

/// <summary>一个坐标点。</summary>
public sealed record Anchor(double X, double Y);

/// <summary>节点的位置与尺寸。</summary>
public sealed record NodePlacement(double X, double Y, double Width, double Height);

/// <summary>一条边的折线。</summary>
public sealed record EdgePath(string EdgeId, IReadOnlyList<Anchor> Points)
{
    /// <summary>结构化相等。必须重写：<see cref="Points"/> 是集合。</summary>
    public bool Equals(EdgePath? other) =>
        other is not null
        && string.Equals(EdgeId, other.EdgeId, StringComparison.Ordinal)
        && Model.CollectionEquality.List(Points, other.Points);

    public override int GetHashCode() =>
        HashCode.Combine(EdgeId, Model.CollectionEquality.ListHash(Points));
}

/// <summary>
/// 自动布局结果。
/// </summary>
/// <remarks>
/// <para>
/// 这是**缓存**，不是事实源。丢了可以重算，所以它带的哈希是用来判断"还能不能用"的，
/// 不是用来保证它正确的。
/// </para>
/// <para>
/// 哈希不符时正确的处置是**静默重算**，而不是报错。用户没有做错任何事——
/// 他只是改了图，缓存自然过期了。把它当成错误弹出来，会让每次正常编辑都伴随一次弹窗。
/// </para>
/// <para>
/// 记录文档标识与版本是为了排查：坐标对不上时，第一个要问的问题是
/// "这份布局是给哪份文档、哪个版本算的"。
/// </para>
/// </remarks>
public sealed record LayoutSidecar
{
    /// <summary>算这份布局时文档的结构哈希。它是判断缓存能否复用的唯一依据。</summary>
    public required string StructuralHash { get; init; }

    public string? DocumentId { get; init; }

    /// <summary>算这份布局时文档的版本号。仅供排查，不参与有效性判断。</summary>
    public int Version { get; init; }

    public IReadOnlyDictionary<string, NodePlacement> Nodes { get; init; } =
        new Dictionary<string, NodePlacement>(StringComparer.Ordinal);

    public IReadOnlyList<EdgePath> Edges { get; init; } = [];

    /// <summary>整幅图的尺寸。用于恢复视口。</summary>
    public double Width { get; init; }

    public double Height { get; init; }

    /// <summary>
    /// 结构化相等。
    /// </summary>
    /// <remarks>
    /// 必须重写：<see cref="Nodes"/> 与 <see cref="Edges"/> 是集合，
    /// 而记录自动生成的相等性对集合用引用比较，内容相同的两份内容会被判为不等。
    /// 这个陷阱在 IR 那边已经踩过一次，这里不能再踩。
    /// </remarks>
    public bool Equals(LayoutSidecar? other) =>
        other is not null
        && string.Equals(StructuralHash, other.StructuralHash, StringComparison.Ordinal)
        && string.Equals(DocumentId, other.DocumentId, StringComparison.Ordinal)
        && Version == other.Version
        && Model.CollectionEquality.Map(Nodes, other.Nodes)
        && Model.CollectionEquality.List(Edges, other.Edges)
        && Width.Equals(other.Width)
        && Height.Equals(other.Height);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(StructuralHash, StringComparer.Ordinal);
        hash.Add(DocumentId, StringComparer.Ordinal);
        hash.Add(Version);
        hash.Add(Model.CollectionEquality.MapHash(Nodes));
        hash.Add(Model.CollectionEquality.ListHash(Edges));
        hash.Add(Width);
        hash.Add(Height);

        return hash.ToHashCode();
    }
}
