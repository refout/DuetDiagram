using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands;

/// <summary>
/// 组合成员关系的读写：把成员挂到容器上、从容器上摘下来、改父级、算深度。
/// </summary>
/// <remarks>
/// <para>
/// 成员关系有**两处表达**：<see cref="CompositeDef.Members"/> 与成员的父级字段
/// （节点的 <see cref="NodeDef.Parent"/>、组合的 <see cref="CompositeDef.Parent"/>）。
/// 约定以成员列表为准，父级字段是便于查询的冗余，两者必须一致——不一致的文档会被
/// 整体校验器报成"成员列表与父级互相矛盾"。
/// </para>
/// <para>
/// 因此增删成员必须**同时改两处**。把这两步收在这里而不是让每条命令各写一遍，
/// 是因为漏改一处的后果是一样的：文档能存下去、界面上看不出异常，
/// 只有校验器会报，而报出来的位置离肇事的那条命令很远。
/// </para>
/// <para>
/// 组合是不可变记录，所以"改一个组合"其实是"用一份新的换掉它"。
/// 这里统一用标识定位，调用方不必关心它在集合里的第几位。
/// </para>
/// </remarks>
internal static class CompositeMembership
{
    /// <summary>读一个成员的父级。既不是节点也不是组合时返回空。</summary>
    public static string? ParentOf(DiagramDocument document, string id)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.FindNode(id) is { } node)
        {
            return node.Parent;
        }

        return document.FindComposite(id) is { } composite ? composite.Parent : null;
    }

    /// <summary>
    /// 把一个成员的父级字段设成给定值。既不是节点也不是组合时什么都不做。
    /// </summary>
    /// <remarks>
    /// 找不到目标就静默返回，而不是抛异常：撤销与重做共用这一条路径，
    /// 而重做时那个成员可能已经因为别的原因不在了。整段逻辑要能重复执行而不出错。
    /// </remarks>
    public static void SetParent(DiagramDocument document, string id, string? parent)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.FindNode(id) is { } node && !string.Equals(node.Parent, parent, StringComparison.Ordinal))
        {
            document.MutableNodes[document.IndexOfNode(id)] = node with { Parent = parent };
            return;
        }

        if (document.FindComposite(id) is { } composite
            && !string.Equals(composite.Parent, parent, StringComparison.Ordinal))
        {
            Replace(document, composite with { Parent = parent });
        }
    }

    /// <summary>把一个成员从容器的成员列表里摘掉。它本来就不在那个容器里时什么都不做。</summary>
    public static void Detach(DiagramDocument document, string containerId, string memberId)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.FindComposite(containerId) is not { } container
            || !container.Members.Contains(memberId, StringComparer.Ordinal))
        {
            return;
        }

        Replace(document, container with
        {
            Members = [.. container.Members.Where(m => !string.Equals(m, memberId, StringComparison.Ordinal))],
        });
    }

    /// <summary>
    /// 把一个成员挂到容器的成员列表上。
    /// </summary>
    /// <param name="index">
    /// 插到成员列表的第几位。传空追加到末尾。越界一律夹紧而不是报错——
    /// 索引可能来自一个在请求发出之后就已经过期的界面状态。
    /// </param>
    public static void Attach(DiagramDocument document, string containerId, string memberId, int? index = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.FindComposite(containerId) is not { } container
            || container.Members.Contains(memberId, StringComparer.Ordinal))
        {
            return;
        }

        var members = new List<string>(container.Members);
        members.Insert(Math.Clamp(index ?? members.Count, 0, members.Count), memberId);

        Replace(document, container with { Members = members });
    }

    /// <summary>
    /// 把一个容器里的某个成员换成一批成员，落在它原来占的那一位上。
    /// </summary>
    /// <remarks>
    /// 解散组合用它：拆掉的那个组合在外层成员列表里占着一位，它里面的成员应当接过这一位。
    /// 一律追加到末尾的话，泳道里的条带次序会跟着变，而这件事在界面上看不出异常。
    /// </remarks>
    public static void ReleaseInto(DiagramDocument document, string containerId, CompositeDef released)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(released);

        if (document.FindComposite(containerId) is not { } container)
        {
            return;
        }

        var members = new List<string>(container.Members.Count + released.Members.Count);
        var spliced = false;

        foreach (var member in container.Members)
        {
            if (string.Equals(member, released.Id, StringComparison.Ordinal))
            {
                spliced = true;

                foreach (var freed in released.Members)
                {
                    if (!members.Contains(freed, StringComparer.Ordinal))
                    {
                        members.Add(freed);
                    }
                }

                continue;
            }

            // 被解散的组合本来就不在这个容器里时，放出来的成员追加到末尾。
            members.Add(member);
        }

        if (!spliced)
        {
            foreach (var freed in released.Members)
            {
                if (!members.Contains(freed, StringComparer.Ordinal))
                {
                    members.Add(freed);
                }
            }
        }

        Replace(document, container with { Members = members });
    }

    /// <summary>
    /// 一个组合的嵌套深度。顶层组合是 1，空标识是 0。
    /// </summary>
    /// <remarks>
    /// 向上走的时候带步数上限：成环的文档已经由整体校验器报过，
    /// 这里再陷进去的话，一次校验或一次命令就会变成死循环。
    /// </remarks>
    public static int DepthOf(DiagramDocument document, string? compositeId)
    {
        ArgumentNullException.ThrowIfNull(document);

        var depth = 0;
        var current = compositeId;
        var guard = 0;

        while (current is not null && guard++ <= document.Composites.Count + 1)
        {
            depth++;
            current = document.FindComposite(current)?.Parent;
        }

        return depth;
    }

    /// <summary>
    /// 以某个组合为根的那棵子树的层数（根本身算 1）。
    /// </summary>
    /// <remarks>
    /// 深度限制要管的是整棵子树，不是被移动的那一个：把一个浅组合搬进一个深容器时，
    /// 跟着它一起沉下去的还有它里面套着的所有东西。只看被移动的那一个，
    /// 就会出现"搬完才知道超了"的状态。
    /// </remarks>
    public static int SubtreeDepth(DiagramDocument document, string rootId)
    {
        ArgumentNullException.ThrowIfNull(document);

        var rootDepth = DepthOf(document, rootId);
        var deepest = rootDepth;

        foreach (var composite in document.Composites)
        {
            if (IsAncestorOf(document, rootId, composite.Id))
            {
                deepest = Math.Max(deepest, DepthOf(document, composite.Id));
            }
        }

        return deepest - rootDepth + 1;
    }

    /// <summary>
    /// <paramref name="ancestorId"/> 是不是 <paramref name="candidateId"/> 的祖先（含它自己）。
    /// </summary>
    /// <remarks>
    /// 成环判定就是它：把一个组合搬进它自己的后代里，会让"向上找容器"这条链永远走不到头。
    /// 含自身这一条同样重要——把一个组合搬进它自己，是最短的一个环。
    /// </remarks>
    public static bool IsAncestorOf(DiagramDocument document, string ancestorId, string candidateId)
    {
        ArgumentNullException.ThrowIfNull(document);

        var current = candidateId;
        var guard = 0;

        while (current is not null && guard++ <= document.Composites.Count + 1)
        {
            if (string.Equals(current, ancestorId, StringComparison.Ordinal))
            {
                return true;
            }

            current = document.FindComposite(current)?.Parent;
        }

        return false;
    }

    /// <summary>用一份新的定义换掉集合里的那一个。找不到就什么都不做。</summary>
    private static void Replace(DiagramDocument document, CompositeDef updated)
    {
        for (var i = 0; i < document.MutableComposites.Count; i++)
        {
            if (string.Equals(document.MutableComposites[i].Id, updated.Id, StringComparison.Ordinal))
            {
                document.MutableComposites[i] = updated;
                return;
            }
        }
    }

    /// <summary>
    /// 把组合集合与成员的父级整体换回快照里的那一份。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 三条组合命令的撤销共用这一段。记整份集合而不是只记"被改的那一个"，
    /// 理由是这几条命令一次会动到**多个**组合：把成员搬进一个新组合，
    /// 除了新组合之外，原来那个容器的成员列表也跟着变了。只记一个的话，
    /// 还原时要按种类再拼一次列表，而那一次拼接必须与命令里的拼接逐字一致——
    /// 两处一旦分叉，撤销出来的成员关系与原来那份会有细微差别，而差异只体现在哈希上。
    /// </para>
    /// <para>
    /// 父级字段要单独记，因为它是**另一处表达**：成员列表换回去了，
    /// 而成员的父级还指着新的容器，文档就成了一份自相矛盾的东西。
    /// </para>
    /// </remarks>
    public static void RestoreAll(
        DiagramDocument document,
        IReadOnlyList<CompositeDef> composites,
        IReadOnlyList<MemberPlacement> parents)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(composites);
        ArgumentNullException.ThrowIfNull(parents);

        document.MutableComposites.Clear();
        document.MutableComposites.AddRange(composites);

        foreach (var placement in parents)
        {
            SetParent(document, placement.Id, placement.Parent);
        }
    }
}
