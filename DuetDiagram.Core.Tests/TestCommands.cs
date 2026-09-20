using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 部分写入后失败 / 抛异常。用于验证 AGENTS.md 约定 2（Apply 必须原子）。
/// 通过组合真实的 <see cref="AddNodeCommand"/> 制造半成品，不依赖内部可见性。
/// </summary>
internal sealed class PartialWriteCommand : DiagramCommandBase
{
    private readonly AddNodeCommand _inner;
    private readonly NodeDef _node;
    private readonly bool _throwAfter;

    public PartialWriteCommand(NodeDef node, bool throwAfter)
        : base(throwAfter ? "test-partial-write-throw" : "test-partial-write-fail")
    {
        _node = node;
        _inner = new AddNodeCommand(node);
        _throwAfter = throwAfter;
    }

    public override ValidationResult Validate(DiagramDocument document) => ValidationResult.Valid;

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // 真实的半成品：文档已经被改过，然后才失败。
        _inner.Apply(document);

        return _throwAfter
            ? throw new InvalidOperationException("simulated failure after partial write")
            : CommandResult.Fail(CommandError.Of(ErrorCodes.InternalError, "simulated failure after partial write"));
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new PartialWriteCommand(_node, _throwAfter);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new AddNodeMemento
    {
        Node = _node,
        Index = document.Nodes.Count,
        AffectedIds = [_node.Id],
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
        => _inner.RestoreMemento(document, memento);
}

/// <summary>Apply 抛异常且未改动文档。</summary>
internal sealed class ThrowingCommand : DiagramCommandBase
{
    public ThrowingCommand()
        : base("test-throwing")
    {
    }

    public override ValidationResult Validate(DiagramDocument document) => ValidationResult.Valid;

    public override CommandResult Apply(DiagramDocument document) =>
        throw new InvalidOperationException("simulated failure");

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new ThrowingCommand();

    protected override CommandMemento CaptureCore(DiagramDocument document) => new AddNodeMemento
    {
        Node = new NodeDef { Id = "never-created" },
        Index = 0,
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
    }
}

/// <summary>
/// 违反 AGENTS.md 约定 7：命令内部再触发一次命令。
/// 用于验证 AsyncLocal 深度检测，包括 Task.Run 中的跨线程传播。
/// </summary>
internal sealed class NestedExecuteCommand : DiagramCommandBase
{
    private readonly DiagramCommandBus _bus;
    private readonly NodeDef _inner;
    private readonly bool _crossThread;

    public NestedExecuteCommand(DiagramCommandBus bus, NodeDef inner, bool crossThread)
        : base("test-nested-execute")
    {
        _bus = bus;
        _inner = inner;
        _crossThread = crossThread;
    }

    public override ValidationResult Validate(DiagramDocument document) => ValidationResult.Valid;

    public override CommandResult Apply(DiagramDocument document)
    {
        var command = new AddNodeCommand(_inner).WithContext(ChangeContext.For(ChangeSource.Llm, "nested"));

        if (_crossThread)
        {
            Task.Run(() => _bus.Execute(command)).GetAwaiter().GetResult();
        }
        else
        {
            _bus.Execute(command);
        }

        return CommandResult.Ok(structural: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new NestedExecuteCommand(_bus, _inner, _crossThread);

    protected override CommandMemento CaptureCore(DiagramDocument document) => new AddNodeMemento
    {
        Node = _inner,
        Index = document.Nodes.Count,
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
    }
}

/// <summary>Apply 在成功语义下不做任何变更，用于验证 NoOp 不推进版本、不入历史。</summary>
internal sealed class AlwaysNoOpCommand : DiagramCommandBase
{
    public AlwaysNoOpCommand()
        : base("test-noop")
    {
    }

    public override ValidationResult Validate(DiagramDocument document) => ValidationResult.Valid;

    public override CommandResult Apply(DiagramDocument document) => CommandResult.NoOp("nothing to do");

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new AlwaysNoOpCommand();

    protected override CommandMemento CaptureCore(DiagramDocument document) => new AddNodeMemento
    {
        Node = new NodeDef { Id = "never-created" },
        Index = 0,
    };

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
    }
}
