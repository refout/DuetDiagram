using DuetDiagram.Core.Model;

namespace DuetDiagram.App.Interaction;

/// <summary>
/// 把"按下了哪个元素"翻译成"这一拖要跟着动的是哪几个节点"。
/// </summary>
/// <remarks>
/// <para>
/// 拖拽的移动集合与选中集合不是一回事，但高度相关：通常拖谁就选谁；
/// 按住修饰键在已有选中上再拖，动的是整批已选中的节点。这份翻译只算一次，
/// 在按下那一刻定下来，拖动过程中不再重算——否则拖到一半选中变了，
/// 跟着动的节点也会变，用户会觉得"我拖的东西自己跑了"。
/// </para>
/// <para>
/// 相连边的集合也在这里算：拖动过程中这些边要跟着重画（见 <see cref="DragController"/>），
/// 而"哪些边连着这批节点"在按下时就是确定的，不必每帧重算。
/// </para>
/// </remarks>
public static class SelectionSet
{
    /// <summary>
    /// 这一拖要移动的节点标识。
    /// </summary>
    /// <param name="selected">当前选中的节点标识（按选中先后次序）。</param>
    /// <param name="hitId">按下的那个元素标识。</param>
    /// <param name="additive">是否增选模式。</param>
    public static IReadOnlyList<string> ResolveDragSet(
        IReadOnlyList<string> selected,
        string hitId,
        bool additive)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(hitId);

        // 非增选：拖谁就只动谁。整批选中在增选模式下才有意义，
        // 普通拖拽若还带着上一轮的选中，用户会莫名其妙地挪动一批不相关的节点。
        if (!additive)
        {
            return [hitId];
        }

        // 增选：点的这个若还没选中，先加进来再一起拖；已经选中的就维持整批。
        if (selected.Contains(hitId, StringComparer.Ordinal))
        {
            return [.. selected];
        }

        var ids = selected.ToList();
        ids.Add(hitId);

        return ids;
    }

    /// <summary>
    /// 连着这批节点的边标识。拖动时它们要跟着重画。
    /// </summary>
    public static IReadOnlyList<string> ConnectedEdges(DiagramDocument document, IReadOnlyList<string> nodeIds)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(nodeIds);

        var set = new HashSet<string>(nodeIds, StringComparer.Ordinal);

        return [.. document.Edges
            .Where(edge => set.Contains(edge.From) || set.Contains(edge.To))
            .Select(edge => edge.Id)];
    }
}
