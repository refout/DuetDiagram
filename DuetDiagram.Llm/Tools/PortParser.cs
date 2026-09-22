namespace DuetDiagram.Llm.Tools;

/// <summary>
/// 一个端点：元素标识加一个可空的端口名。
/// </summary>
/// <param name="Id">元素标识。</param>
/// <param name="Port">端口名。为空表示由布局引擎自动选边。</param>
public sealed record Endpoint(string Id, string? Port);

/// <summary>
/// 把「标识.端口」这种写法拆成两半。
/// </summary>
/// <remarks>
/// <para>
/// 模型描述一条连线时说的本来就是"从 a 的下边到 b 的上边"这一件事，所以端点在参数表里
/// 是一个参数而不是两个。拆成四个参数之后，端点与端口对不上就成了一个可以表达出来、
/// 却没有意义的状态。
/// </para>
/// <para>
/// 元素标识本身不含点（它只由小写字母、数字与连字符组成），所以按第一个点拆是确定的。
/// 端口名也由参数上的约束挡在 schema 那一层，这里不做二次校验。
/// </para>
/// </remarks>
public static class PortParser
{
    /// <summary>拆一个端点。没有点的时候端口为空。</summary>
    public static Endpoint Parse(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        var separator = endpoint.IndexOf('.', StringComparison.Ordinal);

        if (separator < 0)
        {
            return new Endpoint(endpoint, null);
        }

        var port = endpoint[(separator + 1)..];

        return new Endpoint(endpoint[..separator], port.Length == 0 ? null : port);
    }
}
