using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands;

/// <summary>
/// 命令基类。<see cref="CommandId"/> 构造注入，<see cref="Context"/> 私有 set，
/// 只能由 <see cref="WithContext"/> 改写（方案 §4.4）。
/// </summary>
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

    /// <summary>派生类复制自身，但不设置 Context —— 由 <see cref="WithContext"/> 统一回注。</summary>
    protected abstract DiagramCommandBase CloneWith(ChangeContext context);

    protected abstract CommandMemento CaptureCore(DiagramDocument document);

    protected abstract void RestoreCore(DiagramDocument document, CommandMemento memento);
}
