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
    private static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.Ordinal);

    private int _elementCount = -1;

    /// <summary>
    /// 画出来了但点不中的元素标识。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 锁定的图层走这里。**它挂在绘制列表上，而不是让命中测试自己去读文档**：
    /// 命中测试只认几何，读文档就意味着"画的是什么"与"点得中什么"成了两处各自算的
    /// 判断，而它们迟早会不一致——表现是"点得中一个看不见的东西"。
    /// 挂在这里，两者是同一刻、同一处算出来的。
    /// </para>
    /// <para>
    /// 不可见的元素本来就没有指令，所以它们进不进这一份都不影响结果。
    /// 由构建方决定放不放；放进去的好处是这一份单独就能回答"谁能被点中"。
    /// </para>
    /// </remarks>
    public IReadOnlySet<string> Blocked { get; init; } = None;

    /// <summary>空列表。没有内容时用它，宽高为零、背景取白。</summary>
    public static DrawList Empty { get; } = new([], 0, 0, "#ffffff");

    /// <summary>
    /// 列表里出现了多少个不同的元素。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 剔除的阈值判的是它而不是指令条数：一个节点对应好几条指令，
    /// 按指令数判会让"多少个节点才算大图"这件事随标签行数漂移。
    /// </para>
    /// <para>
    /// 算一次要遍历整份列表，而每帧都要问一次，所以结果缓存下来。
    /// 列表是只读的，缓存不会过期。
    /// </para>
    /// </remarks>
    public int ElementCount => _elementCount >= 0 ? _elementCount : _elementCount = CountElements();

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
    /// 必须重写：<see cref="Commands"/> 与 <see cref="Blocked"/> 都是集合，
    /// 记录自动生成的相等性对它们用引用比较。
    /// <see cref="Blocked"/> 也要算进来：把一层锁上只改这一份、不改任何指令，
    /// 不算的话宿主会认为绘制列表没变，于是不通知界面重画——而画布上
    /// "这一片点不动了"这件事正是刚发生的变化。
    /// </remarks>
    public bool Equals(DrawList? other) =>
        other is not null
        && Width.Equals(other.Width)
        && Height.Equals(other.Height)
        && string.Equals(Background, other.Background, StringComparison.Ordinal)
        && Commands.Count == other.Commands.Count
        && Commands.SequenceEqual(other.Commands)
        && Blocked.Count == other.Blocked.Count
        && Blocked.SetEquals(other.Blocked);

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

        // 集合作和：与集合内部的次序无关，而相等性本来就不看次序。
        var blocked = 0;

        foreach (var id in Blocked)
        {
            blocked ^= StringComparer.Ordinal.GetHashCode(id);
        }

        hash.Add(blocked);

        return hash.ToHashCode();
    }

    private int CountElements()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var command in Commands)
        {
            seen.Add(command.ElementId);
        }

        return seen.Count;
    }
}
