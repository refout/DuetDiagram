using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Render.Tests;

/// <summary>
/// 十个场景的绘制列表快照。
/// </summary>
/// <remarks>
/// <para>
/// 十个场景覆盖的是绘制列表的**全部指令类型与全部分支**：空文档、单元素、连线、
/// 八种形状、带标签的分支、嵌套组合、泳道、调色板令牌、四种边样式、多行文本。
/// 加一种指令类型而不加场景，等于那种指令没有任何回归网。
/// </para>
/// <para>
/// 快照文件在 <c>Scenes/</c> 下，进版本库。改动绘制逻辑之后快照会红，
/// 那时要判断的是"这次变化是不是有意的"——**不要顺手把新输出盖上去**，
/// 那份文件就是"上一次确认过的样子"。
/// </para>
/// </remarks>
public sealed class SceneSnapshotTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    [Theory]
    [Trait("Category", "SceneSnapshot")]
    [InlineData("01-empty")]
    [InlineData("02-single-node")]
    [InlineData("03-chain")]
    [InlineData("04-shapes")]
    [InlineData("05-branch")]
    [InlineData("06-nested-groups")]
    [InlineData("07-lane")]
    [InlineData("08-tokens")]
    [InlineData("09-edge-styles")]
    [InlineData("10-multiline")]
    public void The_scene_matches_its_snapshot(string name)
    {
        var list = Build(name);

        Snapshot.Match(name, list.ToText());
    }

    [Fact]
    [Trait("Category", "SceneSnapshot")]
    public void Every_element_in_the_scene_reaches_the_list()
    {
        // 快照红了的时候，第一件要看的是"是画错了还是漏画了"。
        // 这条断言给出的是后半个问题的答案。
        var list = Build("06-nested-groups");

        list.CountOf("outer").Should().Be(2);
        list.CountOf("inner").Should().Be(2);
        list.CountOf("n1").Should().Be(2);
        list.CountOf("n2").Should().Be(2);
    }

    private static DrawList Build(string name)
    {
        var theme = Theme.Default;

        return name switch
        {
            "01-empty" => BuildEmpty(theme),
            "02-single-node" => BuildSingleNode(theme),
            "03-chain" => BuildChain(theme),
            "04-shapes" => BuildShapes(theme),
            "05-branch" => BuildBranch(theme),
            "06-nested-groups" => BuildNestedGroups(theme),
            "07-lane" => BuildLane(theme),
            "08-tokens" => BuildTokens(theme),
            "09-edge-styles" => BuildEdgeStyles(theme),
            "10-multiline" => BuildMultiline(theme),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "没有这个场景。"),
        };
    }

    private static DrawList BuildEmpty(Theme theme) =>
        SceneBuilder.Build(DiagramDocument.CreateFromContent("scene-empty"), Layouts.Result([], [], 0, 0), theme, Measurer);

    private static DrawList BuildSingleNode(Theme theme)
    {
        var node = new NodeDef { Id = "n1", Label = "开始" };
        var document = DiagramDocument.CreateFromContent("scene-single", nodes: [node]);
        var layout = Column([node], theme);

        return SceneBuilder.Build(document, layout, theme, Measurer);
    }

    private static DrawList BuildChain(Theme theme)
    {
        var nodes = new[]
        {
            new NodeDef { Id = "n1", Label = "开始" },
            new NodeDef { Id = "n2", Label = "处理数据" },
            new NodeDef { Id = "n3", Label = "结束" },
        };

        var document = DiagramDocument.CreateFromContent(
            "scene-chain",
            nodes: nodes,
            edges:
            [
                new EdgeDef { Id = "e1", From = "n1", To = "n2" },
                new EdgeDef { Id = "e2", From = "n2", To = "n3" },
            ]);

        var placed = Place(nodes, theme);
        var layout = Layouts.Result(
            placed,
            [Layouts.Vertical("e1", placed[0], placed[1]), Layouts.Vertical("e2", placed[1], placed[2])],
            Extent(placed).Width,
            Extent(placed).Height);

        return SceneBuilder.Build(document, layout, theme, Measurer);
    }

    private static DrawList BuildShapes(Theme theme)
    {
        var shapes = new[]
        {
            NodeShape.Rect, NodeShape.Rounded, NodeShape.Stadium, NodeShape.Diamond,
            NodeShape.Circle, NodeShape.Hexagon, NodeShape.Parallelogram, NodeShape.Cylinder,
        };

        var nodes = shapes
            .Select((shape, index) => new NodeDef { Id = $"n{index + 1}", Label = shape.ToString(), Shape = shape })
            .ToArray();

        var document = DiagramDocument.CreateFromContent("scene-shapes", nodes: nodes);
        var placed = Place(nodes, theme);
        var extent = Extent(placed);

        return SceneBuilder.Build(
            document,
            Layouts.Result(placed, [], extent.Width, extent.Height),
            theme,
            Measurer);
    }

    private static DrawList BuildBranch(Theme theme)
    {
        var nodes = new[]
        {
            new NodeDef { Id = "n1", Label = "条件成立？", Shape = NodeShape.Diamond },
            new NodeDef { Id = "n2", Label = "走这条" },
            new NodeDef { Id = "n3", Label = "走那条" },
        };

        var document = DiagramDocument.CreateFromContent(
            "scene-branch",
            nodes: nodes,
            edges:
            [
                new EdgeDef { Id = "e1", From = "n1", To = "n2", Label = "是" },
                new EdgeDef { Id = "e2", From = "n1", To = "n3", Label = "否", Style = new EdgeStyle { LabelPosition = LabelPosition.End } },
            ]);

        var placed = Place(nodes, theme, gap: 60);
        var layout = Layouts.Result(
            placed,
            [Layouts.Vertical("e1", placed[0], placed[1]), Layouts.Vertical("e2", placed[0], placed[2])],
            Extent(placed).Width,
            Extent(placed).Height);

        return SceneBuilder.Build(document, layout, theme, Measurer);
    }

    private static DrawList BuildNestedGroups(Theme theme)
    {
        var nodes = new[] { new NodeDef { Id = "n1", Label = "外层的节点" }, new NodeDef { Id = "n2", Label = "内层的节点" } };

        var document = DiagramDocument.CreateFromContent(
            "scene-nested",
            nodes: nodes,
            composites:
            [
                new GroupDef { Id = "inner", Label = "内层", Members = ["n2"], Parent = "outer" },
                new GroupDef { Id = "outer", Label = "外层", Members = ["n1", "inner"] },
            ]);

        var placed = Place(nodes, theme, gap: 80);
        var extent = Extent(placed);

        return SceneBuilder.Build(document, Layouts.Result(placed, [], extent.Width, extent.Height), theme, Measurer);
    }

    private static DrawList BuildLane(Theme theme)
    {
        var nodes = new[]
        {
            new NodeDef { Id = "n1", Label = "提交" },
            new NodeDef { Id = "n2", Label = "审核" },
            new NodeDef { Id = "n3", Label = "归档" },
        };

        var document = DiagramDocument.CreateFromContent(
            "scene-lane",
            nodes: nodes,
            composites: [new LaneDef { Id = "lane", Label = "责任方", Members = ["n1", "n2", "n3"] }]);

        var placed = Place(nodes, theme);
        var extent = Extent(placed);

        return SceneBuilder.Build(document, Layouts.Result(placed, [], extent.Width, extent.Height), theme, Measurer);
    }

    private static DrawList BuildTokens(Theme theme)
    {
        var nodes = new[]
        {
            new NodeDef { Id = "n1", Label = "正常", StyleToken = "primary" },
            new NodeDef { Id = "n2", Label = "出错了", StyleToken = "danger" },
            new NodeDef { Id = "n3", Label = "写死的颜色", Style = new NodeStyle { Fill = "#123456", Text = "#ffffff" } },
        };

        var document = DiagramDocument.CreateFromContent(
            "scene-tokens",
            nodes: nodes,
            palette: new Palette
            {
                Entries = new Dictionary<string, PaletteEntry>(StringComparer.Ordinal)
                {
                    ["primary"] = new PaletteEntry { Name = "primary", Fill = "#dbeafe", Stroke = "#2563eb", Text = "#1e3a8a" },
                    ["danger"] = new PaletteEntry { Name = "danger", Fill = "#fee2e2", Stroke = "#dc2626", Text = "#7f1d1d" },
                },
            });

        var placed = Place(nodes, theme.WithPalette(document.Palette));
        var extent = Extent(placed);

        return SceneBuilder.Build(
            document,
            Layouts.Result(placed, [], extent.Width, extent.Height),
            theme.WithPalette(document.Palette),
            Measurer);
    }

    private static DrawList BuildEdgeStyles(Theme theme)
    {
        var nodes = Enumerable.Range(1, 5)
            .Select(index => new NodeDef { Id = $"n{index}", Label = $"第 {index} 步" })
            .ToArray();

        var document = DiagramDocument.CreateFromContent(
            "scene-edge-styles",
            nodes: nodes,
            edges:
            [
                new EdgeDef { Id = "e1", From = "n1", To = "n2" },
                new EdgeDef { Id = "e2", From = "n2", To = "n3", Style = new EdgeStyle { Line = LineStyle.Dashed, Arrow = ArrowStyle.OpenArrow } },
                new EdgeDef { Id = "e3", From = "n3", To = "n4", Style = new EdgeStyle { Line = LineStyle.Dotted, Arrow = ArrowStyle.Circle } },
                new EdgeDef { Id = "e4", From = "n4", To = "n5", Style = new EdgeStyle { Arrow = ArrowStyle.Cross, Weight = 3 } },
            ]);

        var placed = Place(nodes, theme);
        var extent = Extent(placed);

        var edges = Enumerable.Range(0, 4)
            .Select(index => Layouts.Vertical($"e{index + 1}", placed[index], placed[index + 1]))
            .ToArray();

        return SceneBuilder.Build(document, Layouts.Result(placed, edges, extent.Width, extent.Height), theme, Measurer);
    }

    private static DrawList BuildMultiline(Theme theme)
    {
        var nodes = new[]
        {
            new NodeDef { Id = "n1", Label = "第一行\n\n第三行" },
            new NodeDef
            {
                Id = "n2",
                Label = "靠右\n加粗",
                Text = new TextStyle { Align = TextAlign.End, VerticalAlign = VerticalAlign.Start, FontWeight = FontWeight.Bold },
            },
        };

        var document = DiagramDocument.CreateFromContent("scene-multiline", nodes: nodes);
        var placed = Place(nodes, theme, gap: 60);
        var extent = Extent(placed);

        return SceneBuilder.Build(document, Layouts.Result(placed, [], extent.Width, extent.Height), theme, Measurer);
    }

    /// <summary>把一批节点按测量出来的尺寸竖着排一列。</summary>
    /// <remarks>
    /// <para>
    /// 尺寸用 <see cref="SceneBuilder.MeasureNode"/> 量，与真实调用路径一致——
    /// 手写尺寸的快照验不出"标签装不进框"这类问题。
    /// </para>
    /// <para>
    /// 起点留得比一个组合标题还高：组合框的标题在成员之上，起点太靠上会让框伸出画布左上角，
    /// 快照里出现负坐标，读的人会以为算错了。
    /// </para>
    /// </remarks>
    private static PlacedNode[] Place(IReadOnlyList<NodeDef> nodes, Theme theme, double gap = 40)
    {
        var placed = new PlacedNode[nodes.Count];
        var top = 120.0;
        var x = 40.0;

        for (var index = 0; index < nodes.Count; index++)
        {
            var size = SceneBuilder.MeasureNode(nodes[index], theme, Measurer);
            placed[index] = new PlacedNode(nodes[index].Id, x, top, size.Width, size.Height);
            top += size.Height + gap;
        }

        return placed;
    }

    private static EngineLayoutResult Column(IReadOnlyList<NodeDef> nodes, Theme theme)
    {
        var placed = Place(nodes, theme);
        var extent = Extent(placed);

        return Layouts.Result(placed, [], extent.Width, extent.Height);
    }

    private static (double Width, double Height) Extent(IReadOnlyList<PlacedNode> placed) =>
        placed.Count == 0
            ? (0, 0)
            : (placed.Max(p => p.Right) + 40, placed.Max(p => p.Bottom) + 40);
}
