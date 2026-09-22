using System.Text.Json;
using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Time;
using DuetDiagram.Llm.Context;
using DuetDiagram.Llm.Tools;
using FluentAssertions;

namespace DuetDiagram.Llm.Tests;

/// <summary>
/// 测试用的固定环境：一份文档、一条总线、一个可控时钟。
/// </summary>
/// <remarks>
/// <para>
/// 凡是需要"一份能被工具读写的文档"的用例都从这里拿环境，避免各自搭一套——
/// 各搭一套之后，某一份忘了配时钟，表现是摘要里的相对时间变成"刚刚"或一串天数，
/// 而那是环境的问题，不是被测代码的问题。
/// </para>
/// <para>
/// 时钟是手动推进的：摘要里的相对时间要能写成精确断言，而不是一个范围判断。
/// </para>
/// </remarks>
internal static class Harness
{
    /// <summary>固定时刻。所有需要"现在"的地方都用它。</summary>
    public static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    /// <summary>给一份文档配一条总线。</summary>
    public static DiagramCommandBus Bus(
        DiagramDocument document,
        ITimeProvider? clock = null,
        ISessionProvider? session = null) =>
        new(DiagramCommandBusContext.Create(
            document,
            session ?? new SimpleSessionProvider("tester", SessionIds.Llm("c1")),
            NullChangeBroadcaster.Instance,
            DiagramCommandBusOptions.ForGui(),
            clock ?? new ManualTimeProvider(Now)));

    /// <summary>
    /// 一个可改的会话提供者。
    /// </summary>
    /// <remarks>
    /// 撤销重做那一路要区分「这一步是人改的还是模型改的」，而那个判据是历史条目上的会话标识。
    /// 造这种历史要在两次命令之间换一个会话，所以会话提供者必须是可写的、而且测试要拿着它。
    /// </remarks>
    public static SimpleSessionProvider Session(string? sessionId = null) =>
        new("tester", sessionId ?? SessionIds.Llm("c1"));

    /// <summary>一份工具上下文。不给文档时造一张空图。</summary>
    public static DiagramToolContext Context(
        DiagramDocument? document = null,
        IReadOnlyList<NodeRank>? placement = null,
        IReadOnlyList<string>? pinned = null,
        ManualTimeProvider? clock = null,
        ISessionProvider? session = null)
    {
        var subject = document ?? new DiagramDocument("tool-doc");
        var time = clock ?? new ManualTimeProvider(Now);

        return new DiagramToolContext
        {
            Bus = Bus(subject, time, session),
            Placement = placement ?? [],
            PinnedNodes = pinned ?? [],
            Clock = time,
        };
    }

    /// <summary>一份装了内置八个工具的注册表。</summary>
    public static ToolRegistry Registry(
        DiagramDocument? document = null,
        IReadOnlyList<NodeRank>? placement = null,
        IReadOnlyList<string>? pinned = null,
        ManualTimeProvider? clock = null,
        ISessionProvider? session = null) =>
        ToolRegistry.CreateDefault(Context(document, placement, pinned, clock, session));

    /// <summary>按 JSON 参数调一个工具。</summary>
    public static ToolResult Invoke(
        ToolRegistry registry,
        string tool,
        string arguments,
        string? idempotencyKey = null) =>
        registry
            .Invoke(tool, JsonDocument.Parse(arguments).RootElement, default, idempotencyKey)
            .GetAwaiter()
            .GetResult();

    /// <summary>调 <c>diagram_edit</c>。</summary>
    public static ToolResult Edit(ToolRegistry registry, string arguments, string? idempotencyKey = null) =>
        Invoke(registry, DiagramToolset.Edit, arguments, idempotencyKey);

    /// <summary>调 <c>diagram_style</c>。</summary>
    public static ToolResult Style(ToolRegistry registry, string arguments, string? idempotencyKey = null) =>
        Invoke(registry, DiagramToolset.Style, arguments, idempotencyKey);

    /// <summary>从测试程序集的位置逐级上溯，找到含解决方案文件的目录。</summary>
    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DuetDiagram.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("从测试程序集的位置找不到仓库根。");
    }

    /// <summary>某个工具的第一个错误码。</summary>
    public static string CodeOf(ToolResult result)
    {
        result.Errors.Should().NotBeEmpty("这次调用本意是让它失败，而它没有给出任何错误");

        return result.Errors[0].Code;
    }
}
