using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands;

/// <summary>
/// 图层集合的读写：按标识定位、换掉一个、整体还原。
/// </summary>
/// <remarks>
/// <para>
/// 三条图层命令都按标识定位而不是按集合下标。下标是易失的：调用方拿到的第 2 位，
/// 在请求发出到执行之间可能已经因为别处删了一个图层而变成第 1 位。
/// 标识不会这样变。
/// </para>
/// <para>
/// 图层是不可变记录，所以"改一个图层"其实是"用一份新的换掉它"。
/// </para>
/// </remarks>
internal static class LayerAccess
{
    /// <summary>按标识取一个图层。找不到返回空。</summary>
    public static LayerDef? Find(DiagramDocument document, string layerId)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var layer in document.Layers)
        {
            if (string.Equals(layer.Id, layerId, StringComparison.Ordinal))
            {
                return layer;
            }
        }

        return null;
    }

    /// <summary>按标识取一个图层，取不到就抛。</summary>
    /// <remarks>
    /// 给 <c>Apply</c> 用。它跑到这里时校验已经过了，取不到只可能是文档在校验与执行之间
    /// 被别处改了——那时抛出来比返回空要好：返回空会让调用方自己去猜该报什么，
    /// 而这一步没有"猜"的余地。
    /// </remarks>
    public static LayerDef Require(DiagramDocument document, string layerId)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Find(document, layerId)
            ?? throw new InvalidOperationException($"Layer '{layerId}' does not exist.");
    }

    /// <summary>按标识找到图层在集合里的下标。找不到返回 -1。</summary>
    public static int IndexOf(DiagramDocument document, string layerId)
    {
        ArgumentNullException.ThrowIfNull(document);

        for (var i = 0; i < document.Layers.Count; i++)
        {
            if (string.Equals(document.Layers[i].Id, layerId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>用一份新的定义换掉集合里的那一个。找不到就什么都不做。</summary>
    public static void Replace(DiagramDocument document, LayerDef updated)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(updated);

        var index = IndexOf(document, updated.Id);

        if (index >= 0)
        {
            document.MutableLayers[index] = updated;
        }
    }

    /// <summary>
    /// 把整份图层集合换回快照里的那一份。
    /// </summary>
    /// <remarks>
    /// 三条图层命令的撤销共用这一段。重排一次会改到每一个图层的次序，
    /// 只还原被点名的那个的话，其余图层还停在改动之后的样子。
    /// </remarks>
    public static void RestoreAll(DiagramDocument document, IReadOnlyList<LayerDef> layers)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layers);

        document.MutableLayers.Clear();
        document.MutableLayers.AddRange(layers);
    }
}

/// <summary>
/// 图层开关那两条命令共用的那一小段：一次改动的结果与撤销用的快照。
/// </summary>
/// <remarks>
/// 合成一处是因为它们必须一致：两条命令改的都是"图层集合里的一个元素"，
/// 结果里报的变更类型、撤销时换回去的那份快照，两处各写一遍迟早会分叉——
/// 而分叉的表现是撤销之后图层集合停在改动之后的样子，版本号却照样往前走了。
/// </remarks>
internal static class LayerCommands
{
    /// <summary>
    /// 一次改动的结果。
    /// </summary>
    /// <remarks>
    /// **只报外观变更。** 两个开关都不改变任何坐标——藏起来只是不画，锁上什么都不改画面。
    /// 报成结构变更的话，宿主会白重算一遍布局，而结果与刚才逐字节相同。
    /// </remarks>
    public static CommandResult Result(string layerId, bool before, bool after) =>
        CommandResult.Ok(
            affected: [layerId],
            changes:
            [
                new FieldChange
                {
                    ElementId = layerId,
                    Field = FieldNames.LayerElement,
                    OldValue = before.ToString(),
                    NewValue = after.ToString(),
                    Kind = ChangeKind.Modified,
                },
            ],
            structural: false,
            visual: true);

    /// <summary>
    /// 撤销用的那份快照：改之前的整份图层集合。
    /// </summary>
    /// <param name="document">改之前的文档。</param>
    /// <param name="layerId">被改的那个图层。</param>
    /// <param name="undoFrom">撤销时从这里出发，也就是这条命令要写进去的那个值。</param>
    /// <param name="undoTo">撤销时回到这里，也就是它改之前的那个值。</param>
    /// <remarks>
    /// 两个方向的值分开传而不是让这个方法自己去读文档：它是在 <c>Apply</c> **之前**
    /// 被调用的，那时文档里还是旧值，而"要写进去的新值"只有命令自己知道。
    /// </remarks>
    public static CommandMemento Capture(
        DiagramDocument document,
        string layerId,
        bool undoFrom,
        bool undoTo) =>
        new LayerMemento
        {
            PreviousLayers = [.. document.Layers],
            AffectedIds = [layerId],
            InverseChanges =
            [
                new FieldChange
                {
                    ElementId = layerId,
                    Field = FieldNames.LayerElement,
                    OldValue = undoFrom.ToString(),
                    NewValue = undoTo.ToString(),
                    Kind = ChangeKind.Modified,
                },
            ],
        };
}
