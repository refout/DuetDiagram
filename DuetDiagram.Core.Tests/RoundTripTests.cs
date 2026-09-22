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
/// IR 与各类载荷的序列化往返。
/// </summary>
/// <remarks>
/// 断言方式是"序列化结果逐字符相同"而不是逐字段比较。
/// 逐字段比较需要为每个类型写一遍比较逻辑，漏掉任何一个字段都会让往返损失悄悄溜过去；
/// 而序列化结果是一次性的整体表示，只要它相同，就没有任何字段被丢失或改写。
/// </remarks>
public sealed class RoundTripTests
{
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Document_round_trips_losslessly()
    {
        using var harness = new Harness();
        harness.AddNode("start", "开始", NodeShape.Stadium);
        harness.AddNode("check", "校验", NodeShape.Diamond);
        harness.AddNode("fail", "失败", NodeShape.Rect, ChangeSource.Llm);
        harness.Connect("e1", "start", "check", "是");
        harness.Connect("e2", "check", "fail", "否");

        var json = DiagramSerializer.SerializeFull(harness.Document);
        var restored = DiagramSerializer.DeserializeFull(json);

        restored.Id.Should().Be("test-doc");
        restored.Kind.Should().Be(DiagramKind.Flowchart);
        restored.Direction.Should().Be(Direction.LR);
        restored.Version.Should().Be(5);
        restored.StructuralHash.Should().Be(harness.Document.StructuralHash);
        restored.VisualHash.Should().Be(harness.Document.VisualHash);
        restored.Nodes.Should().Equal(harness.Document.Nodes);
        restored.Edges.Should().Equal(harness.Document.Edges);

        // 再序列化一次必须完全一样。这条断言才是往返无损的真正证据。
        DiagramSerializer.Normalize(restored).Should().Be(json);
    }

    /// <summary>
    /// 端点是组合的边要能原样往返。
    /// </summary>
    /// <remarks>
    /// 端点允许是组合之后，线格式不用改——<c>from</c> / <c>to</c> 本来就是字符串。
    /// 但"不用改"是推断，这条用例把它变成验证过的：真要有人日后给端点加上类型前缀，
    /// 这里会红。
    /// </remarks>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Edge_to_a_composite_round_trips()
    {
        var document = IrFixtures.WithEdge(
            IrFixtures.WithComposites(
                IrFixtures.Base(),
                [new GroupDef { Id = "ods", Label = "原始层" }, new GroupDef { Id = "dwd", Label = "明细层" }]),
            new EdgeDef { Id = "e1", From = "ods", To = "dwd", Label = "清洗" });

        var json = DiagramSerializer.SerializeFull(document);
        var restored = DiagramSerializer.DeserializeFull(json);

        restored.Edges.Should().Equal(document.Edges);
        restored.Composites.Should().Equal(document.Composites);
        DiagramSerializer.Normalize(restored).Should().Be(json);
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Empty_document_round_trips()
    {
        using var harness = new Harness();
        harness.AddNode("a");

        // 走一遍"加了再删"，让文档里留下非零的版本号和哈希，覆盖"空集合但状态不为零"的边界。
        harness.Bus.Execute(new RemoveNodeCommand("a").WithContext(ChangeContext.For(ChangeSource.Human)));

        var json = DiagramSerializer.SerializeFull(harness.Document);
        var restored = DiagramSerializer.DeserializeFull(json);

        DiagramSerializer.Normalize(restored).Should().Be(json);
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Every_memento_kind_round_trips_to_its_concrete_type()
    {
        var node = new NodeDef { Id = "n1", Label = "标签", Shape = NodeShape.Hexagon };
        var edge = new EdgeDef
        {
            Id = "e1",
            From = "n1",
            To = "n2",
            Label = "是",
            Style = new EdgeStyle { Line = LineStyle.Dashed },
        };

        CommandMemento[] mementos =
        [
            new AddNodeMemento { Node = node, Index = 3, AffectedIds = ["n1"] },
            new RemoveNodeMemento { Node = node, Index = 1, RemovedEdges = [new EdgePlacement(2, edge)], AffectedIds = ["n1", "e1"] },
            new ConnectEdgeMemento { Edge = edge, Index = 7, AffectedIds = ["e1"] },
            new ReconnectEdgeMemento { EdgeId = "e1", Previous = edge, AffectedIds = ["e1"] },
            new SetEdgeFieldMemento { EdgeId = "e1", Previous = edge, Field = "label", OldValue = "否", NewValue = "是", AffectedIds = ["e1"] },
        ];

        foreach (var memento in mementos)
        {
            var json = DiagramSerializer.SerializeMemento(memento);
            var restored = DiagramSerializer.DeserializeMemento(json);

            // 类型必须精确还原。若多态标签缺失或写错，这里会退化成基类实例，
            // 后续按具体类型做强制转换时就会抛异常——那正是要提前拦住的失败。
            restored.GetType().Should().Be(memento.GetType());

            // 记录类型里的数组字段是引用比较，所以用再序列化的结果做等价判断。
            DiagramSerializer.SerializeMemento(restored).Should().Be(json);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Every_diff_kind_round_trips_to_its_concrete_type()
    {
        DiffResult[] diffs =
        [
            new EmptyDiff(),
            new InvalidDiff(),
            new FullSnapshotDiff { Version = 4, FullJson = "{\"id\":\"d\"}" },
            new ReferenceDiff { Version = 4, BaseVersion = 2, StructuralHash = "abc", AffectedIds = ["n1"] },
            new EntriesDiff
            {
                Entries =
                [
                    new VersionEntry
                    {
                        Version = 3,
                        CommandId = "add-node",
                        Source = ChangeSource.Llm,
                        Timestamp = DateTimeOffset.UnixEpoch,
                        AffectedIds = ["n1"],
                    },
                ],
            },
        ];

        foreach (var diff in diffs)
        {
            var json = DiagramSerializer.SerializeDiff(diff);
            var restored = DiagramSerializer.DeserializeDiff(json);

            restored.GetType().Should().Be(diff.GetType());
            DiagramSerializer.SerializeDiff(restored).Should().Be(json);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Change_notification_round_trips()
    {
        var notification = new Broadcasting.ChangeNotification
        {
            DocumentId = "doc",
            Version = 9,
            AffectedIds = ["n1", "e1"],
            Source = ChangeSource.Mcp,
            Timestamp = DateTimeOffset.UnixEpoch,
        };

        var json = DiagramSerializer.SerializeNotification(notification);
        var restored = DiagramSerializer.DeserializeNotification(json);

        DiagramSerializer.SerializeNotification(restored).Should().Be(json);
        restored.AffectedIds.Should().Equal("n1", "e1");
    }
}
