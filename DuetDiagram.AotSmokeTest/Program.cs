using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Core.Shapes;
using DuetDiagram.Core.Workspace;
using DuetDiagram.Layout;

namespace DuetDiagram.AotSmokeTest;

/// <summary>
/// 原生 AOT 下的端到端冒烟。
/// </summary>
/// <remarks>
/// <para>
/// 走一遍完整的编辑流程：建文档、连总线、增删改、撤销重做、序列化往返。
/// 这些动作覆盖了所有可能被裁剪或依赖反射的地方——尤其是多态逆变更快照的反序列化，
/// 它是这条路径上最脆弱的一环：标签漏标一个，普通运行完全正常，原生发布后才失败。
/// </para>
/// <para>
/// 不做任何格式化输出，只用退出码表达结果，方便持续集成直接断言。
/// </para>
/// </remarks>
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

        // 文档往返：验证编译期生成的读写代码覆盖了全部字段。
        var json = DiagramSerializer.SerializeFull(document);
        var restored = DiagramSerializer.DeserializeFull(json);
        Assert(DiagramSerializer.Normalize(restored) == json, "IR round trip");

        // 多态快照往返：每一种派生类型都必须能被原生编译后的代码正确还原。
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

        // 撤销重做
        Assert(workspace.CommandBus.Undo().IsEffectiveSuccess, "undo");
        Assert(document.Edges.Count == 0, "edge removed by undo");
        Assert(workspace.CommandBus.Redo().IsEffectiveSuccess, "redo");
        Assert(document.Edges.Count == 1, "edge restored by redo");

        // 删节点连带删边，再撤销还原
        var removed = workspace.CommandBus.Execute(new RemoveNodeCommand("check").WithContext(Context(ChangeSource.Human)));
        Assert(removed.IsEffectiveSuccess && document.Nodes.Count == 2 && document.Edges.Count == 0, "cascade remove");
        Assert(workspace.CommandBus.Undo().IsEffectiveSuccess && document.Nodes.Count == 3 && document.Edges.Count == 1, "restore cascade");

        VerifyExtendedIr();
        VerifyShapeLibrary();
        VerifyLayoutEngine();
    }

    /// <summary>
    /// 形状库在原生下要能真的给出几何。
    /// </summary>
    /// <remarks>
    /// 形状表在构造时会枚举枚举的取值来校验"枚举里的形状都有人提供"，而枚举取值
    /// 正是原生下最容易出问题的一环：裁剪器可能把那份元数据剪掉，普通运行完全正常，
    /// 发布之后才在第一次建表时失败。所以这一段要真的建一次表、真的把八个几何都读一遍。
    /// 这也是"注册表不靠反射加载程序集"能不能上生产的前提。
    /// </remarks>
    private static void VerifyShapeLibrary()
    {
        var registry = ShapeRegistry.Default;

        Assert(registry.All.Count == Enum.GetValues<NodeShape>().Length, "shape table covers the enum");

        foreach (var shape in Enum.GetValues<NodeShape>())
        {
            var definition = registry.Find(shape);

            Assert(definition.Name == shape.ToString(), $"shape name for {shape}");
            Assert(definition.Geometry.Describe().Length > 0, $"geometry for {shape}");
        }

        Assert(registry.Find(NodeShape.Diamond).Geometry is PolygonOutline, "diamond is a polygon");
        Assert(registry.Find(NodeShape.Cylinder).Geometry is PathOutline, "cylinder is a path");
    }

    /// <summary>
    /// 扩展后的 IR：九个集合、三个子对象、多态组合。
    /// </summary>
    /// <remarks>
    /// 这一段是原生编译下最脆的地方。多态类型的标签漏标一个，
    /// 普通运行完全正常——反射会兜住——而原生发布之后才在第一次反序列化时失败。
    /// 源生成器不会为此报错，因此只能靠真的跑一遍。
    /// </remarks>
    private static void VerifyExtendedIr()
    {
        var document = BuildExtendedDocument();

        var json = DiagramSerializer.SerializeFull(document);
        var restored = DiagramSerializer.DeserializeFull(json);

        Assert(DiagramSerializer.Normalize(restored) == json, "extended IR round trip");

        // 派生类型的身份必须靠多态标签还原，不能退化成基类。
        // 退化了的话泳道会被当成普通分组处理，而且不会有任何报错。
        Assert(restored.Composites.OfType<GroupDef>().Count() == 1, "group restored as GroupDef");
        Assert(restored.Composites.OfType<LaneDef>().Count() == 1, "lane restored as LaneDef");

        Assert(restored.Nodes.Single(n => n.Id == "a").Ports.Count == 1, "ports restored");
        Assert(restored.Layout.SameRank.Count == 1, "layout hints restored");
        Assert(restored.Palette.Entries.ContainsKey("primary"), "palette restored");
        Assert(restored.Canvas.Grid == GridStyle.Dots, "canvas restored");
        Assert(restored.Fonts.Count == 1 && restored.Tags.Count == 1, "fonts and tags restored");

        // 校验器在原生下也要能跑通——它走的是同一套集合遍历。
        Assert(DiagramValidator.Validate(restored).Count == 0, "validator reports nothing");

        // 快照：深拷贝与整体恢复。
        var snapshot = document.TakeFullSnapshot();
        document.RestoreFromSnapshot(snapshot);
        Assert(DiagramSerializer.Normalize(document) == json, "snapshot restore");
    }

    /// <summary>
    /// 布局引擎在原生下要能真的算出坐标。
    /// </summary>
    /// <remarks>
    /// 布局依赖第三方引擎，而第三方包是否原生友好**分析器看不出来**——
    /// 分析器只看我们自己的代码。只有真的跑一遍才知道。
    /// 这一段的另一个用处是覆盖降级路径：计划校验、超时判定、尝试记录都会被执行。
    /// </remarks>
    private static void VerifyLayoutEngine()
    {
        var engine = new ConstraintLayoutEngine();
        var nodeA = new LayoutNode("a", 80, 40, new LayoutPoint(100, 100));
        var nodeB = new LayoutNode("b", 80, 40);

        var result = new LayoutCoordinator(engine).Compute(
            new LayoutJob(
                [nodeA, nodeB],
                [new LayoutEdge("e1", "a", "b")],
                Direction.TB,
                new LayoutHints()),
            LayoutBudgets.ManualRelayout);

        Assert(result.AppliedLevel == LayoutFallbackLevel.Full, "layout at the top level");
        Assert(result.Nodes.Length == 2, "layout placed both nodes");

        // 固定坐标是唯一的硬保证，原生下同样不能偏。
        var placed = result.Find("a")!;
        Assert(Math.Abs(placed.X - 100) < 0.01 && Math.Abs(placed.Y - 100) < 0.01, "pinned position exact");
        Assert(result.Diagnostics.SatisfiesHardGuarantees, "layout hard guarantees");
    }

    private static DiagramDocument BuildExtendedDocument() => new(
        "aot-extended",
        DiagramKind.Flowchart,
        Direction.TB,
        version: 7,
        structuralHash: "structural",
        visualHash: "visual",
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
                Style = new EdgeStyle { Line = LineStyle.Dashed, Route = EdgeRoute.Curved },
            },
        ],
        composites:
        [
            new GroupDef { Id = "g1", Label = "分组", Members = ["a"] },
            new LaneDef { Id = "lane1", Label = "泳道", Members = ["b"] },
        ],
        tags: [new TagDef { Id = "t1", Label = "重点", Members = ["a", "e1"] }],
        actions: [new ActionDef { Id = "act1", Event = "click", Kind = "open-url", Target = "a" }],
        fonts: [new FontDef { Id = "f1", Name = "思源黑体" }],
        textPresets: [new TextStylePreset { Id = "tp1", Name = "标题", Style = new TextStyle { FontSize = 20 } }],
        palette: new Palette
        {
            Entries = new Dictionary<string, PaletteEntry>(StringComparer.Ordinal)
            {
                ["primary"] = new PaletteEntry { Name = "primary", Fill = "#3366ff" },
            },
        },
        layout: new LayoutHints
        {
            NodeSpacing = 44,
            LayerSpacing = 88,
            SameRank =
            [
                new Constraint<SameRankConstraint>(
                    new SameRankConstraint(["a", "b"]),
                    ConstraintOwner.Human,
                    DateTimeOffset.UnixEpoch),
            ],
        },
        canvas: new CanvasSettings { Grid = GridStyle.Dots, Infinite = false });

    private static ChangeContext Context(ChangeSource source) => ChangeContext.For(source, "aot-smoke-actor");

    private static void Assert(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"assertion failed: {what}");
        }
    }
}
