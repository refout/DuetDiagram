# DuetDiagram

对人和 LLM 双友好的流程图 / 框图绘制软件。

**核心命题**：人和 LLM 通过同一套命令接口操作同一份文档，能力完全对等。
IR 是唯一事实源；GUI 能做的，LLM 通过工具都能做；LLM 能表达的，GUI 都有对应入口。
区别只在操作来源标记和可选的冲突策略。

完整方案见《对人和 LLM 双友好的流程图/框图绘制软件——完整方案.txt》。

## 当前状态

Phase 0（依赖验证）进行中，Phase 1 的垂直切片已经落地。

**技术选型里被推翻的一项**：主选布局引擎不支持固定位置，且在有交叉边的图上横向坐标会爆炸
（20 个节点就能把 116 像素的合理宽度撑到 40803 像素）。改走备选之后，
备选引擎的坐标有界、性能达标，但**固定位置与同层约束两款引擎都不支持**，
必须由我们自建。取证全过程与复现方式见 `reports/phase0a-layout.md`。

| 已完成 | 内容 |
|---|---|
| 仓库地基 | slnx 解决方案、精确版本锁定、契约文档、任务 DAG、LOC 门禁、持续集成 |
| 核心层 | IR、命令总线（执行 / 撤销 / 重做）、版本日志、审计日志、历史栈、广播器、序列化与双哈希 |
| 内置命令 | `add-node` / `remove-node` / `connect-edge` |
| 测试 | 74 个用例，覆盖往返无损、原子性回滚、撤销重做一致性、差异边界、多态注册、依赖边界 |
| Phase 0a | 依赖版本核实、界面栈自检出图、两款布局引擎的并排取证与选型 |

**未实现**：布局引擎封装、Sidecar、Mermaid / DSL、渲染与画布、外部代理接入。
详见 `docs/Architecture.md`。

**已知阻塞**：原生编译发布需要 Visual Studio 的「使用 C++ 的桌面开发」工作负载，
本机未安装，因此这项验收尚未关闭。见 `reports/phase0a.md`。

## 快速开始

```bash
# 编译（要求 .NET SDK 10.0.303+）
dotnet build DuetDiagram.slnx -c Release

# 测试
dotnet test --project DuetDiagram.Core.Tests/DuetDiagram.Core.Tests.csproj

# 按分类跑
dotnet test --project DuetDiagram.Core.Tests/DuetDiagram.Core.Tests.csproj -- --filter-trait "Category=Atomicity"

# 界面栈自检：脱屏渲染一帧后退出，用退出码表达结果
dotnet run --project DuetDiagram.App -c Release -- --selftest --out reports/phase0a-selftest.png

# 布局引擎约束取证（两款候选跑同一套判据）
dotnet run --project tools/Poc/LayoutCandidates -c Release

# 原生编译冒烟（需要 C++ 桌面开发工作负载）
dotnet run --project DuetDiagram.AotSmokeTest -c Release

# LOC 报告
dotnet run --project tools/LocCounter -- --root .
```

## 仓库结构

```
DuetDiagram.slnx            解决方案（slnx 格式）
Directory.Build.props       公共编译属性（net10.0 / 警告即错误 / 裁剪与原生编译分析器）
Directory.Packages.props    精确版本锁定（禁止通配）
global.json                 固定 SDK + 启用 Microsoft.Testing.Platform
AGENTS.md                   Agent 契约：13 条不可违反的约束 + 已知差异
docs/                       架构、IR Schema、错误码、命令清单
reports/                    阶段验证结论与取证数据（含布局引擎缺陷的复现说明）
tasks/                      面向 coding agent 的任务 YAML 与 DAG
tools/LocCounter            LOC 统计（不进 sln）
tools/Poc/                  依赖验证脚手架（不进 sln）
.github/workflows/ci.yml    编译 / 测试 / 契约门禁 / 原生发布
```

## 给 Agent 的入口

先读 `AGENTS.md`，再读 `tasks/README.md` 找可认领的任务。
`AGENTS.md` 里的 13 条约束都有对应测试或 CI 门禁，违反即视为回归。
