using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands;

/// <summary>
/// 页面集合的读写：按标识定位、整体还原。
/// </summary>
/// <remarks>
/// <para>
/// 两条页面命令都按标识定位而不是按集合下标。下标是易失的：调用方拿到的第 2 页，
/// 在请求发出到执行之间可能已经因为别处删了一页而变成第 1 页。标识不会这样变。
/// </para>
/// <para>
/// 还原一律换整份集合，而不是把被删的那一页追加回去。页面的集合位置是加入顺序，
/// 追加到末尾会让撤销之后的次序与删除前不同；而两个哈希都先把集合按标识排序再遍历，
/// 顺序错了照样对得上，这个错在哈希上一点痕迹都没有。
/// </para>
/// </remarks>
internal static class PageAccess
{
    /// <summary>按标识取一页。找不到返回空。</summary>
    public static PageDef? Find(DiagramDocument document, string pageId)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var page in document.Pages)
        {
            if (string.Equals(page.Id, pageId, StringComparison.Ordinal))
            {
                return page;
            }
        }

        return null;
    }

    /// <summary>按标识找到页面在集合里的下标。找不到返回 -1。</summary>
    public static int IndexOf(DiagramDocument document, string pageId)
    {
        ArgumentNullException.ThrowIfNull(document);

        for (var i = 0; i < document.Pages.Count; i++)
        {
            if (string.Equals(document.Pages[i].Id, pageId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>把整份页面集合换回快照里的那一份。</summary>
    public static void RestoreAll(DiagramDocument document, IReadOnlyList<PageDef> pages)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pages);

        document.MutablePages.Clear();
        document.MutablePages.AddRange(pages);
    }
}
