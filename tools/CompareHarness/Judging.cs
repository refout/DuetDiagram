namespace DuetDiagram.Tools.CompareHarness;

/// <summary>
/// 一条检查项在一份语料上的判定。
/// </summary>
/// <param name="Index">在提示词的检查项列表里的序号，从 0 数。</param>
/// <param name="Text">检查项原文。</param>
/// <param name="Machine">这一项有没有谓词。</param>
/// <param name="Pass">通过与否。没有谓词时恒为假，报告里不把它算进分母。</param>
/// <param name="Reason">不通过的原因，给人复核用。</param>
internal sealed record CheckVerdict(int Index, string Text, bool Machine, bool Pass, string? Reason);

/// <summary>
/// 一份语料的判定。
/// </summary>
/// <param name="Item">被判的那一份，带着它的结构清单与提示词。</param>
/// <param name="Checks">逐项判定，顺序与提示词的检查项一致。</param>
internal sealed record Judged(BlindItem Item, CheckVerdict[] Checks)
{
    public ResponseRecord Record => Item.Record;

    public Prompt Prompt => Item.Prompt;

    public StructureListing Listing => Item.Listing;

    /// <summary>解析器给出了结构。被拒绝的必然判不通过。</summary>
    public bool Parsed => !Structure.IsRejected(Listing.Outcome);

    public int MachineCount => Checks.Count(check => check.Machine);

    public int MachinePassed => Checks.Count(check => check.Machine && check.Pass);

    /// <summary>有谓词的项全过。没有谓词的项不参与——它们的结论要等人工。</summary>
    public bool AllMachinePassed => MachineCount > 0 && MachinePassed == MachineCount;
}

/// <summary>
/// 逐项判定：把一份匿名条目过一遍检查项的谓词。
/// </summary>
/// <remarks>
/// <para>
/// 判的是**评分者看到的那一份**：标识已换成流水号、换行标记已抹平。这样机器判定与人工判定
/// 看的是同一件东西，拿人工评分来校谓词才对得上。
/// </para>
/// <para>
/// 这一段与解析、匿名化一样被多个命令共用，理由也相同：各自实现一份的话，
/// 同一份语料在不同命令里会得到不同的判定，而报告之间对不上时看不出是谁错了。
/// </para>
/// </remarks>
internal static class Judging
{
    public static Judged[] RunAll(IReadOnlyList<BlindItem> items) => [.. items.Select(Run)];

    public static Judged Run(BlindItem item)
    {
        var index = new StructureIndex(item.Listing);
        var reject = Structure.IsRejected(item.Listing.Outcome);
        var verdicts = new CheckVerdict[item.Prompt.Checks.Length];

        for (var i = 0; i < item.Prompt.Checks.Length; i++)
        {
            var check = item.Prompt.Checks[i];

            if (check.When is null)
            {
                verdicts[i] = new CheckVerdict(i, check.Text, false, false, null);
            }
            else if (reject)
            {
                // 解析失败必然不通过，且不能从分母里剔除。这是判定口径定死的。
                verdicts[i] = new CheckVerdict(i, check.Text, true, false, "解析器没给出结构，必然不通过。");
            }
            else
            {
                var verdict = check.When.Evaluate(index);
                verdicts[i] = new CheckVerdict(i, check.Text, true, verdict.Pass, verdict.Reason);
            }
        }

        return new Judged(item, verdicts);
    }
}
