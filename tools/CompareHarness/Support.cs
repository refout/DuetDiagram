using System.Collections;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DuetDiagram.Tools.CompareHarness;

/// <summary>本地凭据。从被忽略的文件里读，绝不写进仓库。</summary>
internal sealed record Credentials(string Endpoint, string ApiKey, string Model)
{
    public static Credentials Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"找不到凭据文件 {path}。它应当放在仓库之外或被忽略的目录里，不要提交。", path);
        }

        var node = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidDataException($"凭据文件 {path} 不是合法 JSON。");

        return new Credentials(
            node["endpoint"]?.GetValue<string>() ?? throw new InvalidDataException("凭据缺少 endpoint。"),
            node["apiKey"]?.GetValue<string>() ?? throw new InvalidDataException("凭据缺少 apiKey。"),
            node["model"]?.GetValue<string>() ?? throw new InvalidDataException("凭据缺少 model。"));
    }
}

/// <summary>
/// 一条检查项。
/// </summary>
/// <param name="Text">给人读的那句话。评分者看到的就是它。</param>
/// <param name="When">
/// 可执行的判据。<b>为空表示这一项只能靠人判</b>，<paramref name="Human"/> 里写着为什么。
/// </param>
/// <param name="Human">判不了机器时，说明是哪一类判断。</param>
/// <param name="Note">
/// 原文与现在这句话不一样时，写明原来是什么、为什么改。
/// 改写本身是判断，判断要能被看见——不然读的人以为检查项一直长这样。
/// </param>
internal sealed record Check(string Text, Predicate? When, string? Human, string? Note)
{
    public bool MachineCheckable => When is not null;
}

internal sealed record Prompt(string Id, string Tier, string Title, string Text, Check[] Checks);

/// <summary>
/// 提示词集合。
/// </summary>
/// <remarks>
/// <para>
/// 提示词本身不是机密，随仓库一起版本化——它是实验的一部分，改一条就会让已有语料失效，
/// 所以必须能被追踪和对照。
/// </para>
/// <para>
/// 检查项也在同一个文件里，但它**不随语料冻结**：检查项从来不会被发给模型
/// （看 <c>Generator</c> 就知道，请求体里只有 <c>prompt</c>），所以改写它不影响已有语料。
/// 把两者放在一起是因为它们判的是同一件事，分开放迟早会对不上。
/// </para>
/// </remarks>
internal sealed class PromptSet : IEnumerable<Prompt>
{
    private readonly List<Prompt> _prompts;

    private PromptSet(List<Prompt> prompts) => _prompts = prompts;

    public int Count => _prompts.Count;

    public IEnumerator<Prompt> GetEnumerator() => _prompts.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static PromptSet Load(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidDataException($"提示词文件 {path} 不是合法 JSON。");

        var prompts = new List<Prompt>();

        foreach (var item in root["prompts"]?.AsArray() ?? [])
        {
            if (item is null)
            {
                continue;
            }

            var id = item["id"]?.GetValue<string>() ?? throw new InvalidDataException("提示词缺少 id。");
            var checks = (item["checks"]?.AsArray() ?? [])
                .Select((check, i) => ParseCheck(check, $"{id} 第 {i + 1} 项"))
                .ToArray();

            prompts.Add(new Prompt(
                id,
                item["tier"]?.GetValue<string>() ?? string.Empty,
                item["title"]?.GetValue<string>() ?? string.Empty,
                item["prompt"]?.GetValue<string>() ?? throw new InvalidDataException("提示词缺少 prompt。"),
                checks));
        }

        if (prompts.Count == 0)
        {
            throw new InvalidDataException($"提示词文件 {path} 里没有任何条目。");
        }

        return new PromptSet(prompts);
    }

    /// <summary>
    /// 读一条检查项。
    /// </summary>
    /// <remarks>
    /// 既不写 <c>when</c> 也不写 <c>human</c> 的检查项直接报错，而不是当成"人判"放过去。
    /// 那样写的人多半是忘了给谓词，而放过去的代价是这一项从机器口径里静默消失——
    /// 指标会照常算出来，只是少判了几项，没有任何东西会提示。
    /// </remarks>
    private static Check ParseCheck(JsonNode? node, string where)
    {
        if (node is not JsonObject obj)
        {
            throw new InvalidDataException($"{where}：检查项必须是一个对象，写成 {{\"text\": \"…\", \"when\": {{…}}}}。");
        }

        var text = obj["text"]?.GetValue<string>()
            ?? throw new InvalidDataException($"{where}：缺少 text。");

        var when = obj["when"];
        var human = obj["human"]?.GetValue<string>();

        if (when is null && human is null)
        {
            throw new InvalidDataException($"{where}：既没有 when 也没有 human。判不了机器的项要写明是哪一类判断。");
        }

        if (when is not null && human is not null)
        {
            throw new InvalidDataException($"{where}：when 与 human 只能有一个。");
        }

        return new Check(
            text,
            when is null ? null : Predicate.Parse(when, where),
            human,
            obj["note"]?.GetValue<string>());
    }
}

/// <summary>
/// 写进报告与清单的路径。
/// </summary>
/// <remarks>
/// <see cref="System.IO.Path.Combine(string, string)"/> 在 Windows 上给的是反斜杠，而这些文本
/// 是要交给人照着敲命令的，也要求同一台机器重跑逐字节一致。统一成正斜杠，
/// 两个平台上的产物才是同一份。
/// </remarks>
internal static class Display
{
    public static string Path(string path) => path.Replace('\\', '/');
}

/// <summary>
/// 写 JSON 时的统一设置。
/// </summary>
/// <remarks>
/// 缩进是为了能对着文件核；不转义非 ASCII 是为了能读——默认编码器会把中文写成
/// <c>\uXXXX</c>，而其中一份文件是要交给人手填的，满屏转义码看不出哪一栏写的是什么。
/// 这里写的都是本地文件，不做 HTML 嵌入，所以放宽转义没有风险。
/// </remarks>
internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>
/// 一个对照组。
/// </summary>
/// <remarks>
/// 组定义放在独立的文件里而不是写死在代码里：提示词是实验的自变量，
/// 需要被审阅和对照，塞进代码就看不见了。
/// </remarks>
internal sealed record Arm(string Id, string SystemPrompt)
{
    public static List<Arm> LoadAll(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(directory, "*.md")
                .OrderBy(p => p, StringComparer.Ordinal)
                // 组标识用文件名，避免同一件事在两处维护。
                .Select(path => new Arm(Path.GetFileNameWithoutExtension(path), File.ReadAllText(path).Trim())),
        ];
    }
}

internal sealed record Options(
    string PromptsPath,
    string ArmsDirectory,
    string SecretsPath,
    string OutputRoot,
    int Parallelism,
    bool Force,
    string[] Only)
{
    public const string DefaultPromptsPath = "tools/CompareHarness/prompts.json";

    public const string DefaultArmsDirectory = "tools/CompareHarness/arms";

    public const string DefaultSecretsPath = "secrets/bigmodel.local.json";

    public const string DefaultOutputRoot = "reports/raw";

    /// <summary>冻结语料所在处。生成与评分共用同一份，评的就是它。</summary>
    public const string DefaultCorpusRoot = DefaultOutputRoot;

    /// <summary>盲评清单的输出处。</summary>
    public const string DefaultListingsRoot = "reports/compare-blind";

    /// <summary>
    /// 人工评分放在结果目录下的这个子目录里。
    /// </summary>
    /// <remarks>
    /// 单独一层子目录是为了能整目录忽略：评分文件是人工产出的工作材料，
    /// 里面还带着"哪一条来自哪一组"的对照，不该进版本库。
    /// 汇总结论进版本库，原始评分不进。
    /// </remarks>
    public const string HumanDirectoryName = "human";

    /// <summary>是否选中这条提示词。没指定筛选时全部选中。</summary>
    public bool Selects(Prompt prompt) =>
        Only.Length == 0 || Only.Contains(prompt.Id, StringComparer.OrdinalIgnoreCase);
}
