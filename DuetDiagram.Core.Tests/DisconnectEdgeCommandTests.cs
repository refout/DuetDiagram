using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 删除边的命令。
/// </summary>
/// <remarks>
/// <para>
/// 重心有三处。第一处是**波及面**：删边只该动那一条边，端点节点与别的边都不能跟着变。
/// 第二处是**撤销时的位置**：边要插回原来的索引，而两个哈希都按标识排序后再遍历，
/// 所以位置错了哈希照样对得上——只有专门比顺序的用例能发现。
/// 第三处是**故意留下的悬空引用**：引用了这条边的约束不清理，要能断言它确实还在。
/// </para>
/// <para>
/// 这几条合起来才说明"删一条边"这件事被完整定义了，缺任何一条都会留下一类
/// 在界面上看不出来、只有靠哈希或校验器才能发现的偏差。
/// </para>
/// </remarks>
public sealed class DisconnectEdgeCommandTests
{
    #region 失败路径

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Disconnecting_a_missing_edge_is_rejected_and_rolls_back()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");

        var before = harness.Snapshot();
        var versionBefore = harness.Document.Version;
        var undoBefore = harness.Context.History.UndoCount;

        var result = harness.Bus.Execute(
            new DisconnectEdgeCommand("ghost")
                .WithContext(ChangeContext.For(ChangeSource.Human, "tester")));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.EdgeMissing);
        harness.Snapshot().Should().Be(before);
        harness.Document.Version.Should().Be(versionBefore);
        harness.Context.History.UndoCount.Should().Be(undoBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_blank_edge_id_is_rejected_before_the_document_is_touched()
    {
        using var harness = new Harness();

        // 标识为空属于参数错误，连一条命令都不该构造出来。
        var build = () => new DisconnectEdgeCommand("   ");

        build.Should().Throw<ArgumentException>();
        harness.Document.Edges.Should().BeEmpty();
    }

    #endregion

    #region 波及面

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Disconnecting_removes_only_that_edge()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AddNode("c");
        harness.Connect("e1", "a", "b");
        harness.Connect("e2", "b", "c");

        var result = harness.Bus.Execute(
            new DisconnectEdgeCommand("e1")
                .WithContext(ChangeContext.For(ChangeSource.Human, "tester")));

        result.IsEffectiveSuccess.Should().BeTrue();
        result.AffectedIds.Should().Equal("e1");
        result.StructuralChanged.Should().BeTrue("边的增删要重新求解布局");
        result.VisualChanged.Should().BeTrue();

        harness.Document.Edges.Select(e => e.Id).Should().Equal("e2");

        // 端点节点一个都不能少：删边不是删节点，节点因为失去一条边而改变是没有道理的。
        harness.Document.Nodes.Select(n => n.Id).Should().Equal("a", "b", "c");

        var change = result.FieldChanges.Should().ContainSingle().Subject;
        change.ElementId.Should().Be("e1");
        change.Field.Should().Be(FieldNames.EdgeElement);
        change.OldValue.Should().Be("a->b");
        change.NewValue.Should().BeNull();
        change.Kind.Should().Be(ChangeKind.Removed);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void A_constraint_that_referenced_the_edge_is_left_alone()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AddNode("c");
        harness.Connect("e1", "a", "b");
        harness.Connect("e2", "a", "c");

        // 层内次序存的是出边标识，所以这条约束会指向下面被删掉的那条边。
        harness.AddConstraint(LayoutConstraintSpec.Order("a", ["e1", "e2"]))
            .IsEffectiveSuccess.Should().BeTrue();

        harness.Bus.Execute(
            new DisconnectEdgeCommand("e1")
                .WithContext(ChangeContext.For(ChangeSource.Human, "tester")));

        // 约束原样留着，不顺手删掉。它引用了一条不存在的边，这件事由整体校验器报出来；
        // 命令里替用户删掉的话，用户失去的是一条自己设过的约束，而且没有任何提示。
        var constraint = harness.Document.Layout.Order.Should().ContainSingle().Subject;
        constraint.Value.NodeId.Should().Be("a");
        constraint.Value.Order.Should().Equal("e1", "e2");

        DiagramValidator.Validate(harness.Document)
            .Select(i => i.Code)
            .Should().Contain(ErrorCodes.LayoutOrderEdgeMissing);
    }

    #endregion

    #region 撤销与重做

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Undo_puts_the_edge_back_at_its_original_index()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AddNode("c");
        harness.AddNode("d");
        harness.Connect("e1", "a", "b");
        harness.Connect("e2", "b", "c");
        harness.Connect("e3", "c", "d");

        // 撤销会让版本往前走，所以整份序列化结果比不了——版本号是它的一部分。
        // 比内容用两个哈希（它们不含版本），比位置用集合的次序，两者合起来就是
        // "内容逐字节一致、顺序也一致"。
        var structuralBefore = harness.Document.StructuralHash;
        var visualBefore = harness.Document.VisualHash;
        var orderBefore = harness.Document.Edges.Select(e => e.Id).ToArray();
        var removedBefore = harness.Document.Edges[1];

        // 删中间那一条：追加到末尾与插回原位在这一步之后分得开，删首尾则分不开。
        harness.Bus.Execute(
            new DisconnectEdgeCommand("e2")
                .WithContext(ChangeContext.For(ChangeSource.Human, "tester")));

        harness.Document.Edges.Select(e => e.Id).Should().Equal("e1", "e3");

        harness.Bus.Undo().IsSuccess.Should().BeTrue();

        harness.Document.Edges.Select(e => e.Id).Should().Equal(orderBefore);
        harness.Document.Edges[1].Should().Be(removedBefore);
        harness.Document.StructuralHash.Should().Be(structuralBefore);
        harness.Document.VisualHash.Should().Be(visualBefore);
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Redo_removes_it_again_and_undo_redo_can_be_repeated()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.AddNode("c");
        harness.Connect("e1", "a", "b");
        harness.Connect("e2", "b", "c");

        var structuralBefore = harness.Document.StructuralHash;
        var orderBefore = harness.Document.Edges.Select(e => e.Id).ToArray();

        harness.Bus.Execute(
            new DisconnectEdgeCommand("e1")
                .WithContext(ChangeContext.For(ChangeSource.Human, "tester")));

        // 来回走两遍：还原逻辑会被撤销与重做两条路径共用，而"可重复执行"这件事
        // 只有真的执行两遍才验得到——只走一遍的话，插出两条一样的边也看不出来。
        for (var round = 0; round < 2; round++)
        {
            harness.Bus.Undo().IsSuccess.Should().BeTrue();
            harness.Document.Edges.Select(e => e.Id).Should().Equal(orderBefore);
            harness.Document.StructuralHash.Should().Be(structuralBefore);

            harness.Bus.Redo().IsSuccess.Should().BeTrue();
            harness.Document.Edges.Select(e => e.Id).Should().Equal("e2");
        }
    }

    #endregion

    #region 快照

    [Fact]
    [Trait("Category", "Atomicity")]
    public void The_memento_survives_a_serialization_round_trip()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.Connect("e1", "a", "b", "是");

        var memento = new DisconnectEdgeCommand("e1").CaptureMemento(harness.Document);

        var restored = DiagramSerializer.DeserializeMemento(DiagramSerializer.SerializeMemento(memento));

        // 走一遍序列化是验多态标签：标签漏了的话，普通测试全绿而原生编译下反序列化会失败。
        restored.Should().BeOfType<DisconnectEdgeMemento>();
        restored.AffectedIds.Should().Equal("e1");
        restored.InverseChanges.Should().ContainSingle()
            .Which.Kind.Should().Be(ChangeKind.Added, "撤销删除等于把边加回来");

        new DisconnectEdgeCommand("e1").RestoreMemento(harness.Document, restored);

        harness.Document.Edges.Select(e => e.Id).Should().Equal("e1");
    }

    [Fact]
    [Trait("Category", "Atomicity")]
    public void Restoring_when_the_edge_is_already_there_does_not_duplicate_it()
    {
        using var harness = new Harness();
        harness.AddNode("a");
        harness.AddNode("b");
        harness.Connect("e1", "a", "b");

        var memento = new DisconnectEdgeCommand("e1").CaptureMemento(harness.Document);
        var before = harness.Snapshot();

        // 边还在的时候执行还原，应当什么都不做。少了这一道判断，一次误调用就会
        // 让文档里出现两条标识相同的边，而那种文档要到校验器那里才会被报出来。
        new DisconnectEdgeCommand("e1").RestoreMemento(harness.Document, memento);
        new DisconnectEdgeCommand("e1").RestoreMemento(harness.Document, memento);

        harness.Snapshot().Should().Be(before);
    }

    #endregion
}
