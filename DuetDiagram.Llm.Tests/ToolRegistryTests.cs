using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using DuetDiagram.Core.Model;
using DuetDiagram.Llm.Tools;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DuetDiagram.Llm.Tests;

/// <summary>
/// 工具表：八个工具登记齐了没有、说明与参数齐不齐、两侧是不是同一份。
/// </summary>
public sealed class ToolRegistryTests
{
    #region 登记

    [Fact]
    [Trait("Category", "ToolRegistry")]
    public void The_registry_carries_the_eight_tools()
    {
        var registry = Registry();

        registry.Tools.Select(tool => tool.Name).Should().Equal(RegistryToolNames());
    }

    /// <summary>八个工具的名字，次序与工具表一致。</summary>
    private static string[] RegistryToolNames() =>
    [
        DiagramToolset.Read,
        DiagramToolset.Edit,
        DiagramToolset.Style,
        DiagramToolset.Layout,
        DiagramToolset.Composite,
        DiagramToolset.Export,
        DiagramToolset.Validate,
        DiagramToolset.UndoRedo,
    ];

    [Fact]
    [Trait("Category", "ToolRegistry")]
    public void Tool_names_are_unique()
    {
        var names = Registry().Tools.Select(tool => tool.Name).ToArray();

        names.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    [Trait("Category", "ToolRegistry")]
    public void A_duplicate_name_is_rejected_rather_than_overwritten()
    {
        var registry = Registry();
        var duplicate = ToolDescriptor.Create(Echo, DiagramToolset.Read, "另一个工具，名字与已有的撞上。");

        var act = () => registry.Register(duplicate);

        act.Should().Throw<InvalidOperationException>().WithMessage("*已经被占用*");
        registry.Tools.Should().HaveCount(8);
    }

    [Fact]
    [Trait("Category", "ToolRegistry")]
    public void An_unknown_tool_returns_a_structured_error()
    {
        var registry = Registry();

        var result = Invoke(registry, "diagram_paint", "{}");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Code.Should().Be(ToolErrorCodes.UnknownTool);
        result.Errors[0].Expected.Should().Contain(
            DiagramToolset.Edit,
            "错误里要列出已登记的工具名，否则调用方只能靠猜重试");
    }

    #endregion

    #region 说明与参数

    [Fact]
    [Trait("Category", "ToolRegistry")]
    public void Every_tool_has_a_description()
    {
        foreach (var tool in Registry().Tools)
        {
            // 长度下限只是个地板：说明必须写到"做什么、什么时候用、有什么限制"三样，
            // 而这件事判不出来。地板至少挡住"改图。"这种把模型当熟人看的写法。
            tool.Description.Should().NotBeNullOrWhiteSpace();
            tool.Description.Length.Should().BeGreaterThan(30, $"{tool.Name} 的说明太短，写不下三样");
        }
    }

    [Fact]
    [Trait("Category", "ToolRegistry")]
    public void Every_tool_has_a_parameter_schema()
    {
        foreach (var tool in Registry().Tools)
        {
            var schema = JsonNode.Parse(tool.Parameters.GetRawText())!.AsObject();

            schema["type"]!.GetValue<string>().Should().Be("object", $"{tool.Name} 的参数是一个对象");
            schema["properties"]!.AsObject().Should().NotBeEmpty($"{tool.Name} 至少要收一个参数");
        }
    }

    #endregion

    #region 两侧同源

    [Fact]
    [Trait("Category", "ToolRegistry")]
    public void Both_sides_are_derived_from_the_same_declarations()
    {
        var registry = Registry();
        var model = registry.ToMeaiFunctions();
        var agent = registry.ToMcpTools();

        model.Should().HaveCount(registry.Tools.Count);
        agent.Should().HaveCount(registry.Tools.Count);

        for (var index = 0; index < registry.Tools.Count; index++)
        {
            var tool = registry.Tools[index];

            model[index].Name.Should().Be(tool.Name);
            model[index].Description.Should().Be(tool.Description);
            Same(model[index].JsonSchema, tool.Parameters).Should().BeTrue($"{tool.Name} 的模型侧参数要与声明一致");

            var protocol = agent[index].ProtocolTool;

            protocol.Name.Should().Be(tool.Name);
            protocol.Description.Should().Be(tool.Description);
            Same(protocol.InputSchema, tool.Parameters).Should().BeTrue($"{tool.Name} 的代理侧参数要与声明一致");
        }
    }

    #endregion

    #region 校验与分发

    [Fact]
    [Trait("Category", "ToolRegistry")]
    public async Task Invoking_through_the_model_side_function_goes_through_the_same_checks()
    {
        var edit = FunctionOf(Registry(), DiagramToolset.Edit);

        var raw = await edit.InvokeAsync(
            Arguments("""{"action":"add-node","id":"Node 1"}"""),
            TestContext.Current.CancellationToken);

        var result = raw.Should().BeOfType<JsonElement>().Subject;

        result.GetProperty("isSuccess").GetBoolean().Should().BeFalse();
        result.GetProperty("errors")[0].GetProperty("code").GetString().Should().Be(
            ToolErrorCodes.ArgumentInvalid,
            "模型侧直接调用时也要经过同一条校验，否则越界的参数会一路走到命令层");
    }

    [Fact]
    [Trait("Category", "ToolRegistry")]
    public async Task The_model_side_function_returns_the_result_as_json()
    {
        var echo = FunctionOf(EchoRegistry(), "echo");

        var raw = await echo.InvokeAsync(
            Arguments("""{"id":"a","note":"hello"}"""),
            TestContext.Current.CancellationToken);

        var result = raw.Should().BeOfType<JsonElement>().Subject;

        result.GetProperty("isSuccess").GetBoolean().Should().BeTrue();
        result.GetProperty("message").GetString().Should().Be("a/hello");
    }

    [Fact]
    [Trait("Category", "ToolRegistry")]
    public void Declared_parameters_reach_the_handler()
    {
        var registry = EchoRegistry();

        var result = Invoke(registry, "echo", """{"id":"a","note":"hello"}""");

        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Be("a/hello");
    }

    [Fact]
    [Trait("Category", "ToolRegistry")]
    public void A_missing_required_parameter_is_rejected_before_the_handler_runs()
    {
        var registry = EchoRegistry();

        var result = Invoke(registry, "echo", """{"note":"hello"}""");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Code.Should().Be(ToolErrorCodes.ArgumentMissing);
        result.Errors[0].Parameter.Should().Be("id");
    }

    [Fact]
    [Trait("Category", "ToolRegistry")]
    public void An_unknown_parameter_name_is_rejected_and_the_known_ones_are_listed()
    {
        var registry = EchoRegistry();

        var result = Invoke(registry, "echo", """{"id":"a","nope":"x"}""");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Code.Should().Be(ToolErrorCodes.ArgumentUnknown);
        result.Errors[0].Parameter.Should().Be("nope");
        result.Errors[0].Expected.Should().Contain("id", "错误里要列出认得的参数名");
    }

    [Fact]
    [Trait("Category", "ToolRegistry")]
    public void An_unwired_capability_says_so_without_blaming_the_caller()
    {
        var result = Invoke(Registry(), DiagramToolset.Export, """{"format":"dsl"}""");

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Code.Should().Be(ToolErrorCodes.NotSupported);
        result.Errors[0].Parameter.Should().Be("format", "缺的是这一项能力，不是调用方给错了参数");
        result.Errors[0].Message.Should().Contain("dsl");
    }

    [Fact]
    [Trait("Category", "ToolRegistry")]
    public void Every_tool_now_has_an_execution_body()
    {
        var registry = Registry();

        foreach (var tool in RegistryToolNames())
        {
            var result = Invoke(registry, tool, "{}");

            result.Errors.Should().NotContain(
                error => error.Code == ToolErrorCodes.NotSupported && error.Message.Contains("还没有接上", StringComparison.Ordinal),
                $"{tool} 的执行体应当已经接上；八个工具到此全部有线可走");
        }
    }

    #endregion

    #region 夹具

    /// <summary>一份最小上下文：一张空图，没有人工产物。</summary>
    private static ToolRegistry Registry() => Harness.Registry(new DiagramDocument("doc"));

    /// <summary>一个只回一句话的工具，用来观察参数有没有送到执行体。</summary>
    private static Task<ToolResult> Echo(
        [Description("要回的标识")][Pattern(Patterns.DiagramId)] string id,
        [Description("附注")] string? note = null) =>
        Task.FromResult(ToolResult.Ok($"{id}/{note}"));

    private static ToolRegistry EchoRegistry()
    {
        var registry = new ToolRegistry();
        registry.Register(ToolDescriptor.Create(Echo, "echo", "把收到的标识原样回一句，用来观察参数有没有送到。"));

        return registry;
    }

    private static ToolResult Invoke(ToolRegistry registry, string name, string arguments) =>
        registry.Invoke(name, JsonDocument.Parse(arguments).RootElement).GetAwaiter().GetResult();

    /// <summary>把一份 JSON 参数摊成模型侧调用要的那个参数表。</summary>
    private static AIFunctionArguments Arguments(string arguments)
    {
        var bound = new AIFunctionArguments();

        foreach (var argument in JsonDocument.Parse(arguments).RootElement.EnumerateObject())
        {
            bound[argument.Name] = argument.Value;
        }

        return bound;
    }

    private static AIFunction FunctionOf(ToolRegistry registry, string name) =>
        registry.ToMeaiFunctions().Single(function => string.Equals(function.Name, name, StringComparison.Ordinal));

    private static bool Same(JsonElement left, JsonElement right) =>
        string.Equals(Canonical(left), Canonical(right), StringComparison.Ordinal);

    private static string Canonical(JsonElement schema) =>
        JsonSerializer.Serialize(JsonDocument.Parse(schema.GetRawText()).RootElement);

    #endregion
}
