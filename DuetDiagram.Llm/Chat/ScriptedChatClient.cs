using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace DuetDiagram.Llm.Chat;

/// <summary>
/// 被记下来的一次请求。
/// </summary>
/// <param name="Messages">这一次请求带过去的全部消息，含之前几轮的工具结果。</param>
/// <param name="Options">这一次请求用的选项。</param>
public sealed record ScriptedRequest(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options);

/// <summary>
/// 按写死的脚本回答的假客户端。
/// </summary>
/// <remarks>
/// <para>
/// 它让「工具调用往返」这件事能在没有模型、没有网络的情况下逐字节固定下来。
/// 打真模型的话，同一个用例每次结果不同，而失败时看不出是工具的问题还是模型那天心情不好。
/// 真模型的端到端验收是另一条，与这一层分开。
/// </para>
/// <para>
/// 只做非流式那一路。流式的回答是一串增量，脚本要把一次回答拆成几段才有意义，
/// 而这一层现在没有一处消费流式。
/// </para>
/// </remarks>
/// <param name="responses">按次序回答的那几条。用完之后再问会抛。</param>
public sealed class ScriptedChatClient(params ChatResponse[] responses) : IChatClient
{
    private readonly Queue<ChatResponse> _pending = new(responses);
    private readonly List<ScriptedRequest> _requests = [];

    /// <summary>已经收到过的请求，按次序。</summary>
    public IReadOnlyList<ScriptedRequest> Requests => _requests;

    /// <summary>脚本里还剩几条没用。</summary>
    public int Remaining => _pending.Count;

    /// <summary>模型说一句话，不调工具。</summary>
    public static ChatResponse Says(string text) =>
        new(new ChatMessage(ChatRole.Assistant, text));

    /// <summary>
    /// 模型调一次工具。
    /// </summary>
    /// <remarks>
    /// 参数按 JSON 对象给，逐项转成 JSON 值放进调用内容里——参数从调用内容流到执行体那一段
    /// 认的就是 JSON 形式，放别的类型会在那里被拒。
    /// </remarks>
    /// <param name="name">工具名。</param>
    /// <param name="arguments">参数，一个 JSON 对象。</param>
    /// <param name="callId">这次调用的标识。同一轮里调多个工具时要各不相同。</param>
    public static ChatResponse Calls(string name, string arguments, string callId = "call-1")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);

        var parsed = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (JsonNode.Parse(arguments) is JsonObject root)
        {
            foreach (var (key, value) in root)
            {
                parsed[key] = value is null
                    ? null
                    : JsonDocument.Parse(value.ToJsonString()).RootElement.Clone();
            }
        }

        return new ChatResponse(new ChatMessage(
            ChatRole.Assistant,
            [new FunctionCallContent(callId, name, parsed)]));
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        _requests.Add(new ScriptedRequest([.. messages], options));

        return _pending.Count > 0
            ? Task.FromResult(_pending.Dequeue())
            : throw new InvalidOperationException("脚本里的回答用完了，而调用方又问了一次。");
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("这个假客户端只做非流式那一路。");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
