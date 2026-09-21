using System.Text;

namespace DuetDiagram.Render;

/// <summary>
/// 一帧要画的东西，按顺序排好。
/// </summary>
/// <remarks>
/// <para>
/// **顺序就是层叠顺序**，不另设图层字段。下面的元素先画，上面的后画。
/// 多一个图层字段就多一处可以互相矛盾的真相：顺序说边在下面，图层字段说边在上面，
/// 而画出来的结果只取决于其中一处，另一处就成了骗人的。
/// </para>
/// <para>
/// **构建一次之后只读。** 要改就重建一份，不提供增量修改。
/// 增量更新的价值在于省下重建的开销，而重建一份绘制列表就是遍历一遍元素，
/// 比"判断哪几条指令需要换掉"简单得多也难错得多。要不要重建由上游按两个哈希决定。
/// </para>
/// <para>
/// 列表里没有选中与高亮。那是画布的状态而不是文档的状态，
/// 由画布在这份列表**之上**叠加。放进来会让同一份文档因为"当前选中了谁"
/// 而产生不同的列表，视觉哈希就不再能回答"要不要重绘"。
/// </para>
/// </remarks>
/// <param name="Commands">绘制指令，按层叠顺序。</param>
/// <param name="Width">内容宽度。</param>
/// <param name="Height">内容高度。</param>
/// <param name="Background">背景色，已解析。画布先铺它再依次执行指令。</param>
public sealed record DrawList(
    IReadOnlyList<DrawCommand> Commands,
    double Width,
    double Height,
    string Background)
{
    /// <summary>空列表。没有内容时用它，宽高为零、背景取白。</summary>
    public static DrawList Empty { get; } = new([], 0, 0, "#ffffff");

    /// <summary>元素标识在这一份列表里出现了几次。</summary>
    /// <remarks>差异定位用：同一条指令在不同版本里出现次数变了，说明增删了东西。</remarks>
    public int CountOf(string elementId) =>
        Commands.Count(c => string.Equals(c.ElementId, elementId, StringComparison.Ordinal));

    /// <summary>
    /// 规范文本，每行一条指令。
    /// </summary>
    /// <remarks>
    /// 快照比较的就是它。逐条一行而不是整份一个字符串，
    /// 是为了让差异能落到具体某一行——"第 17 行从 A 变成 B"比"整份不一样"有用得多，
    /// 而第 17 行带着元素标识，于是能直接说出是哪个元素变了。
    /// </remarks>
    public string ToText()
    {
        var text = new StringBuilder();

        text.Append("canvas ").Append(Numbers.Format(Width)).Append('×')
            .Append(Numbers.Format(Height)).Append(" background=").Append(Background).Append('\n');

        foreach (var command in Commands)
        {
            text.Append(command.Describe()).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// 结构化相等。
    /// </summary>
    /// <remarks>
    /// 必须重写：<see cref="Commands"/> 是集合，记录自动生成的相等性对它用引用比较。
    /// </remarks>
    public bool Equals(DrawList? other) =>
        other is not null
        && Width.Equals(other.Width)
        && Height.Equals(other.Height)
        && string.Equals(Background, other.Background, StringComparison.Ordinal)
        && Commands.Count == other.Commands.Count
        && Commands.SequenceEqual(other.Commands);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Width);
        hash.Add(Height);
        hash.Add(Background, StringComparer.Ordinal);
        hash.Add(Commands.Count);

        foreach (var command in Commands)
        {
            hash.Add(command);
        }

        return hash.ToHashCode();
    }
}
