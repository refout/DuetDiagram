using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// IR 扩展后的往返、哈希口径、快照与只读约束。
/// </summary>
/// <remarks>
/// 校验器的用例单独放在 <see cref="ValidatorTests"/>，夹具见 <see cref="IrFixtures"/>。
/// </remarks>
public sealed class IrExtensionTests
{
    // ---- 往返 ----

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Full_ir_round_trips_losslessly()
    {
        var document = IrFixtures.Populated();

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
        var document = IrFixtures.Populated();

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
        var before = IrFixtures.Base();
        var after = IrFixtures.WithLayout(
            IrFixtures.Base(),
            new LayoutHints { NodeSpacing = 99, LayerSpacing = 99 });

        after.StructuralHash.Should().NotBe(before.StructuralHash);
    }

    [Fact]
    [Trait("Category", "IrHashing")]
    public void Port_change_alters_structural_hash()
    {
        // 端口不改变节点坐标，却改变连线的出入点，因此必须让坐标失效。
        var before = IrFixtures.WithNode(IrFixtures.Base(), new NodeDef { Id = "a", Label = "甲" });
        var after = IrFixtures.WithNode(IrFixtures.Base(), new NodeDef
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
        var before = IrFixtures.Base();
        var after = IrFixtures.WithFont(IrFixtures.Base(), new FontDef { Id = "f1", Name = "思源黑体" });

        after.StructuralHash.Should().NotBe(before.StructuralHash);
    }

    [Fact]
    [Trait("Category", "IrHashing")]
    public void Palette_change_alters_only_visual_hash()
    {
        // 换调色板不改变节点尺寸，坐标仍然有效，因此只该重绘。
        var before = IrFixtures.Base();
        var after = IrFixtures.WithPalette(IrFixtures.Base(), new Palette
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
        var before = IrFixtures.WithNode(IrFixtures.Base(), new NodeDef { Id = "a", Label = "甲" });
        var after = IrFixtures.WithNode(IrFixtures.Base(), new NodeDef
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
        var before = IrFixtures.Base();
        var after = IrFixtures.WithAction(
            IrFixtures.Base(),
            new ActionDef { Id = "a1", Event = "click", Kind = "open-url" });

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

        var first = IrFixtures.WithLayout(IrFixtures.Base(), IrFixtures.Hint(early));
        var second = IrFixtures.WithLayout(IrFixtures.Base(), IrFixtures.Hint(late));

        second.StructuralHash.Should().Be(first.StructuralHash);
    }

    [Fact]
    [Trait("Category", "IrHashing")]
    public void Structural_change_always_alters_visual_hash()
    {
        // 包含关系必须靠实现保证，而不是碰巧成立：
        // 早先的实现里视觉哈希漏掉了节点的父级，于是"结构变了视觉却没变"，
        // 而那种不一致没有任何地方会报错。
        var baseline = IrFixtures.Base();

        var variants = new[]
        {
            IrFixtures.WithLayout(IrFixtures.Base(), new LayoutHints { NodeSpacing = 12 }),
            IrFixtures.WithNode(IrFixtures.Base(), new NodeDef { Id = "a", Parent = "g1" }),
            IrFixtures.WithFont(IrFixtures.Base(), new FontDef { Id = "f1", Name = "某字体" }),
            IrFixtures.WithComposite(IrFixtures.Base(), new GroupDef { Id = "g1", Members = ["a"] }),
        };

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
        var document = IrFixtures.Base();
        var snapshot = document.TakeFullSnapshot();

        document.RestoreFromSnapshot(
            IrFixtures.WithLayout(document, new LayoutHints { NodeSpacing = 77 }).TakeFullSnapshot());

        snapshot.Layout.NodeSpacing.Should().Be(LayoutHintsDefaults.NodeSpacing);
        snapshot.Nodes.Should().HaveCount(1);
    }

    [Fact]
    [Trait("Category", "IrSnapshot")]
    public void Restore_brings_back_exact_content()
    {
        var document = IrFixtures.Populated();
        var snapshot = document.TakeFullSnapshot();
        var expected = DiagramSerializer.Normalize(document);

        document.RestoreFromSnapshot(IrFixtures.Base().TakeFullSnapshot());
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
        var mine = IrFixtures.Base();
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
        var document = IrFixtures.Base();

        (document.Nodes as List<NodeDef>).Should().BeNull();
        (document.Edges as List<EdgeDef>).Should().BeNull();
        (document.Composites as List<CompositeDef>).Should().BeNull();

        var act = () => ((IList<PageDef>)document.Pages).Add(new PageDef { Id = "p1" });

        act.Should().Throw<NotSupportedException>();
    }
}
