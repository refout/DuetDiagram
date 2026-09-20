using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands;

/// <summary>
/// 命令基类，把三个"每个命令都要写、又容易写错"的部分收拢到一起：
/// 标识的注入、上下文的规范化回注、快照方法的参数校验。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Context"/> 的 setter 是私有的，外部只能通过 <see cref="WithContext"/> 改动。
/// <see cref="WithContext"/> 做两件事：让派生类复制一份自己（<see cref="CloneWith"/>），
/// 然后把新上下文写到副本上。写副本这一步放在基类里完成，
/// 派生类的复制方法就不需要关心上下文，只负责搬自己的业务字段。
/// </para>
/// <para>
/// 之所以让复制与写上下文分开，是为了避免派生类忘记设置上下文——
/// 如果让派生类在复制时顺便赋值，漏写的命令就会悄悄带着默认来源（系统）执行，
/// 而这类错误在测试里很难发现。
/// </para>
/// <para>
/// 派生类需要实现两对方法：<see cref="CloneWith"/> / 业务逻辑，
/// 以及 <see cref="CaptureCore"/> / <see cref="RestoreCore"/> 这组"算快照 / 按快照还原"。
/// </para>
/// </remarks>
public abstract class DiagramCommandBase : IDiagramCommand
{
    protected DiagramCommandBase(string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        CommandId = commandId;
    }

    public string CommandId { get; }

    public ChangeContext Context { get; private set; } = new();

    public IDiagramCommand WithContext(ChangeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 先复制业务字段，再把上下文写到副本上。同一类型内可以访问其它实例的私有成员，
        // 所以这一行不需要给 Context 开更宽的可见性。
        var clone = CloneWith(context);
        clone.Context = context;

        return clone;
    }

    public CommandMemento CaptureMemento(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return CaptureCore(document);
    }

    public void RestoreMemento(DiagramDocument document, CommandMemento memento)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(memento);
        RestoreCore(document, memento);
    }

    public abstract ValidationResult Validate(DiagramDocument document);

    public abstract CommandResult Apply(DiagramDocument document);

    /// <summary>
    /// 复制自身，但**不要**设置上下文——上下文由 <see cref="WithContext"/> 统一写入。
    /// 实现里只要把构造时接收的业务字段原样传回即可，不要复制任何执行期状态。
    /// </summary>
    protected abstract DiagramCommandBase CloneWith(ChangeContext context);

    /// <summary>计算逆变更。此时文档尚未被修改。</summary>
    protected abstract CommandMemento CaptureCore(DiagramDocument document);

    /// <summary>
    /// 依据 <paramref name="memento"/> 把文档还原到执行前的状态。
    /// 必须能应付"目标状态已经部分存在"的情况：撤销与重做会复用同一份实现，
    /// 而重做前文档里可能已经有元素了，先判断再插入比直接插入更稳。
    /// </summary>
    protected abstract void RestoreCore(DiagramDocument document, CommandMemento memento);
}
