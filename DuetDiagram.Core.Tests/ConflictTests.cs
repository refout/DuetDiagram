using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 冲突判定与字段元数据。
/// </summary>
public sealed class ConflictTests
{
    // ---- 冲突结果必须携带差异 ----

    [Fact]
    [Trait("Category", "ConflictPolicy")]
    public void Conflict_result_carries_the_diff()
    {
        // 界面的处理方式是"弹可视化差异对话框"。差异算出来了却不带回去，
        // 用户只能看到一句"版本冲突"，无从知道冲突在哪。
        // 而且走到这一步时可能已经把整份文档序列化过了——算完丢掉等于白付一次代价。
        using var harness = new Harness(DiagramCommandBusOptions.ForMcp());

        harness.Bus.Execute(
            new AddNodeCommand(new NodeDef { Id = "a" }).WithContext(ChangeContext.For(ChangeSource.Mcp)),
            new VersionCheckRequest { ClientVersion = 0 });

        var stale = harness.Bus.Execute(
            new AddNodeCommand(new NodeDef { Id = "b" }).WithContext(ChangeContext.For(ChangeSource.Mcp)),
            new VersionCheckRequest { ClientVersion = 0 });

        stale.IsSuccess.Should().BeFalse();
        stale.Diff.Should().NotBeNull("冲突结果必须带上算出来的差异");
        stale.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.VersionConflict);
    }

    [Fact]
    [Trait("Category", "ConflictPolicy")]
    public void Diff_is_absent_when_there_is_no_conflict()
    {
        using var harness = new Harness(DiagramCommandBusOptions.ForMcp());

        var ok = harness.Bus.Execute(
            new AddNodeCommand(new NodeDef { Id = "a" }).WithContext(ChangeContext.For(ChangeSource.Mcp)),
            new VersionCheckRequest { ClientVersion = 0 });

        ok.IsEffectiveSuccess.Should().BeTrue();
        ok.Diff.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "ConflictPolicy")]
    public void A_version_ahead_of_the_document_is_a_parameter_error()
    {
        // 调用方版本更靠前说明参数传错了，重试不会好。
        // 与并发冲突混为一谈会让调用方陷入无意义的重试循环。
        using var harness = new Harness(DiagramCommandBusOptions.ForMcp());

        var ahead = harness.Bus.Execute(
            new AddNodeCommand(new NodeDef { Id = "a" }).WithContext(ChangeContext.For(ChangeSource.Mcp)),
            new VersionCheckRequest { ClientVersion = 99 });

        ahead.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.InvalidExpectedVersion);
        ahead.IsRetryable.Should().BeFalse("参数错误重试没有意义");
        ahead.Diff.Should().BeOfType<InvalidDiff>();
    }

    // ---- 合并判定 ----

    [Fact]
    [Trait("Category", "ConflictPolicy")]
    public void Changes_to_different_elements_do_not_conflict()
    {
        var assessment = ChangeConflict.Resolve(
            [Change("n1", FieldNames.Label, ChangeKind.Modified)],
            [Change("n2", FieldNames.Label, ChangeKind.Modified)]);

        assessment.Outcome.Should().Be(MergeOutcome.NoOverlap);
        assessment.CanMergeAutomatically.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "ConflictPolicy")]
    public void Changes_to_different_fields_of_one_element_are_mergeable()
    {
        // 一边改标签、一边改形状，互不相干。一律当冲突处理会让用户
        // 在最不需要干预的时候被打断。
        var assessment = ChangeConflict.Resolve(
            [Change("n1", FieldNames.Label, ChangeKind.Modified)],
            [Change("n1", FieldNames.Shape, ChangeKind.Modified)]);

        assessment.Outcome.Should().Be(MergeOutcome.Mergeable);
        assessment.SharedElementIds.Should().Equal("n1");
        assessment.Conflicts.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "ConflictPolicy")]
    public void Changes_to_the_same_field_conflict()
    {
        var assessment = ChangeConflict.Resolve(
            [Change("n1", FieldNames.Label, ChangeKind.Modified)],
            [Change("n1", FieldNames.Label, ChangeKind.Modified)]);

        assessment.Outcome.Should().Be(MergeOutcome.Conflicting);
        assessment.Conflicts.Should().ContainSingle()
            .Which.Should().Be(new FieldConflict("n1", FieldNames.Label, FieldNames.Label));
    }

    [Fact]
    [Trait("Category", "ConflictPolicy")]
    public void Editing_an_element_the_other_side_created_conflicts()
    {
        // 增删与修改的前提互斥：说"这是新加的"和说"我改了它"不可能同时成立。
        var assessment = ChangeConflict.Resolve(
            [Change("n1", FieldNames.NodeElement, ChangeKind.Added)],
            [Change("n1", FieldNames.Label, ChangeKind.Modified)]);

        assessment.Outcome.Should().Be(MergeOutcome.Conflicting);
    }

    [Fact]
    [Trait("Category", "ConflictPolicy")]
    public void Both_sides_creating_the_same_element_conflicts()
    {
        // 双方都认为自己是创建者，无法自动合并。
        var assessment = ChangeConflict.Resolve(
            [Change("n1", FieldNames.NodeElement, ChangeKind.Added)],
            [Change("n1", FieldNames.NodeElement, ChangeKind.Added)]);

        assessment.Outcome.Should().Be(MergeOutcome.Conflicting);
    }

    [Fact]
    [Trait("Category", "ConflictPolicy")]
    public void Repeated_field_names_do_not_change_the_verdict()
    {
        // 一次命令里连改两次同一个字段，判定结果不该受影响。
        var assessment = ChangeConflict.Resolve(
            [
                Change("n1", FieldNames.Label, ChangeKind.Modified),
                Change("n1", FieldNames.Label, ChangeKind.Modified),
            ],
            [Change("n1", FieldNames.Shape, ChangeKind.Modified)]);

        assessment.Outcome.Should().Be(MergeOutcome.Mergeable);
    }

    [Fact]
    [Trait("Category", "ConflictPolicy")]
    public void Empty_change_sets_never_conflict()
    {
        ChangeConflict.Resolve([], []).Outcome.Should().Be(MergeOutcome.NoOverlap);
        ChangeConflict.Resolve([], [Change("n1", FieldNames.Label, ChangeKind.Modified)])
            .Outcome.Should().Be(MergeOutcome.NoOverlap);
    }

    // ---- 字段元数据 ----

    [Fact]
    [Trait("Category", "FieldMetadata")]
    public void Every_field_emitted_by_commands_is_registered()
    {
        // 命令里写裸字符串曾经造成过一次静默漂移（错误码在两处各写一份，名字不一致
        // 而没有任何东西会报错）。这条断言保证字段名也不会走上同一条路：
        // 哪天新增的字段没登记，这里会先红。
        using var harness = new Harness();

        var results = new List<CommandResult>
        {
            harness.AddNode("a", "甲"),
            harness.AddNode("b", "乙"),
            harness.Connect("e1", "a", "b", "是"),
            harness.Bus.Execute(new RemoveNodeCommand("a").WithContext(ChangeContext.For(ChangeSource.Human))),
        };

        var fields = results
            .SelectMany(r => r.FieldChanges)
            .Select(c => c.Field)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        fields.Should().NotBeEmpty("这些命令确实会产生字段变更，否则这条断言是空转的");
        fields.Should().OnlyContain(f => FieldRegistry.IsKnown(f));
    }

    [Fact]
    [Trait("Category", "FieldMetadata")]
    public void Element_level_names_are_prefixed()
    {
        // 不加前缀的话 layer 有两种读法：图层元素被增删，还是某个节点的图层字段改了。
        // 靠标识也分不出来，因为标识本身不带类型信息。
        FieldNames.IsElementLevel(FieldNames.NodeElement).Should().BeTrue();
        FieldNames.IsElementLevel(FieldNames.LayerElement).Should().BeTrue();
        FieldNames.IsElementLevel(FieldNames.Layer).Should().BeFalse();
        FieldNames.IsElementLevel(FieldNames.Label).Should().BeFalse();
        FieldNames.IsElementLevel(null).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "FieldMetadata")]
    public void Same_field_name_has_the_same_scope_everywhere()
    {
        // 同一个名字在节点上是外观、在边上是结构的话，冲突判定与哈希口径会各按一半理解。
        // 登记表在装载时就会拒绝这种表，这里断言它确实装载成功了。
        FieldRegistry.Descriptor(FieldNames.Label)!.Scope.Should().Be(FieldScope.Visual);
        FieldRegistry.Descriptor(FieldNames.Parent)!.Scope.Should().Be(FieldScope.Structural);
        FieldRegistry.Descriptor(FieldNames.Meta)!.Scope.Should().Be(FieldScope.Neither);

        FieldRegistry.All.Should().NotBeEmpty();
    }

    [Fact]
    [Trait("Category", "FieldMetadata")]
    public void Unknown_fields_are_reported_as_unknown()
    {
        // 没登记就返回空，而不是猜一个作用域。猜错的后果是"改了它图没重排，
        // 看起来没生效"，这类问题极难定位。
        FieldRegistry.Descriptor("从未登记过的字段").Should().BeNull();
        FieldRegistry.IsKnown("从未登记过的字段").Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "FieldMetadata")]
    public void Registered_structural_fields_really_affect_the_structural_hash()
    {
        // 注册表说某个字段是结构类的，改了它就必须让结构哈希变化。
        // 这条断言把登记表与哈希实现绑在一起——两边哪天对不上，这里会红，
        // 而不是等到界面上出现"改了间距图没重排"才发现。
        var baseline = IrFixtures.Base();

        var cases = new (string Field, DiagramDocument Changed)[]
        {
            (FieldNames.Parent, IrFixtures.WithNode(IrFixtures.Base(), new NodeDef { Id = "a", Parent = "g1" })),
            (FieldNames.Ports, IrFixtures.WithNode(IrFixtures.Base(), new NodeDef
            {
                Id = "a",
                Ports = [new PortDef { Name = "out" }],
            })),
            (FieldNames.Members, IrFixtures.WithComposite(
                IrFixtures.Base(),
                new GroupDef { Id = "g1", Members = ["a"] })),
            (FieldNames.NodeSpacing, IrFixtures.WithLayout(
                IrFixtures.Base(),
                new LayoutHints { NodeSpacing = 7 })),
        };

        foreach (var (field, changed) in cases)
        {
            FieldRegistry.Descriptor(field)!.Scope.Should().Be(
                FieldScope.Structural,
                $"字段 {field} 在登记表里必须是结构类的");

            changed.StructuralHash.Should().NotBe(
                baseline.StructuralHash,
                $"登记表说 {field} 是结构类的，改了它就必须让结构哈希变化");
        }
    }

    [Fact]
    [Trait("Category", "FieldMetadata")]
    public void Registered_visual_only_fields_do_not_affect_the_structural_hash()
    {
        var baseline = IrFixtures.Base();

        var cases = new (string Field, DiagramDocument Changed)[]
        {
            (FieldNames.Shape, IrFixtures.WithNode(
                IrFixtures.Base(),
                new NodeDef { Id = "a", Shape = NodeShape.Diamond })),
            (FieldNames.StyleToken, IrFixtures.WithNode(
                IrFixtures.Base(),
                new NodeDef { Id = "a", StyleToken = "danger" })),
        };

        foreach (var (field, changed) in cases)
        {
            FieldRegistry.Descriptor(field)!.Scope.Should().Be(FieldScope.Visual);

            changed.StructuralHash.Should().Be(baseline.StructuralHash);
            changed.VisualHash.Should().NotBe(baseline.VisualHash);
        }
    }

    private static FieldChange Change(string elementId, string field, ChangeKind kind) => new()
    {
        ElementId = elementId,
        Field = field,
        Kind = kind,
    };
}
