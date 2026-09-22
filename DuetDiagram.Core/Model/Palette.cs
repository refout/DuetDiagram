namespace DuetDiagram.Core.Model;

/// <summary>
/// 调色板条目。
/// </summary>
/// <remarks>
/// 令牌名到具体外观的映射。节点与边只写令牌名，实际颜色由这里展开——
/// 换主题时只改这一处，不必遍历全图。
/// </remarks>
public sealed record PaletteEntry
{
    /// <summary>令牌名。与 <see cref="NodeDef.StyleToken"/> 里写的是同一个值。</summary>
    public required string Name { get; init; }

    public string? Fill { get; init; }

    public string? Stroke { get; init; }

    public string? Text { get; init; }

    /// <summary>描边粗细。</summary>
    public double? Weight { get; init; }
}

/// <summary>
/// 调色板：令牌名到具体外观的映射表。
/// </summary>
/// <remarks>
/// 属于外观，因此计入视觉哈希。换调色板不需要重新布局——
/// 颜色不改变节点的尺寸，坐标仍然有效。
/// </remarks>
public sealed record Palette
{
    /// <summary>条目表。键是令牌名。</summary>
    public IReadOnlyDictionary<string, PaletteEntry> Entries { get; init; } =
        new Dictionary<string, PaletteEntry>(StringComparer.Ordinal);

    /// <summary>取一个条目。找不到时返回空，由渲染层决定用什么兜底外观。</summary>
    public PaletteEntry? Find(string? token) =>
        token is not null && Entries.TryGetValue(token, out var entry) ? entry : null;

    /// <summary>
    /// 加入或替换一个条目，返回新的调色板。
    /// </summary>
    /// <remarks>
    /// 返回新实例而不是就地改：调色板可能被多个版本共享，就地改会让"撤销之后回不到原样"。
    /// 字典每次复制一份，因为 <see cref="Entries"/> 对外是只读的，就地加键会让
    /// 已经发出去的那些引用跟着变——而那些引用本该是某一份旧版本的快照。
    /// </remarks>
    public Palette WithEntry(PaletteEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return this with
        {
            Entries = new Dictionary<string, PaletteEntry>(Entries, StringComparer.Ordinal)
            {
                [entry.Name] = entry,
            },
        };
    }

    /// <summary>去掉一个条目，返回新的调色板。条目本来就不在时返回一份内容相同的实例。</summary>
    public Palette WithoutEntry(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var entries = new Dictionary<string, PaletteEntry>(Entries, StringComparer.Ordinal);

        return entries.Remove(name)
            ? this with { Entries = entries }
            : this;
    }

    /// <summary>结构化相等。必须重写：<see cref="Entries"/> 是字典。</summary>
    public bool Equals(Palette? other) =>
        other is not null && CollectionEquality.Map(Entries, other.Entries);

    public override int GetHashCode() => CollectionEquality.MapHash(Entries);
}

/// <summary>
/// 画布设置。
/// </summary>
/// <remarks>
/// 网格与背景属于外观，纸张尺寸只在导出时用到，都不影响布局计算。
/// 因此整个子对象计入视觉哈希而不计入结构哈希。
/// </remarks>
public sealed record CanvasSettings
{
    public GridStyle Grid { get; init; } = GridStyle.None;

    public double GridSize { get; init; } = 20;

    /// <summary>纸张尺寸。导出为可打印格式时使用。</summary>
    public Size PageSize { get; init; } = new(1123, 794);

    public CanvasOrientation Orientation { get; init; } = CanvasOrientation.Landscape;

    public string? Background { get; init; }

    /// <summary>
    /// 是否无限画布。
    /// </summary>
    /// <remarks>
    /// 为真时忽略纸张尺寸，画布随内容扩展。为假时内容超出纸张会被裁切，
    /// 这个行为在导出时很关键——用户多半不希望导出结果被悄悄裁掉一块。
    /// </remarks>
    public bool Infinite { get; init; } = true;
}

/// <summary>宽高。单位与坐标一致。</summary>
public sealed record Size(double Width, double Height);
