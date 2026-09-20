using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands;

/// <summary>
/// 唯一的文档修改入口。GUI、内部 LLM、外部 MCP Agent、导入器全部通过它改文档。
/// </summary>
/// <remarks>
/// 方案 §4.4 还定义了 <c>Undo(DiagramDocument)</c>。本轮用 <see cref="RestoreMemento"/> 承担该职责：
/// 撤销所需的信息全部来自 <see cref="CommandMemento"/>，因此命令必须是**无状态**的
/// （<c>Undo()</c> 需要命令自己缓存最近一次 memento，会让 Redo 的「重新捕获 memento」语义不清）。
/// 差异登记在 AGENTS.md「与方案的已知差异」。
/// </remarks>
public interface IDiagramCommand
{
    string CommandId { get; }

    ChangeContext Context { get; }

    /// <summary>前置校验。返回 Invalid 时命令总线不会调用 Apply。</summary>
    ValidationResult Validate(DiagramDocument document);

    /// <summary>
    /// 应用变更。AGENTS.md 约定 2：必须原子 —— 返回失败或抛异常时，
    /// 文档必须处于与调用前完全一致的状态。命令总线会调用 RestoreMemento 兜底，
    /// 但命令自身也不得留下半成品。
    /// </summary>
    CommandResult Apply(DiagramDocument document);

    /// <summary>捕获逆变更。命令总线在 Apply 之前调用。</summary>
    CommandMemento CaptureMemento(DiagramDocument document);

    /// <summary>把文档恢复到 Apply 之前的状态。</summary>
    void RestoreMemento(DiagramDocument document, CommandMemento memento);

    /// <summary>复制命令并替换上下文。上下文由命令总线规范化后回注。</summary>
    IDiagramCommand WithContext(ChangeContext context);
}
