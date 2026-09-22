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
