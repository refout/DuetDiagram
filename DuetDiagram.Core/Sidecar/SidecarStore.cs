using System.Text.Json;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Sidecar;

/// <summary>
/// Sidecar 的读写与完整性校验。
/// </summary>
/// <remarks>
/// <para>
/// 两类文件的失败处置完全不同，这是整块最要紧的一点：
/// </para>
/// <list type="bullet">
/// <item>布局缓存过期或损坏 → 丢掉重算。用户没有做错任何事，不该被打断。</item>
/// <item>人工产物损坏 → 无法重算，只能提示用户走恢复。丢掉等于毁掉他所有的手工调整。</item>
/// </list>
/// <para>
/// 因此这里的每个加载方法都**如实报告状态**，而不自行决定丢弃还是保留。
/// 决定权在调用方，因为它才知道当前是在打开文档、还是在自动保存、
/// 还是在后台清理——三种场合的正确处置并不相同。
/// </para>
/// <para>
/// 解析失败一律转成 <see cref="SidecarStatus.Unusable"/>，不往外抛异常。
/// 一个附属文件格式坏掉不该让整个文档打不开。文件读写本身的异常则照常抛出：
/// 那是环境问题（权限、磁盘），静默吞掉会让它变成更难查的"保存没生效"。
/// </para>
/// </remarks>
public static class SidecarStore
{
    // ---- 布局缓存 ----

    public static SidecarLoad<LayoutSidecar> LoadLayout(string documentPath, DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var path = SidecarPaths.Layout(documentPath);

        if (!File.Exists(path))
        {
            return SidecarLoad<LayoutSidecar>.Missing();
        }

        LayoutSidecar? layout;

        try
        {
            layout = JsonSerializer.Deserialize(File.ReadAllText(path), SidecarJsonContext.Default.LayoutSidecar);
        }
        catch (JsonException ex)
        {
            return SidecarLoad<LayoutSidecar>.Unusable($"布局文件无法解析：{ex.Message}");
        }

        if (layout is null)
        {
            return SidecarLoad<LayoutSidecar>.Unusable("布局文件是空的。");
        }

        // 来自另一份文档的布局一定用不了。这与"哈希不符"不同：
        // 哈希不符是正常的缓存过期，而文档对不上说明文件被错放了。
        if (layout.DocumentId is not null && !string.Equals(layout.DocumentId, document.Id, StringComparison.Ordinal))
        {
            return SidecarLoad<LayoutSidecar>.Unusable(
                $"布局文件属于文档 {layout.DocumentId}，当前文档是 {document.Id}。");
        }

        if (!string.Equals(layout.StructuralHash, document.StructuralHash, StringComparison.Ordinal))
        {
            // 用户改了图，缓存自然过期。这是最常见的情况，不是错误。
            return SidecarLoad<LayoutSidecar>.Stale("文档改过，布局缓存已过期。");
        }

        return SidecarLoad<LayoutSidecar>.Loaded(layout, [.. OrphanNodes(document, layout.Nodes.Keys)]);
    }

    public static void SaveLayout(string documentPath, LayoutSidecar layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        WriteAtomic(SidecarPaths.Layout(documentPath), SerializeLayout(layout));
    }

    // ---- 人工产物 ----

    public static SidecarLoad<UserSidecar> LoadUser(string documentPath, DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var path = SidecarPaths.User(documentPath);

        if (!File.Exists(path))
        {
            return SidecarLoad<UserSidecar>.Missing();
        }

        UserSidecar? user;

        try
        {
            user = JsonSerializer.Deserialize(File.ReadAllText(path), SidecarJsonContext.Default.UserSidecar);
        }
        catch (JsonException ex)
        {
            return SidecarLoad<UserSidecar>.Unusable($"人工产物文件无法解析：{ex.Message}");
        }

        if (user is null)
        {
            return SidecarLoad<UserSidecar>.Unusable("人工产物文件是空的。");
        }

        if (user.DocumentId is not null && !string.Equals(user.DocumentId, document.Id, StringComparison.Ordinal))
        {
            return SidecarLoad<UserSidecar>.Unusable(
                $"人工产物文件属于文档 {user.DocumentId}，当前文档是 {document.Id}。");
        }

        var orphans = new List<string>();

        orphans.AddRange(OrphanNodes(document, user.PinnedNodes.Keys));
        orphans.AddRange(OrphanNodes(document, user.CustomPorts.Keys));
        orphans.AddRange(user.PinnedEdges.Keys
            .Where(id => !document.Edges.Any(e => string.Equals(e.Id, id, StringComparison.Ordinal))));

        return SidecarLoad<UserSidecar>.Loaded(user, orphans);
    }

    public static void SaveUser(string documentPath, UserSidecar user)
    {
        ArgumentNullException.ThrowIfNull(user);

        WriteAtomic(SidecarPaths.User(documentPath), SerializeUser(user));
    }

    // ---- 内部 ----

    /// <summary>
    /// 从人工产物里摘出仍然有效的部分，丢掉孤儿。
    /// </summary>
    /// <remarks>
    /// 与加载分开：加载如实报告孤儿，要不要丢由调用方决定。
    /// 这个方法是"确实要丢掉"时用的，把决定权留在调用方手上。
    /// </remarks>
    public static UserSidecar PruneOrphans(UserSidecar user, IReadOnlyList<string> orphans)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(orphans);

        if (orphans.Count == 0)
        {
            return user;
        }

        var drop = new HashSet<string>(orphans, StringComparer.Ordinal);

        return user with
        {
            PinnedNodes = user.PinnedNodes.Where(kv => !drop.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            PinnedEdges = user.PinnedEdges.Where(kv => !drop.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            CustomPorts = user.CustomPorts.Where(kv => !drop.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
        };
    }

    private static IEnumerable<string> OrphanNodes(DiagramDocument document, IEnumerable<string> ids) =>
        ids.Where(id => !document.Nodes.Any(n => string.Equals(n.Id, id, StringComparison.Ordinal)));

    /// <summary>
    /// 先写临时文件再改名。
    /// </summary>
    /// <remarks>
    /// 直接覆写的话，写到一半断电或进程被杀，原文件就变成了半截内容——
    /// 而人工产物是本该丢不起的那份。改名在同一分区上是原子操作，
    /// 要么旧的完整、要么新的完整，不会出现中间态。
    /// </remarks>
    private static void WriteAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";

        File.WriteAllText(temporary, content);
        File.Move(temporary, path, overwrite: true);
    }

    private static string SerializeLayout(LayoutSidecar layout) =>
        JsonSerializer.Serialize(layout, SidecarJsonContext.Default.LayoutSidecar);

    private static string SerializeUser(UserSidecar user) =>
        JsonSerializer.Serialize(user, SidecarJsonContext.Default.UserSidecar);
}
