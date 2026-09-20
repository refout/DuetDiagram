using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace DuetDiagram.Poc.SharedTools;

/// <summary>
/// 工具的实现。
/// </summary>
/// <remarks>
/// 只写一份。内部模型与外部代理最终都调用这里，所以行为不可能出现分歧——
/// 需要验证的是「两边的声明是否一致」，而不是「两边的实现是否一致」。
/// </remarks>
internal static class DiagramToolImplementation
{
    public static string Edit(
        [Description("要执行的动作名")] string action,
        [Description("目标节点标识")] string nodeId,
        [Description("可选的原因说明")] string? reason = null)
        => $$"""{"action":"{{action}}","nodeId":"{{nodeId}}","reason":"{{reason}}"}""";

    /// <summary>一个参数更多的方法，用来观察两边的 schema 推导在复杂签名上是否仍然一致。</summary>
    public static string Layout(
        [Description("布局方向")] string direction,
        [Description("同层节点间距")] int nodeSpacing,
        [Description("层间间距")] int layerSpacing,
        [Description("要固定的节点标识")] string[]? pinnedNodes = null)
        => $$"""{"direction":"{{direction}}","nodeSpacing":{{nodeSpacing}},"layerSpacing":{{layerSpacing}}}""";
}

internal sealed record CheckResult(string Name, bool Passed, string Detail);

/// <summary>
/// 验证结论。
/// </summary>
internal static class Checks
{
    public static List<CheckResult> Run()
    {
        var results = new List<CheckResult>();

        results.AddRange(SharedDefinition());
        results.AddRange(SeparatelyMaintained());

        return results;
    }

    /// <summary>
    /// 共用一份定义：先建出模型侧的函数，再由它派生代理侧的工具。
    /// </summary>
    private static IEnumerable<CheckResult> SharedDefinition()
    {
        var function = AIFunctionFactory.Create(
            DiagramToolImplementation.Edit,
            name: "diagram_edit",
            description: "按动作修改图结构。");

        var mcpTool = McpServerTool.Create(function);

        yield return new CheckResult(
            "共用·名称一致",
            string.Equals(mcpTool.ProtocolTool.Name, function.Name, StringComparison.Ordinal),
            $"模型侧 {function.Name}，代理侧 {mcpTool.ProtocolTool.Name}");

        yield return new CheckResult(
            "共用·描述一致",
            string.Equals(mcpTool.ProtocolTool.Description, function.Description, StringComparison.Ordinal),
            $"模型侧 {function.Description}，代理侧 {mcpTool.ProtocolTool.Description}");

        var modelSchema = Canonical(function.JsonSchema);
        var agentSchema = Canonical(mcpTool.ProtocolTool.InputSchema);

        yield return new CheckResult(
            "共用·参数 schema 一致",
            string.Equals(modelSchema, agentSchema, StringComparison.Ordinal),
            string.Equals(modelSchema, agentSchema, StringComparison.Ordinal)
                ? $"一致，长度 {modelSchema.Length}"
                : $"不一致：\n        模型侧 {modelSchema}\n        代理侧 {agentSchema}");

        yield return new CheckResult(
            "共用·schema 非空",
            modelSchema.Length > 10,
            $"长度 {modelSchema.Length}");

        yield return new CheckResult(
            "共用·参数名齐全",
            modelSchema.Contains("action", StringComparison.Ordinal)
                && modelSchema.Contains("nodeId", StringComparison.Ordinal)
                && modelSchema.Contains("reason", StringComparison.Ordinal),
            modelSchema);
    }

    /// <summary>
    /// 退路：两边各自由同一份方法签名推导，看 schema 会不会出现分歧。
    /// </summary>
    /// <remarks>
    /// 方案给这条退路留了位置（"不可行则退化为命令层共用 + 工具定义各自维护"）。
    /// 但如果两边推导出来的参数约束不同，模型看到的和代理看到的就不是同一个工具，
    /// 调用会莫名其妙地失败。所以这条退路必须实测，不能假定它成立。
    /// </remarks>
    private static IEnumerable<CheckResult> SeparatelyMaintained()
    {
        var fromModel = AIFunctionFactory.Create(
            DiagramToolImplementation.Layout,
            name: "diagram_layout",
            description: "调整布局参数。");

        var fromAgent = McpServerTool.Create(
            DiagramToolImplementation.Layout,
            new McpServerToolCreateOptions { Name = "diagram_layout", Description = "调整布局参数。" });

        var modelSchema = Canonical(fromModel.JsonSchema);
        var agentSchema = Canonical(fromAgent.ProtocolTool.InputSchema);

        yield return new CheckResult(
            "各自维护·参数 schema 一致",
            string.Equals(modelSchema, agentSchema, StringComparison.Ordinal),
            string.Equals(modelSchema, agentSchema, StringComparison.Ordinal)
                ? $"一致，长度 {modelSchema.Length}；{modelSchema}"
                : $"不一致：\n        模型侧 {modelSchema}\n        代理侧 {agentSchema}");

        yield return new CheckResult(
            "各自维护·可选参数被识别",
            modelSchema.Contains("required", StringComparison.Ordinal) || modelSchema.Contains("default", StringComparison.Ordinal),
            modelSchema);
    }

    /// <summary>
    /// 规范化 schema 文本，消除键顺序与空白差异，只比较实质内容。
    /// </summary>
    private static string Canonical(JsonElement schema) =>
        JsonSerializer.Serialize(
            JsonDocument.Parse(schema.GetRawText()).RootElement,
            new JsonSerializerOptions { WriteIndented = false });

    public static string Describe(JsonElement schema) => Canonical(schema);
}
