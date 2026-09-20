using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands;

/// <summary>
/// 修改文档的唯一入口。界面、内置 AI、外部代理、导入器全部实现或调用这个接口，
/// 接口之下不再区分变更来自谁，只看 <see cref="ChangeContext"/> 里的来源标记。
/// </summary>
/// <remarks>
/// <para>
/// 调用顺序是有契约的：先 <see cref="Validate"/>，通过后才 <see cref="CaptureMemento"/>，
/// 然后 <see cref="Apply"/>。总线严格按这个顺序调用，命令实现可以依赖它。
/// </para>
/// <para>
/// 为什么没有单独的"撤销"方法：撤销所需的一切都在 <see cref="CommandMemento"/> 里，
/// 由 <see cref="RestoreMemento"/> 完成。如果再加一个不带参数的撤销方法，
/// 命令就必须自己缓存"上一次执行时的状态"，而重做又是"在当前文档上重新捕获快照"，
/// 两套状态混在一起很容易对不上。让命令保持无状态，撤销完全由外部传入的快照驱动，
/// 语义只有一种解释。
/// </para>
/// </remarks>
public interface IDiagramCommand
{
    /// <summary>命令标识，小写连字符形式。它会出现在审计日志与版本日志里，必须是稳定值。</summary>
    string CommandId { get; }

    /// <summary>本次调用的上下文。总线会把它替换成补齐了时间戳与会话的版本。</summary>
    ChangeContext Context { get; }

    /// <summary>
    /// 检查前置条件。全部失败原因一次性返回，不要只报第一个——
    /// 调用方（尤其是外部代理）需要一次拿到完整的修复清单。
    /// </summary>
    ValidationResult Validate(DiagramDocument document);

    /// <summary>
    /// 执行变更。
    /// </summary>
    /// <remarks>
    /// 必须做到要么全成功、要么什么都没发生。中途发现做不下去时：
    /// 返回失败结果，或直接抛异常，两条路都必须保证文档回到调用前的状态。
    /// 总线在失败时还会用快照再兜一次底，但命令自己也不该留下半成品——
    /// 依赖兜底意味着命令无法被单独使用。
    /// </remarks>
    CommandResult Apply(DiagramDocument document);

    /// <summary>
    /// 捕获逆变更。总线在 <see cref="Apply"/> 之前调用，此时文档还是变更前的状态。
    /// </summary>
    /// <remarks>
    /// 这里算出来的索引/位置必须与 <see cref="Apply"/> 将要使用的一致。
    /// 两边各算一次很容易分叉（比如一个夹紧了越界索引、另一个直接抛异常），
    /// 所以实际实现里让两边调用同一个私有方法算出结果。
    /// </remarks>
    CommandMemento CaptureMemento(DiagramDocument document);

    /// <summary>按快照把文档恢复到 <see cref="Apply"/> 之前的状态。</summary>
    void RestoreMemento(DiagramDocument document, CommandMemento memento);

    /// <summary>
    /// 复制出一个上下文被替换过的命令。总线用它把补齐时间戳与会话后的上下文回注给命令，
    /// 这样历史栈里存的是规范化之后的命令，重做时才能复现同样的来源信息。
    /// </summary>
    IDiagramCommand WithContext(ChangeContext context);
}
