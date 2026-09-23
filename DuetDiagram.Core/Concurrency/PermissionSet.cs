namespace DuetDiagram.Core.Concurrency;

/// <summary>
/// 一个主体能做什么。
/// </summary>
/// <remarks>
/// <para>
/// 它回答两件事：**能不能改这份文档**，以及**能改哪些图层**。前者粗，一条命令是不是写入
/// 一眼看得出来，所以传输层就能判；后者细，只有解析过动作参数的那一层才判得出来——
/// 一次写入有没有点名图层、点名的是哪个，藏在参数里。
/// </para>
/// <para>
/// **图层集合为空表示不限图层**，而不是"哪个图层都不许"。两种意思各写一半的话，
/// 一份没写图层的凭据会变成一份什么都改不动的凭据，而配置它的人以为它与以前一样。
/// </para>
/// <para>
/// 这个类型是纯数据：它只说许不许，不说被拒之后怎么办。报错、记日志、给调用方什么建议
/// 都是各自那一层的事。
/// </para>
/// </remarks>
public sealed record PermissionSet
{
    /// <summary>能不能改这份文档。</summary>
    public bool CanWrite { get; init; }

    /// <summary>允许写入的图层标识。为空表示不限。</summary>
    public IReadOnlySet<string> Layers { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>只能读。一份没登记过的主体拿到的就是它。</summary>
    public static PermissionSet ReadOnly { get; } = new();

    /// <summary>不受限制：能改，而且不限图层。</summary>
    public static PermissionSet Full { get; } = new() { CanWrite = true };

    /// <summary>
    /// 这一次写入点名的图层许不许碰。
    /// </summary>
    /// <param name="layerId">写入点名的图层标识。没点名时为空。</param>
    /// <remarks>
    /// <para>
    /// 没点名图层的写入（加一个节点、连一条边）不算图层级写入，由 <see cref="CanWrite"/> 那一档管：
    /// 它没把任何东西放进某个图层里，也就无从违反图层级的限制。
    /// </para>
    /// <para>
    /// 想连它一起挡下来的话，判据得是"这个元素现在落在哪个图层"——那是另一件事，
    /// 要读文档，而且读出来的图层可能与命令执行那一刻的不是同一个。
    /// </para>
    /// </remarks>
    public bool AllowsLayer(string? layerId) =>
        CanWrite
        && (Layers.Count == 0 || layerId is null || Layers.Contains(layerId));
}
