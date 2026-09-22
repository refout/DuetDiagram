using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Logging;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Time;
using DuetDiagram.Llm.Context;
using DuetDiagram.Llm.Tools;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Llm.Tests;

/// <summary>
/// 上下文摘要：六块内容齐不齐、有没有混进不该有的东西、同一份文档是不是永远同一份摘要。
/// </summary>
public sealed class SummaryTests
{
    #region 六块内容

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void The_summary_carries_the_six_blocks()
    {
        var summary = SummaryBuilder.Build(new SummaryInput
        {
            Document = Sample(),
            Placement = Placement(("start", 0), ("check", 1), ("pass", 2), ("fail", 2)),
            PinnedNodes = ["check"],
            RecentChanges = [Entry(1, "set-node-field", Field("fail", "styleToken", "danger"))],
        });

        summary.Nodes.Select(node => node.Id).Should().Equal("check", "fail", "pass", "start");
        summary.Edges.Select(edge => edge.Id).Should().Equal("e1", "e2", "e3");
        summary.Layout.Direction.Should().Be(Direction.LR);
        summary.Layout.LayerCount.Should().Be(3);
        summary.PinnedNodes.Should().Equal("check");
        summary.StyleTokens.Should().Equal("danger", "muted", "primary", "success", "warning");
        summary.RecentChanges.Should().ContainSingle();
    }

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void The_text_has_one_line_per_block()
    {
        var text = Render(Sample(), Placement(("start", 0), ("check", 1), ("pass", 2), ("fail", 2)));

        text.Should().StartWith("图状态：");

        foreach (var title in new[] { "节点", "边", "布局", "锁定", "可用样式", "最近修改" })
        {
            text.Should().Contain($"- {title}：", $"六块里的「{title}」要在文本里各占一行");
        }

        text.Split('\n').Should().HaveCount(7, "一行抬头加六行内容");
    }

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void The_text_follows_the_shape_the_design_asks_for()
    {
        var document = Sample();

        var summary = SummaryBuilder.Build(new SummaryInput
        {
            Document = document,
            Placement = Placement(("start", 0), ("check", 1), ("pass", 2), ("fail", 2)),
            RecentChanges = [Entry(1, "set-node-field", Field("fail", "styleToken", "danger"))],
        });

        var text = SummaryFormatter.Format(summary, Now);

        text.Should().Contain("- 节点：check(校验,diamond), fail(失败), pass(成功), start(开始)");
        text.Should().Contain("- 边：start→check, check→pass\"是\", check→fail\"否\"");
        text.Should().Contain("- 布局：LR, 3层, fail/pass同层");
        text.Should().Contain("- 锁定：无");
        text.Should().Contain("- 可用样式：danger, muted, primary, success, warning");
        text.Should().Contain("- 最近修改：fail.styleToken = danger (system, 刚刚)");
    }

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void An_empty_document_says_so_instead_of_showing_nothing()
    {
        var text = Render(new DiagramDocument("empty"), []);

        text.Should().Contain("- 节点：无");
        text.Should().Contain("- 边：无");
        text.Should().Contain("- 锁定：无");
        text.Should().Contain("- 可用样式：无");
        text.Should().Contain("- 最近修改：无");

        // 没有层投影时只写方向：不自己按拓扑算一个，算了会与实际画面各按一套算法。
        text.Should().Contain("- 布局：TB");
    }

    #endregion

    #region 不含原始坐标

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void No_part_of_the_summary_is_a_raw_coordinate()
    {
        var visited = new HashSet<Type>();
        var offenders = new List<string>();

        Walk(typeof(SummaryPayload), visited, offenders);

        // 少了这两条，走图这件事本身坏掉时下面那条断言也会通过。
        visited.Should().Contain([typeof(DiagramSummary), typeof(SummaryNode), typeof(SummaryLayout)]);
        offenders.Should().BeEmpty(
            "摘要里出现浮点数，模型就会学着照那些数字去微调位置，而那些数字下次重排就全变了");
    }

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void The_text_carries_no_raw_coordinates()
    {
        var document = Sample();

        var text = Render(document, Placement(("start", 0), ("check", 1), ("pass", 2), ("fail", 2)));

        Regex.IsMatch(text, @"\d+\.\d+").Should().BeFalse(
            "文本里出现带小数点的数字，多半是把坐标漏进来了");
    }

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void The_layout_block_quantizes_position_into_a_layer_number()
    {
        var text = Render(Sample(), Placement(("start", 0), ("check", 1), ("pass", 2), ("fail", 2)));

        text.Should().Contain("3层");
        text.Should().Contain("fail/pass同层");
    }

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void Without_a_layer_projection_only_the_direction_is_reported()
    {
        var summary = SummaryBuilder.Build(new SummaryInput { Document = Sample() });

        summary.Layout.LayerCount.Should().BeNull();
        summary.Layout.SameLayerGroups.Should().BeEmpty();
        SummaryFormatter.Format(summary, Now).Should().Contain("- 布局：LR");
    }

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void A_layer_with_a_single_node_is_not_a_group()
    {
        var summary = SummaryBuilder.Build(new SummaryInput
        {
            Document = Sample(),
            Placement = Placement(("start", 0), ("check", 1), ("pass", 2), ("fail", 2)),
        });

        summary.Layout.SameLayerGroups.Should().HaveCount(1, "只有 pass 与 fail 是一组，其余两层各只有一个节点");
        summary.Layout.SameLayerGroups[0].Should().Equal("fail", "pass");
    }

    #endregion

    #region 归一化

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void The_same_document_summarizes_byte_for_byte()
    {
        var forward = Render(Sample(), []);
        var backward = Render(Sample(nodes: [.. SampleNodes.Reverse()], edges: [.. SampleEdges.Reverse()]), []);

        backward.Should().Be(forward, "同一份文档无论集合的插入顺序如何，摘要都要逐字节相同");
    }

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void Pinned_nodes_and_layer_groups_are_normalized_too()
    {
        var document = Sample();

        var forward = SummaryFormatter.Format(
            SummaryBuilder.Build(new SummaryInput
            {
                Document = document,
                Placement = Placement(("start", 0), ("check", 1), ("pass", 2), ("fail", 2)),
                PinnedNodes = ["check", "fail"],
            }),
            Now);

        var backward = SummaryFormatter.Format(
            SummaryBuilder.Build(new SummaryInput
            {
                Document = document,
                Placement = Placement(("fail", 2), ("pass", 2), ("check", 1), ("start", 0)),
                PinnedNodes = ["fail", "check", "fail"],
            }),
            Now);

        backward.Should().Be(forward);
    }

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void Recent_changes_are_capped_and_newest_first()
    {
        var changes = Enumerable.Range(1, 7)
            .Select(version => Entry(version, $"command-{version}", Field("n", "label", $"v{version}")))
            .ToArray();

        var summary = SummaryBuilder.Build(new SummaryInput
        {
            Document = Sample(),
            RecentChanges = [.. changes.Reverse()],
        });

        summary.RecentChanges.Should().HaveCount(SummaryBuilder.RecentChangeLimit);
        summary.RecentChanges.Select(change => change.Version).Should().Equal(7, 6, 5, 4, 3);
    }

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void A_bulk_change_is_reported_as_a_bulk_change()
    {
        var summary = SummaryBuilder.Build(new SummaryInput
        {
            Document = Sample(),
            RecentChanges =
            [
                new VersionEntry
                {
                    Version = 9,
                    CommandId = "import-mermaid",
                    Source = ChangeSource.Import,
                    Timestamp = Now,
                    IsBulkChange = true,
                    OriginalChangeCount = 4200,
                },
            ],
        });

        SummaryFormatter.Format(summary, Now).Should().Contain("批量变更 4200 项 (import, 刚刚)");
    }

    #endregion

    #region 令牌与变更都取自活的来源

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void Style_tokens_come_from_the_palette()
    {
        var before = SummaryBuilder.Build(new SummaryInput { Document = Sample() });
        var after = SummaryBuilder.Build(new SummaryInput
        {
            Document = Sample(palette: Palette("alpha", "beta")),
        });

        before.StyleTokens.Should().NotEqual(after.StyleTokens);
        after.StyleTokens.Should().Equal("alpha", "beta");
    }

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void Recent_changes_come_from_the_version_log()
    {
        var live = Live();

        live.Bus.Execute(new SetNodeFieldCommand("fail", FieldNames.StyleToken, "danger")
            .WithContext(ChangeContext.For(ChangeSource.Human, "alice")));

        live.Clock.Advance(TimeSpan.FromMinutes(2));

        var summary = SummaryBuilder.Build(new SummaryInput
        {
            Document = live.Document,
            RecentChanges = live.Bus.Context.VersionLog.Snapshot(),
        });

        SummaryFormatter.Format(summary, live.Clock.UtcNow)
            .Should().Contain("fail.styleToken = danger (human:alice, 2分钟前)");
    }

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void Changing_a_style_token_moves_the_recent_changes_line()
    {
        var live = Live();

        var before = SummaryFormatter.Format(BuildFrom(live), live.Clock.UtcNow);
        before.Should().Contain("- 最近修改：无");

        live.Bus.Execute(new SetNodeFieldCommand("fail", FieldNames.StyleToken, "danger")
            .WithContext(ChangeContext.For(ChangeSource.Llm)));

        var after = SummaryFormatter.Format(BuildFrom(live), live.Clock.UtcNow);

        after.Should().NotBe(before);
        after.Should().Contain("fail.styleToken = danger (llm, 刚刚)");
    }

    [Fact]
    [Trait("Category", "ContextSummary")]
    public void An_element_level_change_reads_as_an_addition_rather_than_a_field_write()
    {
        var summary = SummaryBuilder.Build(new SummaryInput
        {
            Document = Sample(),
            RecentChanges = [Entry(1, "add-node", Field("retry", FieldNames.NodeElement, "retry", ChangeKind.Added))],
        });

        SummaryFormatter.Format(summary, Now).Should().Contain("retry 已加入 (system, 刚刚)");
    }

    #endregion

    #region diagram_read

    [Fact]
    [Trait("Category", "ContextSummary")]
    public async Task The_read_tool_returns_the_text_and_the_structured_summary()
    {
        var registry = Registry(Sample(), Placement(("start", 0), ("check", 1), ("pass", 2), ("fail", 2)));

        var result = await registry.Invoke(
            DiagramToolset.Read,
            JsonDocument.Parse("{}").RootElement,
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Contain("4 个节点").And.Contain("3 条边");

        var data = result.Data!.Value;

        data.GetProperty("text").GetString().Should().StartWith("图状态：");
        data.GetProperty("summary").GetProperty("nodes").GetArrayLength().Should().Be(4);
    }

    [Fact]
    [Trait("Category", "ContextSummary")]
    public async Task The_read_tool_writes_enums_as_names_rather_than_numbers()
    {
        var registry = Registry(Sample(), []);

        var result = await registry.Invoke(
            DiagramToolset.Read,
            JsonDocument.Parse("{}").RootElement,
            TestContext.Current.CancellationToken);

        var data = result.Data!.Value;
        var layout = data.GetProperty("summary").GetProperty("layout");

        layout.GetProperty("direction").ValueKind.Should().Be(JsonValueKind.String);
        layout.GetProperty("direction").GetString().Should().Be("LR");
        data.GetProperty("summary").GetProperty("kind").GetString().Should().Be("Flowchart");
    }

    [Fact]
    [Trait("Category", "ContextSummary")]
    public async Task Asking_for_one_page_says_the_filter_is_not_wired_yet()
    {
        var registry = Registry(Sample(), []);

        var result = await registry.Invoke(
            DiagramToolset.Read,
            JsonDocument.Parse("""{"pageId":"p2"}""").RootElement,
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Code.Should().Be(ToolErrorCodes.NotSupported);
        result.Errors[0].Parameter.Should().Be("pageId",
            "认下这个参数而按整份文档回，会让调用方以为它读的是某一页");
        result.Errors[0].Expected.Should().NotBeNullOrEmpty("要给一条走得通的做法");
    }

    #endregion

    #region 夹具

    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    private static readonly NodeDef[] SampleNodes =
    [
        new() { Id = "start", Label = "开始" },
        new() { Id = "check", Label = "校验", Shape = NodeShape.Diamond },
        new() { Id = "pass", Label = "成功" },
        new() { Id = "fail", Label = "失败" },
    ];

    private static readonly EdgeDef[] SampleEdges =
    [
        new() { Id = "e1", From = "start", To = "check" },
        new() { Id = "e2", From = "check", To = "pass", Label = "是" },
        new() { Id = "e3", From = "check", To = "fail", Label = "否" },
    ];

    private static DiagramDocument Sample(
        IReadOnlyList<NodeDef>? nodes = null,
        IReadOnlyList<EdgeDef>? edges = null,
        Palette? palette = null) =>
        DiagramDocument.CreateFromContent(
            "sample",
            DiagramKind.Flowchart,
            Direction.LR,
            nodes: nodes ?? SampleNodes,
            edges: edges ?? SampleEdges,
            palette: palette ?? Palette("primary", "success", "warning", "danger", "muted"));

    private static Palette Palette(params string[] tokens) => new()
    {
        Entries = tokens.ToDictionary(
            token => token,
            token => new PaletteEntry { Name = token },
            StringComparer.Ordinal),
    };

    private static NodeRank[] Placement(params (string Id, int Layer)[] ranks) =>
        [.. ranks.Select(rank => new NodeRank(rank.Id, rank.Layer))];

    private static VersionEntry Entry(int version, string command, params FieldChange[] changes) => new()
    {
        Version = version,
        CommandId = command,
        Source = ChangeSource.System,
        Timestamp = Now,
        AffectedIds = [.. changes.Select(change => change.ElementId).Distinct(StringComparer.Ordinal)],
        Changes = changes,
    };

    private static FieldChange Field(
        string element,
        string field,
        string? value,
        ChangeKind kind = ChangeKind.Modified) => new()
        {
            ElementId = element,
            Field = field,
            NewValue = value,
            Kind = kind,
        };

    private static string Render(DiagramDocument document, IReadOnlyList<NodeRank> placement) =>
        SummaryFormatter.Format(
            SummaryBuilder.Build(new SummaryInput { Document = document, Placement = placement }),
            Now);

    private static DiagramSummary BuildFrom(LiveSession live) => SummaryBuilder.Build(new SummaryInput
    {
        Document = live.Document,
        RecentChanges = live.Bus.Context.VersionLog.Snapshot(),
    });

    private static ToolRegistry Registry(DiagramDocument document, IReadOnlyList<NodeRank> placement) =>
        ToolRegistry.CreateDefault(new DiagramToolContext
        {
            Document = document,
            Placement = placement,
            Clock = new ManualTimeProvider(Now),
        });

    private sealed record LiveSession(DiagramDocument Document, DiagramCommandBus Bus, ManualTimeProvider Clock);

    /// <summary>一份带命令总线的文档，用来观察摘要是不是真的读版本日志。</summary>
    private static LiveSession Live()
    {
        var document = Sample();
        var clock = new ManualTimeProvider(Now);

        var context = DiagramCommandBusContext.Create(
            document,
            new SimpleSessionProvider("tester", SessionIds.Gui("w1")),
            NullChangeBroadcaster.Instance,
            DiagramCommandBusOptions.ForGui(),
            clock);

        return new LiveSession(document, new DiagramCommandBus(context), clock);
    }

    /// <summary>
    /// 走一遍摘要的对象图，找出所有浮点字段。
    /// </summary>
    /// <remarks>
    /// 逐条断言挡不住后来者加一个 <c>double</c> 字段——加进去时测试仍然全绿，
    /// 而模型从那一刻起就会照着坐标去调位置。走一遍类型图则会在加的那一刻就报出来。
    /// </remarks>
    private static void Walk(Type type, HashSet<Type> seen, List<string> offenders)
    {
        if (!seen.Add(type))
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = $"{type.Name}.{property.Name}";
            var target = Unwrap(property.PropertyType);

            if (target == typeof(double) || target == typeof(float) || target == typeof(decimal))
            {
                offenders.Add(name);
                continue;
            }

            if (IsScalar(target))
            {
                continue;
            }

            Walk(target, seen, offenders);
        }
    }

    /// <summary>剥掉可空、数组与集合的外壳，取里面那个类型。</summary>
    private static Type Unwrap(Type type)
    {
        var nullable = Nullable.GetUnderlyingType(type);

        if (nullable is not null)
        {
            return Unwrap(nullable);
        }

        if (type.IsArray)
        {
            return Unwrap(type.GetElementType()!);
        }

        if (type.IsGenericType)
        {
            var arguments = type.GetGenericArguments();

            // 字典取值的类型。键是字符串或枚举，不会是坐标。
            return Unwrap(arguments[^1]);
        }

        return type;
    }

    private static bool IsScalar(Type type) =>
        type.IsPrimitive
        || type.IsEnum
        || type == typeof(string)
        || type == typeof(decimal)
        || type == typeof(DateTimeOffset)
        || type == typeof(DateTime)
        || type == typeof(Guid);

    #endregion
}
