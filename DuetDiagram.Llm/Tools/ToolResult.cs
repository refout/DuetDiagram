using System.Text.Json;
using System.Text.Json.Serialization;

namespace DuetDiagram.Llm.Tools;

#region 错误码

/// <summary>
/// 工具层的错误码。与命令层的错误码是两套，不混用。
/// </summary>
/// <remarks>
/// <para>
/// 分开的理由是**处置不同**：命令层的"字段值不合法"说的是这份文档里那个字段写错了，
/// 处置是去改文档；工具层的"参数不合法"说的是这一次调用给错了参数，处置是换一个参数再来。
/// 合成一套的话，同一个码会在两个入口给出两种说法，而调用方按哪一种都可能是错的。
/// </para>
/// <para>
/// 命名风格与命令层一致：大写下划线，<c>TOOL_</c> 前缀表明它出自哪一层。
/// </para>
/// </remarks>
public static class ToolErrorCodes
{
    /// <summary>这个工具没注册过。</summary>
    public const string UnknownTool = "TOOL_UNKNOWN";

    /// <summary>必填参数没给。</summary>
    public const string ArgumentMissing = "TOOL_ARGUMENT_MISSING";

    /// <summary>参数给了，但不满足它自己的约束。</summary>
    public const string ArgumentInvalid = "TOOL_ARGUMENT_INVALID";

    /// <summary>参数名不是这个工具认得的名字。</summary>
    public const string ArgumentUnknown = "TOOL_ARGUMENT_UNKNOWN";

    /// <summary>这一次调用本身成立，但它对应的能力还没接上。</summary>
    public const string NotSupported = "TOOL_NOT_SUPPORTED";
}

#endregion

#region 结果

/// <summary>
/// 一条结构化的工具错误。
/// </summary>
/// <remarks>
/// <para>
/// 四个字段各自有用途，缺一个都会让调用方只能靠猜：<see cref="Code"/> 用来分支，
/// <see cref="Parameter"/> 指出是哪个参数，<see cref="Expected"/> 说明期望的形式，
/// <see cref="Message"/> 是一句给人看的话。
/// </para>
/// <para>
/// 只给一句话的话，模型只能原样重试或者换一个猜法——而每次重试都是一轮往返。
/// </para>
/// </remarks>
public sealed record ToolError
{
    public required string Code { get; init; }

    public required string Message { get; init; }

    /// <summary>出错的参数名。错误不落在某个参数上时为空。</summary>
    public string? Parameter { get; init; }

    /// <summary>期望的形式，例如一个正则表达式或一串可用取值。</summary>
    public string? Expected { get; init; }

    public static ToolError Of(
        string code,
        string message,
        string? parameter = null,
        string? expected = null) => new()
        {
            Code = code,
            Message = message,
            Parameter = parameter,
            Expected = expected,
        };
}

/// <summary>
/// 一次工具调用的结果。
/// </summary>
/// <remarks>
/// <para>
/// 失败也走这个类型返回，不抛异常。抛异常的话，调用方看到的是一个内部错误，
/// 而不是"你给错了参数"——而这两件事的处置完全不同：前者要去查实现，后者改一下参数就行。
/// </para>
/// <para>
/// <see cref="Data"/> 用 <see cref="JsonElement"/> 而不是某个具体类型：
/// 八个工具的返回形状各不相同，而它们都要能直接进模型上下文与代理响应，
/// 中间加一层类型转换只会多一处会分叉的地方。
/// </para>
/// </remarks>
public sealed record ToolResult
{
    public bool IsSuccess { get; init; }

    /// <summary>一句话说明这次调用做了什么，或者为什么没做成。</summary>
    public string? Message { get; init; }

    /// <summary>成功时的结构化载荷。</summary>
    public JsonElement? Data { get; init; }

    public ToolError[] Errors { get; init; } = [];

    public static ToolResult Ok(JsonElement data, string? message = null) => new()
    {
        IsSuccess = true,
        Data = data,
        Message = message,
    };

    public static ToolResult Ok(string? message = null) => new()
    {
        IsSuccess = true,
        Message = message,
    };

    public static ToolResult Fail(params ToolError[] errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        return new ToolResult
        {
            Errors = errors,
            Message = errors.Length > 0 ? errors[0].Message : null,
        };
    }

    /// <summary>
    /// 能力还没接上。
    /// </summary>
    /// <remarks>
    /// 与"参数错了"分开：这一条不是调用方的问题，重试多少次都一样。
    /// 把缺什么写清楚，调用方才知道是换个做法还是等一等。
    /// </remarks>
    public static ToolResult NotSupported(string tool, string? action, string missing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        ArgumentException.ThrowIfNullOrWhiteSpace(missing);

        var subject = string.IsNullOrEmpty(action) ? tool : $"{tool} 的动作 {action}";

        return Fail(ToolError.Of(
            ToolErrorCodes.NotSupported,
            $"{subject} 还没接上：{missing}",
            action is null ? null : "action"));
    }
}

#endregion

#region 序列化

/// <summary>
/// 工具结果的序列化上下文。
/// </summary>
/// <remarks>
/// 结果要交给模型侧与代理侧，两边都会把它序列化成 JSON。用编译期生成的上下文而不是
/// 运行时反射：后者在裁剪与原生编译之后会失败，而失败发生在真的调用模型那一刻。
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ToolResult))]
[JsonSerializable(typeof(ToolError))]
[JsonSerializable(typeof(ToolError[]))]
internal sealed partial class ToolJsonContext : JsonSerializerContext;

#endregion
