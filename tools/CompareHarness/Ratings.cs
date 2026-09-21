using System.Text.Json.Nodes;

namespace DuetDiagram.Tools.CompareHarness;

/// <summary>
/// 一位评分者对一份条目的评分。
/// </summary>
/// <param name="Checks">逐项判定：1 通过、0 不通过、null 未判。</param>
/// <param name="Steps">把这份图改成符合提示词需要几步人工修正。0 表示不用改。</param>
/// <param name="Extra">多出来的东西，一句话。谓词判不了这个。</param>
internal sealed record Rating(int?[] Checks, int? Steps, string? Extra)
{
    /// <summary>逐项都判了。</summary>
    public bool ChecksJudged => Checks.All(value => value is not null);

    /// <summary>逐项与修正步数都填了。缺修正步数时首轮通过率与修正步骤都算不了。</summary>
    public bool Complete => ChecksJudged && Steps is not null;
}

/// <summary>
/// 一份人工评分文件。
/// </summary>
/// <remarks>
/// <para>
/// 格式上**只认严的**：编号不认识、项数对不上、值不是 0/1/null，一律报错并列出位置。
/// 放过去的代价是那几项从分母里静默消失——一致率会照常算出来，只是少判了几项，
/// 而报告上没有任何东西提示这一点。
/// </para>
/// <para>
/// 未判（<c>null</c>）是允许的：判到一半就收工是常事。未判的项不进分母，
/// 但覆盖多少会明写在报告里。
/// </para>
/// </remarks>
internal sealed record RatingsFile(
    string Path,
    string Fingerprint,
    string Rater,
    string Sample,
    IReadOnlyDictionary<string, Rating> Items)
{
    public const string Prefix = "ratings-";

    public const string Suffix = ".json";

    /// <summary>
    /// 读一份评分文件。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <param name="key">抽样对照表，用来核编号与项数。</param>
    /// <param name="prompts">提示词集合，用来查每份条目该有几项。</param>
    public static RatingsFile Load(string path, SampleKey key, PromptSet prompts)
    {
        var byId = prompts.ToDictionary(prompt => prompt.Id, StringComparer.Ordinal);

        var node = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidDataException($"{path} 不是合法 JSON。");

        if (node is not JsonObject root)
        {
            throw new InvalidDataException($"{path} 的根节点必须是一个对象。");
        }

        var rater = root["rater"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(rater))
        {
            throw new InvalidDataException($"{path} 缺少 rater。汇总里要按评分者分栏，没名字就没法分。");
        }

        var sample = root["sample"]?.GetValue<string>()
            ?? throw new InvalidDataException($"{path} 缺少 sample。它是抽样清单的指纹，用来确认这份评分是对着哪一批做的。");

        if (!string.Equals(sample, key.Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{path} 记的抽样清单指纹是 {sample}，当前抽样是 {key.Fingerprint}。"
                + "两边不是同一批条目，编号指的是不同的东西，算不出一致率。"
                + "要换一批就重新发清单、重新评分。");
        }

        var items = new Dictionary<string, Rating>(StringComparer.Ordinal);

        foreach (var (anon, value) in root["items"]?.AsObject() ?? [])
        {
            var entry = key.Find(anon)
                ?? throw new InvalidDataException($"{path} 里的编号 {anon} 不在抽样清单里。");

            if (!byId.TryGetValue(entry.PromptId, out var prompt))
            {
                throw new InvalidDataException($"{path} 里的编号 {anon} 指向提示词 {entry.PromptId}，而提示词文件里没有它。");
            }

            items[anon] = ParseRating(value, prompt.Checks.Length, $"{path} 的编号 {anon}");
        }

        if (items.Count == 0)
        {
            throw new InvalidDataException($"{path} 里没有任何评分。");
        }

        return new RatingsFile(path, Digest.OfFile(path), rater, sample, items);
    }

    /// <summary>
    /// 读目录里全部评分文件。
    /// </summary>
    /// <remarks>
    /// 目录不存在时返回空表而不是报错：第一次跑 agreement 时目录里什么都还没有，
    /// 那种情况该给一句"还没人交"，不该给一段异常堆栈。
    /// </remarks>
    public static IReadOnlyList<RatingsFile> LoadAll(string directory, SampleKey key, PromptSet prompts)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(directory, $"{Prefix}*{Suffix}")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => Load(path, key, prompts)),
        ];
    }

    private static Rating ParseRating(JsonNode? node, int checkCount, string where)
    {
        if (node is not JsonObject obj)
        {
            throw new InvalidDataException($"{where}：评分必须是一个对象，写成 {{\"checks\": […], \"steps\": 0}}。");
        }

        var checksNode = obj["checks"] as JsonArray
            ?? throw new InvalidDataException($"{where}：缺少 checks 数组。");

        if (checksNode.Count != checkCount)
        {
            throw new InvalidDataException(
                $"{where}：checks 有 {checksNode.Count} 项，而这一份的检查项是 {checkCount} 项。"
                + "项数对不上说明评分是对着另一版检查项做的，按序号对齐会把判定安到错的话上。");
        }

        var checks = new int?[checkCount];

        for (var i = 0; i < checkCount; i++)
        {
            checks[i] = checksNode[i] switch
            {
                null => null,
                JsonValue value when value.TryGetValue(out int number) && number is 0 or 1 => number,
                _ => throw new InvalidDataException($"{where} 第 {i + 1} 项：只能填 1（通过）、0（不通过）或 null（未判）。"),
            };
        }

        var steps = obj["steps"] switch
        {
            null => (int?)null,
            JsonValue value when value.TryGetValue(out int number) && number >= 0 => number,
            _ => throw new InvalidDataException($"{where}：steps 只能填非负整数，或 null 表示未判。"),
        };

        var extra = obj["extra"] switch
        {
            null => null,
            JsonValue value when value.TryGetValue(out string? text) => text,
            _ => throw new InvalidDataException($"{where}：extra 只能是字符串。"),
        };

        return new Rating(checks, steps, extra);
    }
}
