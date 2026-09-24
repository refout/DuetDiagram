using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;

namespace DuetDiagram.Core.Templates;

/// <summary>
/// 模板文件读不出来：它带了模板表达不了的东西。
/// </summary>
/// <remarks>
/// <para>
/// 与"JSON 本身坏了"分开：那一种由反序列化器抛 <c>JsonException</c>，
/// 而这一种是**文件合法、内容超纲**——例如模板里带了图层或标签。
/// 两者的处置不同：前者要人去修文件，后者要么改模板，要么把那些集合也支持起来。
/// </para>
/// <para>
/// 做成异常而不是返回值，是因为它跨了"读文件"与"装配模板"两层：
/// 目录发现那边只需要一句"这个文件读不了"加原因，中间那层不必拆包重装。
/// </para>
/// </remarks>
public sealed class TemplateFormatException : Exception
{
    /// <summary>建一条模板格式错误。</summary>
    /// <param name="unsupported">模板里带了、而模板表达不了的那些集合。</param>
    public TemplateFormatException(IReadOnlyList<string> unsupported)
        : base($"模板只带节点、边与组合，这份文件里还有：{string.Join("、", unsupported)}。")
    {
        Unsupported = unsupported;
    }

    private TemplateFormatException(string message)
        : base(message)
    {
    }

    /// <summary>模板里一条元素都没有。</summary>
    /// <remarks>
    /// 单独一条消息而不是并进"带了别的集合"：那一种是文件超纲，这一种是文件空着，
    /// 两种要说的话不一样，而"模板只带节点、边与组合，还有：（一条元素都没有）"
    /// 读起来像是文件里真有这么个集合。
    /// </remarks>
    public static TemplateFormatException Empty() => new("这份模板里一条元素都没有。");

    /// <summary>模板表达不了的那些集合的名字。空表示不是这个原因。</summary>
    public IReadOnlyList<string> Unsupported { get; } = [];
}

/// <summary>
/// 一份可复用的文档片段：一组节点、边与组合，连同它们的样式。
/// </summary>
/// <remarks>
/// <para>
/// **它是文档片段，不是第二套 IR。** 文件就是一份少了几个集合的文档，
/// 读写都走 <see cref="DiagramSerializer"/>——另造一套格式的话，
/// IR 每加一个字段就有两个地方要改，而漏改的那一处只在读到新文件时才暴露。
/// </para>
/// <para>
/// **只带节点、边与组合。** 页面、图层、标签、动作、字体、文本预设、调色板与布局提示
/// 都是文档级的东西：拼进另一份文档时要么没有落点（标签、动作），
/// 要么需要一套合并规则（图层、调色板），要么本来就该跟着目标文档走（页面、画布）。
/// 带了这些的文件会被**拒绝**而不是静默丢掉——静默丢掉的表现是
/// "模板里的颜色不见了"，而用户无从知道是模板没写对还是程序没做。
/// </para>
/// <para>
/// 名字取自文件里的文档标识。模板不需要另外一套命名，
/// 多一个字段就多一处可能与文件名对不上的地方。
/// </para>
/// </remarks>
/// <param name="Name">模板名。取自文件里的文档标识。</param>
/// <param name="Nodes">片段里的节点。</param>
/// <param name="Edges">片段里的边。</param>
/// <param name="Composites">片段里的组合。</param>
public sealed record TemplateDocument(
    string Name,
    IReadOnlyList<NodeDef> Nodes,
    IReadOnlyList<EdgeDef> Edges,
    IReadOnlyList<CompositeDef> Composites)
{
    /// <summary>模板里一共有多少条元素。</summary>
    public int ElementCount => Nodes.Count + Edges.Count + Composites.Count;

    /// <summary>
    /// 从一段 JSON 读一份模板。
    /// </summary>
    /// <remarks>
    /// 空内容按"一条元素都没有"处理，不按参数错误。目录里一个零字节的文件是手滑留下的，
    /// 而它要说的话与一份写成 <c>{}</c> 的模板是同一句；报成参数错误的话，
    /// 用户拿到的是一句框架自己的话，与模板格式无关。
    /// </remarks>
    /// <exception cref="TemplateFormatException">文件里带了模板表达不了的东西，或者一条元素都没有。</exception>
    /// <exception cref="System.Text.Json.JsonException">JSON 本身读不出来。</exception>
    public static TemplateDocument Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        if (string.IsNullOrWhiteSpace(json))
        {
            throw TemplateFormatException.Empty();
        }

        var document = DiagramSerializer.DeserializeFull(json);
        var unsupported = Unsupported(document);

        if (unsupported.Count > 0)
        {
            throw new TemplateFormatException(unsupported);
        }

        var template = new TemplateDocument(document.Id, document.Nodes, document.Edges, document.Composites);

        // 空模板在读文件这一步就挡住。放进画布时才发现"什么都没发生"，
        // 而用户以为是自己点错了位置。
        if (template.ElementCount == 0)
        {
            throw TemplateFormatException.Empty();
        }

        return template;
    }

    /// <summary>
    /// 把一份模板写成 JSON。
    /// </summary>
    /// <remarks>
    /// 写出来的就是一份普通文档，只是集合少几个——与 <see cref="Load"/> 是同一个往返。
    /// </remarks>
    public static string Save(TemplateDocument template)
    {
        ArgumentNullException.ThrowIfNull(template);

        return DiagramSerializer.SerializeFull(
            DiagramDocument.CreateFromContent(
                template.Name,
                nodes: template.Nodes,
                edges: template.Edges,
                composites: template.Composites));
    }

    /// <summary>
    /// 从一份文档里截出一段做成模板。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **组合要连着成员与父级一起带上。** 只带被选中的那些组合的话，
    /// 成员表或父级字段会指向片段里不存在的元素，于是这份模板自己就不合法——
    /// 存的时候看着没问题，用的时候才报错。
    /// </para>
    /// <para>
    /// **两端都在片段里的边一并带上。** 用户圈了两个节点，要的是
    /// "这两个节点以及它们之间的那条线"，而线通常不在框选范围里（框选选的是节点）。
    /// </para>
    /// <para>
    /// **页面归属在这里就去掉。** 模板放进哪一页由目标文档决定，
    /// 记着来源文档的页号的话，放进第二页的东西会被模板带回第一页。
    /// 实例化那一步还会再清一次，因为手写的模板文件里也可能带着它。
    /// </para>
    /// </remarks>
    /// <param name="name">模板名。</param>
    /// <param name="source">从哪份文档里截。</param>
    /// <param name="ids">选中了哪些元素。</param>
    /// <exception cref="ArgumentException">一个元素都没选中。</exception>
    public static TemplateDocument FromDocument(
        string name,
        DiagramDocument source,
        IReadOnlyCollection<string> ids)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(ids);

        var composites = WithMembers(source, ids);
        var elements = new HashSet<string>(ids, StringComparer.Ordinal);

        // 组合的成员也要算进来：成员表里的标识不带类型，它可能指节点也可能指子组合，
        // 两种都收下才不会让片段里的成员表指向空处。
        foreach (var composite in composites)
        {
            elements.Add(composite.Id);

            foreach (var member in composite.Members)
            {
                elements.Add(member);
            }
        }

        var nodes = source.Nodes
            .Where(node => elements.Contains(node.Id))
            .Select(node => node with { Page = null })
            .ToArray();

        var edges = source.Edges
            .Where(edge => elements.Contains(edge.From) && elements.Contains(edge.To))
            .Select(edge => edge with { Page = null })
            .ToArray();

        if (nodes.Length + edges.Length + composites.Count == 0)
        {
            throw new ArgumentException("一个元素都没选中，做不出模板。", nameof(ids));
        }

        return new TemplateDocument(name, nodes, edges, composites);
    }

    /// <summary>
    /// 被选中的组合，连同它们的成员与父级（都递归）。
    /// </summary>
    /// <remarks>
    /// 顺序照文档里的原序，不照递归的先后：泳道成员的先后就是条带顺序，
    /// 而组合集合本身的顺序也有语义，换成递归序会让片段里的次序与来源文档不一致。
    /// </remarks>
    private static IReadOnlyList<CompositeDef> WithMembers(DiagramDocument source, IReadOnlyCollection<string> ids)
    {
        var byId = source.Composites.ToDictionary(composite => composite.Id, StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.Ordinal);

        void Take(string id)
        {
            if (!byId.TryGetValue(id, out var composite) || !taken.Add(id))
            {
                return;
            }

            // 父级链也要走一遍：选了一个内层组合而没选外层时，
            // 只带内层会让片段里的父级指向不存在的东西。
            if (composite.Parent is { } parent)
            {
                Take(parent);
            }

            foreach (var member in composite.Members)
            {
                Take(member);
            }
        }

        foreach (var id in ids)
        {
            Take(id);
        }

        return [.. source.Composites.Where(composite => taken.Contains(composite.Id))];
    }

    /// <summary>文件里那些模板表达不了的集合，按名字列出来。</summary>
    /// <remarks>
    /// 画布设置与图类型、方向、版本、哈希都不算：它们是文档级的呈现设置，
    /// 片段里带着也没有落点，而且不指向任何元素，丢掉不会让谁指空。
    /// 其余几样都会（调色板被样式令牌引用、布局提示引用节点标识），所以它们在拒绝之列。
    /// </remarks>
    private static IReadOnlyList<string> Unsupported(DiagramDocument document)
    {
        var found = new List<string>();

        if (document.Pages.Count > 0)
        {
            found.Add("页面");
        }

        if (document.Layers.Count > 0)
        {
            found.Add("图层");
        }

        if (document.Tags.Count > 0)
        {
            found.Add("标签");
        }

        if (document.Actions.Count > 0)
        {
            found.Add("动作");
        }

        if (document.Fonts.Count > 0)
        {
            found.Add("字体");
        }

        if (document.TextPresets.Count > 0)
        {
            found.Add("文本预设");
        }

        if (document.Palette.Entries.Count > 0)
        {
            found.Add("调色板");
        }

        if (HasLayoutHints(document.Layout))
        {
            found.Add("布局提示");
        }

        return found;
    }

    /// <summary>
    /// 这份布局提示里有没有真的写了东西。
    /// </summary>
    /// <remarks>
    /// 逐项比语义，不拿整份记录做相等比较：约束列表是集合类型，记录合成的相等比较按引用比它们，
    /// 于是"从文件里读出来的一组空列表"与"新建时那一组空列表"会判成不相等。
    /// 那样判的话，一份显式写着空约束块、或者本程序自己写出去又读回来的模板，
    /// 都会被当成"带了布局提示"而拒掉。
    /// </remarks>
    private static bool HasLayoutHints(LayoutHints layout)
    {
        var defaults = new LayoutHints();

        return layout.NodeSpacing != defaults.NodeSpacing
            || layout.LayerSpacing != defaults.LayerSpacing
            || layout.SameRank.Count > 0
            || layout.Order.Count > 0
            || layout.Align.Count > 0
            || layout.Place.Count > 0;
    }
}
