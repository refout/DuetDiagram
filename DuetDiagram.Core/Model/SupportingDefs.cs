namespace DuetDiagram.Core.Model;

/// <summary>
/// 标签。给一组元素打上同一个标记，用于筛选、高亮与批量操作。
/// </summary>
/// <remarks>
/// 标签是**跨集合**的：成员里可以同时有节点、边、组合。
/// 它不参与布局，只影响渲染与交互，因此计入视觉哈希而不计入结构哈希。
/// </remarks>
public sealed record TagDef : IDefinition
{
    public required string Id { get; init; }

    public string Label { get; init; } = string.Empty;

    /// <summary>成员标识列表，可以跨集合。</summary>
    public IReadOnlyList<string> Members { get; init; } = [];

    /// <summary>标签色。取调色板令牌名。</summary>
    public string? Color { get; init; }

    /// <summary>结构化相等。必须重写：<see cref="Members"/> 是集合。</summary>
    public bool Equals(TagDef? other) =>
        other is not null
        && string.Equals(Id, other.Id, StringComparison.Ordinal)
        && string.Equals(Label, other.Label, StringComparison.Ordinal)
        && CollectionEquality.List(Members, other.Members)
        && string.Equals(Color, other.Color, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Id, StringComparer.Ordinal);
        hash.Add(Label, StringComparer.Ordinal);
        hash.Add(CollectionEquality.ListHash(Members));
        hash.Add(Color, StringComparer.Ordinal);

        return hash.ToHashCode();
    }
}

/// <summary>
/// 动作。图上某个元素被触发时要做的事。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Event"/> 与 <see cref="Kind"/> 用字符串而不是枚举：
/// 取值集合由宿主与外部集成决定，是可扩展的。用枚举会把我们自己的假设固化成协议，
/// 第三方想加一个事件类型就得改我们的代码。
/// </para>
/// <para>
/// 不参与布局，也不参与渲染的外观，只影响交互，因此**不进任何一个哈希**。
/// 加进来会让"改了动作"被判成"图变了"，白白触发一次重绘。
/// </para>
/// </remarks>
public sealed record ActionDef : IDefinition
{
    public required string Id { get; init; }

    /// <summary>触发事件名，例如 click、dblclick、enter。</summary>
    public string Event { get; init; } = string.Empty;

    /// <summary>动作类型名，例如 open-url、run-command。</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>动作作用的对象标识。</summary>
    public string? Target { get; init; }

    /// <summary>动作参数。键值均为字符串，具体含义由 <see cref="Kind"/> 决定。</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>结构化相等。必须重写：<see cref="Parameters"/> 是字典。</summary>
    public bool Equals(ActionDef? other) =>
        other is not null
        && string.Equals(Id, other.Id, StringComparison.Ordinal)
        && string.Equals(Event, other.Event, StringComparison.Ordinal)
        && string.Equals(Kind, other.Kind, StringComparison.Ordinal)
        && string.Equals(Target, other.Target, StringComparison.Ordinal)
        && CollectionEquality.Map(Parameters, other.Parameters);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Id, StringComparer.Ordinal);
        hash.Add(Event, StringComparer.Ordinal);
        hash.Add(Kind, StringComparer.Ordinal);
        hash.Add(Target, StringComparer.Ordinal);
        hash.Add(CollectionEquality.MapHash(Parameters));

        return hash.ToHashCode();
    }
}

/// <summary>
/// 字体。
/// </summary>
/// <remarks>
/// <see cref="Path"/> 为空表示使用系统字体，按名称查找。
/// 字体变化会改变标签的实际宽度，进而改变节点尺寸与布局结果，
/// 因此**计入结构哈希**——它不是纯外观。
/// </remarks>
public sealed record FontDef : IDefinition
{
    public required string Id { get; init; }

    /// <summary>字体名。系统字体按此名查找。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>字体文件路径。嵌入字体时使用。</summary>
    public string? Path { get; init; }

    /// <summary>是否等宽。等宽字体在文本测量上可以走更快的一致路径。</summary>
    public bool IsMono { get; init; }
}

/// <summary>
/// 文本样式预设。把一组文本样式存成具名预设，供多处引用。
/// </summary>
public sealed record TextStylePreset : IDefinition
{
    public required string Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public TextStyle Style { get; init; } = new();
}
