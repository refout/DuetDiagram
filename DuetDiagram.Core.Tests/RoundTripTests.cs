using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Core.Tests;

/// <summary>P1 判据 #2：IR 往返无损。</summary>
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

        // 规范化 JSON 逐字节一致，才能作为原子性比较的基准（P1 判据 #7）。
        DiagramSerializer.Normalize(restored).Should().Be(json);
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Empty_document_round_trips()
    {
        using var harness = new Harness();
        harness.AddNode("a");
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
        var edge = new EdgeDef { Id = "e1", From = "n1", To = "n2", Label = "是", Line = LineStyle.Dashed };

        CommandMemento[] mementos =
        [
            new AddNodeMemento { Node = node, Index = 3, AffectedIds = ["n1"] },
            new RemoveNodeMemento { Node = node, Index = 1, RemovedEdges = [new EdgePlacement(2, edge)], AffectedIds = ["n1", "e1"] },
            new ConnectEdgeMemento { Edge = edge, Index = 7, AffectedIds = ["e1"] },
        ];

        foreach (var memento in mementos)
        {
            var json = DiagramSerializer.SerializeMemento(memento);
            var restored = DiagramSerializer.DeserializeMemento(json);

            // record 的数组字段是引用比较，所以用规范化 JSON 断言往返无损。
            restored.GetType().Should().Be(memento.GetType());
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
