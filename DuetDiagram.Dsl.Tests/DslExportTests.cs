using DuetDiagram.Core.Model;
using DuetDiagram.Core.Sidecar;
using DuetDiagram.Dsl.Export;
using DuetDiagram.Dsl.Mapping;
using DuetDiagram.Dsl.Parsing;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Dsl.Tests;

/// <summary>
/// IR 到 DSL 的导出。
/// </summary>
/// <remarks>
/// 断言分两类：一类是"能写出来的都写对了"，一类是"写不出来的都说出来了"。
/// 后者更要紧——导出方向唯一会安静出错的地方就是漏写某一类，
/// 而漏写之后导出的文本仍然解析得出来，只是少了一块内容。
/// </remarks>
public sealed class DslExportTests
{
    #region 头部

    [Theory]
    [InlineData(DiagramKind.Flow, "flow")]
    [InlineData(DiagramKind.Flowchart, "flowchart")]
    [InlineData(DiagramKind.Block, "block")]
    [InlineData(DiagramKind.State, "state")]
    [Trait("Category", "DslExport")]
    public void Every_kind_has_a_keyword(DiagramKind kind, string keyword)
    {
        var text = Export(DiagramDocument.CreateFromContent("doc", kind: kind)).Text;

        text.Should().Contain($"kind {keyword}");
    }

    [Theory]
    [InlineData(Direction.TB, "TB")]
    [InlineData(Direction.LR, "LR")]
    [InlineData(Direction.RL, "RL")]
    [InlineData(Direction.BT, "BT")]
    [Trait("Category", "DslExport")]
    public void Every_direction_has_a_keyword(Direction direction, string keyword)
    {
        var text = Export(DiagramDocument.CreateFromContent("doc", direction: direction)).Text;

        text.Should().Contain($"direction {keyword}");
    }

    [Fact]
    [Trait("Category", "DslExport")]
    public void The_version_is_declared()
    {
        Export(DiagramDocument.CreateFromContent("doc")).Text.Should().StartWith("dsl 1\n");
    }

    #endregion

    #region 节点

    [Fact]
    [Trait("Category", "DslExport")]
    public void Node_fields_are_written_back()
    {
        var text = Export(Map("""
            api "API 服务" shape=hexagon style=primary layer=fore desc="对外接口" ports=req:left,resp:right
            """).Document).Text;

        text.Should().Contain("api \"API 服务\"");
        text.Should().Contain("shape=hexagon");
        text.Should().Contain("style=primary");
        text.Should().Contain("layer=fore");
        text.Should().Contain("desc=\"对外接口\"");
        text.Should().Contain("ports=req:left,resp:right");
    }

    [Fact]
    [Trait("Category", "DslExport")]
    public void Defaults_are_not_written()
    {
        // 缺省值不写：显式写一个与缺省相同的值不增加信息，却会让同一张图的两种文本
        // 产出不同的文档。矩形、实线、有箭头、标签等于标识，这四样都不该出现。
        var text = Export(Map("tmp").Document).Text;

        text.Should().NotContain("shape=rect");
        text.Should().NotContain("shape=");
        text.Should().Contain("tmp\n", "标签与标识相同时不写标签");
        text.Should().NotContain("\"tmp\"");
    }

    [Fact]
    [Trait("Category", "DslExport")]
    public void A_label_different_from_the_id_is_written()
    {
        Export(Map("n \"显示文本\"").Document).Text.Should().Contain("n \"显示文本\"");
    }

    [Fact]
    [Trait("Category", "DslExport")]
    public void Quotes_and_newlines_in_a_label_are_escaped()
    {
        var document = DiagramDocument.CreateFromContent(
            "doc",
            nodes: [new NodeDef { Id = "n", Label = "带\"引号\"与\n换行" }]);

        var text = Export(document).Text;

        text.Should().Contain("\\\"引号\\\"");
        text.Should().Contain("\\n换行");
    }

    #endregion

    #region 边

    [Fact]
    [Trait("Category", "DslExport")]
    public void Edge_fields_are_written_back()
    {
        var text = Export(Map("""
            a "甲" ports=out:right
            b "乙"
            e1: a.out -> b "是" line=dashed arrow=open style=danger
            """).Document).Text;

        text.Should().Contain("e1: a.out -> b \"是\"");
        text.Should().Contain("line=dashed");
        text.Should().Contain("arrow=open");
        text.Should().Contain("style=danger");
    }

    [Fact]
    [Trait("Category", "DslExport")]
    public void An_edge_without_an_arrow_is_written_with_the_dash_connector()
    {
        var text = Export(Map("a -- b").Document).Text;

        text.Should().Contain("a -- b");
        text.Should().NotContain("arrow=none");
    }

    [Fact]
    [Trait("Category", "DslExport")]
    public void Edge_identity_is_always_written()
    {
        // 标识必须写出来：不给标识的话导入方向会自己发一个，而那个多半与原来不同，
        // 于是往返之后边的身份变了——引用它的 order 约束与 sidecar 条目全都对不上。
        Export(Map("a -> b").Document).Text.Should().Contain("e1: a -> b");
    }

    #endregion

    #region 分组

    [Theory]
    [InlineData("group backend \"后端\"", "group backend \"后端\"")]
    [InlineData("lane pay \"支付\"", "lane pay \"支付\"")]
    [InlineData("subflow retry \"重试\"", "subflow retry \"重试\"")]
    [Trait("Category", "DslExport")]
    public void Every_group_kind_has_a_keyword(string source, string expected)
    {
        var text = Export(Map($"{source}\n  n \"节点\"\nend").Document).Text;

        text.Should().Contain(expected);
        text.Should().Contain("end");
    }

    [Fact]
    [Trait("Category", "DslExport")]
    public void Members_are_written_inside_their_group()
    {
        var text = Export(Map("""
            group backend "后端"
              api "API"
              db "库"
            end
            """).Document).Text;

        var lines = Lines(text);

        lines.Should().Contain("group backend \"后端\"");
        lines.Should().Contain("  api \"API\"");
        lines.Should().Contain("  db \"库\"");
        lines.Should().Contain("end");
    }

    [Fact]
    [Trait("Category", "DslExport")]
    public void Nested_groups_are_indented()
    {
        var text = Export(Map("""
            group outer "外"
              group inner "内"
                n "节点"
              end
            end
            """).Document).Text;

        var lines = Lines(text);

        lines.Should().Contain("group outer \"外\"");
        lines.Should().Contain("  group inner \"内\"");
        lines.Should().Contain("    n \"节点\"");
    }

    #endregion

    #region 布局意图

    [Fact]
    [Trait("Category", "DslExport")]
    public void Same_rank_align_and_place_are_written_back()
    {
        var text = Export(Map("""
            a
            b
            c
            same-rank a, b
            align b, c
            place c right-of a
            """).Document).Text;

        text.Should().Contain("same-rank a, b");
        text.Should().Contain("align b, c");
        text.Should().Contain("place c right-of a");
    }

    [Fact]
    [Trait("Category", "DslExport")]
    public void Order_is_written_as_target_node_names()
    {
        // IR 存的是出边的标识，DSL 写的是目标节点名——两者对同一个概念的定义不同，
        // 转换在这一层做。写边标识的话导出的文本读起来是一串 e1、e2，人不认识。
        var text = Export(Map("""
            check
            pass
            fail
            check -> pass "是"
            check -> fail "否"
            order check: pass, fail
            """).Document).Text;

        text.Should().Contain("order check: pass, fail");
    }

    [Fact]
    [Trait("Category", "DslExport")]
    public void Spacing_is_written_only_when_it_differs_from_the_default()
    {
        var plain = Export(Map("a").Document).Text;

        plain.Should().NotContain("node-spacing");
        plain.Should().NotContain("layer-spacing");

        var spaced = Export(Map("a\nnode-spacing 25\nlayer-spacing 90").Document).Text;

        spaced.Should().Contain("node-spacing 25");
        spaced.Should().Contain("layer-spacing 90");
    }

    #endregion

    #region 固定位置

    [Fact]
    [Trait("Category", "DslExport")]
    public void Pins_come_from_the_sidecar()
    {
        var mapped = Map("a\nb");

        var sidecar = new UserSidecar
        {
            DocumentId = "dsl",
            PinnedNodes = new Dictionary<string, Anchor>(StringComparer.Ordinal)
            {
                ["b"] = new Anchor(640, 320),
            },
        };

        Export(mapped.Document, sidecar).Text.Should().Contain("pin b at 640, 320");
    }

    [Fact]
    [Trait("Category", "DslExport")]
    public void No_sidecar_means_no_pins()
    {
        Export(Map("pin a at 10, 20").Document).Text.Should().NotContain("pin a");
    }

    [Fact]
    [Trait("Category", "DslExport")]
    public void A_pin_pointing_at_a_missing_node_is_reported()
    {
        var sidecar = new UserSidecar
        {
            PinnedNodes = new Dictionary<string, Anchor>(StringComparer.Ordinal)
            {
                ["gone"] = new Anchor(1, 2),
            },
        };

        var report = Export(Map("a").Document, sidecar).Report;

        Features(report).Should().Contain("指向不存在节点的固定位置");
    }

    [Fact]
    [Trait("Category", "DslExport")]
    public void Pinned_edges_and_custom_ports_are_reported()
    {
        var sidecar = new UserSidecar
        {
            PinnedEdges = new Dictionary<string, IReadOnlyList<Anchor>>(StringComparer.Ordinal)
            {
                ["e1"] = [new Anchor(1, 2)],
            },
            CustomPorts = new Dictionary<string, IReadOnlyList<PortDef>>(StringComparer.Ordinal)
            {
                ["a"] = [new PortDef { Name = "out", IsCustom = true }],
            },
        };

        var report = Export(Map("a").Document, sidecar).Report;

        Features(report).Should().Contain("固定折线").And.Contain("自定义端口");
    }

    #endregion

    #region 写不出来的东西

    [Fact]
    [Trait("Category", "DslExport")]
    public void A_plain_graph_has_nothing_to_report()
    {
        // DSL 能完整表达一张普通流程图，所以这一份没有丢失。
        // 空清单不等于"无损"这条一般命题，只等于这张图上没有东西落进已知的清单。
        Export(Map("a \"甲\" shape=hexagon\na -> b \"是\"\ngroup g \"组\"\n  c\nend").Document)
            .Report.Dropped.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "DslExport")]
    public void Every_ir_field_the_dsl_cannot_express_is_reported()
    {
        // 这一条是照着 IR 逐字段对出来的清单，是"没有哪一类被静默跳过"的判据。
        // 给 IR 加了新字段而忘了往导出器里补时，这一条不会红——所以清单本身
        // 也要跟着字段一起维护，这是这个测试存在的意义。
        var document = EverythingTheDslCannotExpress();

        var report = Export(document).Report;

        Features(report).Should().BeEquivalentTo(
        [
            "节点归属的页面",
            "自定义形状的路径",
            "富文本内容",
            "数学排版模式",
            "节点具体样式",
            "节点文本样式",
            "宿主附加数据",
            "端口的偏移与来源标记",
            "边归属的页面",
            "边样式的其余字段",
            "组合内部的布局方向",
            "组合的折叠状态",
            "组合样式",
            "组合内部的布局提示",
            "组合框",
            "页面",
            "图层的可见性、锁定与次序",
            "字体",
            "标签",
            "动作",
            "文本样式预设",
            "调色板定义",
            "画布设置",
        ]);
    }

    [Fact]
    [Trait("Category", "DslExport")]
    public void A_combo_is_written_as_a_group_and_reported()
    {
        var document = DiagramDocument.CreateFromContent(
            "doc",
            nodes: [new NodeDef { Id = "a", Parent = "box" }],
            composites: [new ComboDef { Id = "box", Label = "框", Members = ["a"] }]);

        var result = Export(document);

        result.Text.Should().Contain("group box \"框\"");
        Features(result.Report).Should().Contain("组合框");
    }

    [Fact]
    [Trait("Category", "DslExport")]
    public void An_identifier_that_cannot_be_written_is_rewritten_and_reported()
    {
        // DSL 的标识必须以字母或下划线开头、不能出现连续的连字符。
        // 原样写出去的话导出的文本自己解析不了，或者解析出别的意思。
        var document = DiagramDocument.CreateFromContent(
            "doc",
            nodes: [new NodeDef { Id = "1bad", Label = "甲" }, new NodeDef { Id = "good" }],
            edges: [new EdgeDef { Id = "e1", From = "1bad", To = "good" }]);

        var result = Export(document);

        result.Text.Should().Contain("id1bad");
        result.Text.Should().Contain("id1bad -> good", "引用要跟着改名，否则连线指向不存在的节点");
        Features(result.Report).Should().Contain("标识");
    }

    #endregion

    #region 确定性

    [Fact]
    [Trait("Category", "DslExport")]
    public void Two_exports_of_the_same_document_are_byte_identical()
    {
        var document = Map("""
            group g "组"
              b "乙"
              a "甲"
            end
            a -> b "是"
            same-rank a, b
            node-spacing 33
            """).Document;

        Export(document).Text.Should().Be(Export(document).Text);
    }

    #endregion

    #region 辅助

    private static DslExportResult Export(DiagramDocument document, UserSidecar? sidecar = null) =>
        DslExporter.Export(document, sidecar);

    private static MappingResult Map(string source) =>
        DslMapper.Map(DslParser.Parse(source), new MappingOptions { DocumentId = "dsl" });

    private static List<string> Features(DslExportReport report) =>
        [.. report.Dropped.Select(item => item.Feature)];

    private static string[] Lines(string text) =>
        [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>
    /// 一份把 DSL 表达不了的字段全都点上的文档。
    /// </summary>
    /// <remarks>
    /// 用 <c>CreateFromContent</c> 直接造，不走命令层：命令层每条都会重算全量哈希，
    /// 而这一条要的只是一份内容正确的文档，不需要它满足命令层的全部不变量。
    /// </remarks>
    private static DiagramDocument EverythingTheDslCannotExpress() => DiagramDocument.CreateFromContent(
        "doc",
        pages: [new PageDef { Id = "p1", Name = "第一页", Order = 0 }],
        layers: [new LayerDef { Id = "fore", Name = "前景", Visible = false, Locked = true, Order = 1 }],
        nodes:
        [
            new NodeDef
            {
                Id = "n",
                Label = "甲",
                Parent = "g",
                Page = "p1",
                ShapePath = "M0 0L1 0L1 1Z",
                RichText = true,
                MathMode = MathMode.Inline,
                Style = new NodeStyle { Fill = "#cc0000" },
                Text = new TextStyle { FontSize = 14 },
                Meta = new Dictionary<string, string>(StringComparer.Ordinal) { ["k"] = "v" },
                Ports = [new PortDef { Name = "req", Side = PortSide.Left, Offset = 0.2, IsCustom = true }],
            },
        ],
        edges:
        [
            new EdgeDef
            {
                Id = "e1",
                From = "n",
                To = "n",
                Page = "p1",
                Style = new EdgeStyle { Color = "#0000cc", Weight = 2, Route = EdgeRoute.Curved, LabelPosition = LabelPosition.End },
            },
        ],
        composites:
        [
            new GroupDef
            {
                Id = "g",
                Label = "组",
                Direction = Direction.LR,
                Collapsed = true,
                Style = new NodeStyle { Fill = "#eeeeee" },
                LocalLayout = LayoutHintsDefaults.Create() with { NodeSpacing = 10 },
                Members = ["n"],
            },
            new ComboDef { Id = "box", Label = "框", Members = [] },
        ],
        tags: [new TagDef { Id = "t", Label = "标签", Members = ["n"] }],
        actions: [new ActionDef { Id = "a1", Event = "click", Kind = "open-url" }],
        fonts: [new FontDef { Id = "f1", Name = "Inter", IsMono = false }],
        textPresets: [new TextStylePreset { Id = "tp", Name = "标题" }],
        palette: new Palette().WithEntry(new PaletteEntry { Name = "primary", Fill = "#3366cc" }),
        canvas: new CanvasSettings { Grid = GridStyle.Lines, GridSize = 32 });

    #endregion
}
