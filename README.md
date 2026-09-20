# DuetDiagram

对人和 LLM 双友好的流程图 / 框图绘制软件。

**核心命题**：人和 LLM 通过同一套命令接口操作同一份文档，能力完全对等。
IR 是唯一事实源；GUI 能做的，LLM 通过工具都能做；LLM 能表达的，GUI 都有对应入口。
区别只在操作来源标记和可选的冲突策略。

完整方案见《对人和 LLM 双友好的流程图/框图绘制软件——完整方案.txt》。

## 当前状态

Phase 0（依赖验证）尚未开始；**Phase 1 的垂直切片已经落地**，用于先把架构地基钉死：

| 已完成 | 内容 |
|---|---|
| IR | `DiagramDocument` + `NodeDef` + `EdgeDef` |
| 命令总线 | `Execute` / `ExecuteAsync` / `Undo` / `Redo`，门锁 + 嵌套检测 |
| 内置命令 | `add-node` / `remove-node` / `connect-edge` |
| 配套 | 版本日志、审计日志、历史栈、广播器、AOT 安全序列化、结构/视觉哈希 |
| 测试 | 73 个用例，覆盖往返无损、原子性回滚、撤销重做一致性、diff 边界、AOT 多态注册 |
| 契约 | `AGENTS.md`、`docs/`、`tasks/`、LOC 门禁、CI |

**未实现**：Sidecar、布局引擎、Mermaid / DSL、渲染、GUI、LLM、MCP。见 `docs/Architecture.md`。

## 快速开始

```bash
# 编译（要求 .NET SDK 10.0.303+）
dotnet build DuetDiagram.slnx -c Release

# 测试
dotnet test --project DuetDiagram.Core.Tests/DuetDiagram.Core.Tests.csproj

# 按分类跑
dotnet test --project DuetDiagram.Core.Tests/DuetDiagram.Core.Tests.csproj -- --filter-trait "Category=Atomicity"

# 冒烟（CoreCLR 路径；AOT 发布需要 MSVC C++ 工作负载）
dotnet run --project DuetDiagram.AotSmokeTest -c Release

# LOC 报告
dotnet run --project tools/LocCounter -- --root .
```

## 仓库结构

```
DuetDiagram.slnx            解决方案（slnx 格式）
Directory.Build.props       公共编译属性（net10.0 / 警告即错误 / AOT 分析器）
Directory.Packages.props    精确版本锁定（禁止通配）
global.json                 固定 SDK + 启用 Microsoft.Testing.Platform
AGENTS.md                   Agent 契约：13 条不可违反的约束 + 已知差异
docs/                       架构、IR Schema、错误码、命令清单
tasks/                      面向 coding agent 的任务 YAML 与 DAG
tools/LocCounter            LOC 统计（不进 sln）
.github/workflows/ci.yml    编译 / 测试 / 契约门禁 / AOT
```

## 给 Agent 的入口

先读 `AGENTS.md`，再读 `tasks/README.md` 找可认领的任务。
`AGENTS.md` 里的 13 条约束都有对应测试或 CI 门禁，违反即视为回归。
