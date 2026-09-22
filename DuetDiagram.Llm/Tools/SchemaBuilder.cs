using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DuetDiagram.Llm.Tools;

#region 参数约束的声明

/// <summary>
/// 给一个参数挂上正则约束，让它进参数 schema 里的 <c>pattern</c>。
/// </summary>
/// <remarks>
/// <para>
/// 约束写在 schema 里而不只写在描述文字里，是因为两件事的效果不同：描述文字是给模型看的建议，
/// 模型偶尔不照做；schema 里的约束则是在模型生成参数之后、命令层之前就被校验的硬条件。
/// 只写描述的话，越界的值会一路走到命令层，然后以一个看不懂的形式冒出来。
/// </para>
/// <para>
/// 挂在数组参数上时约束落在元素上（<c>items</c>），因为要约束的是"里面的每个标识长什么样"。
/// 写在数组那一层的话，校验器会把整份数组当成一个字符串去匹配。
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class PatternAttribute(string pattern) : Attribute
{
    public string Pattern { get; } = pattern;
}

/// <summary>
/// 标识类参数的取值形态。
/// </summary>
/// <remarks>
/// <para>
/// 这里定的是**工具层的命名约定**，比数据层严：数据层只要求标识非空，
/// 而工具层要求它只由小写字母、数字与连字符组成。严在这里是有意的——
/// 标识会出现在命令的载荷、审计日志与 URL 形式的动作里，
/// 允许大写与空格之后，同一个标识很快会出现两种写法，而两边各自都自洽。
/// </para>
/// <para>
/// 约定只在工具层挡住，不改数据层的判据：改数据层会让既有文件读不进来。
/// </para>
/// </remarks>
public static class Patterns
{
    /// <summary>节点、边、组合、页面、图层、标签、动作与样式令牌的标识。</summary>
    public const string DiagramId = "^[a-z0-9-]+$";

    /// <summary>
    /// 连线的端点：标识后面可以跟一个端口名，用点分隔。
    /// </summary>
    /// <remarks>
    /// 端口写在端点上（<c>a.bottom</c>）而不是另立两个参数，是因为模型描述一条连线时
    /// 说的本来就是"从 a 的下边到 b 的上边"这一件事。拆成四个参数之后，
    /// 端点与端口对不上就成了一个可以表达出来的状态，而那个状态没有意义。
    /// </remarks>
    public const string Endpoint = "^[a-z0-9-]+(\\.[a-z]+)?$";

    /// <summary>一个值是否匹配给定的正则。</summary>
    public static bool Matches(string pattern, string? value) =>
        value is not null && Compile(pattern).IsMatch(value);

    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    /// <summary>
    /// 按模式串取一个编译过的正则。
    /// </summary>
    /// <remarks>
    /// 缓存是必要的：一次调用要对每个参数各匹配一次，而模式串只有那么几个。
    /// 每次都新建的话，一次工具调用会白白构造好几个正则对象。
    /// </remarks>
    private static Regex Compile(string pattern) =>
        Cache.GetOrAdd(pattern, static value => new Regex(value, RegexOptions.CultureInvariant));
}

#endregion

#region schema 的加工与校验

/// <summary>
/// 参数 schema 的加工与校验。
/// </summary>
/// <remarks>
/// <para>
/// 两件事放在一处，是因为它们必须读同一份 schema：约束怎么进去的、就该怎么被读出来。
/// 分成两处之后，某一处改了写法而另一处没跟上，表现是"约束在 schema 里看得见，却拦不住任何东西"。
/// </para>
/// <para>
/// <see cref="Validate"/> 只覆盖本层真正用得上的那几项（必填、不认识的参数名、正则约束），
/// 不是一份完整的 schema 校验器。它不是权威——命令层仍然是权威，
/// 这里的判据只是"在进入命令层之前把明显错的挡住"。
/// </para>
/// </remarks>
public static class SchemaBuilder
{
    /// <summary>
    /// 把声明上标注的参数约束回填进 schema。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 参数与 schema 节点的对应关系从方法的参数表读，不从 schema 推导：
    /// 从 schema 推导的话，约束本身也得先写进 schema 才有得推导，绕回了起点。
    /// </para>
    /// <para>
    /// 参数名按原样匹配。签名里的参数名本来就写成小驼峰，与 schema 里的属性名一致。
    /// </para>
    /// </remarks>
    /// <param name="schema">由方法签名推导出的 schema。</param>
    /// <param name="declaration">被推导的那个方法。为空时原样返回。</param>
    public static JsonElement ApplyParameterConstraints(JsonElement schema, MethodInfo? declaration)
    {
        if (declaration is null
            || schema.ValueKind != JsonValueKind.Object
            || JsonNode.Parse(schema.GetRawText()) is not JsonObject root
            || root["properties"] is not JsonObject properties)
        {
            return schema;
        }

        var changed = false;

        foreach (var parameter in declaration.GetParameters())
        {
            if (parameter.Name is not { Length: > 0 } name
                || parameter.GetCustomAttribute<PatternAttribute>() is not { } pattern
                || !properties.TryGetPropertyValue(name, out var node)
                || node is not JsonObject property)
            {
                continue;
            }

            // 数组参数的约束落在元素上：要约束的是"里面的每个标识长什么样"。
            var target = IsArray(property) && property["items"] is JsonObject items ? items : property;

            target["pattern"] = pattern.Pattern;
            changed = true;
        }

        return changed ? JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() : schema;
    }

    /// <summary>
    /// 按 schema 校验一次调用的参数，返回全部问题。
    /// </summary>
    /// <remarks>
    /// 一次返回全部而不是遇到第一个就停：模型一次给错两个参数是很常见的，
    /// 只报第一个会让它改一次、失败一次，来回好几轮。
    /// </remarks>
    public static IReadOnlyList<ToolError> Validate(JsonElement schema, JsonElement arguments)
    {
        if (schema.ValueKind != JsonValueKind.Object
            || arguments.ValueKind != JsonValueKind.Object
            || JsonNode.Parse(schema.GetRawText()) is not JsonObject root)
        {
            return [];
        }

        var errors = new List<ToolError>();
        var properties = root["properties"] as JsonObject;
        var required = Required(root);

        // 不认得的参数名直接挡掉，并把认得的列出来。默默忽略的话，模型写错一个名字之后
        // 它会以为那个参数生效了，而实际发生的是另一次调用。
        foreach (var argument in arguments.EnumerateObject())
        {
            if (properties is null || !properties.ContainsKey(argument.Name))
            {
                errors.Add(ToolError.Of(
                    ToolErrorCodes.ArgumentUnknown,
                    $"{argument.Name} 不是这个工具的参数",
                    argument.Name,
                    Known(properties)));
            }
        }

        if (properties is null)
        {
            return errors;
        }

        foreach (var (name, node) in properties)
        {
            if (node is not JsonObject property)
            {
                continue;
            }

            var present = arguments.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null;

            if (!present)
            {
                if (required.Contains(name))
                {
                    errors.Add(ToolError.Of(
                        ToolErrorCodes.ArgumentMissing,
                        $"缺少必填参数 {name}",
                        name,
                        Expected(property)));
                }

                continue;
            }

            if (value.ValueKind == JsonValueKind.String
                && Pattern(property) is { } pattern
                && !Patterns.Matches(pattern, value.GetString()))
            {
                errors.Add(ToolError.Of(
                    ToolErrorCodes.ArgumentInvalid,
                    $"参数 {name} 的值 {value.GetString()} 不匹配 {pattern}",
                    name,
                    pattern));
            }
        }

        return errors;
    }

    /// <summary>必填参数名。</summary>
    private static HashSet<string> Required(JsonObject schema) =>
        schema["required"] is JsonArray array
            ? [.. array.Select(item => item?.GetValue<string>()).OfType<string>()]
            : new HashSet<string>(StringComparer.Ordinal);

    /// <summary>这个 schema 节点上的正则约束。</summary>
    private static string? Pattern(JsonObject schema) =>
        schema["pattern"] is JsonValue value && value.TryGetValue<string>(out var pattern) ? pattern : null;

    /// <summary>这个 schema 节点期望的形式，用来填错误的期望字段。</summary>
    private static string? Expected(JsonObject schema) =>
        Pattern(schema) is { } pattern
            ? $"匹配 {pattern}"
            : schema["type"] is JsonValue value && value.TryGetValue<string>(out var type) ? type : null;

    /// <summary>这个工具认得哪些参数。为空时明说它不接受参数。</summary>
    private static string Known(JsonObject? properties) =>
        properties is null || properties.Count == 0
            ? "这个工具不接受参数"
            : $"可用参数：{string.Join('、', properties.Select(property => property.Key))}";

    /// <summary>
    /// 这个 schema 节点描述的是不是数组。
    /// </summary>
    /// <remarks>
    /// 可空参数的 <c>type</c> 是数组形式（<c>["string","null"]</c>），
    /// 直接按字符串读会抛异常，所以要先试一次。
    /// </remarks>
    private static bool IsArray(JsonObject schema) =>
        schema["type"] is JsonValue value && value.TryGetValue<string>(out var type) && type == "array";
}

#endregion
