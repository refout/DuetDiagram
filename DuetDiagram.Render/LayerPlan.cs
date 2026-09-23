using DuetDiagram.Core.Model;

namespace DuetDiagram.Render;

/// <summary>
/// 图层对画面的影响，一次算好：谁画在谁上面、谁不画、谁画了但点不中。
/// </summary>
/// <remarks>
/// <para>
/// **算一次、查多次。** 绘制一份列表要按图层分组遍历全部元素，逐个元素去文档里
/// 找它那一层的话，每次查找都要扫一遍图层集合。这里在构建之前把三件事定下来：
/// 画的前后、哪些元素不画、哪些元素画了但不可命中。
/// </para>
/// <para>
/// **缺省层在最底下，而且位置固定。** 没有归属的元素（以及全部连线与组合）都归它。
/// 让它随图层增减浮动的话，每加一个图层都会有一批元素莫名其妙地换前后——
/// 而"我加了一层"与"原来那批东西跑到后面去了"在用户看来是两件不相干的事。
/// </para>
/// <para>
/// **次序用档位而不是图层自己的次序值。** 文档里两个图层次序相同是可能的
/// （从文件读进来、或由别的工具写出来的），那时"谁在上面"会变成由集合位置决定的
/// 偶然结果。这里排一次序、编成 0、1、2……，同一条命令在同一份文档上永远得到同一个结果。
/// 并列用标识断掉，与 <c>reorder-layer</c> 的口径一致。
/// </para>
/// <para>
/// **指向不存在的图层按缺省层处理，不报错。** 校验器现在不查这条引用，
/// 而渲染的立场与它处理组合成环时一样：合法的文档里不会有，渲染只需要别崩。
/// </para>
/// </remarks>
public sealed class LayerPlan
{
    private readonly Dictionary<string, LayerDef> _layers;
    private readonly Dictionary<string, int> _ranks;

    private LayerPlan(
        Dictionary<string, LayerDef> layers,
        Dictionary<string, int> ranks,
        IReadOnlySet<string> hiddenNodes,
        IReadOnlySet<string> blockedNodes)
    {
        _layers = layers;
        _ranks = ranks;
        PaintOrder = [.. ranks.OrderBy(pair => pair.Value).Select(pair => pair.Key == Default ? null : pair.Key)];
        HiddenNodes = hiddenNodes;
        BlockedNodes = blockedNodes;
    }

    /// <summary>
    /// 缺省层在档位表里的键。
    /// </summary>
    /// <remarks>
    /// 用一个不可能与图层标识撞上的串。空串不行——图层标识是外部给的，
    /// 而"空串"这种值恰恰是最容易被别的工具写进文件里的那种。
    /// </remarks>
    private const string Default = "\u0000default";

    /// <summary>
    /// 出笔的次序，从下往上。空元素表示缺省层。
    /// </summary>
    /// <remarks>
    /// 它总是以缺省层开头，即使一个元素都没有归它——调用方按它逐档遍历，
    /// 不必再为"有没有缺省层"分一次支。
    /// </remarks>
    public IReadOnlyList<string?> PaintOrder { get; }

    /// <summary>在不画的图层上的节点。它们的指令一条都不出。</summary>
    public IReadOnlySet<string> HiddenNodes { get; }

    /// <summary>
    /// 画出来但点不中的节点。不画的与锁定的都在里面。
    /// </summary>
    /// <remarks>
    /// 这份集合挂在绘制列表上，由命中测试读。让命中测试自己去读文档的话，
    /// "画的是什么"与"点得中什么"就成了两处各自算的判断，而它们迟早会不一致——
    /// 表现是"点得中一个看不见的东西"。不可见的那些本来就没有指令，
    /// 把它们也放进来是为了让这一份集合**单独就能回答"谁能被点中"**，
    /// 读的人不必再知道"不可见的没指令"这件事。
    /// </remarks>
    public IReadOnlySet<string> BlockedNodes { get; }

    /// <summary>按一份文档算出图层对画面的影响。</summary>
    public static LayerPlan Of(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var layers = new Dictionary<string, LayerDef>(StringComparer.Ordinal);

        foreach (var layer in document.Layers)
        {
            layers[layer.Id] = layer;
        }

        // 档位：缺省层零，已声明的图层按次序值升序排 1、2、3……
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal) { [Default] = 0 };

        foreach (var layer in document.Layers
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!ranks.ContainsKey(layer.Id))
            {
                ranks[layer.Id] = ranks.Count;
            }
        }

        var hidden = new HashSet<string>(StringComparer.Ordinal);
        var blocked = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in document.Nodes)
        {
            var layer = Find(layers, node.Layer);

            if (layer is { Visible: false })
            {
                hidden.Add(node.Id);
                blocked.Add(node.Id);
                continue;
            }

            if (layer is { Locked: true })
            {
                blocked.Add(node.Id);
            }
        }

        return new LayerPlan(layers, ranks, hidden, blocked);
    }

    /// <summary>
    /// 一个节点实际归在哪一层。空表示缺省层。
    /// </summary>
    /// <remarks>没有归属、或者归属指向一个不存在的图层时，都归缺省层。</remarks>
    public string? EffectiveLayerId(string? declared) =>
        declared is not null && _layers.ContainsKey(declared) ? declared : null;

    /// <summary>这个节点归在哪一层。空表示缺省层。</summary>
    public string? EffectiveLayerId(NodeDef node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return EffectiveLayerId(node.Layer);
    }

    /// <summary>这一层在不在最上面。空表示缺省层，它最低。</summary>
    public int RankOf(string? layerId) =>
        layerId is not null && _ranks.TryGetValue(layerId, out var rank) ? rank : 0;

    /// <summary>这一层画不画。图层不存在时按画处理。</summary>
    public bool IsVisible(string? layerId) => Find(_layers, layerId)?.Visible ?? true;

    /// <summary>这一层能不能改。图层不存在时按能改处理。</summary>
    public bool IsLocked(string? layerId) => Find(_layers, layerId)?.Locked ?? false;

    /// <summary>这个节点画不画。</summary>
    public bool IsVisible(NodeDef node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return !HiddenNodes.Contains(node.Id);
    }

    /// <summary>这个节点能不能改。</summary>
    public bool IsLocked(NodeDef node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return IsLocked(EffectiveLayerId(node));
    }

    /// <summary>这一层上有没有锁着的元素。用来决定要不要拒绝一次改动。</summary>
    public bool IsLocked(IEnumerable<NodeDef> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        return nodes.Any(IsLocked);
    }

    private static LayerDef? Find(IReadOnlyDictionary<string, LayerDef> layers, string? layerId) =>
        layerId is not null && layers.TryGetValue(layerId, out var layer) ? layer : null;
}
