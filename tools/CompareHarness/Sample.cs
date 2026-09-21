using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DuetDiagram.Tools.CompareHarness;

/// <summary>样本里的一条，按清单里的顺序。</summary>
/// <param name="Anon">清单上的编号，就是评分文件里的键。</param>
/// <param name="Arm">组别。<b>不进清单，只进 key 与汇总。</b></param>
internal sealed record SampleEntry(string Anon, string Arm, string PromptId, string Tier);

/// <summary>
/// 抽样清单的对照表。
/// </summary>
/// <remarks>
/// <para>
/// 它把编号对回组别，**不要交给评分者**。同时它把"抽了哪几份"冻在这里：
/// 抽样是按谓词的判定结果分层的，而谓词会被改，改完之后重算抽样会得到另一批条目——
/// 那样已经收上来的人工评分就对不上号了。所以抽样只做一次，结果落盘，
/// 之后的命令一律读这份文件，不重算。
/// </para>
/// </remarks>
internal sealed record SampleKey(
    string Fingerprint,
    string ChecksFingerprint,
    int PerArm,
    IReadOnlyList<SampleEntry> Entries)
{
    public const string FileName = "sample-key.json";

    public const string SheetFileName = "sample-rater.md";

    /// <summary>放在评分文件目录里的空白模板。名字不带 <c>ratings-</c> 前缀，免得被当成一份评分读进来。</summary>
    public const string TemplateFileName = "template.json";

    public static SampleKey Create(PromptSet prompts, IReadOnlyList<SampleEntry> entries, int perArm) =>
        new(
            Digest.Of(entries.Select(entry => $"{entry.Anon}|{entry.Arm}|{entry.PromptId}")),
            Digest.OfChecks(prompts),
            perArm,
            entries);

    public SampleEntry? Find(string anon) =>
        Entries.FirstOrDefault(entry => string.Equals(entry.Anon, anon, StringComparison.Ordinal));

    public string ToJson() => new JsonObject
    {
        ["warning"] = "这份文件把编号对回组别，**不要交给评分者**。它同时冻结了抽样结果，不要手改。",
        ["fingerprint"] = Fingerprint,
        ["checksFingerprint"] = ChecksFingerprint,
        ["perArm"] = PerArm,
        ["entries"] = new JsonArray(
        [
            .. Entries.Select(entry => (JsonNode)new JsonObject
            {
                ["anon"] = entry.Anon,
                ["arm"] = entry.Arm,
                ["promptId"] = entry.PromptId,
                ["tier"] = entry.Tier,
            }),
        ]),
    }.ToJsonString(Json.Options);

    public static SampleKey Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"找不到抽样对照表 {path}。先跑一次 sample 命令生成它；不要手工造，"
                + "它冻结了评分文件里那些编号指的是哪几份。", path);
        }

        var node = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidDataException($"{path} 不是合法 JSON。");

        var entries = new List<SampleEntry>();

        foreach (var item in node["entries"]?.AsArray() ?? [])
        {
            if (item is null)
            {
                continue;
            }

            entries.Add(new SampleEntry(
                item["anon"]?.GetValue<string>() ?? throw new InvalidDataException($"{path} 有条目缺少 anon。"),
                item["arm"]?.GetValue<string>() ?? throw new InvalidDataException($"{path} 有条目缺少 arm。"),
                item["promptId"]?.GetValue<string>() ?? throw new InvalidDataException($"{path} 有条目缺少 promptId。"),
                item["tier"]?.GetValue<string>() ?? string.Empty));
        }

        if (entries.Count == 0)
        {
            throw new InvalidDataException($"{path} 里没有任何条目。");
        }

        return new SampleKey(
            node["fingerprint"]?.GetValue<string>() ?? throw new InvalidDataException($"{path} 缺少 fingerprint。"),
            node["checksFingerprint"]?.GetValue<string>() ?? throw new InvalidDataException($"{path} 缺少 checksFingerprint。"),
            node["perArm"]?.GetValue<int>() ?? 0,
            entries);
    }
}

/// <summary>
/// 抽样：从 150 份里挑出一小批交给人判，用来校谓词。
/// </summary>
/// <remarks>
/// <para>
/// 全量人工评分不现实（150 份 × 每条提示词四五项），而谓词又没有跟人核过。
/// 抽样把"校谓词"这件事压到一次坐得下来的量。
/// </para>
/// <para>
/// 分层的口径是<b>谓词判不通过、且解析成功的优先收</b>，收不满再从其余里按固定顺序补。
/// 理由是这一份的用途：分歧只会出现在谓词判"不通过"的地方，全是"通过"的条目问不出谓词错在哪。
/// 解析失败的那些排除在外，理由见 <see cref="HasPredicateFailure"/>。
/// </para>
/// <para>
/// <b>代价必须写在清单上：这份样本对"谓词太松"不敏感。</b>
/// 谓词把错的判成对——那种条目不会被优先收进来，除非补位恰好抽中；
/// 而补位进来的那些，解析成功的按定义全是"谓词全过"。所以一致率高不能推出谓词对。
/// </para>
/// </remarks>
internal static class Sample
{
    /// <summary>抽样用的种子。与全量清单的种子不同：两个装置各自打乱，抽样结果才不是一段连续的编号。</summary>
    public const ulong Seed = 20260922UL;

    public const int DefaultPerArm = 20;

    private static readonly UTF8Encoding Utf8 = new(false);

    public static int Run(string promptsPath, string corpusRoot, string outputRoot, int perArm)
    {
        if (perArm <= 0)
        {
            Console.Error.WriteLine($"每组份数必须为正数，收到 {perArm}。");
            return 1;
        }

        var prompts = PromptSet.Load(promptsPath);
        var records = Corpus.Load(corpusRoot, Structure.Arms);
        var (missing, drifted) = Corpus.Reconcile(records, prompts);

        if (missing.Length > 0)
        {
            Console.Error.WriteLine($"语料里有提示词文件里没有的条目：{string.Join('、', missing)}");
            return 1;
        }

        if (drifted.Length > 0)
        {
            Console.Error.WriteLine($"下列记录的原始提示词与 {promptsPath} 对不上：{string.Join('、', drifted)}");
            Console.Error.WriteLine("语料与提示词文件不是同一版，检查项会配错对象。");
            return 1;
        }

        var judged = Judging.RunAll(Blind.Build(prompts, records));
        var selected = new List<BlindItem>();
        var layers = new List<Layer>();

        foreach (var arm in Structure.Arms)
        {
            var rows = judged
                .Where(row => string.Equals(row.Record.Arm, arm, StringComparison.Ordinal))
                .ToArray();

            var failing = rows.Where(HasPredicateFailure).Select(row => row.Item).ToList();
            var rest = rows.Where(row => !HasPredicateFailure(row)).Select(row => row.Item).ToList();

            var take = new List<BlindItem>();
            take.AddRange(failing.Take(perArm));
            var fromFailing = take.Count;
            take.AddRange(rest.Take(perArm - take.Count));

            selected.AddRange(take);
            layers.Add(new Layer(
                arm,
                rows.Length,
                rows.Count(row => !row.Parsed),
                failing.Count,
                fromFailing,
                take.Count - fromFailing));
        }

        Blind.Shuffle(selected, Seed);

        var chosen = selected
            .Select((item, i) => (Entry: new SampleEntry(
                (i + 1).ToString("D3", CultureInfo.InvariantCulture),
                item.Record.Arm,
                item.Prompt.Id,
                item.Prompt.Tier), Item: item))
            .ToArray();

        var key = SampleKey.Create(prompts, [.. chosen.Select(row => row.Entry)], perArm);
        var humanRoot = Path.Combine(outputRoot, Options.HumanDirectoryName);

        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(humanRoot);

        Write(Path.Combine(outputRoot, SampleKey.SheetFileName), Sheet(chosen, key, layers, humanRoot));
        Write(Path.Combine(outputRoot, SampleKey.FileName), key.ToJson());
        Write(Path.Combine(humanRoot, SampleKey.TemplateFileName), Template(chosen, key));

        Console.WriteLine($"抽样清单已生成到 {Display.Path(Path.Combine(outputRoot, SampleKey.SheetFileName))}");
        Console.WriteLine();
        Console.WriteLine($"共 {chosen.Length} 份，每组目标 {perArm} 份：");
        Console.WriteLine();
        Console.WriteLine("| 组 | 语料 | 解析失败 | 谓词判不通过 | 收进来的 | 从其余补的 |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|");

        foreach (var layer in layers)
        {
            Console.WriteLine($"| `{layer.Arm}` | {layer.Total} | {layer.Rejected} | {layer.Failing} "
                + $"| {layer.Picked} | {layer.Filled} |");
        }

        Console.WriteLine();
        Console.WriteLine($"空白模板：{Display.Path(Path.Combine(humanRoot, SampleKey.TemplateFileName))}");
        Console.WriteLine($"复制成 ratings-<评分者>.json 填好，再跑 agreement 命令。");
        Console.WriteLine();
        Console.WriteLine($"{SampleKey.FileName} 把编号对回组别，**不要交给评分者**。");

        return 0;
    }

    /// <summary>
    /// 值得优先收的条目：解析出了结构，而且有谓词判了不通过。
    /// </summary>
    /// <remarks>
    /// 解析失败的那些**不算**。它们的谓词必然判不通过，人看一眼也会说不通过，
    /// 两边永远一致，问不出谓词错在哪——把它们排进优先层只会挤掉真正有分歧的条目。
    /// 实测下来这一点是必须的：150 份里"判不通过"的有 6 份，其中 3 份是解析失败——
    /// 分层若不排除它们，收进来的有一半是"人也会说不通过"的条目。
    /// </remarks>
    private static bool HasPredicateFailure(Judged row) =>
        row.Parsed && row.MachineCount > 0 && row.MachinePassed < row.MachineCount;

    /// <param name="Rejected">解析失败，拿不到结构。</param>
    /// <param name="Failing">解析成功、且有谓词判了不通过——真正想收的那一批。</param>
    /// <param name="Picked">从上面那一批里收了几份。</param>
    /// <param name="Filled">从其余里补了几份。</param>
    private sealed record Layer(string Arm, int Total, int Rejected, int Failing, int Picked, int Filled);

    private static void Write(string path, string text) =>
        File.WriteAllText(path, text.Replace("\r\n", "\n", StringComparison.Ordinal), Utf8);

    private static string Sheet(
        IReadOnlyList<(SampleEntry Entry, BlindItem Item)> rows,
        SampleKey key,
        IReadOnlyList<Layer> layers,
        string humanRoot)
    {
        var text = new StringBuilder();
        var machineChecks = rows.Sum(row => row.Item.Prompt.Checks.Count(check => check.MachineCheckable));
        var allChecks = rows.Sum(row => row.Item.Prompt.Checks.Length);

        text.AppendLine("# 抽样评分表（校谓词用）");
        text.AppendLine();
        text.AppendLine($"{rows.Count} 份，每组最多 {key.PerArm} 份，已打乱。"
            + "每份是一条提示词的一次生成结果，**看不出它来自哪种格式**。");
        text.AppendLine();
        text.AppendLine("## 为什么抽这一批");
        text.AppendLine();
        text.AppendLine("检查项已经改写成可执行的谓词，但**谓词没有跟人核过**。");
        text.AppendLine("这一批就是拿去核它的：人判一遍，机器判一遍，两边对不上的地方说明谓词写错了。");
        text.AppendLine();
        text.AppendLine("抽样口径是**谓词判不通过、且解析成功的优先收**，收不满再从其余里按固定顺序补。");
        text.AppendLine("理由是分歧只会出现在谓词判「不通过」的地方，全是「通过」的条目问不出谓词错在哪；");
        text.AppendLine("而解析失败的那些不算——它们的谓词必然判不通过，人看一眼也会说不通过，");
        text.AppendLine("两边永远一致，收进来只会挤掉真正有分歧的条目。它们按比例落在补位的那部分里。");
        text.AppendLine();
        text.AppendLine("| 组 | 语料 | 解析失败 | 谓词判不通过 | 收进来的 | 从其余补的 |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|");

        foreach (var layer in layers)
        {
            text.AppendLine($"| `{layer.Arm}` | {layer.Total} | {layer.Rejected} | {layer.Failing} "
                + $"| {layer.Picked} | {layer.Filled} |");
        }

        text.AppendLine();
        text.AppendLine("**这份样本对「谓词太松」不敏感。** 谓词把错的判成对，那种条目不会被优先收进来；");
        text.AppendLine("它只可能出现在补位的那几份里，而补位进来的那些，解析成功的按定义全是「谓词全过」。");
        text.AppendLine("所以一致率高不能推出谓词对。");
        text.AppendLine();
        text.AppendLine("## 怎么判");
        text.AppendLine();
        text.AppendLine("读法与 `rater.md` 相同：节点写成 `n1「标签」`，标识是流水号不是原文；");
        text.AppendLine("**没有标形状的节点是直角矩形**；清单不含端口、样式、坐标与布局意图。");
        text.AppendLine();
        text.AppendLine("逐项判**通过（1）或不通过（0）**，不要留空——留空的项在汇总里算作未判。");
        text.AppendLine(allChecks == machineChecks
            ? "这一批的检查项**全部有机器判据**，也**照常逐项判**：那份判据还没跟人核过，"
                + "两边都判才能发现它写错的地方。"
            : "标着「（有机器判据）」的项**照常判**：那份判据还没跟人核过，两边都判才能发现它写错的地方。");
        text.AppendLine();
        text.AppendLine("另外两栏是谓词判不了的东西，**必须填**：");
        text.AppendLine();
        text.AppendLine("- `steps`：把这份图改成完全符合提示词，需要几步人工修正（删一个多余节点算 1 步，改一条边也算 1 步）。");
        text.AppendLine("  完全不用改就填 `0`。谓词只判「该有的在不在」，判不了「多出来的该不该有」，这一栏就是补那个缺口。");
        text.AppendLine("- `extra`：多出来的东西，一句话写清。没有就留空字符串。");
        text.AppendLine();
        text.AppendLine("## 怎么交");
        text.AppendLine();
        text.AppendLine($"复制 `{Display.Path(Path.Combine(humanRoot, SampleKey.TemplateFileName))}`（相对仓库根）成 "
            + "`ratings-<你的名字>.json`，填好放在同一个目录里，再跑：");
        text.AppendLine();
        text.AppendLine("```");
        text.AppendLine("dotnet run --project tools/CompareHarness -c Release -- agreement");
        text.AppendLine("```");
        text.AppendLine();
        text.AppendLine($"清单编号：{key.Fingerprint}　检查项：{key.ChecksFingerprint}");
        text.AppendLine();
        text.AppendLine(allChecks == machineChecks
            ? $"这一批共 {allChecks} 项检查项，全部有机器判据。"
            : $"这一批共 {allChecks} 项检查项，其中 {machineChecks} 项有机器判据。");
        text.AppendLine();

        foreach (var (entry, item) in rows)
        {
            text.AppendLine("---");
            text.AppendLine();
            text.AppendLine($"## 编号 {entry.Anon}");
            text.AppendLine();
            text.Append(Blind.RenderItem(item));
        }

        return text.ToString();
    }

    private static string Template(IReadOnlyList<(SampleEntry Entry, BlindItem Item)> rows, SampleKey key)
    {
        var items = new JsonObject();

        foreach (var (entry, item) in rows)
        {
            items[entry.Anon] = new JsonObject
            {
                // 项数先按检查项条数填好：数目对不上会被 agreement 判为格式错，
                // 而不是静默少判几项。空位写成 null，表示还没判。
                ["checks"] = new JsonArray([.. item.Prompt.Checks.Select(_ => (JsonNode)null!)]),
                ["steps"] = null,
                ["extra"] = string.Empty,
            };
        }

        return new JsonObject
        {
            ["rater"] = string.Empty,
            ["sample"] = key.Fingerprint,
            ["note"] = "checks 逐项填 1（通过）或 0（不通过）；steps 填人工修正步数，不用改填 0；"
                + "extra 写多出来的东西，没有就留空。",
            ["items"] = items,
        }.ToJsonString(Json.Options);
    }
}
