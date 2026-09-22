using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DuetDiagram.Core.Broadcasting;
using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;
using DuetDiagram.Llm.Tools;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Llm.Tests;

/// <summary>
/// 参数约束：写进 schema 没有、以什么形式写进去、越界的值挡不挡得住。
/// </summary>
/// <remarks>
/// 判据的重点不是"schema 里有个 pattern 字段"，而是**约束真的在命令层之前起作用**。
/// 所以最后一组用例起一条真总线：越界的参数必须连一条命令都没发出去。
/// </remarks>
public sealed class SchemaTests
{
    #region 约束怎么进 schema

    [Fact]
    [Trait("Category", "ToolSchema")]
    public void A_pattern_on_a_scalar_parameter_lands_in_the_schema()
    {
        var schema = SchemaOf(ToolDescriptor.Create(
            Echo,
            "echo",
            "把收到的标识原样回一句，用来观察参数约束有没有进 schema。"));

        Property(schema, "id")["pattern"]!.GetValue<string>().Should().Be(Patterns.DiagramId);
    }

    [Fact]
    [Trait("Category", "ToolSchema")]
    public void A_pattern_on_an_array_parameter_lands_on_the_items()
    {
        var schema = SchemaOf(ToolDescriptor.Create(
            EchoMany,
            "echo-many",
            "回一句收到多少个标识，用来观察数组参数的约束落在哪一层。"));

        var items = Property(schema, "ids")["items"]!.AsObject();

        items["pattern"]!.GetValue<string>().Should().Be(
            Patterns.DiagramId,
            "数组参数的约束说的是「里面的每个标识长什么样」，写在数组那一层会把整份数组当成一个字符串去匹配");
        Property(schema, "ids").Should().NotContainKey("pattern");
    }

    [Theory]
    [InlineData(DiagramToolset.Read, "pageId")]
    [InlineData(DiagramToolset.Edit, "action")]
    [InlineData(DiagramToolset.Edit, "id")]
    [InlineData(DiagramToolset.Edit, "from")]
    [InlineData(DiagramToolset.Edit, "to")]
    [InlineData(DiagramToolset.Edit, "memberIds")]
    [InlineData(DiagramToolset.Style, "action")]
    [InlineData(DiagramToolset.Style, "id")]
    [InlineData(DiagramToolset.Style, "token")]
    [InlineData(DiagramToolset.Layout, "action")]
    [InlineData(DiagramToolset.Layout, "id")]
    [InlineData(DiagramToolset.Layout, "relativeTo")]
    [InlineData(DiagramToolset.Layout, "memberIds")]
    [InlineData(DiagramToolset.Layout, "subject")]
    [InlineData(DiagramToolset.Composite, "action")]
    [InlineData(DiagramToolset.Composite, "id")]
    [InlineData(DiagramToolset.Composite, "targetId")]
    [InlineData(DiagramToolset.Composite, "memberIds")]
    [InlineData(DiagramToolset.Export, "format")]
    [InlineData(DiagramToolset.UndoRedo, "action")]
    [Trait("Category", "ToolSchema")]
    public void Identifier_parameters_carry_a_constraint(string tool, string parameter)
    {
        var node = Property(SchemaOf(tool), parameter);
        var target = IsArray(node) ? node["items"]!.AsObject() : node;

        target.Should().ContainKey("pattern", $"{tool} 的 {parameter} 是一个标识，约束要进 schema 而不只写在描述里");
    }

    [Fact]
    [Trait("Category", "ToolSchema")]
    public void Every_constraint_in_a_schema_comes_from_a_declared_constant()
    {
        var declared = typeof(Patterns)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        var found = Registry()
            .Tools
            .SelectMany(tool => ConstraintsIn(tool.Parameters))
            .ToArray();

        found.Should().NotBeEmpty();
        found.Should().OnlyContain(pattern => declared.Contains(pattern));
    }

    #endregion

    #region 约束拦不拦得住

    [Fact]
    [Trait("Category", "ToolSchema")]
    public void An_out_of_range_identifier_is_rejected_with_the_parameter_and_the_pattern()
    {
        var result = Invoke(
            Registry(),
            DiagramToolset.Edit,
            """{"action":"add-node","id":"Node 1"}""");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Code.Should().Be(ToolErrorCodes.ArgumentInvalid);
        result.Errors[0].Parameter.Should().Be("id");
        result.Errors[0].Expected.Should().Be(Patterns.DiagramId, "错误里要带上期望的形式，否则模型只能靠猜重试");
    }

    [Fact]
    [Trait("Category", "ToolSchema")]
    public void An_endpoint_with_a_port_passes()
    {
        var result = Invoke(
            Registry(),
            DiagramToolset.Edit,
            """{"action":"connect-edge","id":"e1","from":"a.bottom","to":"b.top"}""");

        result.Errors.Should().NotContain(error => error.Code == ToolErrorCodes.ArgumentInvalid);
    }

    [Fact]
    [Trait("Category", "ToolSchema")]
    public void An_out_of_range_identifier_never_reaches_the_command_layer()
    {
        using var bus = NewBus(out var document);

        var registry = new ToolRegistry();
        registry.Register(ToolDescriptor.Create(
            new BusTool(bus).AddNode,
            "probe-add-node",
            "把收到的标识建成一个节点，用来观察越界的参数有没有走到命令层。"));

        var rejected = Invoke(registry, "probe-add-node", """{"id":"Node 1"}""");

        rejected.IsSuccess.Should().BeFalse();
        document.Version.Should().Be(0, "参数在进入命令层之前就被拒了，版本号不该动");
        document.Nodes.Should().BeEmpty();

        // 反过来验一次：同一个工具收到合规的标识时确实会走到命令层。
        // 少了这一步，上面那两条断言在"工具根本没接上"时也会通过。
        var accepted = Invoke(registry, "probe-add-node", """{"id":"node-1"}""");

        accepted.IsSuccess.Should().BeTrue();
        document.Version.Should().Be(1);
        document.Nodes.Should().ContainSingle().Which.Id.Should().Be("node-1");
    }

    #endregion

    #region 夹具

    /// <summary>一份最小上下文：一张空图，没有版本日志、没有人工产物。参数表与它无关。</summary>
    private static ToolRegistry Registry() =>
        ToolRegistry.CreateDefault(new DiagramToolContext
        {
            Document = new DiagramDocument("schema-doc"),
        });

    private static Task<ToolResult> Echo([Pattern(Patterns.DiagramId)] string id) =>
        Task.FromResult(ToolResult.Ok(id));

    private static Task<ToolResult> EchoMany([Pattern(Patterns.DiagramId)] string[] ids) =>
        Task.FromResult(ToolResult.Ok(ids.Length.ToString()));

    /// <summary>一个真的会往命令层写东西的工具。</summary>
    private sealed class BusTool(DiagramCommandBus bus)
    {
        public Task<ToolResult> AddNode([Pattern(Patterns.DiagramId)] string id)
        {
            var result = bus.Execute(new AddNodeCommand(new NodeDef { Id = id, Label = id }));

            return Task.FromResult(result.IsSuccess
                ? ToolResult.Ok($"version={bus.Context.Document.Version}")
                : ToolResult.Fail(ToolError.Of(ToolErrorCodes.ArgumentInvalid, "命令被拒")));
        }
    }

    private static DiagramCommandBus NewBus(out DiagramDocument document)
    {
        document = new DiagramDocument("schema-doc", DiagramKind.Flowchart, Direction.LR);

        var context = DiagramCommandBusContext.Create(
            document,
            new SimpleSessionProvider("tester", SessionIds.Llm("c1")),
            NullChangeBroadcaster.Instance,
            DiagramCommandBusOptions.ForGui());

        return new DiagramCommandBus(context);
    }

    private static JsonObject SchemaOf(string tool) => SchemaOf(Registry().Find(tool)!);

    private static JsonObject SchemaOf(ToolDescriptor tool) =>
        JsonNode.Parse(tool.Parameters.GetRawText())!.AsObject();

    private static JsonObject Property(JsonObject schema, string name) =>
        schema["properties"]!.AsObject()[name]!.AsObject();

    private static bool IsArray(JsonObject node) =>
        node["type"] is JsonValue value && value.TryGetValue<string>(out var type) && type == "array";

    /// <summary>把一份 schema 里出现的所有正则约束找出来，数组参数要下探到元素上。</summary>
    private static IEnumerable<string> ConstraintsIn(JsonElement schema)
    {
        foreach (var property in JsonNode.Parse(schema.GetRawText())!.AsObject()["properties"]!.AsObject())
        {
            if (property.Value is not JsonObject node)
            {
                continue;
            }

            var target = IsArray(node) && node["items"] is JsonObject items ? items : node;

            if (target["pattern"] is JsonValue value && value.TryGetValue<string>(out var pattern))
            {
                yield return pattern;
            }
        }
    }

    private static ToolResult Invoke(ToolRegistry registry, string name, string arguments) =>
        registry.Invoke(name, JsonDocument.Parse(arguments).RootElement).GetAwaiter().GetResult();

    #endregion
}
