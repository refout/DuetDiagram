namespace DuetDiagram.Core.Model;

/// <summary>
/// 集合成员的结构化比较。
/// </summary>
/// <remarks>
/// <para>
/// 记录类型自动生成的相等性对集合成员用**引用比较**。这一点的后果比看起来严重：
/// 两个字段完全相同、只是集合不是同一个实例的记录会被判为不等。
/// 它不会报错，只会让"这个对象变了吗""这两份内容一样吗"这类判断悄悄给出错误答案。
/// </para>
/// <para>
/// 反序列化是这个问题最容易暴露的地方：反序列化出来的空集合与原来的空集合不是同一个实例，
/// 于是"往返无损"判定失败。但真正的危害在于其它场合——差异计算、去重、memento 比对，
/// 那些地方没有测试盯着。
/// </para>
/// <para>
/// 只要记录的成员里有集合，就必须用这里的比较重写 <c>Equals</c> 与 <c>GetHashCode</c>。
/// </para>
/// </remarks>
internal static class CollectionEquality
{
    /// <summary>按顺序逐项比较。顺序有意义，因此不做排序或集合化处理。</summary>
    public static bool List<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static int ListHash<T>(IReadOnlyList<T>? source)
    {
        if (source is null)
        {
            return 0;
        }

        var hash = new HashCode();

        foreach (var item in source)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    /// <summary>按键比较，与插入顺序无关。</summary>
    public static bool Map<TValue>(
        IReadOnlyDictionary<string, TValue>? left,
        IReadOnlyDictionary<string, TValue>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) || !EqualityComparer<TValue>.Default.Equals(value, other))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 字典的哈希。
    /// </summary>
    /// <remarks>
    /// 先对每一项求哈希再异或，保证内容相同但插入顺序不同的两份字典得到同一个值。
    /// 逐项累加会把顺序带进结果里，而字典的顺序本来就不该影响相等性判断。
    /// </remarks>
    public static int MapHash<TValue>(IReadOnlyDictionary<string, TValue>? source)
    {
        if (source is null)
        {
            return 0;
        }

        var combined = 0;

        foreach (var (key, value) in source)
        {
            combined ^= HashCode.Combine(key, value);
        }

        return combined;
    }

    /// <summary>
    /// 值是列表的字典的比较。
    /// </summary>
    /// <remarks>
    /// 普通字典比较会把列表值按引用比，于是内容相同的两份映射被判为不等。
    /// 这个形状在 Sidecar 里出现两次（固定折线、自定义端口），单列一个方法比每处各写一遍稳。
    /// </remarks>
    public static bool MapOfLists<T>(
        IReadOnlyDictionary<string, IReadOnlyList<T>>? left,
        IReadOnlyDictionary<string, IReadOnlyList<T>>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) || !List(value, other))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc cref="MapOfLists{T}"/>
    public static int MapOfListsHash<T>(IReadOnlyDictionary<string, IReadOnlyList<T>>? source)
    {
        if (source is null)
        {
            return 0;
        }

        var combined = 0;

        foreach (var (key, value) in source)
        {
            combined ^= HashCode.Combine(key, ListHash(value));
        }

        return combined;
    }
}
