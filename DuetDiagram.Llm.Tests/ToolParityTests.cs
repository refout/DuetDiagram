using DuetDiagram.Llm.Tools;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.Llm.Tests;

/// <summary>
/// 模型侧与代理侧是同一份定义派生出来的。
/// </summary>
/// <remarks>
/// <para>
/// **逐字比，不按结构比。** 结构比较会先把两边规范成同一份 JSON，键序与空白上的差异
/// 都被抹掉；而「某天有人把两侧改成各自维护」之后，差异很可能正好出现在那些地方——
/// 那时结构比较仍然全绿，而两边已经开始分叉。
/// </para>
/// <para>
/// 这一条也是外部代理接入时的实测位：可空参数用的是 JSON Schema 的数组形式，
/// 一些老的校验器不认。依赖升级若把它改回另一种写法，这里会红。
/// </para>
/// </remarks>
public sealed class ToolParityTests
{
    /// <summary>八个工具两侧都在，名字与次序一致。</summary>
    [Fact]
    [Trait("Category", "ToolParity")]
    public void Both_sides_cover_the_eight_tools()
    {
        var registry = Harness.Registry();
        var expected = registry.Tools.Select(tool => tool.Name).ToArray();

        registry.ToMeaiFunctions().Select(function => function.Name).Should().Equal(expected);
        registry.ToMcpTools().Select(tool => tool.ProtocolTool.Name).Should().Equal(expected);
    }

    /// <summary>两侧的名称、说明与参数 schema 逐字相同。</summary>
    [Fact]
    [Trait("Category", "ToolParity")]
    public void Both_sides_serialize_the_same_declaration()
    {
        var registry = Harness.Registry();
        var model = registry.ToMeaiFunctions();
        var agent = registry.ToMcpTools();

        agent.Should().HaveCount(model.Count);

        for (var index = 0; index < model.Count; index++)
        {
            var name = model[index].Name;

            agent[index].ProtocolTool.Name.Should().Be(name);
            agent[index].ProtocolTool.Description.Should().Be(
                model[index].Description,
                $"{name} 的说明两侧必须逐字相同");

            agent[index].ProtocolTool.InputSchema.GetRawText().Should().Be(
                model[index].JsonSchema.GetRawText(),
                $"{name} 的参数 schema 两侧必须逐字相同——两边各写一份的话，"
                + "升级依赖之后会静默分叉，表现是「模型能调通的工具代理调不通」");
        }
    }

    /// <summary>可空参数在两侧都是数组形式。</summary>
    /// <remarks>
    /// 数组形式是较新的写法，外部代理用别的实现接入时可能被拒——这是依赖选型时留下的那条待实测。
    /// 这一条把当下的形式固定下来，改形式的人会先看到它红。
    /// </remarks>
    [Fact]
    [Trait("Category", "ToolParity")]
    public void A_nullable_parameter_uses_the_array_form_on_both_sides()
    {
        var registry = Harness.Registry();
        var model = registry.ToMeaiFunctions()
            .Single(function => function.Name == DiagramToolset.Edit);
        var agent = registry.ToMcpTools()
            .Single(tool => tool.ProtocolTool.Name == DiagramToolset.Edit);

        model.JsonSchema.GetRawText().Should().Contain(
            """["string","null"]""",
            "可空参数在模型侧是数组形式");
        agent.ProtocolTool.InputSchema.GetRawText().Should().Contain(
            """["string","null"]""",
            "可空参数在代理侧也必须是同一种写法");
    }
}
