using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DuetDiagram.Tools.CompareHarness;

/// <summary>
/// 语料生成器：按"提示词 × 组"两维调用模型，把响应原样冻结下来。
/// </summary>
/// <remarks>
/// <para>
/// 为什么要把响应冻结成文件而不是每次现调：模型输出不确定。两组结果必须在同一时间、
/// 同一模型状态下产生，比较才成立。冻结之后评分可以慢慢做，与调用时机解耦。
/// </para>
/// <para>
/// 记录里同时保留原始请求体，是为了将来能回答"当时到底发了什么"。
/// 只记响应的话，一旦发现提示词写错了，无法判断影响范围。
/// </para>
/// <para>
/// 逐条幂等：已经存在的结果文件默认跳过。中途失败或中断之后重跑，不会重复花钱，
/// 也不会因为顺序变化让同一份语料出现两个版本。
/// </para>
/// </remarks>
internal static class Generator
{
    private const string EndpointSuffix = "/chat/completions";

    /// <summary>单次请求最多等多久。推理模型耗时长，给足余量。</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    /// <summary>遇到限流时的退避基数，逐次翻倍。</summary>
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(5);

    private const int MaxAttempts = 4;

    public static async Task<int> RunAsync(Options options, CancellationToken cancellationToken)
    {
        var prompts = PromptSet.Load(options.PromptsPath);
        var arms = Arm.LoadAll(options.ArmsDirectory);

        if (arms.Count == 0)
        {
            Console.Error.WriteLine($"没有找到任何组定义，检查目录 {options.ArmsDirectory}");
            return 1;
        }

        var missingArms = arms.Where(a => a.SystemPrompt.Length == 0).Select(a => a.Id).ToArray();

        if (missingArms.Length > 0)
        {
            // 组定义缺失时直接停：跑一半的语料比没有语料更糟，
            // 因为看起来有了结果，实际上缺的组永远补不上同一批条件下的数据。
            Console.Error.WriteLine($"下列组的提示词文件为空或缺失，无法生成：{string.Join("、", missingArms)}");
            return 1;
        }

        var credentials = Credentials.Load(options.SecretsPath);

        var jobs = (
            from arm in arms
            from prompt in prompts
            where options.Force || !File.Exists(ResultPath(options.OutputRoot, arm.Id, prompt.Id))
            select (Arm: arm, Prompt: prompt)).ToArray();

        Console.WriteLine($"模型 {credentials.Model}");
        Console.WriteLine($"提示词 {prompts.Count} 条，组 {arms.Count} 个，待生成 {jobs.Length} 条"
            + $"（已完成 {prompts.Count * arms.Count - jobs.Length} 条，跳过）");
        Console.WriteLine();

        if (jobs.Length == 0)
        {
            Console.WriteLine("没有需要生成的内容。");
            return 0;
        }

        using var http = new HttpClient { Timeout = RequestTimeout };
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {credentials.ApiKey}");

        var completed = 0;
        var failed = 0;

        await Parallel.ForEachAsync(
            jobs,
            new ParallelOptions { MaxDegreeOfParallelism = options.Parallelism, CancellationToken = cancellationToken },
            async (job, token) =>
            {
                var path = ResultPath(options.OutputRoot, job.Arm.Id, job.Prompt.Id);

                try
                {
                    var record = await CallAsync(http, credentials, job.Arm, job.Prompt, token).ConfigureAwait(false);

                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.WriteAllTextAsync(path, record, new UTF8Encoding(false), token).ConfigureAwait(false);

                    var done = Interlocked.Increment(ref completed);
                    Console.WriteLine($"[{done + failed}/{jobs.Length}] {job.Arm.Id}/{job.Prompt.Id} 完成");
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    Console.Error.WriteLine($"[失败] {job.Arm.Id}/{job.Prompt.Id}：{ex.GetType().Name} - {ex.Message}");
                }
            }).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"生成完成：成功 {completed}，失败 {failed}");

        return failed == 0 ? 0 : 1;
    }

    private static async Task<string> CallAsync(
        HttpClient http,
        Credentials credentials,
        Arm arm,
        Prompt prompt,
        CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["model"] = credentials.Model,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = arm.SystemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = prompt.Text },
            },
        };

        var body = payload.ToJsonString();
        var started = DateTimeOffset.UtcNow;

        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                credentials.Endpoint.TrimEnd('/') + EndpointSuffix);

            request.Content = new StringContent(body, new UTF8Encoding(false), "application/json");

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            // 限流与服务端错误值得重试；参数错误重试没有意义，直接抛出来。
            var retryable = (int)response.StatusCode is 429 or >= 500;

            if (response.IsSuccessStatusCode)
            {
                return BuildRecord(credentials, arm, prompt, body, text, started);
            }

            if (!retryable || attempt >= MaxAttempts)
            {
                throw new HttpRequestException($"接口返回 {(int)response.StatusCode}：{Trim(text)}");
            }

            var delay = RetryBaseDelay * Math.Pow(2, attempt - 1);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 组装落盘记录。
    /// </summary>
    /// <remarks>
    /// 正文与推理内容分开保存。这个模型强制思考，推理内容往往比正文长好几倍，
    /// 混在一起会让"模型到底输出了什么"看不清楚；用量也单独记，便于事后核算花费。
    /// </remarks>
    private static string BuildRecord(
        Credentials credentials,
        Arm arm,
        Prompt prompt,
        string requestBody,
        string responseBody,
        DateTimeOffset started)
    {
        var parsed = JsonNode.Parse(responseBody) ?? new JsonObject();
        var message = parsed["choices"]?[0]?["message"];

        var record = new JsonObject
        {
            ["arm"] = arm.Id,
            ["promptId"] = prompt.Id,
            ["tier"] = prompt.Tier,
            ["model"] = credentials.Model,
            ["startedAt"] = started.ToString("O"),
            ["elapsedMs"] = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            ["request"] = JsonNode.Parse(requestBody),
            ["content"] = message?["content"]?.GetValue<string>() ?? string.Empty,
            ["reasoningContent"] = message?["reasoningContent"]?.GetValue<string>()
                ?? message?["reasoning_content"]?.GetValue<string>()
                ?? string.Empty,
            ["finishReason"] = parsed["choices"]?[0]?["finishReason"]?.GetValue<string>()
                ?? parsed["choices"]?[0]?["finish_reason"]?.GetValue<string>() ?? string.Empty,
            ["usage"] = parsed["usage"]?.DeepClone() ?? new JsonObject(),
            ["rawResponse"] = parsed,
        };

        return record.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ResultPath(string root, string armId, string promptId) =>
        Path.Combine(root, armId, $"{promptId}.json");

    private static string Trim(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
