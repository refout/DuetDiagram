using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// IR 扩展后的往返、哈希口径、快照、只读约束与一致性校验。
/// </summary>
/// <remarks>
/// 这些测试不经过命令层，直接用公开的构造装配文档。
/// 理由是命令层目前只覆盖节点与边，用命令装配不出带组合、标签、布局提示的文档；
/// 而本组测试要验的正是那些新集合的行为。
/// </remarks>
public sealed class IrExtensionTests
{
    // ---- 往返 ----

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Full_ir_round_trips_losslessly()
    {
        var document = Populated();

        var json = DiagramSerializer.SerializeFull(document);
        var restored = DiagramSerializer.DeserializeFull(json);

        DiagramSerializer.Normalize(restored).Should().Be(json);
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Document_without_new_fields_still_loads()
    {
        // 旧版本产出的文件里没有后面这些字段。它必须仍然能打开，
        // 而不是因为缺字段就整个读不出来——这与"版本声明可选、解析器记提示但不报错"是同一条约定。
        const string Legacy = """
            {"id":"legacy","kind":"Flowchart","direction":"LR","version":2,
             "structuralHash":"s","visualHash":"v",
             "nodes":[{"id":"a","label":"甲"}],
             "edges":[{"id":"e1","from":"a","to":"a"}]}
            """;

        var restored = DiagramSerializer.DeserializeFull(Legacy);

        restored.Nodes.Should().HaveCount(1);
        restored.Edges.Should().HaveCount(1);
        restored.Pages.Should().BeEmpty();
        restored.Composites.Should().BeEmpty();
        restored.Layout.NodeSpacing.Should().Be(LayoutHintsDefaults.NodeSpacing);
        restored.Canvas.Infinite.Should().BeTrue();
        restored.Palette.Entries.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Composite_polymorphism_survives_round_trip()
    {
        var document = Populated();

        var restored = DiagramSerializer.DeserializeFull(DiagramSerializer.SerializeFull(document));

        // 派生类型的身份必须靠多态标签还原，不能退化成基类——
        // 退化了的话泳道会被当成普通分组处理，而且不会有任何报错。
        restored.Composites.OfType<GroupDef>().Should().HaveCount(1);
        restored.Composites.OfType<LaneDef>().Should().HaveCount(1);
        restored.Composites.Single(c => c.Id == "lane1").Should().BeOfType<LaneDef>();
    }

    // ---- 哈希口径 ----

    [Fact]
    [Trait("Category", "IrHashing")]
    public void Layout_hint_change_alters_structural_hash()
    {
        var before = Base();
        var after = WithLayout(Base(), new LayoutHints { NodeSpacing = 99, LayerSpacing = 99 });

        after.StructuralHash.Should().NotBe(before.StructuralHash);
    }

    [Fact]
    [Trait("Category", "IrHashing")]
    public void Port_change_alters_structural_hash()
    {
        // 端口不改变节点坐标，却改变连线的出入点，因此必须让坐标失效。
        var before = WithNode(Base(), new NodeDef { Id = "a", Label = "甲" });
        var after = WithNode(Base(), new NodeDef
        {
            Id = "a",
            Label = "甲",
            Ports = [new PortDef { Name = "out", Side = PortSide.Right }],
        });

        after.StructuralHash.Should().NotBe(before.StructuralHash);
    }

    [Fact]
    [Trait("Category", "IrHashing")]
    public void Font_change_alters_structural_hash()
    {
        // 字体改变标签宽度，进而改变节点尺寸与布局结果。
        var before = Base();
        var after = WithFont(Base(), new FontDef { Id = "f1", Name = "思源黑体" });

        after.StructuralHash.Should().NotBe(before.StructuralHash);
    }

    [Fact]
    [Trait("Category", "IrHashing")]
    public void Palette_change_alters_only_visual_hash()
    {
        // 换调色板不改变节点尺寸，坐标仍然有效，因此只该重绘。
        var before = Base();
        var after = WithPalette(Base(), new Palette
        {
            Entries = new Dictionary<string, PaletteEntry>(StringComparer.Ordinal)
            {
                ["primary"] = new PaletteEntry { Name = "primary", Fill = "#3366ff" },
            },
        });

        after.StructuralHash.Should().Be(before.StructuralHash);
        after.VisualHash.Should().NotBe(before.VisualHash);
    }

    [Fact]
    [Trait("Category", "IrHashing")]
    public void Style_change_alters_only_visual_hash()
    {
        var before = WithNode(Base(), new NodeDef { Id = "a", Label = "甲" });
        var after = WithNode(Base(), new NodeDef
        {
            Id = "a",
            Label = "甲",
            Style = new NodeStyle { Fill = "#ffcccc", Weight = 2 },
        });

        after.StructuralHash.Should().Be(before.StructuralHash);
        after.VisualHash.Should().NotBe(before.VisualHash);
    }

    [Fact]
    [Trait("Category", "IrHashing")]
    public void Action_change_alters_neither_hash()
    {
        // 动作只影响交互。把它算进去的话，改一个点击行为会导致整幅图重绘。
        var before = Base();
        var after = WithAction(Base(), new ActionDef { Id = "a1", Event = "click", Kind = "open-url" });

        after.StructuralHash.Should().Be(before.StructuralHash);
        after.VisualHash.Should().Be(before.VisualHash);
    }

    [Fact]
    [Trait("Category", "IrHashing")]
    public void Constraint_creation_time_does_not_affect_structural_hash()
    {
        // 创建时间不同但内容相同的约束，说的是同一件事。
        // 把它算进哈希的话，重复添加一条一模一样的约束就会触发一次全图重排。
        var early = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var late = new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);

        var first = WithLayout(Base(), Hint(early));
        var second = WithLayout(Base(), Hint(late));

        second.StructuralHash.Should().Be(first.StructuralHash);
    }

    [Fact]
    [Trait("Category", "IrHashing")]
    public void Structural_change_always_alters_visual_hash()
    {
        // 包含关系必须靠实现保证，而不是碰巧成立：
        // 早先的实现里视觉哈希漏掉了节点的父级，于是"结构变了视觉却没变"，
        // 而那种不一致没有任何地方会报错。
        var variants = new[]
        {
            WithLayout(Base(), new LayoutHints { NodeSpacing = 12 }),
            WithNode(Base(), new NodeDef { Id = "a", Parent = "g1" }),
            WithFont(Base(), new FontDef { Id = "f1", Name = "某字体" }),
            WithComposite(Base(), new GroupDef { Id = "g1", Members = ["a"] }),
        };

        var baseline = Base();

        foreach (var variant in variants)
        {
            variant.StructuralHash.Should().NotBe(baseline.StructuralHash);

            variant.VisualHash.Should().NotBe(
                baseline.VisualHash,
                "结构输入变了，画出来必然不一样");
        }
    }

    // ---- 快照 ----

    [Fact]
    [Trait("Category", "IrSnapshot")]
    public void Snapshot_is_unaffected_by_later_changes()
    {
        var document = Base();
        var snapshot = document.TakeFullSnapshot();

        document.RestoreFromSnapshot(WithLayout(document, new LayoutHints { NodeSpacing = 77 }).TakeFullSnapshot());

        snapshot.Layout.NodeSpacing.Should().Be(LayoutHintsDefaults.NodeSpacing);
        snapshot.Nodes.Should().HaveCount(1);
    }

    [Fact]
    [Trait("Category", "IrSnapshot")]
    public void Restore_brings_back_exact_content()
    {
        var document = Populated();
        var snapshot = document.TakeFullSnapshot();
        var expected = DiagramSerializer.Normalize(document);

        document.RestoreFromSnapshot(Base().TakeFullSnapshot());
        document.RestoreFromSnapshot(snapshot);

        DiagramSerializer.Normalize(document).Should().Be(expected);

        // 版本号与哈希也要一并恢复：退回去的内容必须能与历史上的版本号对上，
        // 否则增量同步会失去参照。
        document.Version.Should().Be(snapshot.Version);
        document.StructuralHash.Should().Be(snapshot.StructuralHash);
        document.VisualHash.Should().Be(snapshot.VisualHash);
    }

    [Fact]
    [Trait("Category", "IrSnapshot")]
    public void Restore_rejects_snapshot_from_another_document()
    {
        var mine = Base();
        var other = new DiagramDocument("another-doc", DiagramKind.Flowchart, Direction.TB);

        var act = () => mine.RestoreFromSnapshot(other.TakeFullSnapshot());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*another-doc*",
                "跨文档套用会得到一个标识是甲的、内容是乙的自相矛盾对象");
    }

    // ---- 只读约束 ----

    [Fact]
    [Trait("Category", "IrReadOnly")]
    public void Collections_cannot_be_mutated_through_a_cast()
    {
        // 把内部列表当接口直接返回只是编译期契约：运行时那个对象仍然可变，
        // 强制转换就能绕过去。这条约束是项目的硬约定之一，值得在运行时也守住。
        var document = Base();

        (document.Nodes as List<NodeDef>).Should().BeNull();
        (document.Edges as List<EdgeDef>).Should().BeNull();
        (document.Composites as List<CompositeDef>).Should().BeNull();

        var act = () => ((IList<PageDef>)document.Pages).Add(new PageDef { Id = "p1" });

        act.Should().Throw<NotSupportedException>();
    }

    // ---- 一致性校验 ----

    [Fact]
    [Trait("Category", "IrValidator")]
    public void Valid_document_has_no_issues()
    {
        DiagramValidator.Validate(Populated()).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void Duplicate_id_across_collections_is_reported()
    {
        // 九个集合共用一个命名空间，成员列表里的标识不区分它是节点还是组合。
        var document = WithTag(
            WithComposite(Base(), new GroupDef { Id = "a", Members = ["node1"] }),
            new TagDef { Id = "t1", Members = ["a"] });

        var issues = DiagramValidator.Validate(document);

        issues.Should().Contain(i => i.Code == "ID_DUPLICATE");
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void Edge_to_missing_node_is_reported()
    {
        var document = WithEdge(Base(), new EdgeDef { Id = "e1", From = "a", To = "不存在" });

        var issues = DiagramValidator.Validate(document);

        issues.Should().Contain(i => i.Code == "EDGE_TO_MISSING");
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void Membership_mismatch_is_reported()
    {
        // 组合的成员列表与节点的父级说的是同一件事。约定以成员列表为准，
        // 不一致时必须报出来——放任不管的话，布局按一处算、渲染按另一处画。
        var document = WithComposite(
            Base(),
            new GroupDef { Id = "g1", Members = ["a"] });

        var issues = DiagramValidator.Validate(document);

        issues.Should().Contain(i => i.Code == "MEMBERSHIP_MISMATCH");
    }

    [Fact]
    [Trait("Category", "IrValidator")]
    public void Missing_port_is_reported()
    {
        var document = WithEdge(
            Base(),
            new EdgeDef { Id = "e1", From = "a", To = "a", FromPort = "不存在" });

        var issues = DiagramValidator.Validate(document);

        issues.Should().Contain(i => i.Code == "EDGE_PORT_MISSING");
    }

    // ---- 装配辅助 ----

    /// <summary>最小文档：一个节点。哈希测试都从它出发，只改一个变量。</summary>
    private static DiagramDocument Base() => Build(nodes: [new NodeDef { Id = "a", Label = "甲" }]);

    /// <summary>装满九个集合与三个子对象的文档。</summary>
    private static DiagramDocument Populated() => Build(
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
            new GroupDef { Id = "g1", Label = "分组", Members = ["a"], },
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

    private static LayoutHints Hint(DateTimeOffset createdAt) => new()
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

    private static DiagramDocument Build(
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
        Construct(
            "ir-test",
            0,
            string.Empty,
            string.Empty,
            pages,
            layers,
            nodes,
            edges,
            composites,
            tags,
            actions,
            fonts,
            textPresets,
            palette,
            layout,
            canvas);

    /// <summary>
    /// 先造一份算出哈希，再带着哈希造第二份。
    /// </summary>
    /// <remarks>
    /// 哈希的 setter 是 <c>internal</c>，测试程序集改不了——这正是"仅命令总线可推进版本与哈希"
    /// 这条约束在起作用。两个哈希都不覆盖自身，因此从空哈希算起与从最终态算起结果相同，
    /// 两趟构造是安全的。
    /// </remarks>
    private static DiagramDocument Construct(
        string id,
        int version,
        string structuralHash,
        string visualHash,
        IReadOnlyList<PageDef>? pages,
        IReadOnlyList<LayerDef>? layers,
        IReadOnlyList<NodeDef>? nodes,
        IReadOnlyList<EdgeDef>? edges,
        IReadOnlyList<CompositeDef>? composites,
        IReadOnlyList<TagDef>? tags,
        IReadOnlyList<ActionDef>? actions,
        IReadOnlyList<FontDef>? fonts,
        IReadOnlyList<TextStylePreset>? textPresets,
        Palette? palette,
        LayoutHints? layout,
        CanvasSettings? canvas)
    {
        var draft = new DiagramDocument(
            id,
            DiagramKind.Flowchart,
            Direction.TB,
            version,
            structuralHash,
            visualHash,
            pages,
            layers,
            nodes,
            edges,
            composites,
            tags,
            actions,
            fonts,
            textPresets,
            palette,
            layout,
            canvas);

        return new DiagramDocument(
            id,
            DiagramKind.Flowchart,
            Direction.TB,
            version,
            DiagramHashing.ComputeStructuralHash(draft),
            DiagramHashing.ComputeVisualHash(draft),
            pages,
            layers,
            nodes,
            edges,
            composites,
            tags,
            actions,
            fonts,
            textPresets,
            palette,
            layout,
            canvas);
    }

    private static DiagramDocument WithLayout(DiagramDocument source, LayoutHints layout) =>
        Rebuild(source, layout: layout);

    private static DiagramDocument WithPalette(DiagramDocument source, Palette palette) =>
        Rebuild(source, palette: palette);

    private static DiagramDocument WithNode(DiagramDocument source, NodeDef node) =>
        Rebuild(source, nodes: [node]);

    private static DiagramDocument WithFont(DiagramDocument source, FontDef font) =>
        Rebuild(source, fonts: [font]);

    private static DiagramDocument WithEdge(DiagramDocument source, EdgeDef edge) =>
        Rebuild(source, edges: [edge]);

    private static DiagramDocument WithAction(DiagramDocument source, ActionDef action) =>
        Rebuild(source, actions: [action]);

    private static DiagramDocument WithComposite(DiagramDocument source, CompositeDef composite) =>
        Rebuild(source, composites: [composite]);

    private static DiagramDocument WithTag(DiagramDocument source, TagDef tag) =>
        Rebuild(source, tags: [tag]);

    /// <summary>
    /// 只替换指定的部分，其余原样带过来。
    /// </summary>
    /// <remarks>
    /// 哈希测试的关键是"只改一个变量"。逐个手工重建文档会让某个字段在两次构建之间
    /// 意外地不一致，而那种不一致正好会污染被测的那个哈希。
    /// 因此统一走这里，调用方只能通过参数指定要改的部分。
    /// </remarks>
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
        Construct(
            source.Id,
            source.Version,
            string.Empty,
            string.Empty,
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
