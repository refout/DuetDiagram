using System.Text.Json;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace DuetDiagram.Llm.Tools;

/// <summary>
/// 工具表：八个粗粒度工具在这里登记，模型侧与代理侧都从这一份取。
/// </summary>
/// <remarks>
/// <para>
/// 注册表本身不碰文档。所有真正的变更走命令总线，注册表只做参数校验与分发。
/// 绕开总线的话，撤销栈、版本号、审计日志与变更广播全都不经过，
/// 而这几样正是"人和模型能力对等"所依赖的东西。
/// </para>
/// <para>
/// 名称唯一是硬约束。重名时直接拒绝而不是覆盖：覆盖会让先注册的那个工具静默消失，
/// 而调用方手上那份声明仍然指着它，表现是"工具明明在表里却调不通"。
/// </para>
/// </remarks>
public sealed class ToolRegistry
{
    /// <summary>幂等表最多记多少条。到顶之后最旧的被挤掉。</summary>
    private const int IdempotencyCapacity = 128;

    private readonly List<ToolDescriptor> _tools = [];
    private readonly Dictionary<string, ToolDescriptor> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ToolResult> _replayed = new(StringComparer.Ordinal);
    private readonly Queue<string> _replayOrder = new();

    /// <summary>已登记的工具，按登记次序。</summary>
    public IReadOnlyList<ToolDescriptor> Tools => _tools;

    /// <summary>登记一个工具。名字已被占用时抛异常。</summary>
    public void Register(ToolDescriptor tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (_byName.ContainsKey(tool.Name))
        {
            throw new InvalidOperationException($"工具名 {tool.Name} 已经被占用。工具名是两侧共用的标识，不能有两个。");
        }

        _byName.Add(tool.Name, tool);
        _tools.Add(tool);
    }

    /// <summary>按名字取一个工具。没登记过时返回空。</summary>
    public ToolDescriptor? Find(string name) =>
        name is not null && _byName.TryGetValue(name, out var tool) ? tool : null;

    /// <summary>
    /// 按名字调用一个工具，参数以 JSON 形式给出。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是工具层的统一入口：参数先按 schema 校验，通过了才进执行体。
    /// 名字不认识时返回结构化错误而不是抛异常——名字可能来自协议对端，
    /// 那是外部输入，不是内部调用方的笔误。
    /// </para>
    /// <para>
    /// <paramref name="idempotencyKey"/> 是**调用侧的属性**，所以不在参数表里：
    /// 放进去的话模型每次都要想一个键，而它根本没有"重试"这个概念。
    /// 代理侧拿协议请求号填，同一个键重复调用直接返回第一次的结果，
    /// 不再施加一次变更——一次网络抖动会变成两次编辑，靠的就是这里挡住。
    /// </para>
    /// </remarks>
    public async Task<ToolResult> Invoke(
        string name,
        JsonElement arguments,
        CancellationToken cancellationToken = default,
        string? idempotencyKey = null)
    {
        if (Find(name) is not { } tool)
        {
            return ToolResult.Fail(ToolError.Of(
                ToolErrorCodes.UnknownTool,
                $"{name} 不是一个已登记的工具",
                null,
                Known()));
        }

        if (string.IsNullOrEmpty(idempotencyKey))
        {
            return await tool.Handler(arguments, cancellationToken).ConfigureAwait(false);
        }

        var key = $"{name}\u0000{idempotencyKey}";

        if (_replayed.TryGetValue(key, out var replay))
        {
            return replay;
        }

        var result = await tool.Handler(arguments, cancellationToken).ConfigureAwait(false);

        Remember(key, result);

        return result;
    }

    /// <summary>
    /// 记下一次调用的结果，超出容量时挤掉最旧的那条。
    /// </summary>
    /// <remarks>
    /// 表是有界的：不设上限的话，一个长跑的服务端会被调用历史撑爆，
    /// 而那些键再也没有第二次机会被撞上。挤掉的顺序按写入先后，
    /// 因为键的存活时间与它被写进来的时间同向。
    /// </remarks>
    private void Remember(string key, ToolResult result)
    {
        if (_replayed.Count >= IdempotencyCapacity && _replayOrder.Count > 0)
        {
            _replayed.Remove(_replayOrder.Dequeue());
        }

        if (_replayed.TryAdd(key, result))
        {
            _replayOrder.Enqueue(key);
        }
    }

    /// <summary>模型侧要的那一份。</summary>
    public IReadOnlyList<AIFunction> ToMeaiFunctions() => [.. _tools.Select(tool => tool.Function)];

    /// <summary>
    /// 代理侧要的那一份。
    /// </summary>
    /// <remarks>
    /// 由模型侧的函数派生，不是另写一份声明。两者指向同一个对象，
    /// 所以名称、描述与参数 schema 逐字相同这件事由结构保证，不靠测试兜。
    /// </remarks>
    public IReadOnlyList<McpServerTool> ToMcpTools() =>
        [.. _tools.Select(tool => McpServerTool.Create(tool.Function))];

    /// <summary>
    /// 建一份装了内置八个工具的注册表。
    /// </summary>
    /// <remarks>
    /// 上下文是必需的，没有"无文档的注册表"这种东西：八个工具全都作用在某一份文档上，
    /// 允许建一个不带文档的注册表只会让调用方在真的调用时才拿到一个空引用异常。
    /// </remarks>
    public static ToolRegistry CreateDefault(DiagramToolContext context)
    {
        var registry = new ToolRegistry();

        foreach (var tool in DiagramToolset.Create(context))
        {
            registry.Register(tool);
        }

        return registry;
    }

    /// <summary>已登记的工具名，用来填"名字不认识"那条错误。</summary>
    private string Known() => $"已登记的工具：{string.Join('、', _tools.Select(tool => tool.Name))}";
}
