using DuetDiagram.Core.Model;

namespace DuetDiagram.App.Interaction;

/// <summary>
/// 把画布上的拖动翻译成布局约束。
/// </summary>
/// <remarks>
/// <para>
/// **落在空白处与落在兄弟节点上是两件事。** 落在空白处是"把我放这儿"，
/// 那是一个绝对位置，记在人工产物里（见固定位置）；落在一个兄弟节点上是
/// "把我排到它旁边"，那是一条相对次序，记在文档的布局约束里。
/// 两者混成一条的话，用户想做相对调整却得到一个绝对位置，
/// 而绝对位置一旦钉住，之后的自动重排就再也动不了它。
/// </para>
/// <para>
/// **合并而不是追加。** 层内次序在文档里是"每个主语一条"：同一个主语上已经有一条时，
/// 这一拖改的是那一条。追加的话，来回拖几次就会在同一对节点上积下几十条互相矛盾的次序，
/// 而求解器按列表顺序取第一条——于是生效的永远是最早那一次，用户后来拖的几下全都白做。
/// </para>
/// </remarks>
public static class ConstraintGestures
{
    /// <summary>
    /// 把一次拖动翻译成一条层内次序约束。不构成次序时为空。
    /// </summary>
    /// <param name="document">当前文档。</param>
    /// <param name="movedIds">这一拖移动的节点。多于一个时不成次序——"把这一堆排到它旁边"没有内容。</param>
    /// <param name="dropTargetId">松手时指针底下的节点。落在空白处时为空。</param>
    /// <returns>一条层内次序的规格，主语是那个共同的上级，成员是排好序的出边。</returns>
    /// <remarks>
    /// 共同上级就是"同时连到被拖节点与落点节点"的那个节点。找不到它就没有次序可言：
    /// 两个节点不共享上级时，"谁先谁后"要跨层比较，而那是同层约束该管的事。
    /// </remarks>
    public static LayoutConstraintSpec? OrderForDrop(
        DiagramDocument document,
        IReadOnlyList<string> movedIds,
        string? dropTargetId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(movedIds);

        if (movedIds.Count != 1 || dropTargetId is null)
        {
            return null;
        }

        var moved = movedIds[0];

        if (string.Equals(moved, dropTargetId, StringComparison.Ordinal)
            || !HasNode(document, moved)
            || !HasNode(document, dropTargetId))
        {
            return null;
        }

        if (CommonParent(document, moved, dropTargetId) is not { } parent)
        {
            return null;
        }

        var sequence = Sequence(document, parent);

        if (sequence.Count < 2)
        {
            return null;
        }

        var movedEdge = EdgeInto(document, sequence, moved);
        var targetEdge = EdgeInto(document, sequence, dropTargetId);

        if (movedEdge is null || targetEdge is null)
        {
            return null;
        }

        return LayoutConstraintSpec.Order(parent, Move(sequence, movedEdge, targetEdge));
    }

    /// <summary>同时连到这两个节点的那个上级。有多个时取文档里靠前的那个。</summary>
    private static string? CommonParent(DiagramDocument document, string moved, string target)
    {
        foreach (var edge in document.Edges)
        {
            if (string.Equals(edge.To, moved, StringComparison.Ordinal)
                && HasEdgeTo(document, edge.From, target))
            {
                return edge.From;
            }
        }

        return null;
    }

    private static bool HasEdgeTo(DiagramDocument document, string from, string to) =>
        document.Edges.Any(e => string.Equals(e.From, from, StringComparison.Ordinal)
            && string.Equals(e.To, to, StringComparison.Ordinal));

    /// <summary>
    /// 这个上级当前的出边次序。
    /// </summary>
    /// <remarks>
    /// 以人工定过的那一条次序为底，再接上还没被排进去的出边。以人工的那条为底而不是
    /// 以列表顺序为底：用户上一次拖出来的次序才是他心里的次序，而列表顺序只是文档里的书写顺序。
    /// 已删掉的边会在这一步被剔掉——留着的话，新写进去的次序里会有一条指向不存在边的成员，
    /// 而它在求解那一侧表现为整组次序被丢掉。
    /// </remarks>
    private static List<string> Sequence(DiagramDocument document, string parent)
    {
        var live = document.Edges
            .Where(e => string.Equals(e.From, parent, StringComparison.Ordinal))
            .Select(e => e.Id)
            .ToList();

        var sequence = new List<string>(live.Count);
        var kept = document.Layout.Order
            .FirstOrDefault(c => c.Owner == ConstraintOwner.Human
                && string.Equals(c.Value.NodeId, parent, StringComparison.Ordinal));

        if (kept is not null)
        {
            foreach (var id in kept.Value.Order)
            {
                if (live.Contains(id, StringComparer.Ordinal) && !sequence.Contains(id, StringComparer.Ordinal))
                {
                    sequence.Add(id);
                }
            }
        }

        foreach (var id in live)
        {
            if (!sequence.Contains(id, StringComparer.Ordinal))
            {
                sequence.Add(id);
            }
        }

        return sequence;
    }

    /// <summary>这个次序里连到那个节点的出边。有多条时取排在最前面的那条。</summary>
    private static string? EdgeInto(DiagramDocument document, List<string> sequence, string nodeId) =>
        sequence.FirstOrDefault(id => FindEdge(document, id) is { } edge
            && string.Equals(edge.To, nodeId, StringComparison.Ordinal));

    private static bool HasNode(DiagramDocument document, string id) =>
        document.Nodes.Any(node => string.Equals(node.Id, id, StringComparison.Ordinal));

    private static EdgeDef? FindEdge(DiagramDocument document, string id) =>
        document.Edges.FirstOrDefault(edge => string.Equals(edge.Id, id, StringComparison.Ordinal));

    /// <summary>把一条边挪到另一条边后面。</summary>
    private static List<string> Move(List<string> sequence, string moved, string after)
    {
        sequence.Remove(moved);
        sequence.Insert(sequence.IndexOf(after) + 1, moved);

        return sequence;
    }
}
