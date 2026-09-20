namespace DuetDiagram.Core.Commands;

/// <summary>
/// 一次变更的来源。命令执行完之后，下游（审计日志、变更高亮、冲突提示）靠它区分
/// 是人改的、AI 改的、外部代理改的，还是导入进来的。
/// <see cref="Undo"/> 与 <see cref="Redo"/> 由命令总线在撤销重做时覆写，
/// 其余取值一律由调用方在构造上下文时声明，总线不会改动。
/// </summary>
public enum ChangeSource
{
    /// <summary>人在界面上直接操作。</summary>
    Human,

    /// <summary>软件内置的 AI 助手。</summary>
    Llm,

    /// <summary>系统自动行为，例如加载时补齐默认值。</summary>
    System,

    /// <summary>从外部文件导入。</summary>
    Import,

    /// <summary>外部代理通过 MCP 协议操作。</summary>
    Mcp,

    /// <summary>撤销。由命令总线在撤销时覆写。</summary>
    Undo,

    /// <summary>重做。由命令总线在重做时覆写。</summary>
    Redo,
}
