using System.Collections.ObjectModel;

namespace DuetDiagram.Core.Model;

/// <summary>
/// 定义的集合。对外只读，写入通道只留给同程序集里的命令实现。
/// </summary>
/// <remarks>
/// <para>
/// 这是"集合对外只读、修改只能通过命令"这条约束的唯一落地点。
/// 九个集合全部用它，约束就是结构性的而不是靠九个地方各写对一次。
/// </para>
/// <para>
/// 顺序有意义，因此内部用 <see cref="List{T}"/> 而不是字典。节点在集合中的位置是层内次序的依据，
/// 边在集合中的位置决定了删除后撤销时能否按原索引插回原来的位置。用字典会丢掉这个顺序，
/// 而顺序一旦丢失，"撤销后文档与操作前逐字节一致"这条硬约束就不可能满足。
/// </para>
/// </remarks>
internal sealed class DefinitionCollection<T> where T : IDefinition
{
    private readonly List<T> _items = [];
    private ReadOnlyCollection<T>? _readOnly;

    /// <summary>
    /// 对外暴露的只读视图。外部程序集拿不到写入通道。
    /// </summary>
    /// <remarks>
    /// 包一层真正的只读集合，而不是直接把内部列表当接口返回。
    /// 后者只是编译期契约：运行时那个对象仍然是可变列表，强制转换就能绕过去。
    /// 这条约束是本项目的硬约定之一，值得在运行时也守住，代价只是一个包装对象。
    /// 包装对象缓存起来，重复读取不会反复分配。
    /// </remarks>
    public IReadOnlyList<T> ReadOnly => _readOnly ??= new ReadOnlyCollection<T>(_items);

    /// <summary>仅命令实现可用的可变视图。命令在同一程序集内，所以用 internal。</summary>
    public List<T> Mutable => _items;

    public bool Has(string id) => IndexOf(id) >= 0;

    public T? Find(string id)
    {
        var index = IndexOf(id);

        return index < 0 ? default : _items[index];
    }

    public int IndexOf(string id)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (string.Equals(_items[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>判断某个标识是否与除自身之外的条目重复。改名时用它做冲突检查。</summary>
    public bool HasOther(string id, T self) =>
        _items.Any(item => !ReferenceEquals(item, self) && string.Equals(item.Id, id, StringComparison.Ordinal));

    /// <summary>反序列化用：整体替换内容。<paramref name="source"/> 为空时视为空集合。</summary>
    public void Replace(IReadOnlyList<T>? source)
    {
        _items.Clear();

        if (source is not null)
        {
            _items.AddRange(source);
        }
    }
}
