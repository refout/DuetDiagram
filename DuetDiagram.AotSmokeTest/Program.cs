using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Core.Workspace;

namespace DuetDiagram.AotSmokeTest;

/// <summary>
/// P1 判据 #9 / P1-18：Memento 在 AOT 下必须能编译、序列化、反序列化并还原文档。
/// 本程序不做花哨输出，退出码 0 即为通过，供 CI 直接断言。
/// </summary>
internal static class Program
{
    private static async Task<int> Main()
    {
        try
        {
            await RunAsync().ConfigureAwait(false);
            Console.WriteLine("AOT smoke test: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"AOT smoke test: FAIL - {ex.Message}");
            return 1;
        }
    }

    private static async Task RunAsync()
    {
        var document = new DiagramDocument("aot-smoke", DiagramKind.Flowchart, Direction.LR);
        var session = new SimpleSessionProvider("smoke-actor", SessionIds.Gui("aot"));
        var options = DiagramCommandBusOptions.ForGui();

        await using var workspace = DiagramWorkspace.CreateOwned(document, session, options);

        var start = new NodeDef { Id = "start", Label = "开始", Shape = NodeShape.Stadium };
        var check = new NodeDef { Id = "check", Label = "校验", Shape = NodeShape.Diamond };
        var fail = new NodeDef { Id = "fail", Label = "失败", StyleToken = "danger" };

        Assert(workspace.CommandBus.Execute(new AddNodeCommand(start).WithContext(Context(ChangeSource.Human))).IsEffectiveSuccess, "add start");
        Assert(workspace.CommandBus.Execute(new AddNodeCommand(check).WithContext(Context(ChangeSource.Llm))).IsEffectiveSuccess, "add check");
        Assert(workspace.CommandBus.Execute(new AddNodeCommand(fail).WithContext(Context(ChangeSource.Llm))).IsEffectiveSuccess, "add fail");

        var edge = new EdgeDef { Id = "e-start-check", From = "start", To = "check", Label = "是" };
        Assert(workspace.CommandBus.Execute(new ConnectEdgeCommand(edge).WithContext(Context(ChangeSource.Llm))).IsEffectiveSuccess, "connect");

        Assert(document.Version == 4, "version after 4 commands");
        Assert(document.Nodes.Count == 3 && document.Edges.Count == 1, "document content");

        // IR 往返无损（AOT 路径：源生成，无反射）
        var json = DiagramSerializer.SerializeFull(document);
        var restored = DiagramSerializer.DeserializeFull(json);
        Assert(DiagramSerializer.Normalize(restored) == json, "IR round trip");

        // 多态 Memento 往返：每一种派生类型都必须能被 AOT 反序列化
        var mementos = new CommandMemento[]
        {
            new AddNodeMemento { Node = start, Index = 0, AffectedIds = ["start"] },
            new RemoveNodeMemento { Node = check, Index = 1, RemovedEdges = [new EdgePlacement(0, edge)], AffectedIds = ["check"] },
            new ConnectEdgeMemento { Edge = edge, Index = 0, AffectedIds = ["e-start-check"] },
        };

        foreach (var memento in mementos)
        {
            var payload = DiagramSerializer.SerializeMemento(memento);
            var roundTripped = DiagramSerializer.DeserializeMemento(payload);
            Assert(roundTripped.GetType() == memento.GetType(), $"memento type {memento.GetType().Name}");
        }

        // 撤销 / 重做在 AOT 下走通
        Assert(workspace.CommandBus.Undo().IsEffectiveSuccess, "undo");
        Assert(document.Edges.Count == 0, "edge removed by undo");
        Assert(workspace.CommandBus.Redo().IsEffectiveSuccess, "redo");
        Assert(document.Edges.Count == 1, "edge restored by redo");

        // 删除节点连带删除边，再撤销还原
        var removed = workspace.CommandBus.Execute(new RemoveNodeCommand("check").WithContext(Context(ChangeSource.Human)));
        Assert(removed.IsEffectiveSuccess && document.Nodes.Count == 2 && document.Edges.Count == 0, "cascade remove");
        Assert(workspace.CommandBus.Undo().IsEffectiveSuccess && document.Nodes.Count == 3 && document.Edges.Count == 1, "restore cascade");
    }

    private static ChangeContext Context(ChangeSource source) => ChangeContext.For(source, "aot-smoke-actor");

    private static void Assert(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"assertion failed: {what}");
        }
    }
}
