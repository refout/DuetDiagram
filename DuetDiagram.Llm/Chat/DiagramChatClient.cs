using System.Text.Json;
using DuetDiagram.Llm.Loop;
using DuetDiagram.Llm.Tools;
using Microsoft.Extensions.AI;

namespace DuetDiagram.Llm.Chat;

/// <summary>
/// 内部模型那条通路：把一次对话接到工具层上，失败按结构化错误回灌。
/// </summary>
/// <remarks>
/// <para>
/// 它自己不实现工具调用循环，而是把依赖里那一个挂上——那个循环已经处理了
/// 多轮往返、并发调用、最大轮数与终止条件，重写一遍只会多出一份会漂移的实现。
/// 这一层加的是两样依赖给不了的东西：**工具走的是注册表那一个入口**（与代理侧同一条），
/// 以及**失败按结构化错误回灌**。
/// </para>
/// <para>
/// 工具执行体不用 <c>context.Function</c> 直接调，而是回到注册表：那个函数把结果
/// 序列化成 JSON 才交出去，原始的失败结果出不来，而回环要的正是它。
/// </para>
/// </remarks>
public sealed class DiagramChatClient : IChatClient
{
    /// <summary>一轮对话里最多让模型往返几次。</summary>
    /// <remarks>
    /// 比依赖缺省的那 40 次紧得多。四十次是给"模型自己会收敛"的场景留的余量，
    /// 而这一层已经有回环那道上限在管同一个错误反复撞；两道都放到 40 的话，
    /// 一次参数错误能烧掉几十轮往返，账单上看得出来、日志里看不出来。
    /// </remarks>
    public const int DefaultMaxIterations = 8;

    private readonly ToolRegistry _registry;
    private readonly string? _instructions;
    private readonly FunctionInvokingChatClient _loop;

    /// <param name="inner">真正的模型客户端。</param>
    /// <param name="registry">工具表。工具调用走它那一个入口。</param>
    /// <param name="instructions">系统提示。每一轮都带上。</param>
    /// <param name="errors">回环状态。为空时新建一个。</param>
    /// <param name="maxIterations">一轮里最多往返几次。</param>
    public DiagramChatClient(
        IChatClient inner,
        ToolRegistry registry,
        string? instructions = null,
        ErrorLoop? errors = null,
        int maxIterations = DefaultMaxIterations)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxIterations, 1);

        _registry = registry;
        _instructions = instructions;
        Errors = errors ?? new ErrorLoop();

        _loop = new FunctionInvokingChatClient(inner)
        {
            MaximumIterationsPerRequest = maxIterations,
            FunctionInvoker = InvokeToolAsync,
        };
    }

    /// <summary>这一轮对话的回环状态。</summary>
    public ErrorLoop Errors { get; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        // 一次请求就是一轮。不在这里清的话，上一轮撞过的错误会让这一轮第一次失败
        // 就被标成「上一次这么改也不行」，而模型其实还没试过那一次。
        Errors.Reset();

        return _loop.GetResponseAsync(
            messages,
            ChatOptionsFactory.Create(_registry, options, _instructions),
            cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        Errors.Reset();

        return _loop.GetStreamingResponseAsync(
            messages,
            ChatOptionsFactory.Create(_registry, options, _instructions),
            cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        _loop.GetService(serviceType, serviceKey);

    public void Dispose() => _loop.Dispose();

    /// <summary>
    /// 一次工具调用：走注册表，成功交结果、失败交结构化错误。
    /// </summary>
    /// <remarks>
    /// 到回环上限时把这一轮停下。只少回灌一次内容而不停的话，循环仍由依赖那几十次兜底，
    /// 而「停下了」这句话就成了空话——模型会继续把同一个调用发下去。
    /// </remarks>
    private async ValueTask<object?> InvokeToolAsync(
        FunctionInvocationContext context,
        CancellationToken cancellationToken)
    {
        var arguments = ToolFunction.ToJson(context.Arguments);

        var result = await _registry
            .Invoke(context.Function.Name, arguments, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return JsonSerializer.SerializeToElement(result, ToolJsonContext.Default.ToolResult);
        }

        var batch = Errors.Feed(result);

        if (batch.Count > 0)
        {
            return JsonSerializer.SerializeToElement(
                [.. batch],
                ToolJsonContext.Default.ErrorEnvelopeArray);
        }

        context.Terminate = true;

        return JsonSerializer.SerializeToElement(
            ErrorEnvelope.Exhausted(Errors.Attempts),
            ToolJsonContext.Default.ErrorEnvelope);
    }
}
