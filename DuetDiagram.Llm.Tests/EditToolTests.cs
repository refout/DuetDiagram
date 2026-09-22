using System.Text.Json;
using System.Text.RegularExpressions;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Llm.Tools;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Llm.Tests;

/// <summary>
/// 动作分发：每个动作落到哪条命令、参数怎么摆、命令被拒时回什么。
/// </summary>
public sealed partial class EditToolTests
{
    #region 动作表与命令清单

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void Every_action_in_the_table_has_a_handler()
    {
        var registry = Harness.Registry();

        foreach (var tool in new[] { DiagramToolset.Edit, DiagramToolset.Style })
        {
            foreach (var action in ActionTable.For(tool))
            {
                var result = Harness.Invoke(registry, tool, $$"""{"action":"{{action.Name}}"}""");

                result.Errors.Should().NotContain(
                    error => error.Code == ToolErrorCodes.NotSupported && error.Message.Contains("还没接上", StringComparison.Ordinal),
                    $"动作表里列了 {action.Name}，执行体就得有它——不然模型调用时才知道这条动作是空的");
            }
        }
    }

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void Every_action_names_a_command_that_the_command_list_has()
    {
        var implemented = ImplementedCommands();

        foreach (var action in ActionTable.Edit.Concat(ActionTable.Style))
        {
            implemented.Should().Contain(
                action.CommandId,
                $"{action.Name} 落到 {action.CommandId}，而命令清单的「已实现」表里没有它——"
                + "多半是动作表抄了一个改名之前的标识");
        }
    }

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void The_action_table_matches_the_documented_table()
    {
        var documented = DocumentedActions();

        documented.Should().Equal(
            [.. ActionTable.Edit.Select(action => (DiagramToolset.Edit, action)),
             .. ActionTable.Style.Select(action => (DiagramToolset.Style, action))],
            "动作表与命令清单里的对照表要逐行一致，两边分头维护之后没人知道该信哪一份");
    }

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void An_unknown_action_lists_the_available_ones()
    {
        var result = Harness.Edit(Harness.Registry(), """{"action":"rename-node"}""");

        result.IsSuccess.Should().BeFalse();
        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Parameter.Should().Be("action");
        result.Errors[0].Expected.Should().Contain("add-node").And.Contain("set-node-field",
            "不列出可用动作的话，模型只能靠猜重试");
    }

    #endregion

    #region 节点与边

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void Adding_a_node_goes_through_the_bus()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        var result = Harness.Edit(registry, """{"action":"add-node","id":"start","label":"开始"}""");

        result.IsSuccess.Should().BeTrue();
        document.Nodes.Should().ContainSingle().Which.Id.Should().Be("start");
        document.Version.Should().Be(1, "走了命令总线，版本号才会推进");

        var outcome = result.Data!.Value;

        outcome.GetProperty("version").GetInt32().Should().Be(1);
        outcome.GetProperty("structuralChanged").GetBoolean().Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void A_node_without_a_label_falls_back_to_its_identifier()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"start"}""");

        document.Nodes.Single().Label.Should().Be("start");
    }

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void Connecting_an_edge_splits_the_port_off_the_endpoint()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        Harness.Edit(registry, """{"action":"add-node","id":"b"}""");
        var result = Harness.Edit(
            registry,
            """{"action":"connect-edge","id":"e1","from":"a.bottom","to":"b.top","label":"是"}""");

        result.IsSuccess.Should().BeTrue();

        var edge = document.Edges.Should().ContainSingle().Subject;

        edge.From.Should().Be("a");
        edge.FromPort.Should().Be("bottom");
        edge.To.Should().Be("b");
        edge.ToPort.Should().Be("top");
        edge.Label.Should().Be("是");
    }

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void Disconnecting_and_reconnecting_go_through_the_bus()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        Harness.Edit(registry, """{"action":"add-node","id":"b"}""");
        Harness.Edit(registry, """{"action":"add-node","id":"c"}""");
        Harness.Edit(registry, """{"action":"connect-edge","id":"e1","from":"a","to":"b"}""");

        Harness.Edit(registry, """{"action":"reconnect-edge","id":"e1","from":"a","to":"c"}""")
            .IsSuccess.Should().BeTrue();

        document.Edges.Single().To.Should().Be("c");

        Harness.Edit(registry, """{"action":"disconnect-edge","id":"e1"}""")
            .IsSuccess.Should().BeTrue();

        document.Edges.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void Removing_a_node_that_is_not_there_reports_the_command_layer_code()
    {
        var result = Harness.Edit(Harness.Registry(), """{"action":"remove-node","id":"ghost"}""");

        result.IsSuccess.Should().BeFalse();
        Harness.CodeOf(result).Should().Be(ErrorCodes.NodeMissing,
            "命令被拒说的是文档里的值不对，所以回的是命令层那套码——"
            + "另起一套的话，按命令层错误码建的修复建议表会漏掉所有经由工具层的失败");
    }

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void A_missing_required_parameter_is_rejected_before_any_command_runs()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        var result = Harness.Edit(registry, """{"action":"remove-node"}""");

        result.IsSuccess.Should().BeFalse();
        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentMissing);
        result.Errors[0].Parameter.Should().Be("id");
        document.Version.Should().Be(0, "参数没给够就不该发命令");
    }

    #endregion

    #region 字段、页面、图层、标签、动作

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void Field_writes_are_passed_through_and_the_command_owns_the_field_table()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");

        Harness.Edit(registry, """{"action":"set-node-field","id":"a","field":"label","value":"甲"}""")
            .IsSuccess.Should().BeTrue();

        document.Nodes.Single().Label.Should().Be("甲");

        var unknown = Harness.Edit(registry, """{"action":"set-node-field","id":"a","field":"colour","value":"red"}""");

        Harness.CodeOf(unknown).Should().Be(ErrorCodes.FieldUnknown,
            "字段表只有命令层那一份，工具层不复制它");
    }

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void Pages_layers_and_tags_go_through_their_commands()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        Harness.Edit(registry, """{"action":"create-page","id":"p2","label":"第二页"}""").IsSuccess.Should().BeTrue();
        Harness.Edit(registry, """{"action":"create-layer","id":"bg","label":"底层"}""").IsSuccess.Should().BeTrue();
        Harness.Edit(registry, """{"action":"rename-layer","id":"bg","label":"背景"}""").IsSuccess.Should().BeTrue();
        Harness.Edit(registry, """{"action":"add-tag","id":"t1","label":"重点","memberIds":["a"]}""").IsSuccess.Should().BeTrue();

        document.Pages.Should().Contain(page => page.Id == "p2");
        document.Layers.Should().ContainSingle().Which.Name.Should().Be("背景");
        document.Tags.Should().ContainSingle().Which.Members.Should().Equal("a");

        Harness.Edit(registry, """{"action":"remove-tag","id":"t1"}""").IsSuccess.Should().BeTrue();
        document.Tags.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void An_action_uses_the_event_kind_and_target_parameters()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        var result = Harness.Edit(
            registry,
            """{"action":"add-action","id":"open","event":"click","kind":"open-url","targetId":"a"}""");

        result.IsSuccess.Should().BeTrue();

        var action = document.Actions.Should().ContainSingle().Subject;

        action.Event.Should().Be("click");
        action.Kind.Should().Be("open-url");
        action.Target.Should().Be("a");

        Harness.Edit(registry, """{"action":"remove-action","id":"open"}""").IsSuccess.Should().BeTrue();
        document.Actions.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void Adding_an_action_without_its_event_is_rejected()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        var result = Harness.Edit(registry, """{"action":"add-action","id":"open","kind":"open-url"}""");

        Harness.CodeOf(result).Should().Be(ToolErrorCodes.ArgumentMissing);
        result.Errors[0].Parameter.Should().Be("event");
        document.Version.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void The_kind_action_takes_the_kind_from_the_value_parameter()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"set-kind","value":"flowchart"}""").IsSuccess.Should().BeTrue();
        document.Kind.Should().Be(DiagramKind.Flowchart);

        var rejected = Harness.Edit(registry, """{"action":"set-kind","value":"sankey"}""");

        Harness.CodeOf(rejected).Should().Be(ToolErrorCodes.ArgumentInvalid);
        rejected.Errors[0].Parameter.Should().Be("value");
        document.Kind.Should().Be(DiagramKind.Flowchart, "枚举值落在定义之外时不能悄悄当成某个取值");
    }

    #endregion

    #region 幂等键

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void The_same_idempotency_key_does_not_apply_the_change_twice()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        var first = Harness.Edit(registry, """{"action":"add-node","id":"a"}""", "req-1");
        var second = Harness.Edit(registry, """{"action":"add-node","id":"a"}""", "req-1");

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.Data!.Value.GetRawText().Should().Be(first.Data!.Value.GetRawText());

        document.Nodes.Should().ContainSingle("同一个键重复调用不该施加第二次变更");
        document.Version.Should().Be(1, "一次网络抖动不该变成两次编辑");
    }

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void Different_idempotency_keys_apply_the_change_each_time()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""", "req-1");
        Harness.Edit(registry, """{"action":"add-node","id":"b"}""", "req-2");

        document.Nodes.Should().HaveCount(2);
        document.Version.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "ToolDispatch")]
    public void Without_a_key_every_call_runs()
    {
        var document = new DiagramDocument("doc");
        var registry = Harness.Registry(document);

        Harness.Edit(registry, """{"action":"add-node","id":"a"}""");
        var again = Harness.Edit(registry, """{"action":"add-node","id":"a"}""");

        Harness.CodeOf(again).Should().Be(ErrorCodes.DuplicateId,
            "没给键就是两次独立的调用，第二次该按重复标识被拒");
    }

    #endregion

    #region 文档里的两张表

    private const string ImplementedHeading = "## 已实现";

    private const string ActionHeading = "## 工具层的动作对照";

    /// <summary>命令清单「已实现」那一节里的命令标识。</summary>
    private static List<string> ImplementedCommands() =>
        Rows(ImplementedHeading, 1, Backticked).Select(cells => cells[0]).ToList();

    /// <summary>命令清单里那张「工具 | 动作 | CommandId」的表。</summary>
    private static List<(string Tool, ToolAction Action)> DocumentedActions() =>
        [.. Rows(ActionHeading, 3, Backticked)
            .Select(cells => (cells[0], new ToolAction(cells[1], cells[2])))];

    /// <summary>
    /// 只扫指定二级标题下的表，逐行取出前若干列里反引号包着的内容。
    /// </summary>
    /// <remarks>
    /// 到下一个二级标题就停：后面那几节的写法（分组名放第一列、一条命令列在括号里）
    /// 与这里要的形态不同，一起扫进来会把无关的行算成命令。
    /// </remarks>
    private static List<string[]> Rows(string heading, int columns, Regex pattern)
    {
        var path = Path.Combine(Harness.RepositoryRoot(), "docs", "Command-List.md");
        File.Exists(path).Should().BeTrue($"命令清单应当在 {path}");

        var rows = new List<string[]>();
        var inside = false;

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                inside = trimmed.StartsWith(heading, StringComparison.Ordinal);
                continue;
            }

            if (!inside || !trimmed.StartsWith('|'))
            {
                continue;
            }

            var cells = trimmed.Split('|');

            if (cells.Length < columns + 1)
            {
                continue;
            }

            var values = new List<string>(columns);

            for (var index = 0; index < columns; index++)
            {
                var match = pattern.Match(cells[index + 1].Trim());

                if (!match.Success)
                {
                    values.Clear();
                    break;
                }

                values.Add(match.Groups[1].Value);
            }

            if (values.Count == columns)
            {
                rows.Add([.. values]);
            }
        }

        rows.Should().NotBeEmpty($"「{heading}」那一节至少要有一行，否则那张表已经不成形了");

        return rows;
    }

    [GeneratedRegex("^`([^`]+)`$")]
    private static partial Regex Backticked { get; }

    #endregion
}
