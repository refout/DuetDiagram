namespace DuetDiagram.Core.Sidecar;

/// <summary>
/// 备份保留策略。
/// </summary>
/// <param name="MaxCount">最多保留几份。</param>
/// <param name="MaxAge">最老的备份可以留多久。</param>
/// <remarks>
/// <para>
/// 两个上限**各自生效**，保留一份备份的条件是"既在前若干份之内、又没有超过最大年龄"。
/// 保留条件写成"最近 10 个或 30 天"时，"或"字有歧义。取交集而不是并集的理由是存储要有上界：
/// 并集在"一天之内改一百次"时会保留全部一百份，而那正是最需要收敛的场合。
/// </para>
/// <para>
/// 交集下"30 天"这一条并不多余：偶尔编辑的文档几天才存一次，
/// 十条记录可能横跨好几个月，这时年龄上限才会起作用。
/// </para>
/// </remarks>
public sealed record BackupPolicy(int MaxCount, TimeSpan MaxAge)
{
    /// <summary>缺省值：最多十份，最老三十天。</summary>
    public static BackupPolicy Default { get; } = new(10, TimeSpan.FromDays(30));

    public static BackupPolicy Keep(int count, int days) => new(count, TimeSpan.FromDays(days));
}

/// <summary>一份备份的概要。</summary>
/// <param name="Path">文件路径。</param>
/// <param name="CreatedAt">由文件名中的时间戳解析而来。</param>
public sealed record BackupInfo(string Path, DateTimeOffset CreatedAt);

/// <summary>一次带备份的保存结果。</summary>
/// <param name="BackupPath">本次产生的备份路径。原先没有文件时为空。</param>
/// <param name="Removed">本次清理掉的备份。</param>
public sealed record SaveOutcome(string? BackupPath, IReadOnlyList<string> Removed);

/// <summary>
/// 人工产物的备份与恢复。
/// </summary>
/// <remarks>
/// <para>
/// 只有人工产物需要备份。布局缓存可以重算，给它做备份等于给一个能随时重建的东西留副本，
/// 除了占地方没有别的效果。
/// </para>
/// <para>
/// **应用程序应当走 <see cref="Save"/> 而不是底层的写入方法。**
/// 直接写入不会产生备份，而"忘了备份"这件事在平时完全看不出来——
/// 直到某天文件损坏、用户点开恢复对话框、发现里面空空如也。
/// </para>
/// </remarks>
public static class SidecarBackup
{
    /// <summary>
    /// 带备份的保存：先备份现有内容，再写入新内容，最后按策略清理。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 顺序不能换。先写后备份的话，备份下来的就是刚写进去的新内容，等于没有备份。
    /// </para>
    /// <para>
    /// 现有文件无法解析时**不产生备份**。理由很简单：解析不了的内容没有可恢复的东西，
    /// 把它放进备份列表只会让用户在恢复对话框里选到一个同样打不开的文件。
    /// 下一句写入会覆盖它，而覆盖一份坏文件没有损失。
    /// </para>
    /// </remarks>
    public static SaveOutcome Save(
        string documentPath,
        UserSidecar user,
        BackupPolicy? policy = null,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(user);

        var at = now ?? DateTimeOffset.UtcNow;
        var effectivePolicy = policy ?? BackupPolicy.Default;

        var backupPath = Create(documentPath, at);
        var removed = Prune(documentPath, effectivePolicy, at);

        SidecarStore.SaveUser(documentPath, user);

        return new SaveOutcome(backupPath, removed);
    }

    /// <summary>
    /// 把当前的用户文件备份一份。
    /// </summary>
    /// <returns>备份路径。文件不存在或无法解析时返回空。</returns>
    public static string? Create(string documentPath, DateTimeOffset? now = null)
    {
        var path = SidecarPaths.User(documentPath);

        if (!File.Exists(path))
        {
            return null;
        }

        // 解析不了的内容没有可恢复的东西，不放进备份列表。
        // 放进去了只会让用户在恢复对话框里选到一个同样打不开的文件。
        try
        {
            var content = File.ReadAllText(path);

            if (System.Text.Json.JsonSerializer.Deserialize(content, SidecarJsonContext.Default.UserSidecar) is null)
            {
                return null;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        var backup = SidecarPaths.Backup(path, now ?? DateTimeOffset.UtcNow);

        File.Copy(path, backup, overwrite: true);

        return backup;
    }

    /// <summary>
    /// 列出可用的备份，由新到旧。
    /// </summary>
    /// <remarks>
    /// 只按文件名里的时间戳排序，不读取内容。列备份是最常用的一步（打开恢复对话框时就会调），
    /// 为了排序去解析每一份内容不划算。内容能不能用由 <see cref="Restore"/> 判断。
    /// </remarks>
    public static IReadOnlyList<BackupInfo> List(string documentPath)
    {
        var userPath = SidecarPaths.User(documentPath);
        var directory = Path.GetDirectoryName(userPath);
        var stem = Path.GetFileNameWithoutExtension(userPath);

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(directory, $"{stem}.*{SidecarPaths.BackupExtension}")
                .Select(path => (Path: path, Time: SidecarPaths.ParseBackupTime(path)))
                .Where(entry => entry.Time is not null)
                .OrderByDescending(entry => entry.Time)
                .Select(entry => new BackupInfo(entry.Path, entry.Time!.Value)),
        ];
    }

    /// <summary>
    /// 按策略清理备份。
    /// </summary>
    /// <param name="documentPath">文档路径。</param>
    /// <param name="policy">保留策略。</param>
    /// <param name="now">当前时刻。用于判断年龄。</param>
    /// <returns>被删掉的备份路径。</returns>
    /// <remarks>
    /// 调用时机是启动时、每次保存后、以及每小时一次。
    /// 定时那一次要由宿主来排——Core 里没有调度器，也不该有。
    /// </remarks>
    public static IReadOnlyList<string> Prune(
        string documentPath,
        BackupPolicy? policy = null,
        DateTimeOffset? now = null)
    {
        var effectivePolicy = policy ?? BackupPolicy.Default;
        var at = now ?? DateTimeOffset.UtcNow;
        var cutoff = at - effectivePolicy.MaxAge;

        var all = List(documentPath);
        var removed = new List<string>();

        for (var i = 0; i < all.Count; i++)
        {
            // 前 MaxCount 份之内、且没有超过最大年龄，两条都满足才留下。
            var keep = i < effectivePolicy.MaxCount && all[i].CreatedAt >= cutoff;

            if (keep)
            {
                continue;
            }

            File.Delete(all[i].Path);
            removed.Add(all[i].Path);
        }

        return removed;
    }

    /// <summary>
    /// 从一份备份恢复。
    /// </summary>
    /// <remarks>
    /// 恢复出来的内容同样要按当前文档校验一遍。备份是很久以前的快照，
    /// 里面记录的节点可能早就被删掉了——那些条目会以孤儿的形式报出来，
    /// 而不是悄悄指向一个不存在的元素。
    /// </remarks>
    public static SidecarLoad<UserSidecar> Restore(
        string backupPath,
        Model.DiagramDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        ArgumentNullException.ThrowIfNull(document);

        if (!File.Exists(backupPath))
        {
            return SidecarLoad<UserSidecar>.Unusable($"备份文件已不存在：{backupPath}");
        }

        UserSidecar? user;

        try
        {
            user = System.Text.Json.JsonSerializer.Deserialize(
                File.ReadAllText(backupPath),
                SidecarJsonContext.Default.UserSidecar);
        }
        catch (System.Text.Json.JsonException ex)
        {
            return SidecarLoad<UserSidecar>.Unusable($"备份无法解析：{ex.Message}");
        }

        if (user is null)
        {
            return SidecarLoad<UserSidecar>.Unusable("备份内容是空的。");
        }

        // 复用常规加载的同一条校验路径：文档标识、孤儿。两处各写一遍迟早会分叉。
        var orphans = new List<string>();

        orphans.AddRange(user.PinnedNodes.Keys.Where(id => !HasNode(document, id)));
        orphans.AddRange(user.CustomPorts.Keys.Where(id => !HasNode(document, id)));
        orphans.AddRange(user.PinnedEdges.Keys.Where(id => !HasEdge(document, id)));

        if (user.DocumentId is not null && !string.Equals(user.DocumentId, document.Id, StringComparison.Ordinal))
        {
            return SidecarLoad<UserSidecar>.Unusable(
                $"备份属于文档 {user.DocumentId}，当前文档是 {document.Id}。");
        }

        return SidecarLoad<UserSidecar>.Loaded(user, orphans);
    }

    private static bool HasNode(Model.DiagramDocument document, string id) =>
        document.Nodes.Any(n => string.Equals(n.Id, id, StringComparison.Ordinal));

    private static bool HasEdge(Model.DiagramDocument document, string id) =>
        document.Edges.Any(e => string.Equals(e.Id, id, StringComparison.Ordinal));
}
