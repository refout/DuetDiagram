using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Templates;

/// <summary>
/// 一份模板拼进目标文档之后长什么样：标识已经改好，引用已经跟着改。
/// </summary>
/// <remarks>
/// 计划与执行分开，是为了让"能不能拼"与"拼成什么"用同一份计算。
/// 校验、算逆变更、真正落地三处各算一遍的话，只要有一处的规则与另外两处不同，
/// 就会出现"校验说可以、落地时撞标识"这类错位。
/// </remarks>
/// <param name="Nodes">改好标识的节点。</param>
/// <param name="Edges">改好标识的边。</param>
/// <param name="Composites">改好标识的组合。</param>
/// <param name="Renames">被改过名的那些标识：模板里的原名 → 拼进文档后的新名。</param>
public sealed record TemplatePlan(
    IReadOnlyList<NodeDef> Nodes,
    IReadOnlyList<EdgeDef> Edges,
    IReadOnlyList<CompositeDef> Composites,
    IReadOnlyDictionary<string, string> Renames)
{
    /// <summary>这次要落进文档的全部元素标识。</summary>
    public string[] Ids =>
    [
        .. Nodes.Select(node => node.Id),
        .. Edges.Select(edge => edge.Id),
        .. Composites.Select(composite => composite.Id),
    ];

    /// <summary>一共有多少条元素。</summary>
    public int ElementCount => Nodes.Count + Edges.Count + Composites.Count;
}

/// <summary>
/// 把一份模板装配到目标文档上：消解标识冲突、核一遍片段自己的引用、改写全部引用。
/// </summary>
/// <remarks>
/// <para>
/// **标识冲突要消解，不许直接拼。** 模板里的标识在目标文档里可能已经存在。
/// 直接拼进去会得到一份有两套同名标识的文档，而校验器报出来的位置离肇事的那一步很远——
/// 用户看到的是"某个我根本没碰过的节点标识重复"。
/// </para>
/// <para>
/// **片段自己的引用也要核。** 模板里的一条边指向片段里没有的节点时，
/// 拼进去的是一份悬空引用的文档：画布上少一条线，而校验器报的是那个不存在的标识。
/// 这一步在拼之前把话说清楚。
/// </para>
/// <para>
/// **不查目标文档里已有的内容。** 那些由整体校验器负责；
/// 这里只管"这次拼进去的东西自不自洽"。
/// </para>
/// <para>
/// **组合的成员表是权威，元素的父级只是它的冗余索引。** 于是拼进去时父级由成员表反推，
/// 而不是照抄片段里写的那一份；片段里两份对不上时**拒绝**，因为一个元素只能有一个父级，
/// 那两份里必然有一份落不了地——拼进去的那一刻整体校验会报出来，
/// 而报出来的位置离"放了哪个模板"很远。
/// </para>
/// </remarks>
public static class TemplateInstantiator
{
    /// <summary>
    /// 算一份装配计划。拼不了时给出结构化错误，不抛。
    /// </summary>
    /// <param name="target">拼进哪份文档。</param>
    /// <param name="template">拼哪份模板。</param>
    /// <param name="plan">算出来的计划。失败时为空。</param>
    /// <param name="errors">失败的原因。</param>
    public static bool TryPlan(
        DiagramDocument target,
        TemplateDocument template,
        out TemplatePlan? plan,
        out IReadOnlyList<CommandError> errors)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(template);

        plan = null;

        if (template.ElementCount == 0)
        {
            errors = [CommandError.Of(ErrorCodes.TemplateEmpty, template.Name)];
            return false;
        }

        var owners = Owners(template);
        var problems = Check(template, target, owners);

        if (problems.Count > 0)
        {
            errors = problems;
            return false;
        }

        plan = Rewrite(target, template, owners);
        errors = [];
        return true;
    }

    #region 核片段

    /// <summary>片段自己立不立得住，以及拼进去会不会把文档撑得太深。</summary>
    private static List<CommandError> Check(
        TemplateDocument template,
        DiagramDocument target,
        IReadOnlyDictionary<string, string> owners)
    {
        var problems = new List<CommandError>();
        var composites = new HashSet<string>(template.Composites.Select(c => c.Id), StringComparer.Ordinal);
        var endpoints = new HashSet<string>(composites, StringComparer.Ordinal);

        foreach (var node in template.Nodes)
        {
            endpoints.Add(node.Id);
        }

        // 标识重复：九个集合共用一个命名空间，所以是整体查一遍而不是逐集合查。
        // 查的是声明顺序上的原始标识，不是去重后的集合：先收进集合再查的话，
        // 片段里两处都叫 a 会被集合合成一处，重复就这么溜过去了。
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in Ids(template))
        {
            if (!seen.Add(id))
            {
                problems.Add(CommandError.Of(ErrorCodes.DuplicateId, id));
            }
        }

        foreach (var node in template.Nodes)
        {
            if (node.Parent is { } parent && !composites.Contains(parent))
            {
                problems.Add(CommandError.Of(ErrorCodes.ParentMissing, node.Id));
            }
        }

        foreach (var composite in template.Composites)
        {
            if (composite.Parent is { } parent && !composites.Contains(parent))
            {
                problems.Add(CommandError.Of(ErrorCodes.ParentMissing, composite.Id));
            }

            foreach (var member in composite.Members)
            {
                if (!endpoints.Contains(member))
                {
                    problems.Add(CommandError.Of(ErrorCodes.GroupMemberMissing, member));
                }
            }
        }

        // 一个标识被两个组合同时收下：成员表是权威，而一个元素只能有一个父级，
        // 两份成员表里必然有一份落不了地。拼进去的那一刻整体校验会报出来，
        // 而报出来的位置离"放了哪个模板"很远。
        var flagged = new HashSet<string>(StringComparer.Ordinal);

        foreach (var composite in template.Composites)
        {
            foreach (var member in composite.Members)
            {
                if (owners.TryGetValue(member, out var owner)
                    && !string.Equals(owner, composite.Id, StringComparison.Ordinal)
                    && flagged.Add(member))
                {
                    problems.Add(CommandError.Of(ErrorCodes.MembershipMismatch, member));
                }
            }
        }

        // 写了父级、而那个组合的成员表里没有它：两处说的不是一件事。
        // 父级不存在的那一种上面已经报过，这里只管"父级在、但没收下它"。
        foreach (var (id, parent) in Declared(template))
        {
            if (parent is null || !composites.Contains(parent))
            {
                continue;
            }

            if (!owners.TryGetValue(id, out var owner) || !string.Equals(owner, parent, StringComparison.Ordinal))
            {
                problems.Add(CommandError.Of(ErrorCodes.MembershipMismatch, id));
            }
        }

        foreach (var edge in template.Edges)
        {
            if (!endpoints.Contains(edge.From))
            {
                problems.Add(CommandError.Of(ErrorCodes.EdgeSourceMissing, edge.Id));
            }

            if (!endpoints.Contains(edge.To))
            {
                problems.Add(CommandError.Of(ErrorCodes.EdgeTargetMissing, edge.Id));
            }
        }

        // 嵌套深度要合起来看：片段自己没超，拼到一份已经很深的文档上照样会超。
        // 这一步不做的话，插入成功而文档立刻变成一份校验不过的东西。
        if (Depth(template.Composites, owners) + Depth(target.Composites) > CompositeLimits.MaxDepth)
        {
            problems.Add(CommandError.Of(ErrorCodes.CompositeTooDeep, template.Name));
        }

        return problems;
    }

    /// <summary>
    /// 每个元素归哪个组合：成员表 → 组合标识。
    /// </summary>
    /// <remarks>
    /// 成员表是权威，元素的父级只是它的冗余索引。同一个标识被两个组合收下时**先到先得**，
    /// 那一种在核片段那一步会被报出来，不会走到这里当真。
    /// </remarks>
    private static Dictionary<string, string> Owners(TemplateDocument template)
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var composite in template.Composites)
        {
            foreach (var member in composite.Members)
            {
                owners.TryAdd(member, composite.Id);
            }
        }

        return owners;
    }

    /// <summary>片段里显式写了父级的那些元素：标识 → 父级。</summary>
    private static IEnumerable<(string Id, string? Parent)> Declared(TemplateDocument template)
    {
        foreach (var node in template.Nodes)
        {
            if (node.Parent is not null)
            {
                yield return (node.Id, node.Parent);
            }
        }

        foreach (var composite in template.Composites)
        {
            if (composite.Parent is not null)
            {
                yield return (composite.Id, composite.Parent);
            }
        }
    }

    /// <summary>一组组合里最深的嵌套层数。顶层算 1，空集合算 0。</summary>
    /// <remarks>
    /// 父级从成员表反推，不看元素自己写的父级字段：片段里完全可以只写成员表，
    /// 那时按字段算会把一个两层结构当成两个顶层。
    /// </remarks>
    private static int Depth(
        IReadOnlyList<CompositeDef> composites,
        IReadOnlyDictionary<string, string>? owners = null)
    {
        var parents = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var composite in composites)
        {
            parents[composite.Id] = owners is not null && owners.TryGetValue(composite.Id, out var owner)
                ? owner
                : composite.Parent;
        }

        var deepest = 0;

        foreach (var composite in composites)
        {
            var depth = 1;
            var current = parents[composite.Id];
            var seen = new HashSet<string>(StringComparer.Ordinal) { composite.Id };

            while (current is not null && parents.ContainsKey(current) && seen.Add(current))
            {
                depth++;
                current = parents[current];
            }

            deepest = Math.Max(deepest, depth);
        }

        return deepest;
    }

    #endregion

    #region 改写

    /// <summary>把片段里的标识换成目标文档里没被占用的，并把引用一起换掉。</summary>
    private static TemplatePlan Rewrite(
        DiagramDocument target,
        TemplateDocument template,
        IReadOnlyDictionary<string, string> owners)
    {
        var renames = Renames(target, template);

        return new TemplatePlan(
            [.. template.Nodes.Select(node => node with
            {
                Id = Renamed(node.Id, renames),
                Parent = Owner(node.Id, owners, renames),

                // 模板不带页面归属：放进哪一页由目标文档决定。带着来源文档的页号的话，
                // 放进第二页的东西会被模板带回第一页。
                Page = null,
            })],
            [.. template.Edges.Select(edge => edge with
            {
                Id = Renamed(edge.Id, renames),
                From = Renamed(edge.From, renames),
                To = Renamed(edge.To, renames),
                Page = null,
            })],
            [.. template.Composites.Select(composite => composite with
            {
                Id = Renamed(composite.Id, renames),
                Parent = Owner(composite.Id, owners, renames),
                Members = [.. composite.Members.Select(member => Renamed(member, renames))],
            })],
            renames);
    }

    /// <summary>元素在拼进去之后归哪个组合。成员表里没有它就是顶层。</summary>
    private static string? Owner(
        string id,
        IReadOnlyDictionary<string, string> owners,
        IReadOnlyDictionary<string, string> renames) =>
        owners.TryGetValue(id, out var composite) ? Renamed(composite, renames) : null;

    /// <summary>
    /// 每个标识在目标文档里的新名字。
    /// </summary>
    /// <remarks>
    /// 冲突时在后头接一个序号，一直试到没人用为止。**序号从 2 起**，
    /// 因为原名占的就是"第一个"；从 1 起会得到一个 <c>a-1</c>，
    /// 而用户看到的"为什么不是 a"没有答案。
    /// <para>
    /// 试的时候要把**这次计划里已经分配出去的**也算上：片段里同时有 <c>a</c> 与 <c>a-2</c>
    /// 而目标文档里已有 <c>a</c> 时，只查目标文档会让两个标识都变成 <c>a-2</c>。
    /// </para>
    /// </remarks>
    private static Dictionary<string, string> Renames(DiagramDocument target, TemplateDocument template)
    {
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in Ids(template))
        {
            var candidate = id;
            var suffix = 2;

            while (target.IsIdTaken(candidate) || !taken.Add(candidate))
            {
                candidate = $"{id}-{suffix++}";
            }

            renames[id] = candidate;
        }

        return renames;
    }

    /// <summary>片段里的全部标识，顺序固定：节点、边、组合，各按声明顺序。</summary>
    private static IEnumerable<string> Ids(TemplateDocument template)
    {
        foreach (var node in template.Nodes)
        {
            yield return node.Id;
        }

        foreach (var edge in template.Edges)
        {
            yield return edge.Id;
        }

        foreach (var composite in template.Composites)
        {
            yield return composite.Id;
        }
    }

    /// <summary>换一个标识。片段里出现过的标识一定在表里。</summary>
    private static string Renamed(string id, IReadOnlyDictionary<string, string> renames) =>
        renames.TryGetValue(id, out var renamed) ? renamed : id;

    #endregion
}
