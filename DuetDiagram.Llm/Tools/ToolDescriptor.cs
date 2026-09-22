using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace DuetDiagram.Llm.Tools;

/// <summary>
/// 一个工具的完整定义：叫什么、干什么、收哪些参数、收到之后做什么。
/// </summary>
/// <remarks>
/// <para>
/// **定义只写一份，两侧都从这里派生。** 模型侧要的是 <see cref="Function"/>，
/// 代理侧要的是由它派生出来的工具；两侧指向同一个对象，声明不可能不一致。
/// 两边各写一份的话，某天升级依赖之后会静默分叉，表现为"模型能调通的工具代理调不通"，
/// 而两边的声明各自都自洽，查不出来。
/// </para>
/// <para>
/// <see cref="Parameters"/> 由 <see cref="Create"/> 从签名推导，不是手写的 JSON 片段。
/// 手写的话，签名改了而 schema 忘了改，模型会照着一份不存在的参数表去调用。
/// </para>
/// </remarks>
public sealed record ToolDescriptor
{
    public required string Name { get; init; }

    /// <summary>
    /// 工具的说明。三样都要写到：做什么、什么时候用它、有什么限制。
    /// </summary>
    /// <remarks>
    /// 只写"做什么"的话，模型会在该用别的工具时用这一个——而它的选择依据只有这段文字。
    /// </remarks>
    public required string Description { get; init; }

    /// <summary>参数 schema。约束在这里是硬的，不只写在描述里。</summary>
    public required JsonElement Parameters { get; init; }

    /// <summary>执行体。参数以 JSON 形式进来，这样两个入口都能直接调用它。</summary>
    public required Func<JsonElement, CancellationToken, Task<ToolResult>> Handler { get; init; }

    /// <summary>
    /// 模型侧的那一份函数。
    /// </summary>
    /// <remarks>
    /// 它被包了一层，是为了让模型侧的调用也走 <see cref="Handler"/>：
    /// 直接用签名推导出来的那个函数的话，模型给错的参数会绕过校验，
    /// 一路走到命令层，然后在那边以一个与调用方无关的形式被拒。
    /// </remarks>
    public required AIFunction Function { get; init; }

    /// <summary>
    /// 由一个方法签名建出一个工具定义。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 签名既是声明也是执行体：参数名与类型决定 schema，方法体决定做什么。
    /// 两者绑在同一个方法上，就不会出现"参数表说一套、实现收另一套"。
    /// </para>
    /// <para>
    /// 结果不做二次序列化。默认会把返回值序列化成 JSON，那样调用方拿到的是一份
    /// 已经摊平的文字，错误码与参数名都散了，而它们正是要回灌给模型的东西。
    /// </para>
    /// </remarks>
    /// <param name="declaration">声明与执行体。</param>
    /// <param name="name">工具名，两侧共用。</param>
    /// <param name="description">工具说明。</param>
    public static ToolDescriptor Create<TDeclaration>(
        TDeclaration declaration,
        string name,
        string description)
        where TDeclaration : Delegate
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var bound = AIFunctionFactory.Create(declaration, new AIFunctionFactoryOptions
        {
            Name = name,
            Description = description,
            MarshalResult = static (value, _, _) => new ValueTask<object?>(value),
        });

        var parameters = SchemaBuilder.ApplyParameterConstraints(bound.JsonSchema, bound.UnderlyingMethod);

        Func<JsonElement, CancellationToken, Task<ToolResult>> handler =
            (arguments, cancellationToken) => InvokeAsync(bound, parameters, arguments, cancellationToken);

        return new ToolDescriptor
        {
            Name = bound.Name,
            Description = bound.Description ?? description,
            Parameters = parameters,
            Handler = handler,
            Function = new ToolFunction(bound.Name, description, parameters, handler),
        };
    }

    /// <summary>
    /// 校验参数，通过之后再交给签名对应的那个方法。
    /// </summary>
    /// <remarks>
    /// 校验放在这里而不是放进每个方法体：放进去的话，八个工具各写一遍，
    /// 而抄漏的那一遍不会报错，只会让某个工具的参数约束形同虚设。
    /// </remarks>
    private static async Task<ToolResult> InvokeAsync(
        AIFunction bound,
        JsonElement parameters,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var errors = SchemaBuilder.Validate(parameters, arguments);

        if (errors.Count > 0)
        {
            return ToolResult.Fail([.. errors]);
        }

        var boundArguments = new AIFunctionArguments();

        if (arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (var argument in arguments.EnumerateObject())
            {
                boundArguments[argument.Name] = argument.Value;
            }
        }

        var raw = await bound.InvokeAsync(boundArguments, cancellationToken).ConfigureAwait(false);

        return raw as ToolResult
            ?? throw new InvalidOperationException($"工具 {bound.Name} 的声明没有返回 {nameof(ToolResult)}。");
    }
}

/// <summary>
/// 交给模型侧与代理侧的那一份函数。
/// </summary>
/// <remarks>
/// <para>
/// 它只做两件事：把进来的参数转成 JSON 交给执行体，把结果转成 JSON 交出去。
/// 包这一层是为了让两条入口（模型侧直接调用、代理侧经由协议）走同一条校验路径。
/// </para>
/// <para>
/// 结果序列化用编译期生成的上下文，不用运行时反射——后者在裁剪与原生编译之后会失败，
/// 而失败发生在真的调用模型那一刻。
/// </para>
/// </remarks>
internal sealed class ToolFunction(
    string name,
    string description,
    JsonElement parameters,
    Func<JsonElement, CancellationToken, Task<ToolResult>> handler) : AIFunction
{
    public override string Name => name;

    public override string Description => description;

    public override JsonElement JsonSchema => parameters;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var result = await handler(ToJson(arguments), cancellationToken).ConfigureAwait(false);

        return JsonSerializer.SerializeToElement(result, ToolJsonContext.Default.ToolResult);
    }

    /// <summary>
    /// 把参数表转回 JSON。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 只认已经解析好的 JSON 形式与几个基本类型，其余一律拒绝。
    /// 走运行时序列化去兜底的话，这条路径在原生编译之后会失败——
    /// 而它失败的位置是模型调用工具的那一刻，离这里很远。
    /// </para>
    /// <para>
    /// 对执行体那一侧也是公开的：模型那条通路把工具调用交给执行体时，也要做同一件事。
    /// 两处各写一份的话，某天一边改了认得的类型，另一边的参数就悄悄对不上，
    /// 而表现是「同一个工具经模型调与经协议调收到的参数不一样」。
    /// </para>
    /// </remarks>
    internal static JsonElement ToJson(AIFunctionArguments arguments)
    {
        var root = new JsonObject();

        foreach (var (key, value) in arguments)
        {
            root[key] = value switch
            {
                null => null,
                JsonElement element => JsonNode.Parse(element.GetRawText()),
                JsonDocument document => JsonNode.Parse(document.RootElement.GetRawText()),
                JsonNode node => node.DeepClone(),
                string text => JsonValue.Create(text),
                bool flag => JsonValue.Create(flag),
                int number => JsonValue.Create(number),
                long number => JsonValue.Create(number),
                double number => JsonValue.Create(number),
                _ => throw new NotSupportedException($"参数 {key} 的值不是 JSON 能直接表达的形式。"),
            };
        }

        return JsonDocument.Parse(root.ToJsonString()).RootElement.Clone();
    }
}
