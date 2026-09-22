using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DuetDiagram.Tools.UserStudy;

/// <summary>
/// 一份用户测试的录入数据。
/// </summary>
/// <remarks>
/// 数据是人工录进来的，所以读取放得宽：允许注释、允许尾逗号、评分允许留空。
/// 留空的那一条不参与计算，但要被数出来并在报告里写明——把没填完的当成零分，
/// 算出来的均值会低一截，而报告上看不出哪里不对。
/// </remarks>
internal sealed class Study
{
    /// <summary>这份测试测的是什么。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>被测的手段，次序就是统计矩阵的列序。</summary>
    public List<string> Arms { get; set; } = [];

    /// <summary>量表下限。</summary>
    public int ScaleMin { get; set; } = 1;

    /// <summary>量表上限。</summary>
    public int ScaleMax { get; set; } = 5;

    /// <summary>受试者。</summary>
    public List<RaterEntry> Raters { get; set; } = [];
}

/// <summary>一个受试者的一条录入。</summary>
internal sealed class RaterEntry
{
    /// <summary>受试者编号。用假名，不写姓名。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>他看这些手段的顺序。算统计时用不上，核对顺序效应是否均衡时要用。</summary>
    public List<string> Order { get; set; } = [];

    /// <summary>每个手段的评分。没填的留空。</summary>
    public Dictionary<string, int?> Scores { get; set; } = [];

    /// <summary>定性反馈。</summary>
    public string? Notes { get; set; }
}

/// <summary>数据的读写。</summary>
internal static class StudyJson
{
    /// <summary>读写用的序列化设置。</summary>
    /// <remarks>
    /// 中文不转义：手段名是中文，转义之后录入文件没法用眼睛核对，
    /// 而人工录入的错行正是靠眼睛看出来的。允许注释与尾逗号是给录入者留的余地，
    /// 他要在某个受试者旁边写一句"设备卡了一下"，不该因为这个文件就读不进来。
    /// </remarks>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static Study Load(string path) =>
        JsonSerializer.Deserialize<Study>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"读不出数据：{path}");

    public static void Save(string path, Study study)
    {
        EnsureDirectory(path);

        // 换行统一成 LF。这些文件要进版本库，而仓库按 LF 归一化：
        // 在 Windows 上写出 CRLF 的话，每次重新生成都会留下一份"整篇都改了"的假差异。
        File.WriteAllText(path, JsonSerializer.Serialize(study, Options).ReplaceLineEndings("\n"));
    }

    /// <summary>数据文件的指纹，取前十六位。</summary>
    /// <remarks>
    /// 报告里要写明这一份结论是从哪一份数据算出来的。报告本身不带时间戳——
    /// 同一份数据每次跑出同一份文件，比对才有意义；而指纹是内容的函数，写进去不影响这一点。
    /// </remarks>
    public static string Fingerprint(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))[..16];

    public static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}

/// <summary>交叉设计的顺序分配。</summary>
internal static class Ordering
{
    /// <summary>第 <paramref name="index"/> 个受试者看这些手段的顺序。</summary>
    /// <remarks>
    /// 轮转拉丁方：三个手段时，第 0 个看甲乙丙、第 1 个看乙丙甲、第 2 个看丙甲乙，之后重复。
    /// 每连续三个受试者构成一个完整的拉丁方，每个手段在三个位置上各出现一次，
    /// 于是任意整块收上来的数据里，学习效应在各手段之间被抵消掉。
    /// 不做这一步的话，先看的那一个会拿到偏低的分，而那个差会被算成手段之间的差。
    /// </remarks>
    public static string[] OrderFor(int index, IReadOnlyList<string> arms)
    {
        var order = new string[arms.Count];

        for (var position = 0; position < arms.Count; position++)
        {
            order[position] = arms[(index + position) % arms.Count];
        }

        return order;
    }

    /// <summary>每个手段在每个位置上出现了几次。行是手段，列是位置。</summary>
    public static int[][] PositionCounts(IReadOnlyList<string> arms, IEnumerable<IReadOnlyList<string>> orders)
    {
        var counts = new int[arms.Count][];

        for (var arm = 0; arm < arms.Count; arm++)
        {
            counts[arm] = new int[arms.Count];
        }

        foreach (var order in orders)
        {
            for (var position = 0; position < order.Count; position++)
            {
                var arm = IndexOf(arms, order[position]);

                if (arm >= 0 && position < arms.Count)
                {
                    counts[arm][position]++;
                }
            }
        }

        return counts;
    }

    /// <summary>每个手段在每个位置上出现次数是不是一样多。</summary>
    public static bool IsBalanced(int[][] counts) =>
        counts.Length > 0 && counts.All(row => row.All(cell => cell == counts[0][0]));

    /// <summary>不均衡时，把差在哪儿说清楚。</summary>
    public static string BalanceNote(IReadOnlyList<string> arms, int[][] counts)
    {
        if (IsBalanced(counts))
        {
            var each = counts.Length > 0 && counts[0].Length > 0 ? counts[0][0] : 0;

            return $"每个手段在每个位置上各出现 {each} 次，顺序效应被抵消。";
        }

        var lines = new List<string>
        {
            "顺序不均衡：",
        };

        for (var arm = 0; arm < arms.Count; arm++)
        {
            lines.Add($"  {arms[arm]}：{string.Join(" / ", counts[arm])}");
        }

        lines.Add("  收上来的份数不是手段数的整数倍时会出现这种局面。");

        return string.Join(Environment.NewLine, lines);
    }

    private static int IndexOf(IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (string.Equals(values[index], value, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
