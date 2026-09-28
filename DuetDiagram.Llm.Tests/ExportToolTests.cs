using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Llm.Tools;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Llm.Tests;

/// <summary>
/// 导出：DSL、SVG、PNG 与 PDF 的载荷与丢失清单，以及它不改文档。
/// </summary>
public sealed class ExportToolTests
{
    #region SVG

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Svg_export_returns_the_text_and_the_dropped_list()
    {
        var registry = Harness.Registry(
            new DiagramDocument("doc"),
            svgExporter: (_, _) => new SvgExport("<svg/>", [new DroppedFeature("文字", [], "留成 <text>")]));

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"svg"}""");

        result.IsSuccess.Should().BeTrue();
        result.Data!.Value.GetProperty("format").GetString().Should().Be("svg");
        result.Data!.Value.GetProperty("text").GetString().Should().Be("<svg/>");
        result.Data!.Value.GetProperty("dropped").GetArrayLength().Should().Be(1,
            "静默丢失会让模型以为导出的文件就是全部内容");
        result.Message.Should().Contain("没按原样写出");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Svg_export_hands_the_page_to_the_renderer()
    {
        // 按页过滤那一套口径在 Core 里只有一份，而且布局与绘制列表构建都要用它，
        // 所以工具层不先过滤一遍——它只把页面标识原样交下去。先过滤一次的话，
        // 导出看到的这一页与画布看到的这一页会各按一套算，而对不上时两边都不报错。
        string? seen = null;

        var document = DiagramDocument.CreateFromContent(
            "doc",
            DiagramKind.Flowchart,
            Direction.TB,
            pages: [new PageDef { Id = "p1", Order = 0 }, new PageDef { Id = "p2", Order = 1 }],
            nodes: [new NodeDef { Id = "second", Label = "第二页上的", Page = "p2" }]);

        var registry = Harness.Registry(
            document,
            svgExporter: (_, pageId) =>
            {
                seen = pageId;

                return new SvgExport("<svg/>", []);
            });

        Harness.Invoke(registry, DiagramToolset.Export, """{"format":"svg","pageId":"p2"}""")
            .IsSuccess.Should().BeTrue();

        seen.Should().Be("p2");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Svg_export_says_so_when_the_host_has_no_render_layer()
    {
        // 工具层不引渲染层，所以渲染那一步只能由宿主喂进来。宿主没接上时要说清是宿主的事，
        // 不能给一份空文件——调用方会把空文件当成"这张图就是空的"。
        var registry = Harness.Registry(new DiagramDocument("doc"));

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"svg"}""");

        result.IsSuccess.Should().BeFalse();
        Harness.CodeOf(result).Should().Be(ToolErrorCodes.NotSupported);
        result.Errors[0].Parameter.Should().Be("format");
        result.Errors[0].Message.Should().Contain("宿主");
        result.Errors[0].Message.Should().NotContain("还没排到", "这是宿主没接上，不是排期问题");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Svg_export_says_so_when_the_renderer_gives_nothing()
    {
        // 渲染层拿到了文档却排不出结果（例如布局彻底失败）。失败的原因归宿主，
        // 工具层看不见布局引擎的异常类型，所以它只能说"排不出来"并指向校验那一条。
        var registry = Harness.Registry(new DiagramDocument("doc"), svgExporter: (_, _) => null);

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"svg"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.NotSupported);
        result.Errors[0].Message.Should().Contain("排不出结果");
        result.Errors[0].Expected.Should().Contain("diagram_validate", "要说清下一步该去查什么");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Two_svg_exports_of_the_same_document_are_byte_identical()
    {
        var registry = Harness.Registry(
            new DiagramDocument("doc"),
            svgExporter: (_, _) => new SvgExport("<svg/>", []));

        var first = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"svg"}""");
        var second = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"svg"}""");

        second.Data!.Value.GetRawText().Should().Be(first.Data!.Value.GetRawText());
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Svg_export_does_not_touch_the_version_or_the_history()
    {
        var document = new DiagramDocument("doc");
        var context = Harness.Context(document, svgExporter: (_, _) => new SvgExport("<svg/>", []));
        var registry = ToolRegistry.CreateDefault(context);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");

        var version = document.Version;
        var history = context.Bus.Context.History.UndoCount;

        Harness.Invoke(registry, DiagramToolset.Export, """{"format":"svg"}""").IsSuccess.Should().BeTrue();

        document.Version.Should().Be(version, "导出是只读的，不得触发版本号变化");
        context.Bus.Context.History.UndoCount.Should().Be(history, "导出不进撤销栈");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Svg_exporting_a_page_that_is_not_there_is_refused()
    {
        var registry = Harness.Registry(
            new DiagramDocument("doc"),
            svgExporter: (_, _) => new SvgExport("<svg/>", []));

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"svg","pageId":"p1"}""");

        Harness.CodeOf(result).Should().Be(ErrorCodes.PageMissing);
        result.Errors[0].Parameter.Should().Be("pageId");
    }

    #endregion

    #region PNG

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Png_export_returns_the_bytes_as_base64()
    {
        var registry = Harness.Registry(
            new DiagramDocument("doc"),
            bitmapExporter: (_, _) => new BitmapExport([1, 2, 3, 4], 640, 480, [new DroppedFeature("可编辑性", [], "都成了像素")]));

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"png"}""");

        result.IsSuccess.Should().BeTrue();

        var data = result.Data!.Value;

        data.GetProperty("format").GetString().Should().Be("png");
        Convert.FromBase64String(data.GetProperty("base64").GetString()!).Should().Equal([1, 2, 3, 4]);
        data.TryGetProperty("text", out _).Should().BeFalse(
            "位图那一档没有文本；给它一个空的 text 会让调用方以为这是一张空图");
        data.GetProperty("dropped").GetArrayLength().Should().Be(1);
        result.Message.Should().Contain("640").And.Contain("480");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Png_export_hands_the_page_to_the_renderer()
    {
        // 与 SVG 那条同一条理由：按页过滤那一套口径在 Core 里只有一份，
        // 工具层不先过滤一遍，只把页面标识原样交下去。
        string? seen = null;

        var document = DiagramDocument.CreateFromContent(
            "doc",
            DiagramKind.Flowchart,
            Direction.TB,
            pages: [new PageDef { Id = "p1", Order = 0 }, new PageDef { Id = "p2", Order = 1 }],
            nodes: [new NodeDef { Id = "second", Label = "第二页上的", Page = "p2" }]);

        var registry = Harness.Registry(
            document,
            bitmapExporter: (_, pageId) =>
            {
                seen = pageId;

                return new BitmapExport([1], 10, 10, []);
            });

        Harness.Invoke(registry, DiagramToolset.Export, """{"format":"png","pageId":"p2"}""")
            .IsSuccess.Should().BeTrue();

        seen.Should().Be("p2");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Png_export_says_so_when_the_host_has_no_render_layer()
    {
        var registry = Harness.Registry(new DiagramDocument("doc"));

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"png"}""");

        result.IsSuccess.Should().BeFalse();
        Harness.CodeOf(result).Should().Be(ToolErrorCodes.NotSupported);
        result.Errors[0].Parameter.Should().Be("format");
        result.Errors[0].Message.Should().Contain("宿主");
        result.Errors[0].Message.Should().NotContain("还没排到", "这是宿主没接上，不是排期问题");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Png_export_says_so_when_the_renderer_gives_nothing()
    {
        var registry = Harness.Registry(new DiagramDocument("doc"), bitmapExporter: (_, _) => null);

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"png"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.NotSupported);
        result.Errors[0].Message.Should().Contain("排不出结果");
        result.Errors[0].Expected.Should().Contain("diagram_validate", "要说清下一步该去查什么");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Two_png_exports_of_the_same_document_are_byte_identical()
    {
        var registry = Harness.Registry(
            new DiagramDocument("doc"),
            bitmapExporter: (_, _) => new BitmapExport([9, 8, 7], 20, 10, []));

        var first = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"png"}""");
        var second = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"png"}""");

        second.Data!.Value.GetRawText().Should().Be(first.Data!.Value.GetRawText());
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Png_export_does_not_touch_the_version_or_the_history()
    {
        var document = new DiagramDocument("doc");
        var context = Harness.Context(document, bitmapExporter: (_, _) => new BitmapExport([1], 10, 10, []));
        var registry = ToolRegistry.CreateDefault(context);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");

        var version = document.Version;
        var history = context.Bus.Context.History.UndoCount;

        Harness.Invoke(registry, DiagramToolset.Export, """{"format":"png"}""").IsSuccess.Should().BeTrue();

        document.Version.Should().Be(version, "导出是只读的，不得触发版本号变化");
        context.Bus.Context.History.UndoCount.Should().Be(history, "导出不进撤销栈");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Png_exporting_a_page_that_is_not_there_is_refused()
    {
        var registry = Harness.Registry(
            new DiagramDocument("doc"),
            bitmapExporter: (_, _) => new BitmapExport([1], 10, 10, []));

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"png","pageId":"p1"}""");

        Harness.CodeOf(result).Should().Be(ErrorCodes.PageMissing);
        result.Errors[0].Parameter.Should().Be("pageId");
    }

    #endregion

    #region PDF

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Pdf_export_returns_the_bytes_as_base64()
    {
        var registry = Harness.Registry(
            new DiagramDocument("doc"),
            pdfExporter: (_, _) => new PdfExport([1, 2, 3, 4], 2, [new DroppedFeature("字体的整份嵌入", [], "文件大")]));

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"pdf"}""");

        result.IsSuccess.Should().BeTrue();

        var data = result.Data!.Value;

        data.GetProperty("format").GetString().Should().Be("pdf");
        Convert.FromBase64String(data.GetProperty("base64").GetString()!).Should().Equal([1, 2, 3, 4]);
        data.TryGetProperty("text", out _).Should().BeFalse(
            "PDF 是二进制；给它一个空的 text 会让调用方以为这是一份空文件");
        data.GetProperty("dropped").GetArrayLength().Should().Be(1);
    }

    /// <summary>
    /// 页数要报出来。
    /// </summary>
    /// <remarks>
    /// 这是 PDF 与另外三种格式唯一一处结构上的差别：一份文档可以出好几页，
    /// 而调用方拿到的是一串字节，自己数不出页数。不报的话，
    /// "我导的是整份还是某一页"这个问题只能靠猜。
    /// </remarks>
    [Fact]
    [Trait("Category", "ExportTool")]
    public void Pdf_export_reports_the_page_count()
    {
        var registry = Harness.Registry(
            new DiagramDocument("doc"),
            pdfExporter: (_, _) => new PdfExport([1], 3, []));

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"pdf"}""");

        result.Message.Should().Contain("3 页");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Pdf_export_hands_the_page_to_the_renderer()
    {
        // 与另外两条同一条理由：按页过滤那一套口径在 Core 里只有一份。
        // PDF 还多一层——页面标识为空时，出哪几页由宿主按文档自己声明的页序决定，
        // 而"文档有哪几页"这一层看不见。
        string? seen = null;

        var document = DiagramDocument.CreateFromContent(
            "doc",
            DiagramKind.Flowchart,
            Direction.TB,
            pages: [new PageDef { Id = "p1", Order = 0 }, new PageDef { Id = "p2", Order = 1 }],
            nodes: [new NodeDef { Id = "second", Label = "第二页上的", Page = "p2" }]);

        var registry = Harness.Registry(
            document,
            pdfExporter: (_, pageId) =>
            {
                seen = pageId;

                return new PdfExport([1], 1, []);
            });

        Harness.Invoke(registry, DiagramToolset.Export, """{"format":"pdf","pageId":"p2"}""")
            .IsSuccess.Should().BeTrue();

        seen.Should().Be("p2");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Pdf_export_says_so_when_the_host_has_no_render_layer()
    {
        var registry = Harness.Registry(new DiagramDocument("doc"));

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"pdf"}""");

        result.IsSuccess.Should().BeFalse();
        Harness.CodeOf(result).Should().Be(ToolErrorCodes.NotSupported);
        result.Errors[0].Parameter.Should().Be("format");
        result.Errors[0].Message.Should().Contain("宿主");
        result.Errors[0].Message.Should().NotContain("还没排到", "这是宿主没接上，不是排期问题");
        result.Errors[0].Message.Should().NotContain("选型", "选型已经定下来了，不再是这个理由");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Pdf_export_says_so_when_the_renderer_gives_nothing()
    {
        var registry = Harness.Registry(new DiagramDocument("doc"), pdfExporter: (_, _) => null);

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"pdf"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.NotSupported);
        result.Errors[0].Message.Should().Contain("排不出结果");
        result.Errors[0].Expected.Should().Contain("diagram_validate", "要说清下一步该去查什么");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Two_pdf_exports_of_the_same_document_are_byte_identical()
    {
        var registry = Harness.Registry(
            new DiagramDocument("doc"),
            pdfExporter: (_, _) => new PdfExport([9, 8, 7], 1, []));

        var first = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"pdf"}""");
        var second = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"pdf"}""");

        second.Data!.Value.GetRawText().Should().Be(first.Data!.Value.GetRawText());
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Pdf_export_does_not_touch_the_version_or_the_history()
    {
        var document = new DiagramDocument("doc");
        var context = Harness.Context(document, pdfExporter: (_, _) => new PdfExport([1], 1, []));
        var registry = ToolRegistry.CreateDefault(context);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");

        var version = document.Version;
        var history = context.Bus.Context.History.UndoCount;

        Harness.Invoke(registry, DiagramToolset.Export, """{"format":"pdf"}""").IsSuccess.Should().BeTrue();

        document.Version.Should().Be(version, "导出是只读的，不得触发版本号变化");
        context.Bus.Context.History.UndoCount.Should().Be(history, "导出不进撤销栈");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Pdf_exporting_a_page_that_is_not_there_is_refused()
    {
        var registry = Harness.Registry(
            new DiagramDocument("doc"),
            pdfExporter: (_, _) => new PdfExport([1], 1, []));

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"pdf","pageId":"p1"}""");

        Harness.CodeOf(result).Should().Be(ErrorCodes.PageMissing);
        result.Errors[0].Parameter.Should().Be("pageId");
    }

    #endregion

    #region DSL

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Dsl_export_returns_the_text()
    {
        var registry = Harness.Registry(new DiagramDocument("doc"));

        Harness.Edit(registry, """{"action":"add-node","id":"start","label":"开始"}""");
        Harness.Edit(registry, """{"action":"add-node","id":"check","label":"校验"}""");
        Harness.Edit(registry, """{"action":"connect-edge","id":"e1","from":"start","to":"check"}""");

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"dsl"}""");

        result.IsSuccess.Should().BeTrue();

        var text = result.Data!.Value.GetProperty("text").GetString()!;

        text.Should().Contain("dsl 1").And.Contain("start").And.Contain("check");
        text.Should().Contain("start -> check", "连线是导出方向必须保住的那一部分");
        result.Data!.Value.GetProperty("format").GetString().Should().Be("dsl");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Two_dsl_exports_of_the_same_document_are_byte_identical()
    {
        var registry = Harness.Registry(new DiagramDocument("doc"));

        Harness.Edit(registry, """{"action":"add-node","id":"b"}""");
        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");

        var first = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"dsl"}""");
        var second = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"dsl"}""");

        second.Data!.Value.GetRawText().Should().Be(first.Data!.Value.GetRawText());
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void A_plain_graph_exports_without_a_dropped_list()
    {
        // DSL 能完整表达一张普通流程图，所以这一份的丢失清单是空的。
        // 空清单不等于"无损"这条一般命题，只等于这张图上没有东西落进已知的清单。
        var registry = Harness.Registry(new DiagramDocument("doc"));

        Harness.Edit(registry, """{"action":"add-node","id":"a","label":"甲"}""");
        Harness.Edit(registry, """{"action":"add-node","id":"b","label":"乙"}""");
        Harness.Edit(registry, """{"action":"connect-edge","id":"e1","from":"a","to":"b","label":"是"}""");

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"dsl"}""");

        result.Data!.Value.GetProperty("dropped").GetArrayLength().Should().Be(0);
        result.Message.Should().NotContain("写不进去");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Dsl_export_says_the_pinned_positions_could_not_be_written()
    {
        // 固定坐标在人工产物里，工具层只拿到标识、拿不到坐标。少了它们，
        // 用户拖过的位置会回到自动结果——那是要付的代价，但必须说清付了什么。
        var registry = Harness.Registry(new DiagramDocument("doc"), pinned: ["check"]);

        Harness.Edit(registry, """{"action":"add-node","id":"check","label":"校验"}""");

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"dsl"}""");

        var features = result.Data!.Value.GetProperty("dropped").EnumerateArray()
            .Select(item => item.GetProperty("feature").GetString())
            .ToList();

        features.Should().Contain("固定位置");
        result.Message.Should().Contain("写不进去");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Dsl_export_does_not_touch_the_version_or_the_history()
    {
        var document = new DiagramDocument("doc");
        var context = Harness.Context(document);
        var registry = ToolRegistry.CreateDefault(context);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");

        var version = document.Version;
        var history = context.Bus.Context.History.UndoCount;

        Harness.Invoke(registry, DiagramToolset.Export, """{"format":"dsl"}""").IsSuccess.Should().BeTrue();

        document.Version.Should().Be(version, "导出是只读的，不得触发版本号变化");
        context.Bus.Context.History.UndoCount.Should().Be(history, "导出不进撤销栈");
    }

    #endregion

    #region 格式与页面

    [Fact]
    [Trait("Category", "ExportTool")]
    public void An_unknown_format_lists_the_available_ones()
    {
        var registry = Harness.Registry(new DiagramDocument("doc"));

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"excalidraw"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Parameter.Should().Be("format");
        result.Errors[0].Expected.Should().Contain("dsl").And.Contain("svg");
    }

    [Fact]
    [Trait("Category", "ExportTool")]
    public void Exporting_a_page_that_is_not_there_is_refused()
    {
        var registry = Harness.Registry(new DiagramDocument("doc"));

        var result = Harness.Invoke(registry, DiagramToolset.Export, """{"format":"dsl","pageId":"p1"}""");

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
            """{"format":"dsl","pageId":"p2"}""");

        result.IsSuccess.Should().BeTrue();

        var text = result.Data!.Value.GetProperty("text").GetString();

        text.Should().Contain("second");
        text.Should().NotContain("first", "别的页面上的节点不该出现在这一页的导出里");
    }

    #endregion
}
