namespace DuetDiagram.Core.Commands;

/// <summary>
/// 变更来源。由工具层设置，命令总线不修改（Undo / Redo 除外）。
/// </summary>
public enum ChangeSource
{
    Human,
    Llm,
    System,
    Import,
    Mcp,
    Undo,
    Redo,
}
