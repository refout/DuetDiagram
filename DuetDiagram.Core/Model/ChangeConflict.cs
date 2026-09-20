using DuetDiagram.Core.Commands;

namespace DuetDiagram.Core.Model;

/// <summary>两组变更的合并结论。</summary>
public enum MergeOutcome
{
    /// <summary>两边碰到的元素不相交，直接合并即可。</summary>
    NoOverlap,

    /// <summary>碰到了同一个元素，但改的是不同字段，可以自动合并。</summary>
    Mergeable,

    /// <summary>同一个字段被两边都改了，必须由人来决定听谁的。</summary>
    Conflicting,
}

/// <summary>一处具体冲突。</summary>
public sealed record FieldConflict(string ElementId, string LocalField, string RemoteField);

/// <summary>合并评估结果。</summary>
public sealed record MergeAssessment(
    MergeOutcome Outcome,
    IReadOnlyList<string> SharedElementIds,
    IReadOnlyList<FieldConflict> Conflicts)
{
    public bool CanMergeAutomatically => Outcome != MergeOutcome.Conflicting;
}

/// <summary>
/// 两组字段变更能否自动合并。
/// </summary>
/// <remarks>
/// <para>
/// 判据只有一条：**两边有没有改同一个元素的同一个字段**。
/// 元素不同、或元素相同但字段不同，都不构成冲突——把它们一律当冲突处理，
/// 会让用户在最不需要干预的时候被打断。
/// </para>
/// <para>
/// 有一类情况看着像"改了不同地方"，实际不能合并：一侧增删了整个元素，
/// 另一侧修改了这个元素的某个字段。增删与修改的前提互相矛盾——
/// 说"这是新加的"和说"我改了它"不可能同时成立，因此一律判为冲突。
/// </para>
/// <para>
/// 本方法只做判定，不做合并。合并需要写回文档，而写回必须走命令层——
/// 在这里直接改文档会绕过版本推进、历史入栈与广播，那正是"唯一事实源"最容易被破坏的地方。
/// </para>
/// </remarks>
public static class ChangeConflict
{
    public static MergeAssessment Resolve(
        IEnumerable<FieldChange> local,
        IEnumerable<FieldChange> remote)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);

        var left = Group(local);
        var right = Group(remote);

        var shared = new List<string>();
        var conflicts = new List<FieldConflict>();

        foreach (var (elementId, leftFields) in left)
        {
            if (!right.TryGetValue(elementId, out var rightFields))
            {
                continue;
            }

            shared.Add(elementId);

            var leftElementLevel = leftFields.Where(FieldNames.IsElementLevel).ToArray();
            var rightElementLevel = rightFields.Where(FieldNames.IsElementLevel).ToArray();

            if (leftElementLevel.Length > 0 || rightElementLevel.Length > 0)
            {
                // 一侧增删、另一侧改动，前提互斥。两侧都增删同一个元素也一样：
                // 双方都认为自己是创建者。
                conflicts.Add(new FieldConflict(
                    elementId,
                    leftElementLevel.FirstOrDefault() ?? leftFields.First(),
                    rightElementLevel.FirstOrDefault() ?? rightFields.First()));

                continue;
            }

            foreach (var field in leftFields.Where(rightFields.Contains))
            {
                conflicts.Add(new FieldConflict(elementId, field, field));
            }
        }

        var outcome = conflicts.Count > 0
            ? MergeOutcome.Conflicting
            : shared.Count > 0
                ? MergeOutcome.Mergeable
                : MergeOutcome.NoOverlap;

        return new MergeAssessment(outcome, shared, conflicts);
    }

    /// <summary>
    /// 按元素归并字段名。
    /// </summary>
    /// <remarks>
    /// 同一个元素上可能出现重复的字段名（例如一次命令里连续改了两次标签），
    /// 去重之后判定结果不受重复次数影响。
    /// </remarks>
    private static Dictionary<string, HashSet<string>> Group(IEnumerable<FieldChange> changes)
    {
        var grouped = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var change in changes)
        {
            if (!grouped.TryGetValue(change.ElementId, out var fields))
            {
                fields = new HashSet<string>(StringComparer.Ordinal);
                grouped[change.ElementId] = fields;
            }

            fields.Add(change.Field);
        }

        return grouped;
    }
}
