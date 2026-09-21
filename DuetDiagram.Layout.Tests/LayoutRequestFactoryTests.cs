using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Layout.Tests;

/// <summary>
/// 从文档造布局输入。
/// </summary>
/// <remarks>
/// 这一层是 IR 与几何之间的接缝：语义里没有坐标与尺寸，几何里必须有。
/// 接缝写错的症状是"图看起来莫名其妙地不对"，而很难追到是翻译这一步出的问题。
/// </remarks>
public sealed class LayoutRequestFactoryTests
{
    private static readonly ConstraintLayoutEngine Engine = new();

    /// <summary>跑一次布局。取消令牌走测试上下文的，让用例可被整体取消。</summary>
    private static EngineLayoutResult Compute(LayoutJob job) =>
        Engine.Layout(job.ToRequest(), TestContext.Current.CancellationToken);

    [Fact]
    [Trait("Category", "Layout")]
    public void Nodes_are_measured_and_ports_carried()
    {
        var document = Document(
            nodes:
            [
                new NodeDef
                {
                    Id = "a",
                    Ports = [new PortDef { Name = "out", Side = PortSide.Bottom, Offset = 0.25 }],
                },
                new NodeDef { Id = "b" },
            ],
            edges: [new EdgeDef { Id = "e1", From = "a", To = "b", FromPort = "out" }]);

        var request = LayoutRequestFactory.FromDocument(document, _ => new Size(120, 60));

        request.Nodes.Should().HaveCount(2);
        request.Nodes[0].Width.Should().Be(120);
        request.Nodes[0].Height.Should().Be(60);
        request.Nodes[0].Ports!.Single().Name.Should().Be("out");
        request.Nodes[0].Ports!.Single().Offset.Should().Be(0.25);
        request.Edges.Single().FromPort.Should().Be("out");
    }

    [Fact]
    [Trait("Category", "Layout")]
    public void Size_is_asked_from_the_caller_per_node()
    {
        // 尺寸取决于字体、字号与文本长度，IR 里没有也不该有。
        var document = Document(nodes: [new NodeDef { Id = "短" }, new NodeDef { Id = "很长很长的标签" }]);

        var request = LayoutRequestFactory.FromDocument(
            document,
            node => node.Id == "短" ? new Size(60, 30) : new Size(200, 30));

        request.Nodes.Single(n => n.Id == "短").Width.Should().Be(60);
        request.Nodes.Single(n => n.Id == "很长很长的标签").Width.Should().Be(200);
    }

    [Fact]
    [Trait("Category", "Layout")]
    public void Pins_come_from_the_provided_mapping_not_from_the_ir()
    {
        // 坐标属于渲染结果。存进 IR 会让同一份语义在不同机器上产生不同的文档内容。
        var document = Document(nodes: [new NodeDef { Id = "a" }, new NodeDef { Id = "b" }]);

        var request = LayoutRequestFactory.FromDocument(
            document,
            _ => new Size(80, 40),
            new Dictionary<string, LayoutPoint>(StringComparer.Ordinal) { ["a"] = new LayoutPoint(300, 200) });

        request.Nodes.Single(n => n.Id == "a").Pinned.Should().Be(new LayoutPoint(300, 200));
        request.Nodes.Single(n => n.Id == "b").Pinned.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Layout")]
    public void Direction_spacings_and_same_rank_come_from_the_document()
    {
        var hints = new LayoutHints
        {
            NodeSpacing = 55,
            LayerSpacing = 99,
            SameRank =
            [
                new Constraint<SameRankConstraint>(
                    new SameRankConstraint(["a", "b"]),
                    ConstraintOwner.Human,
                    DateTimeOffset.UnixEpoch),
            ],
        };

        var document = Document(
            nodes: [new NodeDef { Id = "a" }, new NodeDef { Id = "b" }],
            direction: Direction.LR,
            layout: hints);

        var request = LayoutRequestFactory.FromDocument(document, _ => new Size(80, 40));

        request.ToRequest().Options.Direction.Should().Be(Direction.LR);
        request.ToRequest().Options.NodeSpacing.Should().Be(55);
        request.ToRequest().Options.LayerSpacing.Should().Be(99);
        request.ToRequest().Options.SameRankGroups.Should().ContainSingle()
            .Which.Should().Equal("a", "b");
    }

    [Fact]
    [Trait("Category", "Layout")]
    public void Constraints_are_taken_regardless_of_who_declared_them()
    {
        // 归属方决定的是冲突时听谁的，而冲突已经在进布局之前处理掉了。
        // 到了这一层，留下的每一条约束都应当被执行。
        var hints = new LayoutHints
        {
            SameRank =
            [
                new Constraint<SameRankConstraint>(
                    new SameRankConstraint(["a", "b"]),
                    ConstraintOwner.Llm,
                    DateTimeOffset.UnixEpoch),
            ],
        };

        var document = Document(
            nodes: [new NodeDef { Id = "a" }, new NodeDef { Id = "b" }],
            layout: hints);

        LayoutRequestFactory.FromDocument(document, _ => new Size(80, 40))
            .ToRequest().Options.SameRankGroups.Should().ContainSingle();
    }

    [Fact]
    [Trait("Category", "Layout")]
    public void A_document_lays_out_end_to_end()
    {
        var hints = new LayoutHints
        {
            SameRank =
            [
                new Constraint<SameRankConstraint>(
                    new SameRankConstraint(["a", "b"]),
                    ConstraintOwner.Human,
                    DateTimeOffset.UnixEpoch),
            ],
        };

        var document = Document(
            nodes: [new NodeDef { Id = "start" }, new NodeDef { Id = "a" }, new NodeDef { Id = "b" }],
            edges:
            [
                new EdgeDef { Id = "e1", From = "start", To = "a" },
                new EdgeDef { Id = "e2", From = "start", To = "b" },
            ],
            layout: hints);

        var result = Compute(LayoutRequestFactory.FromDocument(document, _ => new Size(80, 40)));

        result.Nodes.Should().HaveCount(3);
        result.Find("a")!.Y.Should().Be(result.Find("b")!.Y, "同层约束来自文档");
        result.Diagnostics.SatisfiesHardGuarantees.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Layout")]
    public void An_edge_between_two_subgraphs_satisfies_the_hard_guarantees()
    {
        // 放宽 IR 让边的端点可以是组合之后，布局这一侧原本跟不上：
        // 组合整个被忽略，端点落在组合上的边会走到"输入引用了不存在的节点"那条分支，
        // 于是**一份校验通过的文档，布局仍会报硬保证不满足**。
        var document = Document(
            nodes:
            [
                new NodeDef { Id = "a", Parent = "ods" },
                new NodeDef { Id = "b", Parent = "dwd" },
            ],
            edges: [new EdgeDef { Id = "e1", From = "ods", To = "dwd" }],
            composites:
            [
                new GroupDef { Id = "ods", Members = ["a"] },
                new GroupDef { Id = "dwd", Members = ["b"] },
            ]);

        var result = Compute(LayoutRequestFactory.FromDocument(document, _ => new Size(80, 40)));

        result.Diagnostics.UnresolvedEndpoints.Should().Be(0);
        result.Diagnostics.SatisfiesHardGuarantees.Should().BeTrue();
        result.Edges.Should().ContainSingle();
    }

    private static DiagramDocument Document(
        IReadOnlyList<NodeDef>? nodes = null,
        IReadOnlyList<EdgeDef>? edges = null,
        IReadOnlyList<CompositeDef>? composites = null,
        Direction direction = Direction.TB,
        LayoutHints? layout = null) => new(
            "layout-test",
            DiagramKind.Flowchart,
            direction,
            version: 0,
            structuralHash: "s",
            visualHash: "v",
            pages: null,
            layers: null,
            nodes,
            edges,
            composites,
            tags: null,
            actions: null,
            fonts: null,
            textPresets: null,
            palette: null,
            layout,
            canvas: null);
}
