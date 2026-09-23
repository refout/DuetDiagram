using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Llm.Tools;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Llm.Tests;

/// <summary>
/// 导出：Mermaid 的文本与丢失清单、还没接上的那几种格式、以及它不改文档。
/// </summary>
public sealed class ExportToolTests
{
    #region Mermaid

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Mermaid_export_returns_the_text()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"start","label":"开始"}""");
        Harness.Edit(registry, """{"action":"add-node","id":"stop","label":"结束"}""");
        Harness.Edit(registry, """{"action":"connect-edge","id":"e1","from":"start","to":"stop"}""");

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"mermaid"}""");

        result.IsSuccess.Should().BeTrue();

        var text = result.Data!.Value.GetProperty("text").GetString()!;

        text.Should().Contain("start").And.Contain("stop");
        result.Data!.Value.GetProperty("format").GetString().Should().Be("mermaid");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Two_exports_of_the_same_document_are_byte_identical()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"b"}""");
        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");

        var first = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"mermaid"}""");
        var second = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"mermaid"}""");

        second.Data!.Value.GetRawText().Should().Be(first.Data!.Value.GetRawText());
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void What_the_format_cannot_express_is_reported()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        Harness.Edit(registry, """{"action":"add-node","id":"b"}""");
        Harness.Invoke(
            registry,
            DiagramToolset.Layout,
            """{"action":"add-constraint","kind":"same-rank","memberIds":["a","b"]}""");

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"mermaid"}""");

        result.Data!.Value.GetProperty("dropped").GetArrayLength().Should().BeGreaterThan(0,
            "静默丢失会让模型以为导出的文本就是全部内容，而 IR 的表达力严格强于 Mermaid");
        result.Message.Should().Contain("写不进去");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Export_does_not_touch_the_version_or_the_history()
    {
        var document = new DiagramDocument("doc");
        var context = Harness.Context(document);
        var registry = ToolRegistry.CreateDefault(context);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");

        var version = document.Version;
        var history = context.Bus.Context.History.UndoCount;

        Harness.Invoke(registry, DiagramToolset.Export, """{"format":"mermaid"}""").IsSuccess.Should().BeTrue();

        document.Version.Should().Be(version, "导出是只读的，不得触发版本号变化");
        context.Bus.Context.History.UndoCount.Should().Be(history, "导出不进撤销栈");
    }

    #endregion

    #region 还没接上的那几种

    [Theory]
    [InlineData("dsl")]
    [InlineData("svg")]
    [InlineData("png")]
    [InlineData("pdf")]
    [Trait("Category", "ExportTool")]
    public void Formats_without_an_implementation_say_so(string format)
    {
        var registry = Harness.Registry(new DiagramDocument("doc"));

        var result = Harness.Invoke(registry, DiagramToolset.Export, $$"""{"format":"{{format}}"}""");

        result.IsSuccess.Should().BeFalse();
        Harness.CodeOf(result).Should().Be(ToolErrorCodes.NotSupported);
        result.Errors[0].Parameter.Should().Be("format");
        result.Errors[0].Message.Should().Contain(format);
        result.Errors[0].Expected.Should().Contain("mermaid", "要说清现在能用的是哪一种");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void An_unknown_format_lists_the_available_ones()
    {
        var registry = Harness.Registry(new DiagramDocument("doc"));

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"excalidraw"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Parameter.Should().Be("format");
        result.Errors[0].Expected.Should().Contain("mermaid").And.Contain("svg");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Exporting_a_page_that_is_not_there_is_refused()
    {
        var registry = Harness.Registry(new DiagramDocument("doc"));

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"mermaid","pageId":"p1"}""");

        // 点名的页面不在文档里要如实说，而不是按整份文档导出——那会让调用方
        // 以为它导出的是某一页。
        Harness.CodeOf(result).Should().Be(ErrorCodes.PageMissing);
        result.Errors[0].Parameter.Should().Be("pageId");
        result.Errors[0].Expected.Should().NotBeNullOrEmpty("要说清现有的页面有哪些");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Exporting_a_page_leaves_the_other_page_out()
    {
        // 两页各放一个节点。导出第二页时只该有第二页上的那个。
        var document = DiagramDocument.CreateFromContent(
            "doc",
            DiagramKind.Flowchart,
            Direction.TB,
            pages: [new PageDef { Id = "p1", Order = 0 }, new PageDef { Id = "p2", Order = 1 }],
            nodes:
            [
                new NodeDef { Id = "first", Label = "第一页上的", Page = "p1" },
                new NodeDef { Id = "second", Label = "第二页上的", Page = "p2" },
            ]);

        var result = Harness.Invoke(
            Harness.Registry(document),
            DiagramToolset.Export,
            """{"format":"mermaid","pageId":"p2"}""");

        result.IsSuccess.Should().BeTrue();

        var text = result.Data!.Value.GetProperty("text").GetString();

        text.Should().Contain("second");
        text.Should().NotContain("first", "别的页面上的节点不该出现在这一页的导出里");
    }

    #endregion
}
