using System.Text.Json.Nodes;

namespace DuetDiagram.Tools.CompareHarness;

/// <summary>
/// 冻结语料里的一份响应。
/// </summary>
/// <param name="Arm">所属组。</param>
/// <param name="PromptId">提示词标识。</param>
/// <param name="Tier">难度。</param>
/// <param name="Content">模型输出的图代码，原文未动。</param>
/// <param name="RequestedPrompt">当时实际发出去的用户消息。</param>
/// <param name="Model">模型名。</param>
/// <param name="FinishReason">结束原因。<c>length</c> 表示被截断。</param>
/// <param name="ElapsedMs">耗时。</param>
internal sealed record ResponseRecord(
    string Arm,
    string PromptId,
    string Tier,
    string Content,
    string RequestedPrompt,
    string Model,
    string FinishReason,
    long ElapsedMs);

/// <summary>
/// 读取冻结语料。
/// </summary>
/// <remarks>
/// <para>
/// 只读，不写。语料是实验的因变量，任何"顺手修一下"都会让比较不成立。
/// </para>
/// <para>
/// 缺文件时抛异常而不是跳过。少一份就少一条观测，而按 50 算的分母会悄悄变小——
/// 那种错误不会让任何测试变红，只会让结论偏掉。
/// </para>
/// </remarks>
internal static class Corpus
{
    public static IReadOnlyList<ResponseRecord> Load(string root, IReadOnlyList<string> arms)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"找不到语料目录 {root}。");
        }

        var records = new List<ResponseRecord>();

        foreach (var arm in arms)
        {
            var directory = Path.Combine(root, arm);

            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException($"语料里缺少组 {arm}（{directory}）。");
            }

            var files = Directory.EnumerateFiles(directory, "*.json")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            if (files.Length == 0)
            {
                throw new InvalidDataException($"组 {arm} 里没有任何响应文件。");
            }

            foreach (var path in files)
            {
                records.Add(Read(path, arm));
            }
        }

        return records;
    }

    private static ResponseRecord Read(string path, string arm)
    {
        var node = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidDataException($"{path} 不是合法 JSON。");

        var promptId = node["promptId"]?.GetValue<string>()
            ?? throw new InvalidDataException($"{path} 缺少 promptId。");

        // 用户消息从记录里的原始请求体取，而不是回头去读 prompts.json。
        // 理由是"当时到底发了什么"只有这份记录能回答：提示词文件后来改过一版的话，
        // 拿现在的文件去配当时的响应，配对的是两份不同的东西。
        var messages = node["request"]?["messages"]?.AsArray()
            ?? throw new InvalidDataException($"{path} 里没有 request.messages。");

        var prompt = messages
            .Where(m => string.Equals(m?["role"]?.GetValue<string>(), "user", StringComparison.Ordinal))
            .Select(m => m?["content"]?.GetValue<string>())
            .FirstOrDefault(text => text is not null)
            ?? throw new InvalidDataException($"{path} 里没有用户消息。");

        return new ResponseRecord(
            arm,
            promptId,
            node["tier"]?.GetValue<string>() ?? string.Empty,
            node["content"]?.GetValue<string>() ?? string.Empty,
            prompt,
            node["model"]?.GetValue<string>() ?? string.Empty,
            node["finishReason"]?.GetValue<string>() ?? string.Empty,
            node["elapsedMs"]?.GetValue<long>() ?? 0);
    }
}
