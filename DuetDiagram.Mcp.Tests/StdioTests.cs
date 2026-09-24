using System.Text;
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

    /// <summary>
    /// 导出 SVG 真的走通了：服务端自己把文档排出来，再把绘制列表写成 SVG。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这一条核的是宿主那一侧的接线。工具层只留了一个口子——它不引渲染层，
    /// 渲染那一步由宿主喂进去。不接的话，这一层会回一句"这个宿主没有接上渲染层"，
    /// 而服务端其余七条工具一切正常，那种缺法在别处看不出来。
    /// </para>
    /// <para>
    /// 服务端是拉起来的真进程，所以这一条顺带验了原生绘图库在这个部署形态下能加载：
    /// 加进来的那一步依赖它，而它在别的宿主里能跑不代表在这里也能。
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Category", "McpStdio")]
    public async Task The_agent_gets_svg_text_out_of_the_export_tool()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        await Harness.CallSucceedsAsync(
            session.Client,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"a","label":"甲"}""",
            cancellationToken,
            Harness.At(0));

        var payload = await Harness.CallSucceedsAsync(
            session.Client,
            DiagramToolset.Export,
            """{"format":"svg"}""",
            cancellationToken);

        var data = payload.GetProperty("data");

        data.GetProperty("format").GetString().Should().Be("svg");

        var svg = data.GetProperty("text").GetString()!;

        svg.Should().StartWith("<?xml").And.Contain("<svg").And.EndWith("</svg>\n");
        svg.Should().Contain("甲", "节点标签要真的写进 SVG 里，而不是给一份空文件");
        svg.Should().Contain("viewBox", "坐标系要显式写出来，不然不同查看器里的尺寸不一样");

        data.GetProperty("dropped").GetArrayLength().Should().BeGreaterThan(0,
            "文字留成 <text> 而不是转成路径这件事要如实带上");
    }

    /// <summary>
    /// 导出 PNG 真的走通了：服务端自己把文档排出来，再光栅化成一张位图。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与 SVG 那条同一条理由：工具层只留了一个口子，渲染那一步由宿主喂进去。
    /// 不接的话这一层会回一句"这个宿主没有接上渲染层"，而服务端其余七条工具一切正常。
    /// </para>
    /// <para>
    /// **这一条还顺带验了光栅化是无头的。** 服务端是个真子进程，没有窗口平台；
    /// 依赖窗口的话，这里会直接失败，而界面上一切正常。
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Category", "McpStdio")]
    public async Task The_agent_gets_png_bytes_out_of_the_export_tool()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        await Harness.CallSucceedsAsync(
            session.Client,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"a","label":"甲"}""",
            cancellationToken,
            Harness.At(0));

        var payload = await Harness.CallSucceedsAsync(
            session.Client,
            DiagramToolset.Export,
            """{"format":"png"}""",
            cancellationToken);

        var data = payload.GetProperty("data");

        data.GetProperty("format").GetString().Should().Be("png");
        data.TryGetProperty("text", out _).Should().BeFalse(
            "位图那一档没有文本；给它一个空的 text 会让调用方以为这是一张空图");

        var bytes = Convert.FromBase64String(data.GetProperty("base64").GetString()!);

        bytes.Should().StartWith([0x89, 0x50, 0x4E, 0x47], "导出的要是一张真的 PNG，而不是一份空文件");
        bytes.Length.Should().BeGreaterThan(100, "一张真画了东西的位图不会这么小");

        data.GetProperty("dropped").GetArrayLength().Should().BeGreaterThan(0,
            "位图把一切都变成像素这件事要如实带上");
    }

    /// <summary>
    /// 代理从导出工具里拿到一份真的 PDF。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这一条盯的是"无头"那一件事：进程里没有窗口平台，PDF 照样写得出来。
    /// 依赖窗口的话，界面上一切正常而这条通路必失败。
    /// </para>
    /// <para>
    /// 判据落在文件自己身上：开头是 PDF 的标识，里面有页对象，而且字体嵌进去了——
    /// 最后这一条是汉字能不能画出来的分界，也是这一档与位图那一档的差别所在。
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Category", "McpStdio")]
    public async Task The_agent_gets_pdf_bytes_out_of_the_export_tool()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var session = await AgentSession.ConnectAsync(cancellationToken: cancellationToken);

        await Harness.CallSucceedsAsync(
            session.Client,
            DiagramToolset.Edit,
            """{"action":"add-node","id":"a","label":"甲"}""",
            cancellationToken,
            Harness.At(0));

        var payload = await Harness.CallSucceedsAsync(
            session.Client,
            DiagramToolset.Export,
            """{"format":"pdf"}""",
            cancellationToken);

        var data = payload.GetProperty("data");

        data.GetProperty("format").GetString().Should().Be("pdf");
        data.TryGetProperty("text", out _).Should().BeFalse(
            "PDF 是二进制；给它一个空的 text 会让调用方以为这是一份空文件");

        var bytes = Convert.FromBase64String(data.GetProperty("base64").GetString()!);
        var file = Encoding.Latin1.GetString(bytes);

        bytes.Should().StartWith("%PDF"u8.ToArray(), "导出的要是一份真的 PDF，而不是一份空文件");
        file.Should().Contain("/Type /Page");
        file.Should().Contain("/FontFile", "字体没嵌进去，收件人机器上没有那份字体");
        file.Should().Contain("/Type0", "汉字要按 CID 型字体嵌，否则到了别处是方块");

        data.GetProperty("dropped").GetArrayLength().Should().BeGreaterThan(0,
            "字体整份嵌入的代价要如实带上");
    }
}
