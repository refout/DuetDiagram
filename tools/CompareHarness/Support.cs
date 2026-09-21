using System.Collections;
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

internal sealed record Prompt(string Id, string Tier, string Title, string Text, string[] Checks);

/// <summary>
/// 提示词集合。
/// </summary>
/// <remarks>
/// 提示词本身不是机密，随仓库一起版本化——它是实验的一部分，改一条就会让已有语料失效，
/// 所以必须能被追踪和对照。
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

            prompts.Add(new Prompt(
                item["id"]?.GetValue<string>() ?? throw new InvalidDataException("提示词缺少 id。"),
                item["tier"]?.GetValue<string>() ?? string.Empty,
                item["title"]?.GetValue<string>() ?? string.Empty,
                item["prompt"]?.GetValue<string>() ?? throw new InvalidDataException("提示词缺少 prompt。"),
                [.. (item["checks"]?.AsArray() ?? []).Select(c => c?.GetValue<string>() ?? string.Empty)]));
        }

        if (prompts.Count == 0)
        {
            throw new InvalidDataException($"提示词文件 {path} 里没有任何条目。");
        }

        return new PromptSet(prompts);
    }


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

    /// <summary>是否选中这条提示词。没指定筛选时全部选中。</summary>
    public bool Selects(Prompt prompt) =>
        Only.Length == 0 || Only.Contains(prompt.Id, StringComparer.OrdinalIgnoreCase);
}
