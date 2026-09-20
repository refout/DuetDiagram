using DuetDiagram.Core.Bus;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Commands.Builtin;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Tests;

/// <summary>
/// 先改文档再失败的命令，用来验证失败时的回滚。
/// </summary>
/// <remarks>
/// <para>
/// 关键在于它**真的改了文档**：内部先调用一次真实的新增节点命令把节点插进去，
/// 然后才报告失败或抛异常。这样才能测出总线有没有把半成品清理干净。
/// 如果造一个"什么都不做直接失败"的假命令，回滚逻辑根本不会被执行到，
/// 测试会假通过——这正是这类测试最容易自我欺骗的地方。
/// </para>
/// <para>
/// 用组合真实命令的方式制造半成品，而不是直接去改文档的内部集合，
/// 这样测试不需要任何特权访问，也顺便验证了"命令可以互相组合"这条路是通的。
/// </para>
/// </remarks>
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

/// <summary>直接抛异常且完全没碰文档的命令，用来覆盖"未改动就失败"这条路径。</summary>
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
/// 在命令内部再次发起命令，用于验证嵌套检测。
/// </summary>
/// <remarks>
/// <see cref="DiagramCommandBus.NestedExecuteMessage"/> 描述的两种触发方式都在这里覆盖：
/// 同线程直接调用，以及另起一个任务调用。后者同样会被拦住，因为执行上下文局部变量
/// 会沿着任务边界自动传播——这是有意的保守策略，宁可拦住也不能漏。
/// </remarks>
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
            // 同步等待，让异常直接从这一层抛出来，避免被包装成聚合异常而影响断言。
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

/// <summary>报告"合法但无事可做"的命令，用来验证空操作不推进版本、不进历史。</summary>
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
