using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 连边、重连端点、改边字段这三条命令。
/// </summary>
/// <remarks>
/// 重心与 <see cref="NodeFieldTests"/> 同构：字段表、读写实现与命令三者有没有对上；
/// 以及失败整条回滚、撤销逐字节还原这两件事。端点允许是组合这一条是边特有的，
/// 单独有用例覆盖"端口不能落在组合上"。
/// </remarks>
public sealed class EdgeCommandTests
{
    #region 原子性

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Reconnect_to_a_missing_edge_is_rolled_back()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");

        var before = harness.Snapshot();
        var versionBefore = harness.Document.Version;

        var result = harness.Bus.Execute(
            new ReconnectEdgeCommand("ghost", "a", null, "b", null)
                .WithContext(ChangeContext.For(ChangeSource.Human, "tester")));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.EdgeMissing);
        harness.Snapshot().Should().Be(before);
        harness.Document.Version.Should().Be(versionBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Reconnect_reports_endpoint_and_port_problems_together()
    {
        var document = IrFixtures.Populated();

        // ghost 不是端点，g1 是组合却带了端口名。两种处置不同，必须一次报全。
        var result = new ReconnectEdgeCommand("e1", "ghost", "p", "g1", "q").Validate(document);

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.Code)
            .Should().BeEquivalentTo([ErrorCodes.EdgeSourceMissing, ErrorCodes.EdgePortOnComposite]);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Reconnect_to_a_port_on_a_composite_is_rejected()
    {
        var document = IrFixtures.Populated();

        var result = new ReconnectEdgeCommand("e1", "g1", "x", "b", null).Validate(document);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.EdgePortOnComposite);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void SetEdgeField_rejected_for_unknown_field_rolls_back()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.Connect("e1", "a", "b");

        var before = harness.Snapshot();

        var result = harness.Bus.Execute(
            new SetEdgeFieldCommand("e1", "no-such-field", "x")
                .WithContext(ChangeContext.For(ChangeSource.Human, "tester")));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.FieldUnknown);
        harness.Snapshot().Should().Be(before);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void SetEdgeField_on_a_missing_edge_is_rejected()
    {
        using var harness = new Harness();

        var result = harness.Bus.Execute(
            new SetEdgeFieldCommand("ghost", FieldNames.Label, "x")
                .WithContext(ChangeContext.For(ChangeSource.Human, "tester")));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.EdgeMissing);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void SetEdgeField_with_a_malformed_style_is_rejected()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.Connect("e1", "a", "b");

        var before = harness.Snapshot();
        var versionBefore = harness.Document.Version;

        var result = harness.Bus.Execute(
            new SetEdgeFieldCommand("e1", FieldNames.Style, "{ not json")
                .WithContext(ChangeContext.For(ChangeSource.Human, "tester")));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.FieldValueInvalid);
        result.Errors[0].Payload.Should().NotBeNullOrWhiteSpace();
        harness.Snapshot().Should().Be(before);
        harness.Document.Version.Should().Be(versionBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Reconnect_to_the_same_endpoints_is_a_no_op()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.Connect("e1", "a", "b");

        var versionBefore = harness.Document.Version;
        var undoBefore = harness.Context.History.UndoCount;

        var result = harness.Bus.Execute(
            new ReconnectEdgeCommand("e1", "a", null, "b", null)
                .WithContext(ChangeContext.For(ChangeSource.Human, "tester")));

        result.IsNoOp.Should().BeTrue();
        harness.Document.Version.Should().Be(versionBefore);
        harness.Context.History.UndoCount.Should().Be(undoBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Reconnect_undo_restores_the_previous_edge_byte_for_byte()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AddNode("c");
        harness.Connect("e1", "a", "b");

        var original = harness.Document.Edges[0];

        harness.Bus.Execute(
            new ReconnectEdgeCommand("e1", "a", null, "c", null)
                .WithContext(ChangeContext.For(ChangeSource.Human, "tester")));

        harness.Document.Edges[0].To.Should().Be("c");

        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        // 撤销把整条边放回去，逐字段与逐字节都应与原来那份一致。
        // memento 存的是整条边，少还原一个字段只会体现在哈希上、界面上看不出来。
        harness.Document.Edges[0].Should().Be(original);

        harness.Bus.Redo().IsSuccess.Should().BeTrue();
        harness.Document.Edges[0].To.Should().Be("c");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void SetEdgeField_undo_restores_the_previous_edge()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.Connect("e1", "a", "b", "是");

        harness.Bus.Execute(
            new SetEdgeFieldCommand("e1", FieldNames.Label, "否")
                .WithContext(ChangeContext.For(ChangeSource.Human, "tester")));

        harness.Document.Edges[0].Label.Should().Be("否");

        harness.Bus.Undo().IsSuccess.Should().BeTrue();
        harness.Document.Edges[0].Label.Should().Be("是");

        harness.Bus.Redo().IsSuccess.Should().BeTrue();
        harness.Document.Edges[0].Label.Should().Be("否");
    }

    #endregion

    #region 边字段

    [Fact]
    [Trait("Category", "EdgeField")]
    public void Writable_fields_are_exactly_the_registered_edge_fields()
    {
        var registered = FieldRegistry.All
            .Where(f => f.Owner == "边")
            .Select(f => f.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        EdgeFieldValue.Writable.Order(StringComparer.Ordinal).Should().Equal(registered);
    }

    [Fact]
    [Trait("Category", "EdgeField")]
    public void Element_level_names_are_not_writable_on_edges()
    {
        EdgeFieldValue.IsWritable(FieldNames.EdgeElement).Should().BeFalse();
        EdgeFieldValue.IsWritable(FieldNames.NodeElement).Should().BeFalse();
        EdgeFieldValue.IsWritable(null).Should().BeFalse();
        EdgeFieldValue.IsWritable("no-such-field").Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "EdgeField")]
    public void A_missing_edge_is_rejected_before_touching_the_document()
    {
        using var harness = new Harness();

        var result = harness.Bus.Execute(
            new SetEdgeFieldCommand("ghost", FieldNames.Label, "x")
                .WithContext(ChangeContext.For(ChangeSource.Human, "tester")));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.EdgeMissing);
    }

    [Fact]
    [Trait("Category", "EdgeField")]
    public void Writing_the_same_value_is_a_no_op()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.Connect("e1", "a", "b", "是");

        var versionBefore = harness.Document.Version;

        var result = harness.Bus.Execute(
            new SetEdgeFieldCommand("e1", FieldNames.Label, "是")
                .WithContext(ChangeContext.For(ChangeSource.Human, "tester")));

        result.IsNoOp.Should().BeTrue();
        harness.Document.Version.Should().Be(versionBefore);
        harness.Context.History.UndoCount.Should().Be(3);
    }

    [Fact]
    [Trait("Category", "EdgeField")]
    public void A_successful_label_write_advances_the_version()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.Connect("e1", "a", "b", "是");

        var versionBefore = harness.Document.Version;
        var hashBefore = harness.Document.VisualHash;

        var result = harness.Bus.Execute(
            new SetEdgeFieldCommand("e1", FieldNames.Label, "否")
                .WithContext(ChangeContext.For(ChangeSource.Human, "tester")));

        result.IsEffectiveSuccess.Should().BeTrue();
        harness.Document.Version.Should().Be(versionBefore + 1);
        harness.Document.Edges[0].Label.Should().Be("否");
        harness.Document.VisualHash.Should().NotBe(hashBefore);

        var change = result.FieldChanges.Should().ContainSingle().Subject;
        change.ElementId.Should().Be("e1");
        change.Field.Should().Be(FieldNames.Label);
        change.OldValue.Should().Be("是");
        change.NewValue.Should().Be("否");
    }

    [Fact]
    [Trait("Category", "EdgeField")]
    public void A_successful_style_write_is_reflected_in_the_edge()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.Connect("e1", "a", "b");

        harness.Bus.Execute(
            new SetEdgeFieldCommand("e1", FieldNames.Style, "{\"line\":\"Dashed\",\"weight\":2}")
                .WithContext(ChangeContext.For(ChangeSource.Human, "tester")));

        harness.Document.Edges[0].Style.Line.Should().Be(LineStyle.Dashed);
        harness.Document.Edges[0].Style.Weight.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "EdgeField")]
    public void The_memento_survives_a_serialization_round_trip()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.Connect("e1", "a", "b", "是");

        var memento = new SetEdgeFieldCommand("e1", FieldNames.Label, "Z").CaptureMemento(harness.Document);

        var restored = DiagramSerializer.DeserializeMemento(DiagramSerializer.SerializeMemento(memento));

        restored.Should().BeOfType<SetEdgeFieldMemento>();
        restored.AffectedIds.Should().Equal("e1");

        new SetEdgeFieldCommand("e1", FieldNames.Label, "Z").RestoreMemento(harness.Document, restored);

        harness.Document.Edges[0].Label.Should().Be("是");
    }

    [Fact]
    [Trait("Category", "EdgeField")]
    public void The_reconnect_memento_survives_a_serialization_round_trip()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.Connect("e1", "a", "b");

        var memento = new ReconnectEdgeCommand("e1", "a", null, "b", null).CaptureMemento(harness.Document);

        var restored = DiagramSerializer.DeserializeMemento(DiagramSerializer.SerializeMemento(memento));

        restored.Should().BeOfType<ReconnectEdgeMemento>();
        restored.AffectedIds.Should().Equal("e1");
    }

    [Fact]
    [Trait("Category", "EdgeField")]
    public void Restoring_a_field_on_an_edge_that_is_gone_does_nothing()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.Connect("e1", "a", "b", "是");

        var memento = new SetEdgeFieldCommand("e1", FieldNames.Label, "否").CaptureMemento(harness.Document);

        harness.Bus.Execute(new RemoveNodeCommand("a").WithContext(ChangeContext.For(ChangeSource.Human, "tester")));
        var after = harness.Snapshot();

        // 还原一条已经不存在的边的字段，逆操作是"改回去"而不是"把它插回来"。
        new SetEdgeFieldCommand("e1", FieldNames.Label, "否").RestoreMemento(harness.Document, memento);

        harness.Snapshot().Should().Be(after);
    }

    #endregion
}
