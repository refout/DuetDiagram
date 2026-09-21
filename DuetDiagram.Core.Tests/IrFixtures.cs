using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// IR 相关的测试夹具。
/// </summary>
/// <remarks>
/// <para>
/// 这些测试不经过命令层，直接用公开的构造装配文档。理由是命令层目前只覆盖节点与边，
/// 用命令装配不出带组合、标签、布局提示的文档；而要验的正是那些新集合的行为。
/// </para>
/// <para>
/// 哈希相关的用例关键是"只改一个变量"。逐个手工重建文档会让某个字段在两次构建之间
/// 意外地不一致，而那种不一致正好会污染被测的那个哈希。因此统一走重建方法，
/// 调用方只能通过参数指定要改的部分。
/// </para>
/// </remarks>
internal static class IrFixtures
{
    /// <summary>最小文档：一个节点。哈希测试都从它出发，只改一个变量。</summary>
    public static DiagramDocument Base() => Build(nodes: [new NodeDef { Id = "a", Label = "甲" }]);

    /// <summary>装满九个集合与三个子对象的文档。</summary>
    public static DiagramDocument Populated() => Build(
        pages: [new PageDef { Id = "p1", Name = "主页" }],
        layers: [new LayerDef { Id = "l1", Name = "前景", Order = 1 }],
        nodes:
        [
            new NodeDef
            {
                Id = "a",
                Label = "甲",
                Shape = NodeShape.Stadium,
                Parent = "g1",
                Layer = "l1",
                StyleToken = "primary",
                Style = new NodeStyle { Fill = "#eef", Radius = 6 },
                Text = new TextStyle { FontSize = 14, Align = TextAlign.Center },
                Ports = [new PortDef { Name = "out", Side = PortSide.Right, IsCustom = true }],
                RichText = true,
                MathMode = MathMode.Inline,
                Desc = "起点的说明",
                Meta = new Dictionary<string, string>(StringComparer.Ordinal) { ["k"] = "v" },
            },
            new NodeDef { Id = "b", Label = "乙", Parent = "lane1" },
        ],
        edges:
        [
            new EdgeDef
            {
                Id = "e1",
                From = "a",
                To = "b",
                FromPort = "out",
                Label = "是",
                Style = new EdgeStyle
                {
                    Line = LineStyle.Dashed,
                    Arrow = ArrowStyle.OpenArrow,
                    Route = EdgeRoute.Curved,
                    Color = "#333",
                },
            },
        ],
        composites:
        [
            new GroupDef { Id = "g1", Label = "分组", Members = ["a"] },
            new LaneDef { Id = "lane1", Label = "泳道", Members = ["b"] },
        ],
        tags: [new TagDef { Id = "t1", Label = "重点", Members = ["a", "e1"], Color = "danger" }],
        actions: [new ActionDef { Id = "act1", Event = "click", Kind = "open-url", Target = "a" }],
        fonts: [new FontDef { Id = "f1", Name = "思源黑体", IsMono = false }],
        textPresets: [new TextStylePreset { Id = "tp1", Name = "标题", Style = new TextStyle { FontSize = 20 } }],
        palette: new Palette
        {
            Entries = new Dictionary<string, PaletteEntry>(StringComparer.Ordinal)
            {
                ["primary"] = new PaletteEntry { Name = "primary", Fill = "#3366ff", Stroke = "#1a3fa0" },
            },
        },
        layout: Hint(new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero)),
        canvas: new CanvasSettings
        {
            Grid = GridStyle.Dots,
            GridSize = 16,
            PageSize = new Size(1123, 794),
            Orientation = CanvasOrientation.Landscape,
            Background = "#ffffff",
            Infinite = false,
        });

    public static LayoutHints Hint(DateTimeOffset createdAt) => new()
    {
        NodeSpacing = 44,
        LayerSpacing = 88,
        SameRank =
        [
            new Constraint<SameRankConstraint>(
                new SameRankConstraint(["a", "b"]),
                ConstraintOwner.Human,
                createdAt),
        ],
        Order =
        [
            new Constraint<OrderConstraint>(
                new OrderConstraint("a", ["e1"]),
                ConstraintOwner.Llm,
                createdAt),
        ],
        Align =
        [
            new Constraint<AlignConstraint>(new AlignConstraint(["a", "b"]), ConstraintOwner.Auto, createdAt),
        ],
        Place =
        [
            new Constraint<PlaceConstraint>(
                new PlaceConstraint("b", "a", PlaceRelation.RightOf),
                ConstraintOwner.Human,
                createdAt),
        ],
    };

    public static DiagramDocument Build(
        IReadOnlyList<PageDef>? pages = null,
        IReadOnlyList<LayerDef>? layers = null,
        IReadOnlyList<NodeDef>? nodes = null,
        IReadOnlyList<EdgeDef>? edges = null,
        IReadOnlyList<CompositeDef>? composites = null,
        IReadOnlyList<TagDef>? tags = null,
        IReadOnlyList<ActionDef>? actions = null,
        IReadOnlyList<FontDef>? fonts = null,
        IReadOnlyList<TextStylePreset>? textPresets = null,
        Palette? palette = null,
        LayoutHints? layout = null,
        CanvasSettings? canvas = null) =>
        DiagramDocument.CreateFromContent(
            "ir-test",
            pages: pages,
            layers: layers,
            nodes: nodes,
            edges: edges,
            composites: composites,
            tags: tags,
            actions: actions,
            fonts: fonts,
            textPresets: textPresets,
            palette: palette,
            layout: layout,
            canvas: canvas);

    public static DiagramDocument WithLayout(DiagramDocument source, LayoutHints layout) =>
        Rebuild(source, layout: layout);

    public static DiagramDocument WithPalette(DiagramDocument source, Palette palette) =>
        Rebuild(source, palette: palette);

    public static DiagramDocument WithNode(DiagramDocument source, NodeDef node) =>
        Rebuild(source, nodes: [node]);

    public static DiagramDocument WithNodes(DiagramDocument source, IReadOnlyList<NodeDef> nodes) =>
        Rebuild(source, nodes: nodes);

    public static DiagramDocument WithFont(DiagramDocument source, FontDef font) =>
        Rebuild(source, fonts: [font]);

    public static DiagramDocument WithEdge(DiagramDocument source, EdgeDef edge) =>
        Rebuild(source, edges: [edge]);

    public static DiagramDocument WithAction(DiagramDocument source, ActionDef action) =>
        Rebuild(source, actions: [action]);

    public static DiagramDocument WithComposite(DiagramDocument source, CompositeDef composite) =>
        Rebuild(source, composites: [composite]);

    public static DiagramDocument WithComposites(DiagramDocument source, IReadOnlyList<CompositeDef> composites) =>
        Rebuild(source, composites: composites);

    public static DiagramDocument WithTag(DiagramDocument source, TagDef tag) =>
        Rebuild(source, tags: [tag]);

    private static DiagramDocument Rebuild(
        DiagramDocument source,
        IReadOnlyList<NodeDef>? nodes = null,
        IReadOnlyList<EdgeDef>? edges = null,
        IReadOnlyList<CompositeDef>? composites = null,
        IReadOnlyList<TagDef>? tags = null,
        IReadOnlyList<ActionDef>? actions = null,
        IReadOnlyList<FontDef>? fonts = null,
        Palette? palette = null,
        LayoutHints? layout = null) =>
        DiagramDocument.CreateFromContent(
            source.Id,
            source.Kind,
            source.Direction,
            source.Pages,
            source.Layers,
            nodes ?? source.Nodes,
            edges ?? source.Edges,
            composites ?? source.Composites,
            tags ?? source.Tags,
            actions ?? source.Actions,
            fonts ?? source.Fonts,
            source.TextPresets,
            palette ?? source.Palette,
            layout ?? source.Layout,
            source.Canvas);
}
