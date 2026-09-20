# 架构

## 分层

```
                 ┌────────── 入口层 ──────────┐
  GUI 画布/面板   内部 LLM   MCP stdio/HTTP   文件导入导出
        │            │            │              │
        └────────────┴────────────┴──────────────┘
                          │
                   ┌──────▼──────┐
                   │  DiagramCommandBus  │   ← 两条通路在此汇合
                   └──────┬──────┘
        校验 → 应用 → 版本/哈希 → 版本日志 → 审计日志 → 历史栈 → 广播
                          │
                   ┌──────▼──────┐
                   │ DiagramDocument (IR) │   ← 唯一事实源
                   └─────────────┘
```

「人和 LLM 能力对等」的落点就是这条汇合线：**命令之下没有任何东西知道变更来自谁**，
只有一个 `ChangeSource` 字段用于审计与变更高亮。

## 已实现模块（垂直切片）

| 模块 | 文件 | 说明 |
|---|---|---|
| IR | `Model/` | `DiagramDocument` + `NodeDef` + `EdgeDef` + 5 个枚举 |
| 命令契约 | `Commands/` | `IDiagramCommand`、`DiagramCommandBase`、`CommandMemento`、`CommandResult`、`CommandError`、`ValidationResult`、`FieldChange`、`ChangeContext`、`ISessionProvider`、`SessionIds` |
| 内置命令 | `Commands/Builtin/` | `AddNodeCommand`、`RemoveNodeCommand`、`ConnectEdgeCommand` |
| 版本日志 | `Logging/VersionLog.cs`、`DiffResult.cs`、`VersionEntry.cs` | 环形 100 条；`BuildDiff` 的五种结果 |
| 审计日志 | `Logging/AuditLog.cs` | 容量 1000 |
| 历史栈 | `History/HistoryStack.cs` | 撤销/重做双栈，各 500 |
| 广播 | `Broadcasting/` | `InProcessBroadcaster`（有界 1024 / DropOldest）、`NullChangeBroadcaster` |
| 命令总线 | `Bus/` | `Execute` / `ExecuteAsync` / `Undo` / `Redo`，门锁 + AsyncLocal 嵌套检测 |
| 序列化 | `Serialization/` | 源生成 JSON（AOT 安全）、结构哈希、视觉哈希 |
| 工作区 | `Workspace/DiagramWorkspace.cs` | 文档 + 总线 + 广播器所有权 |
| 诊断 | `Diagnostics/` | 最小告警出口，避免 Core 依赖 `Microsoft.Extensions.Logging` |

## 未实现模块

| 模块 | 方案位置 | 计划 |
|---|---|---|
| Sidecar（`layout.json` / `user.json` / `.dsl`） | §4.3 / §11 | Phase 1 P1-06 / P1-07 |
| 布局引擎 + 四级降级 | §5 | Phase 1 P1-11 / P1-12 |
| Mermaid 导入导出 | §二 | Phase 1 P1-08 ~ P1-10 |
| DSL 解析器 | §一 | Phase 1 P1-16 / P1-17 |
| 四叉树视口索引 | §十 | Phase 1 P1-13 |
| 校验器 / 冲突策略 | §4.4 | Phase 1 P1-04 / P1-05 |
| 渲染与导出 | §三 | Phase 2 |
| LLM / MCP / Skills | §六~八 | Phase 3 |

## 两条关键不变量

**1. 版本单调。** 每次成功且非空操作的命令都让 `DiagramDocument.Version` +1；
失败、被拒、`NoOp` 都不推进。撤销与重做也会推进版本 —— 版本描述的是**状态序列**，
不是「变更次数」。因此 `VersionLog` 的区间 `(from, to]` 永远可以完整重建状态。

**2. 结构哈希 ⟂ 视觉哈希。**
`StructuralHash` 只覆盖「谁和谁相连」+ `Parent`，决定是否需要重布局；
`VisualHash` 覆盖标签、形状、样式令牌，决定是否需要重绘。
两者都不覆盖 `Version` 与自身，避免自引用。
门禁：`Structural_hash_ignores_labels_but_visual_hash_does_not`。

## 构建环境要求

| 需求 | 用途 | 现状 |
|---|---|---|
| .NET SDK 10.0.303 | 全部 | 已满足 |
| NuGet 源 `nuget.azure.cn` | 还原 | 已在 `nuget.config` 固定 |
| MSVC「使用 C++ 的桌面开发」 | `PublishAot` 的原生链接步骤 | **本机缺失** |

缺少 MSVC 时 `dotnet publish` 会在 `Platform linker not found` 处失败，
但 IL 编译与 AOT 分析器已经在 `dotnet build` 阶段跑完（0 警告）。
此时可用 `dotnet run --project DuetDiagram.AotSmokeTest` 验证同一套源生成路径的逻辑，
它**不能**替代 AOT 发布验收（P1 判据 #9 / #21）。
