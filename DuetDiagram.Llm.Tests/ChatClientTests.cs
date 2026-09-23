using System.Text.Json;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Diagnostics;
using DuetDiagram.Llm.Chat;
using DuetDiagram.Llm.Loop;
using DuetDiagram.Llm.Tools;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DuetDiagram.Llm.Tests;

/// <summary>
/// 模型那条通路的往返：工具调用走通，失败按结构化错误回灌。
/// </summary>
/// <remarks>
/// <para>
/// 用脚本化的假客户端把往返序列固定下来，不打真模型：打真模型的话，
/// 同一个用例每次结果不同，而失败时看不出是工具的问题还是模型那天心情不好。
/// </para>
/// <para>
/// 这一层验的是**往返的形状**：工具真的被执行了、执行结果真的回到了模型手里、
/// 失败时回灌的是结构化错误而不是一句笼统的话。
/// </para>
/// </remarks>
public sealed class ChatClientTests
{
    /// <summary>一个命令层会拒掉的调用：要删的节点不存在。</summary>
    private const string MissingNode = """{"action":"remove-node","id":"ghost"}""";

    #region 往返

    /// <summary>工具调用真的改了文档，结果也真的回到了模型手里。</summary>
    /// <remarks>
    /// 只断言最终那句话的话，一个把工具调用丢掉的实现也能全绿——模型照着自己的脚本
    /// 说「加好了」，而文档里什么都没有。所以要同时看文档与第二次请求里的工具结果。
    /// </remarks>
    [Fact]
    [Trait("Category", "ChatClient")]
    public async Task A_tool_call_reaches_the_document_and_comes_back()
    {
        var context = Harness.Context();
        var registry = ToolRegistry.CreateDefault(context);
        var inner = new ScriptedChatClient(
            ScriptedChatClient.Calls(DiagramToolset.Edit, """{"action":"add-node","id":"a","label":"甲"}"""),
            ScriptedChatClient.Says("加好了"));

        var client = new DiagramChatClient(inner, registry);

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "加一个节点")],
            cancellationToken: TestContext.Current.CancellationToken);

        response.Text.Should().Be("加好了");
        context.Document.Nodes.Should().ContainSingle(node => node.Id == "a");

        inner.Requests.Should().HaveCount(2);

        var payload = ToolPayloads(inner, 1).Should().ContainSingle().Which;

        payload.GetProperty("isSuccess").GetBoolean().Should().BeTrue();
        payload.GetProperty("data").GetProperty("structuralChanged").GetBoolean()
            .Should().BeTrue("加一个节点是结构变更，宿主据此决定要不要重排");
    }

    /// <summary>每一轮请求都把八个工具带上。</summary>
    /// <remarks>
    /// 工具表在注册表里，而模型要看见它们还得有人把那一份放进请求。漏掉的话，
    /// 模型永远发不出工具调用，而表现是「模型答非所问」——查起来要绕一大圈。
    /// </remarks>
    [Fact]
    [Trait("Category", "ChatClient")]
    public async Task Every_request_advertises_the_eight_tools()
    {
        var registry = Harness.Registry();
        var inner = new ScriptedChatClient(ScriptedChatClient.Says("好"));

        var client = new DiagramChatClient(inner, registry);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "看看图")],
            cancellationToken: TestContext.Current.CancellationToken);

        inner.Requests.Should().ContainSingle();
        inner.Requests[0].Options!.Tools!.Select(tool => tool.Name)
            .Should().Equal(registry.Tools.Select(tool => tool.Name));
    }

    /// <summary>调用方自己给的那一份选项不会被就地改掉。</summary>
    /// <remarks>
    /// 复用同一个选项对象是常见的写法。就地填工具的话，第二次请求会带上第一次的痕迹，
    /// 而那种错只在"同一份选项被用了两次"时出现。
    /// </remarks>
    [Fact]
    [Trait("Category", "ChatClient")]
    public async Task The_callers_options_are_not_modified_in_place()
    {
        var registry = Harness.Registry();
        var inner = new ScriptedChatClient(ScriptedChatClient.Says("好"));
        var mine = new ChatOptions { Instructions = "你自己写的" };

        var client = new DiagramChatClient(inner, registry, "这里的不该盖掉它");

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "看看图")],
            mine,
            TestContext.Current.CancellationToken);

        mine.Tools.Should().BeNull("给出去的那一份是调用方的东西，不该被就地填上工具");
        mine.Instructions.Should().Be("你自己写的");

        var sent = inner.Requests[0].Options!;

        sent.Instructions.Should().Be("你自己写的");
        sent.Tools.Should().HaveCount(8);
    }

    /// <summary>这一轮匹配上的 Skill 正文接在系统提示后面，往返的每一轮都带上。</summary>
    /// <remarks>
    /// 只第一轮带是不够的：工具调用往返之后模型还在这一轮里，而那一轮里它最容易忘掉顺序。
    /// 另外，正文是**接在**系统提示后面而不是替掉它——两者说的不是一回事。
    /// </remarks>
    [Fact]
    [Trait("Category", "ChatClient")]
    public async Task The_matched_skill_rides_along_with_the_system_prompt()
    {
        const string Skill = "这一轮要按顺序改图。";

        var registry = Harness.Registry();
        var inner = new ScriptedChatClient(
            ScriptedChatClient.Calls(DiagramToolset.Edit, """{"action":"add-node","id":"a"}"""),
            ScriptedChatClient.Says("加好了"));

        var client = new DiagramChatClient(inner, registry, "基础提示", skill: Skill);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "加一个节点")],
            cancellationToken: TestContext.Current.CancellationToken);

        inner.Requests.Should().HaveCount(2);

        foreach (var request in inner.Requests)
        {
            request.Options!.Instructions.Should().StartWith("基础提示");
            request.Options.Instructions.Should().EndWith(Skill);
        }
    }

    #endregion

    #region 失败回灌

    /// <summary>命令被拒时，回灌的是结构化错误：码、出错参数、一句可执行的建议。</summary>
    [Fact]
    [Trait("Category", "ChatClient")]
    public async Task A_rejected_call_comes_back_as_a_structured_error()
    {
        var registry = Harness.Registry();
        var inner = new ScriptedChatClient(
            ScriptedChatClient.Calls(DiagramToolset.Edit, MissingNode),
            ScriptedChatClient.Says("知道了"));

        var client = new DiagramChatClient(inner, registry);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "删掉 ghost")],
            cancellationToken: TestContext.Current.CancellationToken);

        var envelope = ToolPayloads(inner, 1).Should().ContainSingle().Which;

        envelope.GetProperty("code").GetString().Should().Be(ErrorCodes.NodeMissing);
        envelope.GetProperty("parameter").GetString().Should().Be("id");
        envelope.GetProperty("suggestion").GetString().Should().NotBeEmpty();
    }

    /// <summary>同一个错误连着来第二次时，回灌里多一句「上一次这么改也不行」。</summary>
    [Fact]
    [Trait("Category", "ChatClient")]
    public async Task The_same_error_twice_carries_the_warning()
    {
        var registry = Harness.Registry();
        var inner = new ScriptedChatClient(
            ScriptedChatClient.Calls(DiagramToolset.Edit, MissingNode, "call-1"),
            ScriptedChatClient.Calls(DiagramToolset.Edit, MissingNode, "call-2"),
            ScriptedChatClient.Says("好吧"));

        var client = new DiagramChatClient(inner, registry);

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "删掉 ghost")],
            cancellationToken: TestContext.Current.CancellationToken);

        ToolPayloads(inner, 1).Single().GetProperty("repeated").GetBoolean().Should().BeFalse();
        ToolPayloads(inner, 2).Single().GetProperty("repeated").GetBoolean().Should().BeTrue();
        ToolPayloads(inner, 2).Single().GetProperty("note").GetString()
            .Should().Contain("上一次这么改也不行");
    }

    /// <summary>到回环上限就停下，并把整段往返记录写进应用日志。</summary>
    /// <remarks>
    /// 判据是**第三次请求根本没有发生**：只少回灌一次内容而不停的话，
    /// 循环仍由依赖那几十次兜底，而「停下了」这句话就成了空话。
    /// 那一条「别再试了」的信封只在最终响应里看得到——它后面没有下一次请求可以承载它。
    /// </remarks>
    [Fact]
    [Trait("Category", "ChatClient")]
    public async Task The_client_stops_when_the_round_is_exhausted()
    {
        var registry = Harness.Registry();
        var diagnostics = new CollectingDiagnosticsSink();
        var inner = new ScriptedChatClient(
            ScriptedChatClient.Calls(DiagramToolset.Edit, MissingNode, "call-1"),
            ScriptedChatClient.Calls(DiagramToolset.Edit, MissingNode, "call-2"),
            ScriptedChatClient.Says("这一句不该被用到"));

        var client = new DiagramChatClient(inner, registry, errors: new ErrorLoop(maxAttempts: 1, diagnostics));

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "删掉 ghost")],
            cancellationToken: TestContext.Current.CancellationToken);

        inner.Requests.Should().HaveCount(2, "上限用完就该停下，不该再问第三次");
        inner.Remaining.Should().Be(1, "脚本里最后那一句不该被用到");

        client.Errors.Exhausted.Should().BeTrue();

        var last = Payloads(response.Messages).Should().ContainSingle().Which;

        last.GetProperty("code").GetString().Should().Be(ToolErrorCodes.RetryExhausted);
        last.GetProperty("attempt").GetInt32().Should().Be(2);

        var record = diagnostics.Warnings.Should().ContainSingle().Which;

        record.Should().Contain("NODE_MISSING");
        record.Should().Contain("第 2 次");
    }

    /// <summary>换一轮之后回环重新开始。</summary>
    /// <remarks>
    /// 不清的话，上一轮撞过的错误会让这一轮第一次失败就被标成「上一次这么改也不行」，
    /// 而模型其实还没试过那一次。
    /// </remarks>
    [Fact]
    [Trait("Category", "ChatClient")]
    public async Task The_next_request_starts_a_new_round()
    {
        var registry = Harness.Registry();
        var inner = new ScriptedChatClient(
            ScriptedChatClient.Calls(DiagramToolset.Edit, MissingNode, "call-1"),
            ScriptedChatClient.Says("一"),
            ScriptedChatClient.Calls(DiagramToolset.Edit, MissingNode, "call-2"),
            ScriptedChatClient.Says("二"));

        var client = new DiagramChatClient(inner, registry);
        var messages = new ChatMessage[] { new(ChatRole.User, "删掉 ghost") };

        await client.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);
        await client.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        ToolPayloads(inner, 1).Single().GetProperty("repeated").GetBoolean().Should().BeFalse();
        ToolPayloads(inner, 3).Single().GetProperty("repeated").GetBoolean()
            .Should().BeFalse("这是新的一轮，上一轮撞过什么不算数");
    }

    /// <summary>成功的那一次不进回环，次数也不算掉。</summary>
    [Fact]
    [Trait("Category", "ChatClient")]
    public async Task A_successful_call_does_not_spend_the_budget()
    {
        var registry = Harness.Registry();
        var inner = new ScriptedChatClient(
            ScriptedChatClient.Calls(DiagramToolset.Edit, """{"action":"add-node","id":"a"}"""),
            ScriptedChatClient.Says("加好了"));

        var client = new DiagramChatClient(inner, registry, errors: new ErrorLoop(maxAttempts: 1));

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "加一个节点")],
            cancellationToken: TestContext.Current.CancellationToken);

        client.Errors.Attempts.Should().Be(0);
        client.Errors.Exhausted.Should().BeFalse();
    }

    #endregion

    #region 取数

    /// <summary>
    /// 某一次请求里带过去的工具结果。
    /// </summary>
    /// <remarks>
    /// 工具结果是跟着**下一次**请求回到模型手里的，所以按请求号取，
    /// 而不是按调用号取：第 n 次调用的结果出现在第 n+1 次请求里。
    /// </remarks>
    private static JsonElement[] ToolPayloads(ScriptedChatClient inner, int requestIndex)
    {
        inner.Requests.Should().HaveCountGreaterThan(requestIndex, "这一次请求应当已经发生过");

        return Payloads(inner.Requests[requestIndex].Messages);
    }

    /// <summary>
    /// 这些消息里最后那一个工具结果。
    /// </summary>
    /// <remarks>
    /// 摊平成数组：成功那一路回的是一个结果对象，失败那一路回的是一批信封。
    /// 两条路都按同一段代码读，用例里就不用各写一份。
    /// 取最后一个而不是全部：一轮里可能调了好几次工具，而要看的是最后那一次。
    /// </remarks>
    private static JsonElement[] Payloads(IEnumerable<ChatMessage> messages)
    {
        var result = messages
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .LastOrDefault();

        result.Should().NotBeNull("这些消息里应当带着工具结果");

        var payload = result!.Result.Should().BeOfType<JsonElement>().Subject;

        return payload.ValueKind == JsonValueKind.Array ? [.. payload.EnumerateArray()] : [payload];
    }

    #endregion
}
