using System.Text.Json;
using DuetDiagram.Llm.Tools;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Mcp.Tests;

/// <summary>
/// 标准输入输出这条传输：服务端作为子进程拉起来，工具发现与工具调用都真的走一遍。
/// </summary>
/// <remarks>
/// 这一组刻意起真进程，不在内存里对拍。内存流对拍验不到两件事：日志有没有跑到标准输出上，
/// 以及服务端重启之后调用还能不能成。前者的症状是对端解析失败，后者的症状是
/// 「换个实例就不认了」——两样都只在真进程里看得出来。
/// </remarks>
public sealed class StdioTests
{
    [Fact]
    [Trait("Category", "McpStdio")]
    public async Task The_agent_discovers_the_eight_tools()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        var tools = await session.Client.ListToolsAsync(cancellationToken: cancellationToken);

        tools.Select(tool => tool.Name).Should().BeEquivalentTo(
            [
                DiagramToolset.Read,
                DiagramToolset.Edit,
                DiagramToolset.Style,
                DiagramToolset.Layout,
                DiagramToolset.Composite,
                DiagramToolset.Export,
                DiagramToolset.Validate,
                DiagramToolset.UndoRedo,
            ]);
    }

    [Fact]
    [Trait("Category", "McpStdio")]
    public async Task A_write_reaches_the_document_and_comes_back_in_the_summary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        // 空图停在第 0 版，所以这一次声明第 0 版。
        var written = await Harness.CallSucceedsAsync(
            session.Client,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"a","label":"甲"}""",
            cancellationToken,
            Harness.At(0));

        var outcome = written.GetProperty("data");

        outcome.GetProperty("structuralChanged").GetBoolean().Should().BeTrue();
        outcome.GetProperty("version").GetInt32().Should().Be(1);

        var summary = await Harness.CallSucceedsAsync(
            session.Client,
            DiagramToolset.Read,
            "{}",
            cancellationToken);

        summary.GetProperty("data").GetProperty("summary").GetProperty("version").GetInt32().Should().Be(1);
        summary.GetProperty("data").GetProperty("text").GetString().Should().Contain("甲");
    }

    [Fact]
    [Trait("Category", "McpStdio")]
    public async Task The_same_call_works_against_a_restarted_server()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workspace = Harness.NewWorkspace();
        var document = Harness.WriteDocument(workspace, "restart-doc");

        try
        {
            await using (var first = await AgentSession.ConnectAsync(document, cancellationToken: cancellationToken))
            {
                await Harness.CallSucceedsAsync(
                    first.Client,
                    DiagramToolset.Edit,
                    """{"action":"add-node","id":"a"}""",
                    cancellationToken,
                    Harness.At(0, "restart-doc"));
            }

            // 服务端不保存会话上下文，文档也没有写回文件，所以新实例看到的还是第 0 版。
            // 同一个自包含的调用在它上面照样成立——这正是「换个实例也认」要验的事。
            await using var second = await AgentSession.ConnectAsync(document, cancellationToken: cancellationToken);

            var written = await Harness.CallSucceedsAsync(
                second.Client,
                DiagramToolset.Edit,
                """{"action":"add-node","id":"a"}""",
                cancellationToken,
                Harness.At(0, "restart-doc"));

            written.GetProperty("data").GetProperty("version").GetInt32().Should().Be(1);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "McpStdio")]
    public async Task The_server_logs_its_requests_to_standard_error()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        await session.Client.ListToolsAsync(cancellationToken: cancellationToken);

        await Harness.CallSucceedsAsync(session.Client, DiagramToolset.Read, "{}", cancellationToken);

        // 日志能出现在标准错误上，就说明它没有走标准输出——走了的话，
        // 对端在解析协议消息时就会失败，而这里连一个请求都跑不完。
        var line = await session.Log.WaitAsync(
            text => text.Contains("tools/call", StringComparison.Ordinal),
            cancellationToken);

        line.Should().Contain("未声明会话状态");
    }

    [Fact]
    [Trait("Category", "McpStdio")]
    public async Task A_failed_call_comes_back_as_a_structured_error()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        var payload = await Harness.CallAsync(
            session.Client,
            DiagramToolset.Edit,
            """{"action":"remove-node","id":"ghost"}""",
            cancellationToken,
            Harness.At(0));

        payload.GetProperty("isSuccess").GetBoolean().Should().BeFalse();
        payload.GetProperty("errors")[0].GetProperty("code").GetString()
            .Should().Be("NODE_MISSING");
    }

    [Fact]
    [Trait("Category", "McpStdio")]
    public async Task A_document_file_supplies_the_document_the_server_edits()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var workspace = Harness.NewWorkspace();
        var document = Harness.WriteDocument(workspace, "file-doc");

        try
        {
            await using var session = await AgentSession.ConnectAsync(document, cancellationToken: cancellationToken);

            var payload = await Harness.CallSucceedsAsync(
                session.Client,
                DiagramToolset.Read,
                "{}",
                cancellationToken);

            payload.GetProperty("data").GetProperty("summary").GetProperty("documentId").GetString()
                .Should().Be("file-doc");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>返回的那段文本确实是一份 JSON，而不是被包成了别的形状。</summary>
    [Fact]
    [Trait("Category", "McpStdio")]
    public async Task The_result_text_is_the_tool_result_as_json()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        var result = await session.Client.CallToolAsync(
            DiagramToolset.Read,
            new Dictionary<string, object?>(),
            cancellationToken: cancellationToken);

        result.IsError.Should().NotBe(true);

        var text = result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Single().Text;

        var parsed = JsonDocument.Parse(text);

        parsed.RootElement.TryGetProperty("isSuccess", out var success).Should().BeTrue();
        success.GetBoolean().Should().BeTrue();
    }
}
