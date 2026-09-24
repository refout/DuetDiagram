using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Shapes;

/// <summary>
/// 形状表：按枚举值查几何。
/// </summary>
/// <remarks>
/// <para>
/// **它是"加一个形状只改一处"里的那一处。** 渲染层不再按形状枚举写分支，
/// 而是从这里取几何；工具与属性面板的取值表也从这里取名字。
/// </para>
/// <para>
/// **构造时就把两件事校验掉**：同一个枚举值被注册两次，以及枚举里有值没被注册。
/// 放到构造时而不是留到查询时，是因为这两种错都是"静默少一个形状"：
/// 前者的表现是后注册的把先注册的顶掉，后者的表现是那个形状画不出来或者干脆崩在渲染里，
/// 而两种都要等到有人真的用到那个形状才会发现。
/// </para>
/// <para>
/// 校验用的是枚举自身的取值集合，所以"注册表覆盖了枚举"这件事不是靠一条测试盯着，
/// 而是构造时就成立的。测试只需钉住"八个内置形状的几何是什么"。
/// </para>
/// </remarks>
public sealed class ShapeRegistry
{
    private readonly Dictionary<NodeShape, ShapeDefinition> _byShape;

    /// <summary>用若干个提供者建一份形状表。</summary>
    /// <param name="providers">形状提供者，按顺序合并。</param>
    /// <exception cref="InvalidOperationException">同一个形状注册了两次，或有枚举值没人提供。</exception>
    public ShapeRegistry(params IShapeProvider[] providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _byShape = [];
        var all = new List<ShapeDefinition>();

        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);

            foreach (var definition in provider.Shapes)
            {
                if (!_byShape.TryAdd(definition.Shape, definition))
                {
                    throw new InvalidOperationException(
                        $"形状 {definition.Name} 被注册了两次。同一份表里一个形状只能有一份几何。");
                }

                all.Add(definition);
            }
        }

        var missing = Enum.GetValues<NodeShape>().Where(shape => !_byShape.ContainsKey(shape)).ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"这些形状没有人提供：{string.Join(" / ", missing)}。"
                + "枚举里的形状必须有几何，否则画到它的时候无从下手。");
        }

        All = all;
    }

    /// <summary>
    /// 内置那一份。
    /// </summary>
    /// <remarks>
    /// 只有一份：形状表是无状态的纯数据，每个窗口各建一份没有意义。
    /// </remarks>
    public static ShapeRegistry Default { get; } = new(BuiltinShapeProvider.Instance);

    /// <summary>全部形状，按提供者给出的顺序。</summary>
    public IReadOnlyList<ShapeDefinition> All { get; }

    /// <summary>按枚举值取定义。</summary>
    /// <exception cref="KeyNotFoundException">这个形状没有定义。构造时已挡住，走到这里说明表被绕开了。</exception>
    public ShapeDefinition Find(NodeShape shape) =>
        _byShape.TryGetValue(shape, out var definition)
            ? definition
            : throw new KeyNotFoundException($"形状表里没有 {shape} 的几何。");

    /// <summary>按枚举值取定义，取不到返回假。</summary>
    public bool TryFind(NodeShape shape, out ShapeDefinition definition) =>
        _byShape.TryGetValue(shape, out definition!);
}
