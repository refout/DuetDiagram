using DuetDiagram.Core.Sidecar;

namespace DuetDiagram.Dsl.Mapping;

/// <summary>
/// 把文本里的固定位置并进已有的 sidecar。
/// </summary>
/// <remarks>
/// <para>
/// **规则：已有记录不动，文本只补它没有的那部分。**
/// </para>
/// <para>
/// 这不是两套规则，是已有的归属规则在存储上的投影：DSL 里的 <c>pin</c> 是 <c>Llm</c>，
/// sidecar 里的是 <c>Human</c>，冲突时 <c>Human</c> 赢。
/// 固定位置本来就是**人工覆盖布局算出的坐标**，与固定折线、自定义端口并列，
/// 后两样也住在 sidecar 里。
/// </para>
/// <para>
/// **反过来做会出两种问题。** 若文本覆盖 sidecar：用户每次拖动都会被下一版文本抹掉，
/// 而模型重生成一次文本是常事。若把文本里的 pin 也写进 sidecar：
/// 那份文件就不再只装人工产物了，而它的备份策略正是建立在"只有人工产物需要备份"上。
/// </para>
/// <para>
/// 判定放在这里而不是映射层，是因为映射层拿不到已有的 sidecar——
/// 它只负责如实产出"文本里写了什么"。
/// </para>
/// </remarks>
public static class SidecarMerge
{
    /// <summary>
    /// 用文本里的固定位置补已有 sidecar 的空缺，返回合并结果。
    /// </summary>
    /// <param name="existing">已有的 sidecar（人工产物）。可以是空的。</param>
    /// <param name="fromText">文本里产出的那一份（见 <see cref="MappingResult.Sidecar"/>）。</param>
    public static UserSidecar Fill(UserSidecar existing, UserSidecar fromText)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(fromText);

        var pinned = new Dictionary<string, Anchor>(existing.PinnedNodes, StringComparer.Ordinal);

        foreach (var (id, anchor) in fromText.PinnedNodes)
        {
            // 已经有记录就不动。TryAdd 让"不动"这件事在代码上一眼可见，
            // 而写成 ContainsKey 加赋值则要多读一行才知道谁赢。
            pinned.TryAdd(id, anchor);
        }

        return existing with
        {
            DocumentId = existing.DocumentId ?? fromText.DocumentId,
            PinnedNodes = pinned,
        };
    }
}
